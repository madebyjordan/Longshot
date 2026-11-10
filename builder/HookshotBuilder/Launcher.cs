using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HookshotBuilder;

/// One row of the launcher's version list: a release (played in the launch folder, which is updated or built again to it
/// first when it holds another), or a dev build in a folder beside it (nx2/version.py names it "dev 86148ce";
/// scripts/build-game.ps1 makes one and writes its hookshot_build.txt)
sealed class LaunchChoice
{
    public string Label { get; init; } = "";
    public string Badge { get; init; } = "";
    public Brush BadgeBrush { get; init; } = Brushes.Gray;
    public string? Tag { get; init; }      // (a release's tag, v1.0.0)
    public string? Folder { get; init; }   // (a dev build's folder)
    public string? Build { get; init; }    // (a dev build's name, "dev 86148ce")
    public string Key => Tag != null ? "release:" + Tag : "dev:" + Folder;
}

public partial class MainWindow
{
    static readonly Brush LatestInk = Frozen(0x8D, 0xB0, 0x62), DevInk = Frozen(0x8F, 0xB4, 0xE0), QuietInk = Frozen(0x9C, 0x94, 0x88);
    static Brush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    readonly List<LaunchChoice> launchChoices = new();
    string? launchBuilt;   // (the release the launch folder holds)
    enum Go { Play, Update, Switch, None }
    Go launchGo;

    async void ShowLauncher(string folder)
    {
        launchFolder = folder;
        OutPath.Text = folder;
        StepPill.Visibility = Visibility.Collapsed;
        UseOwnFont();
        LauncherScreen.Visibility = Visibility.Visible;
        LauncherScreen.Opacity = 0;
        LauncherScreen.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
        launchBuilt = GameVersion(folder) ?? ReadVersion(folder);
        // (what is on this PC at once; GitHub's releases when they come)
        FillChoices(new List<string>(), null);
        LauncherStatus.Text = "Checking for updates…";
        List<string> tags;
        if (Environment.GetEnvironmentVariable("HOOKSHOT_LATEST_TAG") is { Length: > 0 } fake)
            (tags, latestTag) = fake == "none" ? (new List<string>(), (string?)null) : (new List<string> { fake }, fake);
        else
            (tags, latestTag) = await Updater.Releases();
        updateTarget = latestTag ?? Tools.Find()?.Version;
        FillChoices(tags, latestTag);
    }

    /// The releases (GitHub's, this folder's, the builders installed here), newest first, then the dev builds beside the folder;
    /// the one picked last time for this folder is picked again, else the one the folder holds
    void FillChoices(List<string> tags, string? latest)
    {
        var all = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        if (launchBuilt != null && IsRelease(launchBuilt)) all.Add(launchBuilt);
        foreach (var t in InstalledTools().Keys) all.Add(t);
        launchChoices.Clear();
        foreach (var tag in all.OrderByDescending(ReleaseOrder))
            launchChoices.Add(new LaunchChoice
            {
                Tag = tag,
                Label = "Version " + tag.TrimStart('v', 'V'),
                Badge = SameTag(tag, latest) ? "Latest" : SameTag(tag, launchBuilt) ? "Installed" : "",
                BadgeBrush = SameTag(tag, latest) ? LatestInk : QuietInk,
            });
        foreach (var (dir, build) in DevBuilds(launchFolder))
            launchChoices.Add(new LaunchChoice
            {
                Folder = dir,
                Build = build,
                Label = "Dev build",
                Badge = build.StartsWith("dev ") ? build[4..] : build,
                BadgeBrush = DevInk,
            });
        var keep = (LauncherVersions.SelectedItem as LaunchChoice)?.Key ?? LastChoice(launchFolder);
        LauncherVersions.ItemsSource = null;
        LauncherVersions.ItemsSource = launchChoices;
        LauncherVersions.SelectedItem = launchChoices.FirstOrDefault(c => c.Key == keep)
                                        ?? launchChoices.FirstOrDefault(c => SameTag(c.Tag, launchBuilt))
                                        ?? launchChoices.FirstOrDefault();
        ShowGo();
    }

    void LauncherVersionChanged(object sender, SelectionChangedEventArgs e) => ShowGo();

    /// What the button does for the version picked, and the line under it says
    void ShowGo()
    {
        var c = LauncherVersions.SelectedItem as LaunchChoice;
        string text;
        if (c == null)
        {
            (launchGo, text) = (Go.None, "No game in this folder");
        }
        else if (c.Folder != null)
        {
            launchGo = File.Exists(GameExe(c.Folder)) ? Go.Play : Go.None;
            text = $"{c.Build} · {System.IO.Path.GetFileName(c.Folder)} (its own saves and settings)";
        }
        else if (SameTag(c.Tag, launchBuilt))
        {
            launchGo = File.Exists(GameExe(launchFolder)) ? Go.Play : Go.None;
            text = latestTag == null ? (LauncherStatus.Text == "Checking for updates…" ? LauncherStatus.Text : "Could not check for updates")
                 : SameTag(latestTag, launchBuilt) ? "Up to date"
                 : $"{latestTag} is out: pick it to update";
        }
        else if (SameTag(c.Tag, latestTag) && GameVersion(launchFolder) != null)
        {
            launchGo = Go.Update;
            text = $"Updates this game to {c.Tag}. Your saves and settings stay.";
        }
        else if (GameVersion(launchFolder) != null && InstalledTools().ContainsKey(c.Tag!))
        {
            launchGo = Go.Switch;
            text = $"Builds this game again as {c.Tag} (as long as an update). Your saves and settings stay.";
        }
        else
        {
            launchGo = Go.None;
            text = GameVersion(launchFolder) == null
                ? "This folder was built before it kept its game files: build it again with the Longshot Builder"
                : $"{c.Tag} is not on this PC: only the latest release can be downloaded here";
        }
        LauncherGoText.Text = launchGo switch { Go.Update => "Update", Go.Switch => "Switch", _ => "Play" };
        LauncherGo.IsEnabled = launchGo != Go.None;
        LauncherStatus.Text = text;
    }

    void LauncherGoClick(object sender, RoutedEventArgs e)
    {
        if (LauncherVersions.SelectedItem is not LaunchChoice c) return;
        SaveChoice(launchFolder, c.Key);
        switch (launchGo)
        {
            case Go.Play:
                StartGame(c.Folder ?? launchFolder);
                Application.Current.Shutdown();
                break;
            case Go.Update:
                LauncherScreen.Visibility = Visibility.Collapsed;
                Updater.UpdateFolder(this, launchFolder, c.Tag, fetched: false);
                break;
            case Go.Switch when InstalledTools().TryGetValue(c.Tag!, out var tools):
                LauncherScreen.Visibility = Visibility.Collapsed;
                BeginUpdate(launchFolder, c.Tag);
                UpdateBuild(tools, launchFolder);
                break;
        }
    }

    void LauncherFolderClick(object sender, RoutedEventArgs e)
    {
        var c = LauncherVersions.SelectedItem as LaunchChoice;
        Process.Start(new ProcessStartInfo(c?.Folder ?? launchFolder) { UseShellExecute = true });
    }

    /// (a font of the player's own, for the launcher's text: the first .otf or .ttf in LongshotBuilder\fonts. Never shipped with the
    /// builder (a commercial font such as FOT-Chiaro may not be passed on): Marcellus, OFL, is the launcher's own)
    void UseOwnFont()
    {
        try
        {
            var dir = System.IO.Path.Combine(Storage.Base, "fonts");
            if (!Directory.Exists(dir)) return;
            var file = Directory.GetFiles(dir).Where(f => f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (file == null) return;
            var face = new GlyphTypeface(new Uri(file));
            var family = face.Win32FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out var en) ? en : face.Win32FamilyNames.Values.First();
            if (LauncherScreen.Child is FrameworkElement screen)
                screen.Resources["Serif"] = new FontFamily(new Uri(dir.TrimEnd('\\') + "\\"), "./#" + family);
        }
        catch (Exception) { }   // (a font that cannot be read: the launcher's own)
    }

    // ---- what is on this PC ----

    /// The builders installed here (versions\<tag>, as the setup keeps them): each one's tools build a folder as that release
    static Dictionary<string, Tools> InstalledTools()
    {
        var found = new Dictionary<string, Tools>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var d in Directory.GetDirectories(System.IO.Path.Combine(Storage.Base, "versions")))
            {
                var tools = System.IO.Path.Combine(d, "HookshotBuilder", "tools");
                var py = System.IO.Path.Combine(tools, "python", "python.exe");
                var t = new Tools(tools, py);
                if (File.Exists(System.IO.Path.Combine(tools, "nx2", "make_game.py")) && File.Exists(py) && t.Version is { } v && IsRelease(v))
                    found[v] = t;
            }
        }
        catch (Exception) { }
        return found;
    }

    /// Dev builds in the folders beside this one: their hookshot_build.txt says channel=dev (scripts/build-game.ps1)
    static List<(string Dir, string Build)> DevBuilds(string folder)
    {
        var found = new List<(string, string)>();
        try
        {
            var parent = System.IO.Path.GetDirectoryName(folder);
            if (parent == null) return found;
            foreach (var d in Directory.GetDirectories(parent).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(d.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                var info = System.IO.Path.Combine(d, "hookshot_build.txt");
                if (!File.Exists(info) || !File.Exists(GameExe(d))) continue;
                string? name = null, channel = null;
                foreach (var line in File.ReadAllLines(info))
                {
                    if (line.StartsWith("name=")) name = line[5..].Trim();
                    else if (line.StartsWith("channel=")) channel = line[8..].Trim();
                }
                if (channel == "dev") found.Add((d, name ?? "dev"));
            }
        }
        catch (Exception) { }
        return found;
    }

    static bool IsRelease(string tag) => System.Text.RegularExpressions.Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$");
    static bool SameTag(string? a, string? b) => a != null && b != null && string.Equals(a.TrimStart('v'), b.TrimStart('v'), StringComparison.OrdinalIgnoreCase);
    static Version ReleaseOrder(string tag) => Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var v) ? v : new Version(0, 0);

    // ---- the choice, kept per game folder: launcher.json beside the versions ----

    static string ChoicesPath => System.IO.Path.Combine(Storage.Base, "launcher.json");

    static Dictionary<string, string> Choices()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ChoicesPath)) ?? new(); }
        catch (Exception) { return new(); }
    }

    static string? LastChoice(string folder) => Choices().GetValueOrDefault(folder.ToLowerInvariant());

    static void SaveChoice(string folder, string key)
    {
        try
        {
            var all = Choices();
            all[folder.ToLowerInvariant()] = key;
            Directory.CreateDirectory(Storage.Base);
            File.WriteAllText(ChoicesPath, JsonSerializer.Serialize(all));
        }
        catch (Exception) { }
    }
}
