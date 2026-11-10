using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using static HookshotBuilder.LaunchList;

namespace HookshotBuilder;

/// The launcher: Longshot.exe in a game folder opens the builder with --launch <folder> (builder/setup/setup.c). What its bar of
/// versions holds is decided in LaunchList.cs; this is the window around it
public partial class MainWindow
{
    static readonly Brush QuietInk = Frozen(0xBC, 0xBC, 0xBC), NewsInk = Frozen(0xEB, 0xB0, 0x7E);
    static Brush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }

    Lists? launchLists;                          // (the bar's lists, as last made)
    List<ReleaseInfo> launchReleases = new();    // (GitHub's releases, as last answered)
    string? launchBuilt;                         // (the release the launch folder holds)
    string? launchPick;                          // (the version picked: its key)
    bool launchChecking, launchChecked, versionsOpen, olderOpen;
    enum Go { Play, Bring, None }                // (Bring: the folder is updated or built again to the version picked first)
    Go launchGo;

    LaunchChoice? Picked => launchLists?.Picked;

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
            LauncherMax.Content = ((char)(full ? 0xE923 : 0xE922)).ToString();   // (Segoe Fluent Icons: restore, maximise)
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
        // (development) HOOKSHOT_UI_PREVIEW=versions,older,max,pick=release:v1.1.0 (any of them): the bar of versions open from the
        // start, its older releases unfolded, the window maximised, that version picked
        var preview = (Environment.GetEnvironmentVariable("HOOKSHOT_UI_PREVIEW") ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (preview.FirstOrDefault(p => p.StartsWith("pick=")) is { } pick) launchPick = pick[5..];
        olderOpen = preview.Contains("older");
        // (what is on this PC at once; GitHub's releases when they come)
        FillChoices();
        if (preview.Contains("versions")) ShowVersions(true, atOnce: true);
        if (preview.Contains("max")) WindowState = WindowState.Maximized;
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
        List<ReleaseInfo> found;
        string? latest;
        // (development) HOOKSHOT_LATEST_TAG=v1.2.0-rc.1,v1.1.0,v1.0.0?: the releases instead of GitHub's, newest first. One with
        // a part after its numbers is a pre-release, the first without is the latest, one ending in ? has no builder to
        // download; none: no answer
        if (Environment.GetEnvironmentVariable("HOOKSHOT_LATEST_TAG") is { Length: > 0 } fake)
        {
            found = fake == "none" ? new List<ReleaseInfo>()
                : fake.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(t => new ReleaseInfo(t.TrimEnd('?'), IsPre(t), !t.EndsWith('?'))).ToList();
            latest = found.FirstOrDefault(r => !r.Pre)?.Tag;
        }
        else
            (found, latest) = await Updater.Releases();
        // (no answer: the releases of the check before stay)
        if (found.Count > 0 || launchReleases.Count == 0) (launchReleases, latestTag) = (found, latest);
        updateTarget = latestTag ?? Tools.Find()?.Version;
        launchChecked = true;
        FillChoices();
        if (say)
        {
            LauncherCheck.Content = found.Count == 0 ? "Could not check" : NewerOut() ? VersionName(latestTag!) + " is out" : "Up to date";
            await Task.Delay(2400);
        }
        LauncherCheck.Content = "Check for updates";
        LauncherCheck.IsHitTestVisible = true;
        launchChecking = false;
    }

    async void LauncherCheckClick(object sender, RoutedEventArgs e) => await CheckReleases(say: true);

    /// (the latest release is ahead of the one this folder holds)
    bool NewerOut() => latestTag != null && launchBuilt != null && IsRelease(launchBuilt) && Newer(latestTag, launchBuilt);

    /// The bar's lists (LaunchList.cs), from GitHub's releases, this folder's, the builders installed here and the dev builds
    /// beside the folder. The version picked last time for this folder is picked again, else the one the folder holds
    void FillChoices()
    {
        launchLists = Make(launchReleases, latestTag, launchBuilt, InstalledTools().Keys, DevBuilds(launchFolder),
                           GameVersion(launchFolder) != null, launchPick, olderOpen);
        ReleaseList.ItemsSource = launchLists.Releases;
        PreList.ItemsSource = launchLists.Pre;
        DevList.ItemsSource = launchLists.Dev;
        ReleaseVersions.Visibility = launchLists.Releases.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PreVersions.Visibility = launchLists.Pre.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DevVersions.Visibility = launchLists.Dev.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FoldRow.Visibility = launchLists.Older > 0 ? Visibility.Visible : Visibility.Collapsed;
        FoldText.Text = launchLists.Folded ? $"{launchLists.Older} older releases" : "Fewer releases";
        FoldTurn.ScaleY = launchLists.Folded ? 1 : -1;   // (the arrow points down while they are folded, up to fold them again)
        ShowGo();
    }

    void LauncherRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LaunchChoice c) return;
        launchPick = c.Key;
        FillChoices();
    }

    void LauncherFoldClick(object sender, RoutedEventArgs e)
    {
        olderOpen = !olderOpen;
        FillChoices();
    }

    /// (the builder of a release, when it is installed here: its tools build the folder as that release with nothing downloaded)
    static Tools? BuilderHere(string tag)
    {
        foreach (var (version, tools) in InstalledTools())
            if (SameTag(version, tag)) return tools;
        return null;
    }

    /// What the button does for the version picked; its name under the button, and a line under that when there is one to say
    void ShowGo()
    {
        var c = Picked;
        var (text, news, verb) = ("", false, "Play");
        if (c == null)
        {
            launchGo = Go.None;
        }
        else if (c.Folder != null)
        {
            launchGo = File.Exists(GameExe(c.Folder)) ? Go.Play : Go.None;
            text = $"In {System.IO.Path.GetFileName(c.Folder)}, with its own saves and settings";
        }
        else if (c.Here)
        {
            launchGo = File.Exists(GameExe(launchFolder)) ? Go.Play : Go.None;
            if (NewerOut()) (text, news) = ($"{VersionName(latestTag!)} is out", true);
            else if (launchChecked && launchReleases.Count == 0) text = "Could not check for updates";
        }
        else if (c.Reach)
        {
            // (the folder holds one release at a time: another one takes its place. One behind it that has to be downloaded
            // comes with its own builder, which the setup makes the one installed)
            var ahead = launchBuilt == null || !IsRelease(launchBuilt) || Newer(c.Tag!, launchBuilt);
            launchGo = Go.Bring;
            verb = ahead ? "Update" : "Switch";
            var was = launchBuilt != null ? VersionName(launchBuilt) : "the game";
            text = ahead || BuilderHere(c.Tag!) != null
                ? $"Replaces {was} in this folder. Your saves and settings stay."
                : $"Goes back to {VersionName(c.Tag!)}, and Longshot Builder with it. Your saves and settings stay.";
        }
        else
        {
            launchGo = Go.None;
            text = GameVersion(launchFolder) == null
                ? "This folder was built before it kept its game files: build it again with the Longshot Builder"
                : $"{VersionName(c.Tag!)} is not on this PC, and its release has no builder to download";
        }
        LauncherGoText.Text = verb;
        LauncherGo.IsEnabled = launchGo != Go.None;
        LauncherVersion.Content = c?.Title ?? "No game in this folder";
        LauncherVersion.IsHitTestVisible = c != null;
        LauncherStatus.Text = text;
        LauncherStatus.Foreground = news ? NewsInk : QuietInk;
        LauncherAll.Tag = NewerOut() ? "news" : null;   // (the dot on All Versions: a newer release is in there)
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
            case Go.Bring when BuilderHere(c.Tag!) is { } tools:
                LauncherWindow(false);
                BeginUpdate(launchFolder, c.Tag);
                UpdateBuild(tools, launchFolder);
                break;
            case Go.Bring:
                // (the latest release as before; any other by its own tag: a pre-release, or one to go back to)
                LauncherWindow(false);
                Updater.UpdateFolder(this, launchFolder, c.Tag, fetched: false, exact: !SameTag(c.Tag, latestTag));
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
