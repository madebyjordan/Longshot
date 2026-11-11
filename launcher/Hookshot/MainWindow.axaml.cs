using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HookshotBuilder;

namespace Hookshot.Launcher;

public partial class MainWindow : Window
{
    readonly GameFolder folder;

    public MainWindow() : this(GameFolder.Read("")) { }

    internal MainWindow(GameFolder folder)
    {
        this.folder = folder;
        InitializeComponent();
        Render();
        AddHandler(Button.ClickEvent, Clicked);
    }

    /// Play and the version under it, from what the folder holds (Build while there is no game yet)
    void Render()
    {
        GoText.Text = folder.HasGame ? "Play" : "Build";
        Version.Content = !folder.HasGame ? "No game yet"
            : folder.Version is { } v ? (LaunchList.IsRelease(v) ? LaunchList.VersionName(v) : v) : "Your game";
    }

    void Clicked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Tag: string page } && page.StartsWith("https://"))
            try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); }
            catch (Exception) { }   // (no browser to open it)
    }
}
