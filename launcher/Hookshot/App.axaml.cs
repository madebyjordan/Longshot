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
            var args = desktop.Args ?? Array.Empty<string>();
            var folder = args.SkipWhile(a => a != "--launch").Skip(1).FirstOrDefault() ?? "";
            desktop.MainWindow = new MainWindow(GameFolder.Read(folder), MainWindow.Preview.FromEnvironment());
        }
        base.OnFrameworkInitializationCompleted();
    }
}
