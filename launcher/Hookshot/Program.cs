using Avalonia;
using Avalonia.Headless;

namespace Hookshot.Launcher;

static class Program
{
    /// hookshot --launch <game folder>: the launcher of that folder (the game folder's starter opens it so)
    [STAThread]
    public static int Main(string[] args)
    {
        var folder = args.SkipWhile(a => a != "--launch").Skip(1).FirstOrDefault() ?? "";
        // (development) HOOKSHOT_UI_SHOT=<file.png>: the window drawn by Skia with no screen (Avalonia.Headless) to that file, and out
        if (Environment.GetEnvironmentVariable("HOOKSHOT_UI_SHOT") is { Length: > 0 } shot)
        {
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                      .WithInterFont().SetupWithoutStarting();
            return Shot.Draw(folder, shot);
        }
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()
                  .StartWithClassicDesktopLifetime(args);
        return 0;
    }
}
