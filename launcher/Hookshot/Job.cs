using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using HookshotBuilder;

namespace Hookshot.Launcher;

/// One thing under way, and only one: the first build of a game (the Build tab) or a release brought into the game folder (the
/// menu's foot). Its builder is got first when it is not on this PC (Builders: the setup's engine mode, linux_update.py, or the
/// release's own download), then that builder's make_game.py builds the folder. Their lines move LaunchWork's number and words;
/// all of them are kept for Show details and Copy error log
sealed class Job
{
    public readonly bool FirstBuild;
    public readonly string Tag;
    public readonly LaunchWork Work;
    public readonly StringBuilder Log = new();
    public readonly DateTime Started = DateTime.Now;
    /// (each line, on the UI thread: the window draws what changed)
    public event Action<string>? Said;

    readonly CancellationTokenSource cancel = new();
    Process? process;

    /// ahead: the release is ahead of the one in the folder (an update), else a switch back. FirstBuild: no game in the folder yet
    /// rebuild: the version in the folder built again with the settings now (Settings' Rebuild, or its own row in Releases)
    public Job(string tag, bool firstBuild, bool ahead, bool fetch, bool rebuild = false)
    {
        (Tag, FirstBuild) = (tag, firstBuild);
        Work = new LaunchWork(LaunchList.VersionName(tag), ahead, fetch, rebuild);
    }

    public bool Cancelled => cancel.IsCancellationRequested;

    public void Cancel()
    {
        cancel.Cancel();
        try { process?.Kill(entireProcessTree: true); } catch (Exception) { }
    }

    /// What it all comes to: null when the game was built (make_game.py's @@done), "cancelled", or why it stopped. args: make_game.py's,
    /// made once the builder is here (what it knows is read from its own make_game.py)
    public async Task<string?> Run(Func<Builder, ProcessStartInfo> makeGame)
    {
        var installed = Builders.Installed();
        if (!installed.TryGetValue(Tag, out var builder))
        {
            if (await Install(installed) is { } failed) return Cancelled ? "cancelled" : failed;
            if (!Builders.Installed().TryGetValue(Tag, out builder))
                return $"The builder of {LaunchList.VersionName(Tag)} is not there after it was installed. Your antivirus may have removed it.";
        }
        if (Cancelled) return "cancelled";
        ProcessStartInfo si;
        try { si = makeGame(builder); }
        catch (ArgumentException e) { return e.Message; }
        var (code, error) = await Start(si, belowNormal: true);
        if (Work.Done != null) return null;
        if (Cancelled) return "cancelled";
        return Work.Error ?? error ?? (code == 0 ? "The build tools ended without a game." : "The build tools stopped without saying why. See the details.");
    }

    /// (the release's builder put beside the others: by what is here, else by the release's own download first)
    async Task<string?> Install(Dictionary<string, Builder> installed)
    {
        var si = Builders.Install(Tag, installed);
        if (si == null)
        {
            if (await Builders.Bootstrap(Tag, Line, cancel.Token) is { } why) return why;
            if (!OperatingSystem.IsWindows()) return null;   // (Linux: the AppImage unpacked is the builder)
            si = Builders.Install(Tag, Builders.Installed());
            if (si == null) return "The setup could not be found after it was downloaded. Your antivirus may have removed it.";
        }
        if (Cancelled) return "cancelled";
        var (code, error) = await Start(si, belowNormal: false);
        return code == 0 ? null : Work.Error ?? error ?? "The builder's setup stopped without saying why.";
    }

    /// A hidden program, its lines read as they come. Its exit code, and the last thing it said on stderr (null if nothing)
    async Task<(int Code, string? Error)> Start(ProcessStartInfo si, bool belowNormal)
    {
        string? lastError = null;
        try
        {
            using var p = Process.Start(si)!;
            process = p;
            if (Cancelled) try { p.Kill(entireProcessTree: true); } catch (Exception) { }
            if (belowNormal) try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { }
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                if (!string.IsNullOrWhiteSpace(e.Data)) lastError = e.Data.Trim();
                Dispatcher.UIThread.Post(() => Line(e.Data));
            };
            p.BeginErrorReadLine();
            while (await p.StandardOutput.ReadLineAsync() is { } line) Line(line);
            await p.WaitForExitAsync();
            process = null;
            return (p.ExitCode, lastError);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            process = null;
            return (-1, $"{Path.GetFileName(si.FileName)} could not be started: {e.Message}");
        }
    }

    void Line(string line)
    {
        Work.Line(line);
        Log.AppendLine(line);
        if (Log.Length > 600_000) Log.Remove(0, Log.Length - 400_000);
        Said?.Invoke(line);
    }

    /// (for a bug report: what stopped, the versions, the reason and the last of what the hidden programs said)
    public string Report(string why, string? folderHolds)
    {
        var said = Log.ToString();
        var what = FirstBuild ? $"The build of {LaunchList.VersionName(Tag)} stopped" : Work.StoppedTitle;
        return $"Hookshot launcher: {what}\n{Environment.OSVersion}, the folder holds {folderHolds ?? "no release"}\n{why}\n\n" +
               (said.Length > 6000 ? said[^6000..] : said);
    }
}
