using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HookshotBuilder;

namespace Hookshot.Launcher;

/// The launcher's side of the hand-over from a 2.x Builder (docs/launcher-upgrade.md, NotRexed/Hookshot#2722): where it is
/// installed (section 2), the import files a Builder drops for it (section 3), and the Builder's own settings read on a first
/// start with nothing dropped. The builds themselves stay where the Builder keeps its versions (Builders.Base): this launcher
/// installs and drives each release's builder there, as the WPF and Tk launchers do, so they are shared, not imported.
static class Handover
{
    /// The launcher's home, section 2: HOOKSHOT_LAUNCHER_HOME, else %LOCALAPPDATA%\HookshotLauncher on Windows,
    /// ${XDG_DATA_HOME:-~/.local/share}/hookshot/launcher elsewhere. The upgrader installs the launcher in it (app\ on Windows,
    /// Hookshot.AppImage on Linux) and drops its import files in import/
    public static string Home
    {
        get
        {
            if (Environment.GetEnvironmentVariable("HOOKSHOT_LAUNCHER_HOME") is { Length: > 0 } home) return home;
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HookshotLauncher");
            var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(data, "hookshot", "launcher");
        }
    }

    public static string ImportDir => Path.Combine(Home, "import");

    /// What a Builder handed over for one game folder: section 3's import file, format 1
    public sealed record Import(string Folder, string? Version, string Track, bool Default, BuilderSettings Settings, string? Language,
                                string? Work);

    /// An import file's text read, or null when it is not one this launcher takes (another format, no absolute folder, not JSON).
    /// Paths are kept as they are, absolute, whether or not they still exist (the build asks again for one that does not)
    public static Import? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.Number ||
                f.GetInt32() != 1)
                return null;
            if (Text(r, "folder") is not { } folder || !Absolute(folder)) return null;
            var version = Text(r, "version");
            var track = Text(r, "track") is { } t && t is "release" or "rc" or "nightly" ? t : TrackOf(version);
            var d = !r.TryGetProperty("default", out var def) || def.ValueKind != JsonValueKind.False;
            var s = r.TryGetProperty("settings", out var set) && set.ValueKind == JsonValueKind.Object ? set : default;
            bool Flag(string name) => s.ValueKind == JsonValueKind.Object && s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
            string? Str(string name) => s.ValueKind == JsonValueKind.Object ? Text(s, name) : null;
            int? Num(string name) => s.ValueKind == JsonValueKind.Object && s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number &&
                                     v.TryGetInt32(out var n) && n > 0 ? n : null;
            var settings = new BuilderSettings
            {
                Out = folder, Nup = Str("nup"), Romfs = Str("romfs"), Exefs = Str("exefs"),
                Dlss = Flag("dlss"), DlssLicense = Flag("dlssLicense"), Fsr = Flag("fsr"), FsrLicense = Flag("fsrLicense"),
                // (prepareShaders missing: prepared, as the Builder's tick defaults)
                PrepareLater = s.ValueKind == JsonValueKind.Object && s.TryGetProperty("prepareShaders", out var p) && p.ValueKind == JsonValueKind.False,
                MemoryGb = Num("memoryGb"), Track = track,
            };
            return new Import(folder, version, track, d, settings, Str("language"), Str("work"));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    /// (an absolute path of either system, /home/... or C:\... or \\server\...: the contract's sample files are read on both)
    static bool Absolute(string path) =>
        path.StartsWith('/') || path.StartsWith(@"\\") ||
        path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';

    static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    /// A build's track from its version (Updater.TrackOf, versioning.md): git describe's v2.3.0-4-g1a2b3c4 the nightly,
    /// v2.3.0-rc.2 a release candidate, anything else (or nothing) a release
    public static string TrackOf(string? version) =>
        version == null ? "release"
        : System.Text.RegularExpressions.Regex.IsMatch(version, @"-\d+-g[0-9a-f]+(-dirty)?$") || version.StartsWith("nightly-") ? "nightly"
        : System.Text.RegularExpressions.Regex.IsMatch(version, @"^v\d+\.\d+\.\d+-rc\.\d+$") ? "rc" : "release";

    /// Every import file waiting in import/ (not a .part still being written), read and deleted, oldest first. One that cannot be
    /// read is renamed .bad and kept, so nothing the player had is lost silently
    public static List<Import> Take(string? dir = null)
    {
        dir ??= ImportDir;
        var found = new List<Import>();
        string[] files;
        try { files = Directory.GetFiles(dir, "*.json"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return found; }
        foreach (var file in files.OrderBy(f => File.GetLastWriteTimeUtc(f)).ThenBy(f => f, StringComparer.Ordinal))
            try
            {
                if (Parse(File.ReadAllText(file, Encoding.UTF8)) is { } import)
                {
                    found.Add(import);
                    File.Delete(file);
                }
                else File.Move(file, file + ".bad", overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // (left for the next start)
        return found;
    }

    /// The imports taken into this launcher's settings (Builders.SettingsPath). With no settings of its own yet, the last import's
    /// are its settings: the build starts where the player left the Builder. With settings already, they stay, and an import's
    /// folder becomes the one opened with nothing asked only while there is none that holds a game ("the launcher keeps its own
    /// default"). The build-files folder (Windows work.txt) is taken when none is chosen. True when anything was written
    public static bool Apply(IReadOnlyList<Import> imports, string settingsPath, string? workFile)
    {
        if (imports.Count == 0) return false;
        var own = File.Exists(settingsPath) ? BuilderSettings.Load(settingsPath) : null;
        var s = own ?? imports[^1].Settings;
        if (own != null)
            foreach (var i in imports.Where(i => i.Default))
                if (s.Out is not { Length: > 0 } out_ || !GameFolder.Read(out_).HasGame) s.Out = i.Folder;
        var wrote = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath + ".new", s.ToJson());
            File.Move(settingsPath + ".new", settingsPath, overwrite: true);
            wrote = true;
            if (workFile != null && !File.Exists(workFile) && imports.LastOrDefault(i => i.Work != null)?.Work is { } work)
                File.WriteAllText(workFile, work + "\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return wrote;
    }

    // ---- a first start with nothing dropped (section 3, last paragraph): the Linux Builder's builder.ini read as if it had been ----

    /// The Linux Builder's settings file: ${XDG_CONFIG_HOME:-~/.config}/hookshot/builder.ini
    public static string BuilderIni =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c
                     : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "hookshot", "builder.ini");

    /// builder.ini's key=value lines, as the Builder reads them (hookshot_builder.load_config)
    public static Dictionary<string, string> ReadIni(string text)
    {
        var cfg = new Dictionary<string, string>();
        foreach (var line in text.Split('\n'))
            if (line.TrimEnd('\r') is var l && l.IndexOf('=') is var at && at > 0) cfg[l[..at]] = l[(at + 1)..];
        return cfg;
    }

    /// The import builder.ini stands for (section 3's table, Linux column; linux_upgrade.import_dict): the folder (null: its saved
    /// install folder), the version its stamp says, its language.txt, and whether NVIDIA's license was accepted in the old SDK folder
    public static Import? FromIni(Dictionary<string, string> cfg, string? folder, string? version, string? folderLanguage, bool oldLicense)
    {
        string? Get(string k) => cfg.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
        string? PathOf(string? v) => v == null ? null : v.StartsWith('~') ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + v[1..]
            : Path.IsPathRooted(v) ? v : Path.GetFullPath(v);
        if (PathOf(folder ?? Get("out")) is not { } at) return null;
        var kind = Get("kind") ?? "nup";
        var track = Get("track") is { } t && t is "release" or "rc" or "nightly" ? t : TrackOf(version);
        var settings = new BuilderSettings
        {
            Out = at,
            Nup = kind == "nup" ? PathOf(Get("nup")) : null,
            Romfs = kind == "folders" ? PathOf(Get("romfs")) : null,
            Exefs = kind == "folders" ? PathOf(Get("exefs")) : null,
            Dlss = Get("dlss") == "1", DlssLicense = Get("nvidia_license") == "1" || oldLicense,
            PrepareLater = Get("prepare") == "0", Track = track,
        };
        return new Import(at, version, track, true, settings, Get("language") ?? folderLanguage, null);
    }

    /// FromIni on this PC: builder.ini, its saved folder's stamp and language.txt, the old SDK folder. Null with no builder.ini
    public static Import? FromBuilderIni()
    {
        string text;
        try { text = File.ReadAllText(BuilderIni, Encoding.UTF8); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        var cfg = ReadIni(text);
        if (!cfg.TryGetValue("out", out var folder) || folder.Length == 0) return null;
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var oldLicense = File.Exists(Path.Combine(data, "hookshot", "builder", "ngx-sdk", "LICENSE.txt"));
        string? language = null;
        try { language = File.ReadLines(Path.Combine(folder, "language.txt")).FirstOrDefault()?.Trim() is { Length: > 0 } l ? l : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return FromIni(cfg, folder, GameFolder.Read(folder).Version, language, oldLicense);
    }

    /// At every start, before anything is shown: the import files taken in; on a first start (no settings of this launcher's yet)
    /// with none dropped, on Linux, the Builder's builder.ini instead (on Windows the launcher reads the Builder's own
    /// builder-settings.json already: they are one file)
    public static void AtStart()
    {
        var imports = Take();
        if (imports.Count == 0 && !OperatingSystem.IsWindows() && !File.Exists(Builders.SettingsPath) && FromBuilderIni() is { } ini)
            imports.Add(ini);
        Apply(imports, Builders.SettingsPath, OperatingSystem.IsWindows() ? Path.Combine(Builders.Base, "work.txt") : null);
    }

    // ---- the 2.x command line (section 4) ----

    /// What the launcher was started for: --launch <folder> (open on it), --update <folder> [--tag <tag>] [--fetched] (update it:
    /// the game's own update offer, or a 2.x Builder passing it on), --play [<folder>] (start it with no window)
    public sealed record Start(string? Folder, bool Update, string? Tag, bool Play);

    public static Start ReadArgs(IReadOnlyList<string> args)
    {
        string? After(string name)
        {
            var i = args.ToList().IndexOf(name);
            return i >= 0 && i + 1 < args.Count && !args[i + 1].StartsWith("--") && args[i + 1].Length > 0 ? args[i + 1] : null;
        }
        if (args.Contains("--play")) return new Start(After("--play"), false, null, true);
        if (After("--update") is { } update) return new Start(update, true, After("--tag"), false);
        return new Start(After("--launch"), false, null, false);
    }

    /// (a short name for an import file, as the upgrader names its own: one a folder)
    public static string FileName(string folder) =>
        "builder-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16].ToLowerInvariant() + ".json";
}
