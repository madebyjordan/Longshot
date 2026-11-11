using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using HookshotBuilder;
using static HookshotBuilder.LaunchList;

namespace Hookshot.Launcher;

public partial class MainWindow : Window
{
    readonly GameFolder folder;
    List<ReleaseInfo> releases = new();      // (GitHub's, as last answered)
    string? latest;                          // (its latest release's tag)
    bool checkedOnce, olderOpen;
    double? typicalBuild;                    // (how long a build took on this PC lately: BuildTimes)

    /// (what the window was asked to show first: the menu open on a tab, the older releases unfolded, pre-releases on.
    /// Development: HOOKSHOT_UI_PREVIEW=menu,tab=build|releases|settings,older,pre)
    public sealed record Preview(bool Menu, string? Tab, bool Older, bool Pre)
    {
        public static Preview FromEnvironment()
        {
            var p = (Environment.GetEnvironmentVariable("HOOKSHOT_UI_PREVIEW") ?? "").Split(',', StringSplitOptions.TrimEntries);
            var tab = p.FirstOrDefault(x => x.StartsWith("tab="))?[4..];
            return new(p.Contains("menu") || tab != null, tab, p.Contains("older"), p.Contains("pre"));
        }
    }

    /// (the releases are in, and the window is drawn as they are: what a screenshot waits for)
    public Task Ready { get; }

    public MainWindow() : this(GameFolder.Read(""), new Preview(false, null, false, false)) { }

    internal MainWindow(GameFolder folder, Preview preview)
    {
        this.folder = folder;
        InitializeComponent();
        olderOpen = preview.Older;
        PreTick.IsChecked = preview.Pre;
        typicalBuild = BuildTimes.Typical();
        OutText.Text = folder.Path.Length > 0 ? folder.Path : "Choose a folder";
        foreach (var (box, items) in new[] { (LanguageBox, new[] { "English (US)" }), (WorkerBox, new[] { "Auto" }), (MemoryBox, new[] { "Auto" }) })
        {
            box.ItemsSource = items;
            box.SelectedIndex = 0;
        }
        BuildGame.IsEnabled = false;   // (no game file chosen yet)
        BuildLine.Text = "Choose your game file to build";
        BuildLine.IsVisible = true;

        MenuButton.Click += (_, _) => ShowMenu(true);
        CloseMenu.Click += (_, _) => ShowMenu(false);
        foreach (var tab in new[] { TabBuild, TabReleases, TabSettings }) tab.IsCheckedChanged += (_, _) => ShowPage();
        FoldRow.Click += (_, _) => { olderOpen = !olderOpen; FillChoices(); };
        PreTick.Click += (_, _) => FillChoices();
        LowMemTick.Click += (_, _) => LowMemClick();
        CheckButton.Click += async (_, _) => await CheckReleases(say: true);
        Go.Click += (_, _) => GoClick();
        AddHandler(Button.ClickEvent, LinkClick);

        // (no game yet: the menu opens on Build; with one, on Releases)
        (folder.HasGame ? TabReleases : TabBuild).IsChecked = true;
        if (preview.Tab != null) (preview.Tab == "build" ? TabBuild : preview.Tab == "settings" ? TabSettings : TabReleases).IsChecked = true;
        ShowMenu(preview.Menu || !folder.HasGame);
        ShowLowMem();
        FillChoices();
        Ready = CheckReleases(say: false);
    }

    // ---- the menu ----

    void ShowMenu(bool open)
    {
        Bar.IsVisible = open;
        MenuButton.IsVisible = !open;
    }

    void ShowPage()
    {
        BuildPage.IsVisible = TabBuild.IsChecked == true;
        ReleasesPage.IsVisible = TabReleases.IsChecked == true;
        SettingsPage.IsVisible = TabSettings.IsChecked == true;
    }

    // ---- the versions ----

    /// GitHub's releases asked for: when the window opens, and by Check for updates, which then says what came of it
    async Task CheckReleases(bool say)
    {
        CheckButton.IsEnabled = false;
        CheckButton.Content = "Checking…";
        var (found, newest) = await ReleaseFeed.Get();
        if (found.Count > 0 || releases.Count == 0) (releases, latest) = (found, newest);
        checkedOnce = true;
        FillChoices();
        if (say && (found.Count == 0 || !NewerOut()))
        {
            CheckButton.Content = found.Count == 0 ? "Could not check" : "Up to date";
            await Task.Delay(2400);
        }
        CheckButton.Content = "Check for updates";
        CheckButton.IsEnabled = true;
    }

    /// (the latest release is ahead of the one the folder holds)
    bool NewerOut() => latest != null && folder.Version is { } v && IsRelease(v) && Newer(latest, v);

    /// The Releases tab's rows (LaunchList.cs: the same rules as the WPF launcher), the fold row, Play and the foot
    void FillChoices()
    {
        var lists = Make(releases, latest, folder.Version, Array.Empty<string>(), folder.Rebuilds, null, olderOpen);
        // (one list, newest first: the pre-releases among the releases while Settings has them on, and always the one in the folder)
        var pre = PreTick.IsChecked == true;
        ReleaseList.ItemsSource = lists.Pre.Where(c => pre || c.Here || c.Picked).Concat(lists.Releases).OrderByDescending(c => c.Tag!, Order).ToList();
        FoldRow.IsVisible = lists.Older > 0;
        FoldText.Text = lists.Folded ? $"{lists.Older} older releases" : "Fewer releases";
        FoldMark.RenderTransform = new ScaleTransform(1, lists.Folded ? 1 : -1);   // (down while they are folded, up to fold them again)
        ReleasesNote.Text = checkedOnce && releases.Count == 0 ? "GitHub did not answer: only the versions on this PC are listed." : "";
        ReleasesNote.IsVisible = ReleasesNote.Text.Length > 0;
        Render(lists);
    }

    void Render(Lists lists)
    {
        GoText.Text = folder.HasGame ? "Play" : "Build";
        Version.Content = !folder.HasGame ? "No game yet"
            : lists.Picked is { Here: true } p ? p.Title : folder.Version is { } v ? (IsRelease(v) ? VersionName(v) : v) : "Your game";
        var available = NewerOut() && lists.All.Any(c => SameTag(c.Tag, latest) && c.Reach);
        MenuDot.IsVisible = NewerOut();
        CheckButton.IsVisible = !available;
        UpdateButton.IsVisible = available;
        FootLine.Text = available ? $"{VersionName(latest!)} Latest" : "";
        FootLine.IsVisible = available;
        FootNote.Text = available ? $"Rebuilds your game from your game files · {BuildTimes.About(typicalBuild)}" : "";
        FootNote.IsVisible = available;
    }

    // ---- what the buttons do ----

    void GoClick()
    {
        if (!folder.HasGame)
        {
            TabBuild.IsChecked = true;
            ShowMenu(true);
        }
        // (Play and the builds come next: this window shows them, the WPF launcher runs them for now)
    }

    /// Settings' "Lower memory use" as the game folder's hookshot_env.txt has it (GameEnv.cs), and the cache file's size under it
    void ShowLowMem()
    {
        var game = folder.Path.Length > 0 && Directory.Exists(folder.Path) ? new DirectoryInfo(folder.Path) : null;
        LowMemTick.IsEnabled = game != null;
        LowMemTick.IsChecked = game != null && GameEnv.SkipsCache(game);
        var bytes = game != null ? GameEnv.CacheBytes(game) : -1;
        LowMemNote.Text = game == null ? "" : bytes > 0
            ? $"Skips the {BuildTimes.Size(bytes)} pipeline cache file: less memory, may stutter more the first time."
            : "Skips the pipeline cache file the game makes as it plays: less memory, may stutter more the first time.";
        LowMemNote.IsVisible = LowMemNote.Text.Length > 0;
    }

    void LowMemClick()
    {
        try { GameEnv.SetSkip(new DirectoryInfo(folder.Path), LowMemTick.IsChecked == true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        ShowLowMem();   // (what the file says now, whether or not it could be written)
    }

    void LinkClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Tag: string page } || !page.StartsWith("https://")) return;
        try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); }
        catch (Exception) { }   // (no browser to open it)
    }
}

/// How long a build took on this PC lately (make_game.py's build_times.json, in the builder's folder: the middle of the last five), and
/// that in words, as the WPF launcher says it (Launcher.cs, AboutTime)
static class BuildTimes
{
    public static string Base => Environment.GetEnvironmentVariable("HOOKSHOT_SETUP_BASE") is { Length: > 0 } tried ? tried
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HookshotBuilder");

    public static double? Typical()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Base, "build_times.json")));
            var took = doc.RootElement.GetProperty("builds").EnumerateArray()
                          .Select(b => b.TryGetProperty("total_seconds", out var s) && s.TryGetDouble(out var v) ? v : 0)
                          .Where(v => v > 0).TakeLast(5).OrderBy(v => v).ToList();
            return took.Count > 0 ? took[took.Count / 2] : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public static string About(double? seconds)
    {
        if (seconds is not double t) return "around half an hour or more";
        var minutes = Math.Max(5, (int)Math.Round(t / 300) * 5);
        return minutes >= 60 ? $"about {minutes / 60} h {(minutes % 60 > 0 ? $"{minutes % 60} min" : "")}".TrimEnd() : $"about {minutes} minutes";
    }

    public static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" : $"{bytes >> 20} MB";
}
