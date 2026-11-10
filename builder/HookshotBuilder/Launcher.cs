using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace HookshotBuilder;

/// One version in the launcher's bar: a release (played in the launch folder, which is updated or built again to it first
/// when it holds another), or a dev build in a folder beside it (nx2/version.py names it "pre-1.1.0 6353e9b";
/// scripts/build-game.ps1 makes one and writes its hookshot_build.txt)
sealed class LaunchChoice
{
    static readonly Brush PickedInk = Frozen(0xEB, 0xB0, 0x7E), RowInk = Frozen(0xBC, 0xBC, 0xBC), FaintInk = Frozen(0x74, 0x74, 0x74);
    static Brush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    public string Label { get; init; } = "";
    public string? Tag { get; init; }      // (a release's tag, v1.0.0)
    public string? Folder { get; init; }   // (a dev build's folder)
    public string? Build { get; init; }    // (a dev build's name, "pre-1.1.0 6353e9b")
    public bool Here { get; init; }        // (on this PC: the release the launch folder holds, or a dev build)
    public bool Reach { get; init; }       // (not here, and the button brings it: the latest release, or one whose builder is installed)
    public bool Picked { get; set; }
    public string Key => Tag != null ? "release:" + Tag : "dev:" + Folder;

    // (the row, MainWindow.xaml's VersionRow: the tick is on the version picked when it is here, the arrow on one the button can bring)
    public Brush Ink => Picked ? PickedInk : Here || Reach ? RowInk : FaintInk;
    public Visibility Check => Picked && Here ? Visibility.Visible : Visibility.Collapsed;
    public Visibility Fetch => !Here && Reach ? Visibility.Visible : Visibility.Collapsed;
    public string? Tip => Folder != null ? Folder : Here ? null : Reach ? "Not on this PC yet" : "Not on this PC";
}

public partial class MainWindow
{
    readonly List<LaunchChoice> launchChoices = new();
    List<string> launchTags = new();   // (GitHub's releases, as last answered)
    string? launchBuilt;               // (the release the launch folder holds)
    string? launchPick;                // (the version picked: its key)
    bool launchChecking, launchChecked, versionsOpen;
    enum Go { Play, Update, Switch, None }
    Go launchGo;

    LaunchChoice? Picked => launchChoices.FirstOrDefault(c => c.Picked);

    async void ShowLauncher(string folder)
    {
        launchFolder = folder;
        OutPath.Text = folder;
        StepPill.Visibility = Visibility.Collapsed;
        UseOwnFont();
        LauncherWindow(true);
        StateChanged += (_, _) =>
        {
            var full = WindowState == WindowState.Maximized;
            Root.Margin = full ? Glass.Overhang(this) : new Thickness(0);
            LauncherMax.Content = full ? "" : "";
            LauncherMax.ToolTip = full ? "Restore" : "Maximize";
            System.Windows.Automation.AutomationProperties.SetName(LauncherMax, full ? "Restore" : "Maximize");
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !versionsOpen) return;
            ShowVersions(false);
            e.Handled = true;
        };
        launchBuilt = GameVersion(folder) ?? ReadVersion(folder);
        launchPick = LastChoice(folder);
        // (what is on this PC at once; GitHub's releases when they come)
        FillChoices();
        // (development) HOOKSHOT_UI_PREVIEW=versions: the bar of versions open from the start; max: the window maximised
        var preview = Environment.GetEnvironmentVariable("HOOKSHOT_UI_PREVIEW");
        if (preview == "versions") ShowVersions(true, atOnce: true);
        if (preview == "max") WindowState = WindowState.Maximized;
        await CheckReleases(say: false);
        // (development) HOOKSHOT_UI_SHOT=<file.png>: the window as drawn, once the releases are in, and out (the window opens away
        // from the screen and takes no keys, MainWindow's constructor)
        if (Environment.GetEnvironmentVariable("HOOKSHOT_UI_SHOT") is { Length: > 0 } shot)
        {
            await Task.Delay(500);
            var picture = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            picture.Render(this);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(picture));
            using (var file = File.Create(shot)) png.Save(file);
            Application.Current.Shutdown();
        }
    }

    /// The launcher's window: 960x640 under its own title bar, and it can be maximised. The installer's (an update or a switch the
    /// launcher starts goes on in it) keeps the size it has and its own buttons
    void LauncherWindow(bool on)
    {
        LauncherScreen.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        CaptionButtons.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        Main.Visibility = on ? Visibility.Hidden : Visibility.Visible;   // (the installer's pages under the launcher: out of the Tab key's way)
        if (on)
        {
            (MinWidth, MinHeight, Height) = (960, 640, 640);
            ResizeMode = ResizeMode.CanResize;
        }
        else
        {
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanMinimize;
        }
        if (WindowChrome.GetWindowChrome(this) is { IsFrozen: false } chrome) chrome.CaptionHeight = on ? 48 : 64;
    }

    /// GitHub's releases asked for (when the launcher opens, and by Check for updates, which then says what came of it)
    async Task CheckReleases(bool say)
    {
        if (launchChecking) return;
        launchChecking = true;
        LauncherCheck.IsHitTestVisible = false;
        LauncherCheck.Content = "Checking…";
        List<string> tags;
        string? latest;
        // (development) HOOKSHOT_LATEST_TAG=v1.1.0,v1.0.0: the releases, the latest first, instead of GitHub's; none: no answer
        if (Environment.GetEnvironmentVariable("HOOKSHOT_LATEST_TAG") is { Length: > 0 } fake)
        {
            tags = fake == "none" ? new List<string>() : fake.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            latest = tags.FirstOrDefault();
        }
        else
            (tags, latest) = await Updater.Releases();
        // (no answer: the releases of the check before stay)
        if (latest != null || latestTag == null) (launchTags, latestTag) = (tags, latest);
        updateTarget = latestTag ?? Tools.Find()?.Version;
        launchChecked = true;
        FillChoices();
        if (say)
        {
            LauncherCheck.Content = latest == null ? "Could not check" : SameTag(latest, launchBuilt) ? "Up to date" : VersionName(latest) + " is out";
            await Task.Delay(2400);
        }
        LauncherCheck.Content = "Check for updates";
        LauncherCheck.IsHitTestVisible = true;
        launchChecking = false;
    }

    async void LauncherCheckClick(object sender, RoutedEventArgs e) => await CheckReleases(say: true);

    /// The bar's three lists, from the releases (GitHub's, this folder's, the builders installed here), newest first, and the dev
    /// builds beside the folder. The version picked last time for this folder is picked again, else the one the folder holds
    void FillChoices()
    {
        var builders = InstalledTools();
        var all = new HashSet<string>(launchTags, StringComparer.OrdinalIgnoreCase);
        if (launchBuilt != null && IsRelease(launchBuilt)) all.Add(launchBuilt);
        foreach (var t in builders.Keys) all.Add(t);
        var rebuilds = GameVersion(launchFolder) != null;   // (the folder keeps its game files: it can be built as another release)
        launchChoices.Clear();
        foreach (var tag in all.OrderByDescending(ReleaseOrder))
        {
            var here = SameTag(tag, launchBuilt);
            launchChoices.Add(new LaunchChoice
            {
                Tag = tag,
                Label = VersionName(tag) + (SameTag(tag, latestTag) ? " - Latest" : ""),
                Here = here,
                Reach = !here && rebuilds && (SameTag(tag, latestTag) || builders.ContainsKey(tag)),
            });
        }
        // ("pre-1.1.0 6353e9b", nx2/version.py: dev is the next release before it is out; an older build said "dev 86148ce")
        foreach (var (dir, build) in DevBuilds(launchFolder))
            launchChoices.Add(new LaunchChoice { Folder = dir, Build = build, Label = build, Here = true });
        var pick = launchChoices.FirstOrDefault(c => c.Key == launchPick)
                   ?? launchChoices.FirstOrDefault(c => SameTag(c.Tag, launchBuilt))
                   ?? launchChoices.FirstOrDefault(c => c.Here)
                   ?? launchChoices.FirstOrDefault();
        if (pick != null) pick.Picked = true;

        // (the latest release, when the folder holds an older one, has its own list above what is here)
        var built = launchBuilt != null && IsRelease(launchBuilt) ? ReleaseOrder(launchBuilt) : null;
        var onPc = launchChoices.Where(c => c.Here).ToList();
        var fresh = launchChoices.Where(c => !c.Here && SameTag(c.Tag, latestTag) && (built == null || ReleaseOrder(c.Tag!) > built)).ToList();
        var other = launchChoices.Where(c => !c.Here && !fresh.Contains(c)).ToList();
        NewList.ItemsSource = fresh;
        HereList.ItemsSource = onPc;
        OtherList.ItemsSource = other;
        NewVersions.Visibility = fresh.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HereVersions.Visibility = onPc.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OtherVersions.Visibility = other.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OtherTitle.Text = built != null && other.Any(c => ReleaseOrder(c.Tag!) > built) ? "Other versions" : "Older versions";
        ShowGo();
    }

    void LauncherRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LaunchChoice c) return;
        launchPick = c.Key;
        FillChoices();
    }

    /// What the button does for the version picked; its name under the button, and a line under that when there is one to say
    void ShowGo()
    {
        var c = Picked;
        var text = "";
        if (c == null)
        {
            launchGo = Go.None;
        }
        else if (c.Folder != null)
        {
            launchGo = File.Exists(GameExe(c.Folder)) ? Go.Play : Go.None;
            text = $"In {System.IO.Path.GetFileName(c.Folder)}, with its own saves and settings";
        }
        else if (SameTag(c.Tag, launchBuilt))
        {
            launchGo = File.Exists(GameExe(launchFolder)) ? Go.Play : Go.None;
            text = latestTag == null ? (launchChecked ? "Could not check for updates" : "")
                 : SameTag(latestTag, launchBuilt) ? ""
                 : $"{VersionName(latestTag)} is out: it is in All Versions";
        }
        else if (SameTag(c.Tag, latestTag) && GameVersion(launchFolder) != null)
        {
            launchGo = Go.Update;
            text = $"Updates this game to {VersionName(c.Tag!)}. Your saves and settings stay.";
        }
        else if (GameVersion(launchFolder) != null && InstalledTools().ContainsKey(c.Tag!))
        {
            launchGo = Go.Switch;
            text = $"Builds this game again as {VersionName(c.Tag!)} (as long as an update). Your saves and settings stay.";
        }
        else
        {
            launchGo = Go.None;
            text = GameVersion(launchFolder) == null
                ? "This folder was built before it kept its game files: build it again with the Longshot Builder"
                : $"{VersionName(c.Tag!)} is not on this PC: only the latest release can be downloaded here";
        }
        LauncherGoText.Text = launchGo switch { Go.Update => "Update", Go.Switch => "Switch", _ => "Play" };
        LauncherGo.IsEnabled = launchGo != Go.None;
        LauncherVersion.Content = c?.Label ?? "No game in this folder";
        LauncherVersion.IsHitTestVisible = c != null;
        LauncherStatus.Text = text;
    }

    void LauncherGoClick(object sender, RoutedEventArgs e)
    {
        if (Picked is not { } c) return;
        SaveChoice(launchFolder, c.Key);
        switch (launchGo)
        {
            case Go.Play:
                StartGame(c.Folder ?? launchFolder);
                Application.Current.Shutdown();
                break;
            case Go.Update:
                LauncherWindow(false);
                Updater.UpdateFolder(this, launchFolder, c.Tag, fetched: false);
                break;
            case Go.Switch when InstalledTools().TryGetValue(c.Tag!, out var tools):
                LauncherWindow(false);
                BeginUpdate(launchFolder, c.Tag);
                UpdateBuild(tools, launchFolder);
                break;
        }
    }

    /// (the version under the button, pressed: the folder it is in)
    void LauncherFolderClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Picked?.Folder ?? launchFolder) { UseShellExecute = true });

    /// (GitHub and Discord: the page in the button's Tag, in the browser)
    void LauncherLinkClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string page) return;
        try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); }
        catch (Exception) { }   // (no browser to open it)
    }

    void LauncherMaxClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ---- All Versions: the bar from the left ----

    void LauncherAllClick(object sender, RoutedEventArgs e) => ShowVersions(true);

    /// (a press in the window that no button took: the bar closes)
    void LauncherBackClick(object sender, MouseButtonEventArgs e) => ShowVersions(false);

    /// (a press in the bar itself, between its rows: it stays)
    void LauncherBarClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    void ShowVersions(bool open, bool atOnce = false)
    {
        if (open == versionsOpen) return;
        versionsOpen = open;
        LauncherAll.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        if (open) VersionBar.Visibility = Visibility.Visible;
        if (atOnce) return;
        var time = TimeSpan.FromMilliseconds(open ? 200 : 150);
        var away = -Math.Max(240, VersionBar.ActualWidth);
        var slide = new DoubleAnimation(open ? away : 0, open ? 0 : away, time)
        {
            EasingFunction = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        // (opened again before it was away: it stays)
        if (!open) slide.Completed += (_, _) => { if (!versionsOpen) VersionBar.Visibility = Visibility.Collapsed; };
        VersionBarSlide.BeginAnimation(TranslateTransform.XProperty, slide);
        VersionBar.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 0 : 1, open ? 1 : 0, time));
    }

    /// (a font of the player's own, for the button's word: the first .otf or .ttf in LongshotBuilder\fonts. Never shipped with the
    /// builder (a commercial font such as FOT-Chiaro, which the launcher was drawn with, may not be passed on): Marcellus, OFL, is
    /// the launcher's own)
    void UseOwnFont()
    {
        try
        {
            var dir = System.IO.Path.Combine(Storage.Base, "fonts");
            var file = !Directory.Exists(dir) ? null
                : Directory.GetFiles(dir).Where(f => f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
                           .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (file != null)
            {
                var face = new GlyphTypeface(new Uri(file));
                var family = face.Win32FamilyNames.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out var en) ? en : face.Win32FamilyNames.Values.First();
                if (LauncherScreen.Child is FrameworkElement screen)
                    screen.Resources["Serif"] = new FontFamily(new Uri(dir.TrimEnd('\\') + "\\"), "./#" + family);
            }
        }
        catch (Exception) { }   // (a font that cannot be read: the launcher's own)
        CentreGoText();
    }

    /// The button's word with its capitals' middle on the button's middle, as it is drawn (a line of text is centred by its whole
    /// height, the room under the baseline too, which sets the word too high or low by a few pixels, differently in each font)
    void CentreGoText()
    {
        try
        {
            var family = LauncherGoText.FontFamily;
            var face = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var size = LauncherGoText.FontSize;
            LauncherGoNudge.Y = Math.Round(family.LineSpacing * size / 2 - family.Baseline * size + face.CapsHeight * size / 2);
        }
        catch (Exception) { }
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
    static string VersionName(string tag) => "v" + tag.TrimStart('v', 'V');   // (as the launcher writes a release: v1.0.0)

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
