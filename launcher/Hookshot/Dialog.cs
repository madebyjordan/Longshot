using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hookshot.Launcher;

/// A question in the launcher's own look (the panel's colours and buttons, Styles.axaml), over the window: Update to..., Switch
/// back to..., NVIDIA's and AMD's licenses. Yes is the accent; Escape and the window's close are No
static class Dialog
{
    /// (development: the window drawn with no screen answers Yes, as the WPF launcher's screenshots do)
    public static bool Headless;

    public static async Task<bool> Ask(Window owner, string title, string text, string yes = "Yes", string no = "No")
    {
        if (Headless) return true;
        var answer = false;
        var d = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.Parse("#1A1A1A")),
            Icon = owner.Icon,
        };
        var yesButton = new Button { Content = yes, Classes = { "block" }, Background = new SolidColorBrush(Color.Parse("#B86C00")), MinWidth = 120, IsDefault = true };
        var noButton = new Button { Content = no, Classes = { "block" }, MinWidth = 120, IsCancel = true };
        yesButton.Click += (_, _) => { answer = true; d.Close(); };
        noButton.Click += (_, _) => d.Close();
        var body = new SelectableTextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = new SolidColorBrush(Color.Parse("#D6D6D6")),
        };
        d.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 16,
            Children =
            {
                new TextBlock { Text = title, Classes = { "line" }, FontWeight = FontWeight.SemiBold },
                body,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { noButton, yesButton } },
            },
        };
        d.KeyDown += (_, e) => { if (e.Key == Key.Escape) d.Close(); };
        await d.ShowDialog(owner);
        return answer;
    }

    public static Task Tell(Window owner, string title, string text) => Ask(owner, title, text, "OK", "Close");
}
