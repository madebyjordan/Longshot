using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HookshotBuilder;

namespace Hookshot.Launcher;

/// One release's builder installed on this PC: its folder (versions\<tag>), whose tools build the game as that release
sealed record Builder(string Tag, string Dir)
{
    /// Windows: versions\<tag>\HookshotBuilder\tools (the setup's layout, builder/setup/setup.c). Linux: versions/<tag>/usr/share/hookshot,
    /// beside its AppRun (the AppImage's layout, builder/linux/linux_update.py)
    public string Tools => OperatingSystem.IsWindows() ? Path.Combine(Dir, "HookshotBuilder", "tools") : Path.Combine(Dir, "usr", "share", "hookshot");
    public string AppRun => Path.Combine(Dir, "AppRun");
}

/// Where the builders are and how they are driven: the same folders and programs the WPF launcher and the Linux builder use, so
/// a builder one of them installed is this launcher's too
static class Builders
{
    /// Windows: %LOCALAPPDATA%\HookshotBuilder. Linux: $XDG_DATA_HOME/hookshot/builder (linux_update.data_root). HOOKSHOT_SETUP_BASE
    /// (both) or HOOKSHOT_BUILDER_HOME (Linux, as linux_update reads it): another folder, for trying installs without the real one
    public static string Base
    {
        get
        {
            if (Environment.GetEnvironmentVariable("HOOKSHOT_SETUP_BASE") is { Length: > 0 } tried) return tried;
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HookshotBuilder");
            var data = Environment.GetEnvironmentVariable("HOOKSHOT_BUILDER_HOME") is { Length: > 0 } home ? home
                : Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(data, "hookshot", "builder");
        }
    }

    public static string SettingsPath => Path.Combine(Base, "builder-settings.json");
    public static string NgxSdk => Path.Combine(Base, "ngx-sdk");
    public static string FfxSdk => Path.Combine(Base, "ffx-sdk");

    /// The builders installed here, by tag: complete ones only (not <tag>.part or <tag>.gone, which an install stopped half way or a
    /// removal left, and on Windows with the setup's installed.txt)
    public static Dictionary<string, Builder> Installed()
    {
        var found = new Dictionary<string, Builder>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path.Combine(Base, "versions")))
            {
                var tag = Path.GetFileName(d);
                if (tag.EndsWith(".part") || tag.EndsWith(".gone") || !LaunchList.IsRelease(tag)) continue;
                var b = new Builder(tag, d);
                var complete = OperatingSystem.IsWindows()
                    ? File.Exists(Path.Combine(d, "installed.txt")) && File.Exists(Path.Combine(b.Tools, "python", "python.exe"))
                    : File.Exists(b.AppRun) && File.Exists(Path.Combine(d, "usr", "python", "bin", "python3"));
                if (complete && File.Exists(Path.Combine(b.Tools, "nx2", "make_game.py"))) found[tag] = b;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return found;
    }

    /// The newest builder installed (the one a new game is built with), or null
    public static Builder? Newest(Dictionary<string, Builder> installed) =>
        installed.Values.Where(b => !LaunchList.IsPre(b.Tag)).OrderByDescending(b => b.Tag, LaunchList.Order).FirstOrDefault()
        ?? installed.Values.OrderByDescending(b => b.Tag, LaunchList.Order).FirstOrDefault();

    /// nx2/make_game.py of that builder, with these arguments: on Windows its own Python with the folders it builds with (as
    /// builder/HookshotBuilder/Runner.cs starts it), on Linux its AppRun --cli, which sets the AppImage's own up
    public static ProcessStartInfo MakeGame(Builder b, IEnumerable<string> args, int? workers = null, int? memoryGb = null,
                                           bool dlss = false, bool fsr = false)
    {
        ProcessStartInfo si;
        if (OperatingSystem.IsWindows())
        {
            si = new ProcessStartInfo(Path.Combine(b.Tools, "python", "python.exe")) { WorkingDirectory = b.Tools };
            foreach (var (name, folder) in new[] { ("EMSDK", "emsdk"), ("RX_CPM", "cpm"), ("WGPU_NATIVE", "wgpu-native") })
                if (Directory.Exists(Path.Combine(b.Tools, folder))) si.Environment[name] = Path.Combine(b.Tools, folder);
            si.ArgumentList.Add("-I");
            si.ArgumentList.Add(Path.Combine(b.Tools, "nx2", "make_game.py"));
        }
        else
        {
            si = new ProcessStartInfo(b.AppRun) { WorkingDirectory = b.Dir };
            si.ArgumentList.Add("--cli");
        }
        foreach (var a in args) si.ArgumentList.Add(a);
        si.UseShellExecute = false;
        si.CreateNoWindow = true;
        si.RedirectStandardOutput = si.RedirectStandardError = true;
        si.StandardOutputEncoding = si.StandardErrorEncoding = System.Text.Encoding.UTF8;
        si.Environment["PYTHONIOENCODING"] = "utf-8";
        BuildWorkers.Apply(si, workers);
        BuildMemory.Apply(si, memoryGb);
        // (NVIDIA DLSS and AMD FSR 4: their SDKs kept in the builder's folder only when ticked; one set on the PC is not passed on)
        if (dlss) si.Environment["HOOKSHOT_NGX_SDK"] = NgxSdk; else si.Environment.Remove("HOOKSHOT_NGX_SDK");
        if (fsr) si.Environment["HOOKSHOT_FFX_SDK"] = FfxSdk; else si.Environment.Remove("HOOKSHOT_FFX_SDK");
        return si;
    }

    /// (a builder's make_game.py takes that option: read from its text, as each release runs its own tools)
    public static bool Knows(Builder b, string option)
    {
        try { return File.ReadAllText(Path.Combine(b.Tools, "nx2", "make_game.py")).Contains("\"" + option + "\""); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    // ---- installing a release's builder ----

    /// The setup in its engine mode (builder/setup/setup.c --engine): beside this launcher when it ships one, else an installed
    /// builder's (each holds the setup it came with: its Repair runs it). Null: none that has the mode
    public static string? EngineSetup(Dictionary<string, Builder> installed)
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "HookshotBuilder-Setup.exe") };
        candidates.AddRange(installed.Values.OrderByDescending(b => b.Tag, LaunchList.Order)
                                     .Select(b => Path.Combine(b.Dir, "HookshotBuilder", "HookshotBuilder-Setup.exe")));
        candidates.Add(DownloadedSetup);
        return candidates.FirstOrDefault(HasEngine);
    }

    /// (the setup Bootstrap downloaded, when nothing here had one)
    static string DownloadedSetup => Path.Combine(Base, "downloads", "HookshotBuilder-Setup.exe");

    /// (a setup with the engine mode: the word is in its text. One from before would take it for an ordinary install: it would open
    /// its window, make that version the one in use and remove the others)
    static bool HasEngine(string setup)
    {
        try { return File.Exists(setup) && File.ReadAllBytes(setup).AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("--engine")) >= 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// Windows: that release's builder installed beside the others by the setup's engine mode (lines @@setup, @@percent, @@installed,
    /// @@error on stdout). Linux, with a builder here: that builder's own Python and linux_update.py install it (only the parts that
    /// changed are downloaded, each checked). Null: nothing here can install it (Windows with no setup, Linux with no builder: Bootstrap)
    public static ProcessStartInfo? Install(string tag, Dictionary<string, Builder> installed)
    {
        ProcessStartInfo si;
        if (OperatingSystem.IsWindows())
        {
            if (EngineSetup(installed) is not { } setup) return null;
            si = new ProcessStartInfo(setup);
            foreach (var a in new[] { "--engine", "--tag", tag }) si.ArgumentList.Add(a);
        }
        else
        {
            if (Newest(installed) is not { } from) return null;
            si = new ProcessStartInfo(Path.Combine(from.Dir, "usr", "python", "bin", "python3"));
            si.ArgumentList.Add("-I");
            si.ArgumentList.Add("-c");
            si.ArgumentList.Add(LinuxInstall);
            si.ArgumentList.Add(Path.Combine(from.Tools, "builder", "linux"));
            si.ArgumentList.Add(from.Dir);
            si.ArgumentList.Add(tag);
            si.Environment["HOOKSHOT_BUILDER_HOME"] = Path.GetDirectoryName(Path.GetDirectoryName(Base))!;
        }
        si.UseShellExecute = false;
        si.CreateNoWindow = true;
        si.RedirectStandardOutput = si.RedirectStandardError = true;
        si.StandardOutputEncoding = si.StandardErrorEncoding = System.Text.Encoding.UTF8;
        return si;
    }

    /// (Linux: the installed builder's own updater, builder/linux/linux_update.py, asked for one release: its list, checked to be that
    /// release's, its parts downloaded and checked, the unchanged ones copied from the builder that runs this. Lines as the setup's.
    /// Only what every release's linux_update.py has: LIST_NAME, fetch_bytes, parse_list, read_manifest, install)
    const string LinuxInstall = """
import os, sys
sys.path.insert(0, sys.argv[1])
import linux_update as u
root, tag = sys.argv[2], sys.argv[3]
def say(kind, text):
    print(kind + " " + " ".join(str(text).split()), flush=True)
try:
    say("@@setup", "Looking for the builder of " + tag)
    # (the release's own list, fetched here: linux_update.py's fetch_manifest takes a tag only in releases after 2.1.0)
    url = "https://github.com/NotRexed/Hookshot/releases/download/%s/%s" % (tag, u.LIST_NAME)
    m = u.parse_list(u.fetch_bytes(url, 30).decode("utf-8"), local=False)
    if m.tag != tag:
        raise ValueError("GitHub offered %s, not %s: its download links can lag right after a release. Try again in a few minutes" % (m.tag, tag))
    u.install(m, root, u.read_manifest(root), note=lambda message: say("@@setup", message))
    say("@@installed", tag)
except Exception as e:
    say("@@error", e)
    sys.exit(1)
""";

    /// A release's file downloaded from GitHub into dest, its SHA-256 checked against GitHub's digest for it (one without a digest
    /// is not used). match: which of the release's files. Null when done, else why not
    static async Task<string?> Fetch(string tag, Func<string, bool> match, string dest, Action<string> line, CancellationToken cancel)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Hookshot");
        line("@@setup Looking for the builder of " + tag);
        using var meta = await http.GetAsync("https://api.github.com/repos/NotRexed/Hookshot/releases/tags/" + Uri.EscapeDataString(tag), cancel);
        if (!meta.IsSuccessStatusCode) return $"GitHub did not answer for {tag} ({(int)meta.StatusCode}). Try again in a few minutes.";
        using var doc = JsonDocument.Parse(await meta.Content.ReadAsStringAsync(cancel));
        JsonElement? asset = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() is { } n && match(n)) asset = a;
        if (asset is not { } got) return $"{tag} has no builder to download for this system.";
        if ((got.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null) is not { } sha ||
            !sha.StartsWith("sha256:") || sha.Length != 71)
            return $"GitHub gives no checksum for {tag}'s builder, so it is not used.";
        var size = got.GetProperty("size").GetInt64();
        line($"@@setup Downloading part 1 of 1 ({Math.Max(1, size >> 20)} MB)");
        using var response = await http.GetAsync(got.GetProperty("browser_download_url").GetString(), HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        await using (var from = await response.Content.ReadAsStreamAsync(cancel))
        await using (var to = File.Create(dest))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 20];
            long done = 0;
            var said = -1;
            for (int n; (n = await from.ReadAsync(buffer, cancel)) > 0;)
            {
                await to.WriteAsync(buffer.AsMemory(0, n), cancel);
                hash.AppendData(buffer, 0, n);
                done += n;
                if (size > 0 && (int)(done * 100 / size) is var p && p != said) line("@@percent " + (said = p));
            }
            if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), sha[7..], StringComparison.OrdinalIgnoreCase))
                return "The download does not match GitHub's checksum for it, so it is not used. Try again.";
        }
        return null;
    }

    /// The first builder, when none here can install one. Windows: the release's setup (HookshotBuilder.exe) downloaded to
    /// downloads\, where EngineSetup then finds it (a setup from before the engine mode cannot install beside the others: not
    /// used). Linux: the release's AppImage downloaded and unpacked as versions/<tag> (the layout linux_update.py installs: AppRun
    /// at its root). Lines as the setup's. Null when done, else why not
    public static async Task<string?> Bootstrap(string tag, Action<string> line, CancellationToken cancel)
    {
        var downloads = Path.Combine(Base, "downloads");
        var versions = Path.Combine(Base, "versions");
        var dest = Path.Combine(versions, tag);
        var part = dest + ".part";
        try
        {
            Directory.CreateDirectory(downloads);
            if (OperatingSystem.IsWindows())
            {
                var setup = DownloadedSetup;
                if (await Fetch(tag, n => n == "HookshotBuilder.exe", setup, line, cancel) is { } failed) return failed;
                if (!HasEngine(setup))
                {
                    File.Delete(setup);
                    return $"{tag}'s setup is from before this launcher: choose a newer release.";
                }
                return null;
            }
            Directory.CreateDirectory(versions);
            if (Directory.Exists(part)) Directory.Delete(part, true);
            Directory.CreateDirectory(part);
            var image = Path.Combine(part, "Hookshot.AppImage");
            // (the Builder's AppImage, not this launcher's own, Hookshot-Launcher-<version>-x86_64.AppImage)
            if (await Fetch(tag, n => n.StartsWith("Hookshot") && !n.StartsWith("Hookshot-Launcher") && n.EndsWith("x86_64.AppImage"), image, line, cancel) is { } why)
                return why;
            File.SetUnixFileMode(image, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            line("@@setup Unpacking the builder");
            using (var unpack = Process.Start(new ProcessStartInfo(image, "--appimage-extract")
                   { WorkingDirectory = part, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!)
            {
                var drain = unpack.StandardError.ReadToEndAsync(cancel);
                await unpack.StandardOutput.ReadToEndAsync(cancel);
                await drain;
                await unpack.WaitForExitAsync(cancel);
                if (unpack.ExitCode != 0) return "The builder's AppImage could not be unpacked.";
            }
            var root = Path.Combine(part, "squashfs-root");
            if (!File.Exists(Path.Combine(root, "AppRun"))) return "The builder's AppImage could not be unpacked.";
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.Move(root, dest);
            File.WriteAllText(Path.Combine(Base, "current"), tag + "\n");
            line("@@installed " + tag);
            return null;
        }
        catch (OperationCanceledException) { return "cancelled"; }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or JsonException
                                       or KeyNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "The builder could not be downloaded: " + e.Message;
        }
        finally
        {
            try { if (Directory.Exists(part)) Directory.Delete(part, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// The game in a folder started, as the folder's own starter does it, with the update check off (the launcher just checked).
    /// Windows: bin\hookshot.exe. Linux: the builder's AppRun --play (the libraries it brings), else bin/hookshot itself
    public static void Play(string folder, Builder? with)
    {
        ProcessStartInfo si;
        if (OperatingSystem.IsWindows()) si = new ProcessStartInfo(Path.Combine(folder, "bin", "hookshot.exe"));
        else if (with != null && File.Exists(with.AppRun))
        {
            si = new ProcessStartInfo(with.AppRun);
            si.ArgumentList.Add("--play");
            si.ArgumentList.Add(folder);
        }
        else si = new ProcessStartInfo(Path.Combine(folder, "bin", "hookshot"));
        si.UseShellExecute = false;
        si.WorkingDirectory = folder;
        si.Environment["HOOKSHOT_NO_UPDATE_CHECK"] = "1";
        Process.Start(si)?.Dispose();
    }

    /// Where builds keep their files: Windows the builder's folder, or the one the player chose (work.txt); Linux make_game.py's
    /// own default, ~/.cache/hookshot-builder
    public static string Work
    {
        get
        {
            if (!OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } c ? c
                                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "hookshot-builder");
            try
            {
                var chosen = File.ReadAllText(Path.Combine(Base, "work.txt")).Trim();
                if (chosen.Length > 2 && Path.IsPathFullyQualified(chosen)) return chosen;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return Base;
        }
    }

    /// After a build: what it no longer needs goes, as the WPF launcher's FreeAfterBuild (builder/HookshotBuilder/Storage.cs): the
    /// translated code and the downloads. The compiled objects stay, so the next version compiles only what changed. The builders
    /// kept: the release the folder holds now, the newest, the one in use. HOOKSHOT_KEEP_BUILD_FILES=1 keeps everything
    public static void FreeAfterBuild(string? keep)
    {
        if (Environment.GetEnvironmentVariable("HOOKSHOT_KEEP_BUILD_FILES") is { Length: > 0 } k && k != "0") return;
        var gone = new List<string> { Path.Combine(Work, "sa"), Path.Combine(Work, "exefs"), Path.Combine(Work, "probe"),
                                      Path.Combine(Base, "downloads"), Path.Combine(Base, "update") };
        var installed = Installed();
        var newest = Newest(installed);
        string? current = null;
        try { current = File.ReadAllText(OperatingSystem.IsWindows() ? Path.Combine(Base, "versions", "current.txt") : Path.Combine(Base, "current")).Trim(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var running = Path.GetFullPath(AppContext.BaseDirectory);
        foreach (var b in installed.Values)
            if (!LaunchList.SameTag(b.Tag, keep) && b != newest && !LaunchList.SameTag(b.Tag, current) &&
                !running.StartsWith(Path.GetFullPath(b.Dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                gone.Add(b.Dir);
        foreach (var path in gone)
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // (a file in use: it goes after the next build)
    }
}
