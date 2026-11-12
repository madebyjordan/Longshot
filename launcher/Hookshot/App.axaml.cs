using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Hookshot.Launcher;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // (--launch <folder>: the game folder's starter. --update <folder> [--tag <tag>]: the game's own update offer, answered
            // yes there, host/hos_update.c, or a 2.x Builder passing it on: that release brought into the folder at once.
            // Handover.ReadArgs: the 2.x Builder's command line, docs/launcher-upgrade.md section 4)
            var start = Handover.ReadArgs(desktop.Args ?? Array.Empty<string>());
            Builders.Remember();   // (the game folders' starters open this launcher: Builders.cs)
            desktop.MainWindow = new MainWindow(GameFolder.Read(start.Folder ?? ""), MainWindow.Preview.FromEnvironment(), start.Tag, start.Update);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
