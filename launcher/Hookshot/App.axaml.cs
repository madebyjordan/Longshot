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
            // (--launch <folder>: the game folder's starter. --update <folder> --tag <tag>: the game's own update offer, answered
            // yes there, host/hos_update.c: that release brought into the folder at once)
            var args = desktop.Args ?? Array.Empty<string>();
            string? After(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
            var folder = After("--launch") ?? After("--update") ?? "";
            var tag = After("--update") != null ? After("--tag") : null;
            desktop.MainWindow = new MainWindow(GameFolder.Read(folder), MainWindow.Preview.FromEnvironment(), tag);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
