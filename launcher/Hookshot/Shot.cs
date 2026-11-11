using Avalonia.Headless;
using Avalonia.Threading;

namespace Hookshot.Launcher;

/// (development) The window as Skia draws it, with no screen, to a PNG: for comparing with the design (Paper's "Hookshot v2"). It is
/// drawn once GitHub's releases are in (or did not answer), as the window would show them. HOOKSHOT_UI_ACT=build|update: then that
/// is done as if pressed (Build Game; Update Available), drawn on the way at <file>-working.png and as it ended at <file>; with
/// "details" too (build,details), Show details is pressed on the way
static class Shot
{
    public static int Draw(string folder, string file)
    {
        Dialog.Headless = true;
        var w = new MainWindow(GameFolder.Read(folder), MainWindow.Preview.FromEnvironment());
        w.Show();
        Pump(() => w.Ready.IsCompleted, 20);
        var act = (Environment.GetEnvironmentVariable("HOOKSHOT_UI_ACT") ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (act[0].Length > 0)
        {
            var doing = w.Act(act[0]);
            var drawn = false;
            Pump(() =>
            {
                if (!drawn && w.Working is >= 0.4)
                {
                    drawn = true;
                    if (act.Contains("details")) w.Act("details");
                    Save(w, Path.ChangeExtension(file, null) + "-working.png");
                }
                return doing.IsCompleted;
            }, 120);
        }
        var ok = Save(w, file);
        w.Close();
        return ok ? 0 : 1;
    }

    static void Pump(Func<bool> until, int seconds)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!until() && DateTime.UtcNow < end)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    static bool Save(MainWindow w, string file)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = w.CaptureRenderedFrame();
        frame?.Save(file);
        return frame != null;
    }
}
