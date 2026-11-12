using Avalonia;
using Avalonia.Headless;
using HookshotBuilder;

namespace Hookshot.Launcher;

static class Program
{
    /// HookshotLauncher --launch <game folder>: the launcher of that folder (the game folder's starter opens it so). The 2.x Builder's
    /// command line too (docs/launcher-upgrade.md section 4): --update <folder> [--tag <tag>] [--fetched], --play [<folder>]
    [STAThread]
    public static int Main(string[] args)
    {
        var start = Handover.ReadArgs(args);
        var folder = start.Folder ?? "";
        // (development) HOOKSHOT_UI_SHOT=<file.png>: the window drawn by Skia with no screen (Avalonia.Headless) to that file, and out.
        // What a Builder handed over is taken in first only from a home named outright (HOOKSHOT_LAUNCHER_HOME, a scratch one):
        // a picture never empties the PC's own import folder
        if (Environment.GetEnvironmentVariable("HOOKSHOT_UI_SHOT") is { Length: > 0 } shot)
        {
            if (Environment.GetEnvironmentVariable("HOOKSHOT_LAUNCHER_HOME") is { Length: > 0 }) Handover.AtStart();
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                      .WithInterFont().SetupWithoutStarting();
            return Shot.Draw(folder, shot);
        }
        // (what a 2.x Builder handed over, taken in before anything is shown: Handover.cs)
        Handover.AtStart();
        if (start.Play && PlayNow(start.Folder)) return 0;
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()
                  .StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// --play [<folder>]: the game started with no window (the folder's, else the one built last), as the 2.x Builder's --play
    /// does. False: no game there, and the launcher opens on it instead
    static bool PlayNow(string? folder)
    {
        folder ??= BuilderSettings.Load(Builders.SettingsPath) is { Out: { Length: > 0 } o } ? Builders.LoadPath(o) : null;
        if (folder == null || !GameFolder.Read(folder).HasGame) return false;
        try
        {
            Builders.Play(folder, Builders.Newest(Builders.Installed()));
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }
}
