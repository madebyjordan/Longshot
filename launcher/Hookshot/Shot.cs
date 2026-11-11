using Avalonia.Headless;
using Avalonia.Threading;

namespace Hookshot.Launcher;

/// (development) The window as Skia draws it, with no screen, to a PNG: for comparing with the design (Paper's "Hookshot v2"). It is
/// drawn once GitHub's releases are in (or did not answer), as the window would show them
static class Shot
{
    public static int Draw(string folder, string file)
    {
        var w = new MainWindow(GameFolder.Read(folder), MainWindow.Preview.FromEnvironment());
        w.Show();
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!w.Ready.IsCompleted && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = w.CaptureRenderedFrame();
        if (frame == null) return 1;
        frame.Save(file);
        w.Close();
        return 0;
    }
}
