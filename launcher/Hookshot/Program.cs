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
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// The app as Avalonia's tools expect it by this name: an editor's preview pane, and live reload in a Debug build
    /// (HotAvalonia, scripts/launcher/live.ps1). X11's window class stays Hookshot, not the executable's name: the desktop's top
    /// bar and dock name it so, and the AppImage's hookshot.desktop matches it (StartupWMClass=Hookshot)
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().With(new X11PlatformOptions { WmClass = "Hookshot" }).WithInterFont()
#if HOTAVALONIA_ENABLE
                  // (live reload: what goes wrong, a mistake in a saved .axaml too, shows in live.ps1's terminal)
                  .LogToTextWriter(Console.Out, Avalonia.Logging.LogEventLevel.Warning);
#else
                  .LogToTrace();
#endif

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
