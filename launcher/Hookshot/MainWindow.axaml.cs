using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HookshotBuilder;
using static HookshotBuilder.LaunchList;

namespace Hookshot.Launcher;

public partial class MainWindow : Window
{
    GameFolder folder;
    List<ReleaseInfo> releases = new();      // (GitHub's, as last answered)
    string? latest;                          // (its latest release's tag)
    bool checkedOnce, olderOpen, checking;
    double? typicalBuild;                    // (how long a build took on this PC lately: BuildTimes)
    Lists? lists;

    // ---- the build's choices (the Build tab), kept in builder-settings.json as the WPF builder keeps them ----
    string outPath;
    string? nup, romfs, exefs;
    (bool Ok, string Text)? sourceCheck;     // (the game file's version, as the builder's make_game.py --check says it; null: not known)
    bool sourceChecking;
    bool fsrLicense;

    // ---- the one thing under way, or the one that stopped ----
    Job? job;
    Job? stoppedJob;
    string? stoppedWhy;
    bool detailsShown;
    string? readyNote;                       // (under Build Game once, after a build: that the game is ready, and in how long)
    readonly string? updateTag;              // (the game's own offer: hookshot --update <folder> --tag <tag>, hos_update.c)
    readonly bool update;                    // (--update <folder>, with or without --tag: a 2.x Builder passes it on, docs/launcher-upgrade.md)

    /// (what the window was asked to show first: a page (tab=build|releases|settings; menu alone: Build), the older releases
    /// unfolded, pre-releases on, a made-up build or update in a state, nothing run: building[=0..1], stopped, updating[=0..1],
    /// updatestopped, and a made-up launcher update for the note: launcherupdate[=v0.1.0].
    /// Development: HOOKSHOT_UI_PREVIEW=tab=settings,older,pre,building=0.4)
    public sealed record Preview(bool Menu, string? Tab, bool Older, bool Pre, string? State = null, double At = 0.4, string? LauncherUpdate = null)
    {
        public static Preview FromEnvironment()
        {
            var p = (Environment.GetEnvironmentVariable("HOOKSHOT_UI_PREVIEW") ?? "").Split(',', StringSplitOptions.TrimEntries);
            var tab = p.FirstOrDefault(x => x.StartsWith("tab="))?[4..];
            var state = p.FirstOrDefault(x => x.Split('=')[0] is "building" or "stopped" or "updating" or "updatestopped");
            var at = state?.Split('=') is [_, var f] && double.TryParse(f, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 1) : 0.4;
            var first = state?.Split('=')[0] is "building" or "stopped";
            var launcher = p.FirstOrDefault(x => x.Split('=')[0] == "launcherupdate") is { } l ? (l.Contains('=') ? l.Split('=')[1] : "v0.1.0") : null;
            tab ??= first ? "build" : p.Contains("menu") ? "build" : null;
            return new(tab != null, tab, p.Contains("older"), p.Contains("pre"), state?.Split('=')[0], at, launcher);
        }
    }

#if HOTAVALONIA_ENABLE
    // ---- (development) live reload, Debug builds only (HotAvalonia, scripts/launcher/live.ps1) ----
    // A saved .axaml fills this window again without its constructor: what the constructor wired up and drew (the buttons'
    // handlers, Play's version line, Update beside it) stays with the controls it replaced. So a fresh window takes its place,
    // where this one was and as big, made as this one was (the same folder and preview), and this one closes

    Preview? livePreview;
    bool liveShown, liveReplaced;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        liveShown = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ContentProperty || !liveShown || liveReplaced || livePreview is not { } again) return;
        liveReplaced = true;
        Dispatcher.UIThread.Post(() =>
        {
            var fresh = new MainWindow(GameFolder.Read(folder.Path), again)
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Position = Position, Width = Width, Height = Height,
                WindowState = WindowState,
            };
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = fresh;
            fresh.Show();
            closeAsked = true;
            Close();
        });
    }
#endif

    /// (development) The made-up build or update a preview asks for (Preview.State): a Job never run, fed the lines a builder
    /// would say up to that point, so the window draws it as it would be. Closing does not ask about it
    void PreviewJob(Preview preview)
    {
        if (preview.State == null) return;
        var first = preview.State is "building" or "stopped";
        var tag = folder.Version is { } v && !first ? NextOf(v) : Environment.GetEnvironmentVariable("HOOKSHOT_LATEST_TAG")?.Split(',')[0].TrimEnd('?') ?? "v2.1.0";
        var j = new Job(tag, first, ahead: true, fetch: !first);
        // (the lines a builder would have said by then, so the step shown is the one at that point: an update's builder being
        // downloaded first, then the translating, the compile with its files and time, the swap, the shaders)
        var at = preview.At;
        var f = (double x) => x.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var make = first ? at : (at - 0.12) / 0.88;   // (make_game.py's share, after an update's download)
        var said = new List<string>();
        if (!first && at < 0.12) said.AddRange(new[] { "@@setup Downloading part 2 of 3 (210 MB)", "@@percent " + (int)(at / 0.12 * 300 % 100) });
        else
        {
            if (!first) said.Add("@@installed " + tag);
            if (make < 0.2) said.AddRange(new[] { "@@stage lift", "@@progress " + f(Math.Max(0, make - 0.05) / 0.15) });
            else if (make < 0.9)
            {
                var done = (make - 0.2) / 0.7;
                said.AddRange(new[] { "@@stage compile", "@@plan 120 640" });
                for (var i = 1; i <= (int)(done * 120); i++) said.Add($"@@file src_{i}.c 3 new");
                said.AddRange(new[] { "@@progress " + f(done), "@@eta " + (int)((1 - done) * 1500) });
            }
            else if (make < 0.92) said.Add("@@stage finish");
            else said.AddRange(new[] { "@@stage " + ShaderStep.Id, "@@progress " + f((make - 0.92) / 0.08) });
        }
        foreach (var line in said) j.Work.Line(line);
        closeAsked = true;
        if (preview.State is "building" or "updating") job = j;
        else (stoppedJob, stoppedWhy) = (j, "The build tools stopped: there is not enough space on drive C: (32 GB needed, 4 GB free).");
    }

    /// (a made-up next release for an update preview: the minor version up)
    static string NextOf(string version) =>
        System.Version.TryParse(version.TrimStart('v').Split('-')[0], out var x) ? $"v{x.Major}.{x.Minor + 1}.0" : "v9.9.0";

    /// (the releases are in, and the window is drawn as they are: what a screenshot waits for)
    public Task Ready { get; }

    public MainWindow() : this(GameFolder.Read(""), new Preview(false, null, false, false)) { }

    internal MainWindow(GameFolder folder, Preview preview, string? updateTag = null, bool update = false)
    {
        var saved = BuilderSettings.Load(Builders.SettingsPath) ?? new BuilderSettings();
        // (a portable home keeps them relative to itself: whole again here)
        (saved.Out, saved.Nup, saved.Romfs, saved.Exefs) =
            (Builders.LoadPath(saved.Out), Builders.LoadPath(saved.Nup), Builders.LoadPath(saved.Romfs), Builders.LoadPath(saved.Exefs));
        // (opened with no folder: the game built last, when it is still there)
        if (folder.Path.Length == 0 && saved.Out is { Length: > 0 } last && GameFolder.Read(last) is { HasGame: true } before) folder = before;
        this.folder = folder;
        this.updateTag = updateTag;
        this.update = update || updateTag != null;
        InitializeComponent();
#if HOTAVALONIA_ENABLE
        livePreview = preview;
#endif
        Chrome();
        olderOpen = preview.Older;
        typicalBuild = BuildTimes.Typical();

        // ---- the saved choices ----
        outPath = folder.Path.Length > 0 ? folder.Path
            : saved.Out is { Length: > 2 } o ? o : Builders.DefaultGame;
        if (saved.Romfs is { } r && saved.Exefs is { } x && Directory.Exists(r) && Directory.Exists(x)) (romfs, exefs) = (r, x);
        else if (saved.Nup is { } n && File.Exists(n)) nup = n;
        PrepareTick.IsChecked = !saved.PrepareLater;
        DlssTick.IsChecked = saved.Dlss;
        fsrLicense = saved.FsrLicense;
        FsrTick.IsChecked = saved.Fsr && saved.FsrLicense;
        PreTick.IsChecked = preview.Pre || (saved.Track is { } t && t != "release");
        LanguageBox.ItemsSource = Strings.All;
        LanguageBox.SelectedItem = SystemLanguage.Pick();
        WorkerBox.ItemsSource = BuildWorkers.Choices(Environment.ProcessorCount).ToList();
        WorkerBox.SelectedIndex = 0;
        var memory = BuildMemory.Choices(BuildMemory.TotalGb()).ToList();
        MemoryBox.ItemsSource = memory;
        MemoryBox.SelectedItem = BuildMemory.Saved(memory, saved.MemoryGb);

        // ---- what the buttons do ----
        foreach (var tab in new[] { NavPlay, TabBuild, TabSettings }) tab.IsCheckedChanged += (_, _) => ShowPage();
        FoldRow.Click += (_, _) => { olderOpen = !olderOpen; FillChoices(); };
        PreTick.Click += (_, _) => { SaveSettings(); FillChoices(); };
        PrepareTick.Click += (_, _) => SaveSettings();
        DlssTick.Click += async (_, _) => await DlssClick();
        FsrTick.Click += async (_, _) => await FsrClick();
        RebuildLink.Click += async (_, _) => { if (folder.Version is { } v && IsRelease(v)) await Bring(v, ask: true, rebuild: true); };
        MemoryBox.SelectionChanged += (_, _) => SaveSettings();
        LowMemTick.Click += (_, _) => LowMemClick();
        ShareButton.Click += async (_, _) => await ShareClick();
        CheckButton.Click += async (_, _) => await CheckReleases(say: true);
        UpdateButton.Click += async (_, _) => await NoteUpdateClick();
        Go.Click += (_, _) => GoClick();
        Version.Click += (_, _) => ShowReleases(!ReleasesLayer.IsVisible);
        ReleasesShade.PointerPressed += (_, _) => ShowReleases(false);
        OpenFolderLink.Click += (_, _) => { ShowReleases(false); OpenFolder(); };
        SourceChange.Click += (_, _) => SourceMenu();
        OutChange.Click += async (_, _) => await PickOut();
        BuildGame.Click += async (_, _) => await StartBuild();
        BuildStopped.Click += async (_, _) => await Retry();
        FootStopped.Click += async (_, _) => await Retry();
        BuildCancel.Click += (_, _) => job?.Cancel();
        FootCancel.Click += (_, _) => job?.Cancel();
        BuildDetails.Click += (_, _) => ShowDetails(!detailsShown);
        FootDetails.Click += (_, _) => ShowDetails(!detailsShown);
        BuildCopy.Click += async (_, _) => await CopyReport(BuildCopy);
        FootCopy.Click += async (_, _) => await CopyReport(FootCopy);
        NoteClose.Click += (_, _) => CloseNote();
        AddHandler(Button.ClickEvent, LinkClick);
        AddHandler(Button.ClickEvent, RowClick);
        AddHandler(DragDrop.DropEvent, Dropped);
        DragDrop.SetAllowDrop(this, true);
        KeyDown += KeyPressed;
        // (a controller moves through the window as the arrow keys and Tab do, Input/Gamepad.cs: B goes back, Start opens the menu)
        if (!Dialog.Headless && Gamepad.Start(this) is { } pad)
        {
            pad.Back += Back;
            pad.Menu += () => ShowMenu(!Bar.IsVisible);
            Closed += (_, _) => pad.Dispose();
        }
        Closing +=(_, e) => { if (job != null && !closeAsked) { e.Cancel = true; _ = AskClose(); } };

        // (no game yet: it opens on Build; with one, on Play, where an update shows in the note)
        launcherUpdate = preview.LauncherUpdate;
        if (preview.Tab is "build" or "settings") (preview.Tab == "build" ? TabBuild : TabSettings).IsChecked = true;
        else (folder.HasGame ? NavPlay : TabBuild).IsChecked = true;
        ShowReleases(preview.Tab == "releases");
        ShowPage();
        ShowLowMem();
        CheckSource();
        FillChoices();
        PreviewJob(preview);
        Ready = Start();
    }

    /// (the releases asked for; then the game's own offer, when it opened this: that release brought, as it was asked there.
    /// --update with no --tag, as the game's offer on a release track starts the Builder: the latest, only when it is newer)
    async Task Start()
    {
        await CheckReleases(say: false);
        if (update && folder.HasGame && job == null && (updateTag ?? (NewerOut() ? latest : null)) is { } to) await Bring(to, ask: false);
    }

    /// (development, Shot.cs: a button pressed with no screen, and how far the work under way is)
    internal Task Act(string what)
    {
        switch (what)
        {
            case "build": return StartBuild();
            case "update": return UpdateClick();
            case "details": ShowDetails(true); return Task.CompletedTask;
            case "cancel": job?.Cancel(); return Task.CompletedTask;
            case "closenote": CloseNote(); return Task.CompletedTask;
            case "share": return ShareClick();
            case "rebuild": return folder.Version is { } v ? Bring(v, ask: false, rebuild: true) : Task.CompletedTask;
            default: return what.StartsWith("row:") ? Bring(what[4..], ask: false) : Task.CompletedTask;   // (a release's row)
        }
    }

    internal double? Working => job?.Work.Fraction;

    // ---- the window: its own buttons at the top right, dragged by its top ----

    void Chrome()
    {
        // (Windows: the system's frame kept (its shadow, snapping, resizing) with nothing drawn of its title bar. Elsewhere none:
        // the window draws its own top as on Windows)
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
        }
        else SystemDecorations = SystemDecorations.BorderOnly;
        MinButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaxButton.Click += (_, _) => ToggleMax();
        CloseButton.Click += (_, _) => Close();
        PointerPressed += (_, e) =>
        {
            if (e.GetPosition(this).Y >= 48 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleMax();
            else BeginMoveDrag(e);
        };
    }

    void ToggleMax() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    bool closeAsked;

    /// (closed while something is under way: asked, and that is stopped first)
    async Task AskClose()
    {
        var what = job?.FirstBuild == true ? "build" : "update";
        if (!await Dialog.Ask(this, "Hookshot", $"Stop the {what} and close? Nothing in your game folder is changed until it is done.", "Stop and close", "Keep going"))
            return;
        job?.Cancel();
        closeAsked = true;
        Close();
    }

    void KeyPressed(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (Bar.IsVisible || ReleasesLayer.IsVisible || detailsShown))
        {
            Back();
            e.Handled = true;
        }
    }

    /// (Escape, or a controller's B: the releases dropdown closed, else the details put away, else the panel closed)
    void Back()
    {
        if (ReleasesLayer.IsVisible) ShowReleases(false);
        else if (detailsShown) ShowDetails(false);
        else if (Bar.IsVisible) ShowMenu(false);
    }

    // ---- the pages: Play, and Build, Releases or Settings in the panel beside the words ----

    /// (open: a page in the panel, Build when Play was shown; closed: back to Play)
    void ShowMenu(bool open)
    {
        if (!open) NavPlay.IsChecked = true;
        else if (NavPlay.IsChecked == true) TabBuild.IsChecked = true;
    }

    void ShowPage()
    {
        var was = Bar.IsVisible;
        BuildPage.IsVisible = TabBuild.IsChecked == true;
        SettingsPage.IsVisible = TabSettings.IsChecked == true;
        var open = BuildPage.IsVisible || SettingsPage.IsVisible;
        Bar.IsVisible = open;
        BarTitle.Text = BuildPage.IsVisible ? "Build" : "Settings";
        if (open) ShowReleases(false);
        // (the Play screen gives the panel its place; Credits, the note and the words stay)
        PlayScreen.IsVisible = !open;
        if (!open && job is not { FirstBuild: false } && stoppedJob is not { FirstBuild: false }) ShowDetails(false);
        if (was == open || !IsLoaded) return;
        // (the keyboard's or the controller's place follows: onto the page's word, back to Play when it closes)
        var tab = new[] { TabBuild, TabSettings }.FirstOrDefault(t => t.IsChecked == true) ?? TabBuild;
        Dispatcher.UIThread.Post(() => (open ? (Control)tab : Go.IsEnabled ? Go : NavPlay).Focus());
    }

    /// The releases dropdown, opening upwards from the version under Play (closed by a click elsewhere, a version picked, the
    /// version pressed again, Escape)
    void ShowReleases(bool open)
    {
        if (open && !PlayScreen.IsVisible) return;
        var was = ReleasesLayer.IsVisible;
        ReleasesLayer.IsVisible = open;
        if (was == open || !IsLoaded) return;
        // (the keyboard's or the controller's place: onto the list's first version, back to the version when it closes)
        Dispatcher.UIThread.Post(() =>
        {
            if (!open) Version.Focus();
            else if (ReleasesDrop.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.IsEnabled) is { } first) first.Focus();
        });
    }

    // ---- the versions ----

    /// GitHub's releases asked for: when the window opens, and by Check for updates, which then says what came of it
    async Task CheckReleases(bool say)
    {
        checking = true;
        CheckButton.Content = "Checking…";
        Render();
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
        checking = false;
        Render();
    }

    /// (the latest release is ahead of the one the folder holds)
    bool NewerOut() => latest != null && folder.Version is { } v && IsRelease(v) && Newer(latest, v);

    /// The Releases tab's rows (LaunchList.cs: the same rules as the WPF launcher), the fold row, Play and the foot
    void FillChoices()
    {
        typicalBuild = BuildTimes.Typical();
        var installed = Builders.Installed().Keys;
        lists = Make(releases, latest, folder.Version, installed, folder.Rebuilds, null, olderOpen);
        if (job is { FirstBuild: false } j)
            foreach (var c in lists.All) c.Busy = SameTag(c.Tag, j.Tag);
        // (three lists, newest first: the releases; and, while Settings has pre-releases on, the release candidates and the
        // nightly. The one in the folder, picked or being brought shows whatever the setting)
        var pre = PreTick.IsChecked == true;
        ReleaseList.ItemsSource = lists.Releases.OrderByDescending(c => c.Tag!, Order).ToList();
        var pres = lists.Pre.Where(c => pre || c.Here || c.Picked || c.Busy).OrderByDescending(c => c.Tag!, Order).ToList();
        PreList.ItemsSource = pres.Where(c => !IsNightly(c.Tag)).ToList();
        NightlyList.ItemsSource = pres.Where(c => IsNightly(c.Tag)).ToList();
        PreHead.IsVisible = pres.Any(c => !IsNightly(c.Tag));
        NightlyHead.IsVisible = pres.Any(c => IsNightly(c.Tag));
        FoldRow.IsVisible = lists.Older > 0;
        FoldText.Text = lists.Folded ? $"{lists.Older} older releases" : "Fewer releases";
        FoldMark.RenderTransform = new ScaleTransform(1, lists.Folded ? 1 : -1);   // (down while they are folded, up to fold them again)
        ReleasesNote.Text = checkedOnce && releases.Count == 0 ? "GitHub did not answer: only the versions on this PC are listed." : "";
        ReleasesNote.IsVisible = ReleasesNote.Text.Length > 0;
        Render();
    }

    /// (a nightly's version: git describe's v2.2.0-4-g1a2b3c4, the setup's nightly_tag and Updater.TrackOf's rule)
    static bool IsNightly(string? tag) => tag != null && System.Text.RegularExpressions.Regex.IsMatch(tag, @"-\d+-g[0-9a-f]+(-dirty)?$");

    /// A release's row pressed: one not here is brought into the folder (asked first)
    async void RowClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Tag: LaunchChoice c } || job != null) return;
        ShowReleases(false);
        // (the one in the folder: built again with the settings now, asked first; a folder without its game files cannot be)
        if (c.Here && c.Rebuilds && folder.HasGame && c.Tag != null) await Bring(c.Tag, ask: true, rebuild: true);
        else if (!c.Here && c.Reach && c.Tag != null) await Bring(c.Tag, ask: true);
    }

    /// (the launcher's own update, newer than this one: none is looked for yet, a preview makes one up, launcherupdate)
    string? launcherUpdate;

    /// (a release's page on GitHub: "see what's new")
    static string ReleasePage(string tag) => "https://github.com/NotRexed/Hookshot/releases/tag/" + Uri.EscapeDataString(tag);

    /// The note's button: the Hookshot release brought (a rebuild, asked first); else the launcher's own update, whose page opens
    /// (the launcher does not update itself yet)
    async Task NoteUpdateClick()
    {
        if (Available) await UpdateClick();
        else if (launcherUpdate != null)
            try { Process.Start(new ProcessStartInfo(ReleasePage(launcherUpdate)) { UseShellExecute = true }); }
            catch (Exception) { }
    }

    async Task UpdateClick()
    {
        if (latest != null && lists?.All.FirstOrDefault(c => SameTag(c.Tag, latest)) is { Reach: true }) await Bring(latest, ask: true);
    }

    /// (Update Available: no update or check under way, a newer release is out, and it can be brought)
    bool Available => job == null && stoppedJob == null && !checking && NewerOut() && lists?.All.Any(c => SameTag(c.Tag, latest) && c.Reach) == true;

    // ---- the window from what is going on ----

    /// Play and the version under it, Update beside it; the Build tab (Build Game, the bar of the build under way, the red button
    /// of one that stopped); the foot (Check for updates, Update Available, the bar of an update under way, the red button of
    /// one that stopped). Called after anything changes
    void Render()
    {
        var busy = job != null;
        var buildWorking = job is { FirstBuild: true };
        var buildStopped = stoppedJob is { FirstBuild: true };
        var footWorking = job is { FirstBuild: false };
        var footStopped = stoppedJob is { FirstBuild: false };

        // ---- Play ----
        GoText.Text = folder.HasGame ? "Play" : "Build";
        Go.IsEnabled = !busy;
        Version.Content = !folder.HasGame ? "No game yet"
            : lists?.Picked is { Here: true } p ? p.Title : folder.Version is { } v ? (IsRelease(v) ? VersionName(v) : v) : "Your game";
        Version.IsHitTestVisible = folder.HasGame;
        var available = Available;

        // ---- Build ----
        var idle = !buildWorking && !buildStopped;
        BuildGame.IsVisible = idle;
        BuildGame.IsEnabled = !busy && SourceReady && outPath.Length > 2 && BuildTag() != null;
        BuildBar.IsVisible = buildWorking;
        BuildStopped.IsVisible = buildStopped;
        BuildOptions.IsVisible = idle;
        BuildLinks.IsVisible = !idle;
        BuildCancel.IsVisible = buildWorking;
        BuildCopy.IsVisible = buildStopped;
        SourceChange.IsEnabled = OutChange.IsEnabled = !busy;
        SourceText.Text = romfs != null ? "romfs and exefs folders" : nup != null ? Path.GetFileName(nup) : "Browse files";
        ToolTip.SetTip(SourceChange, romfs != null ? $"{romfs}\n{exefs}" : nup);
        OutText.Text = outPath.Length > 0 ? outPath : "Choose a folder";
        ToolTip.SetTip(OutChange, outPath);
        var (line, bad) = buildWorking ? (NowLine(job!), false) : idle ? IdleLine() : ("", false);
        BuildLine.Text = line;
        BuildLine.Foreground = new SolidColorBrush(Color.Parse(bad ? "#FF8373" : "#B8B8B8"));
        BuildLine.IsVisible = line.Length > 0;
        // (why it stopped, in a few words; the whole of it under the pointer, in Show details and in the error log)
        BuildWhy.Text = buildStopped ? LaunchWork.Brief(stoppedWhy!) : "";
        ToolTip.SetTip(BuildWhy, buildStopped ? stoppedWhy : null);
        BuildWhy.IsVisible = buildStopped;
        if (buildWorking) SetBar(BuildFill, BuildBarText, job!.Work.Fraction, $"Building... {job!.Work.Percent}");

        // ---- the note at the bottom left: a Hookshot release out (Update to it), the launcher's own update, one under way (its bar,
        // step and time left, Cancel), one that stopped (why, Try again, the details) ----
        CheckButton.IsEnabled = !busy && !checking;
        var launcherOut = launcherUpdate != null && !available && !footWorking && !footStopped && !SameTag(closedLauncher, launcherUpdate);
        // (a release out, unless its note was closed: the note only; it can still be brought from Releases)
        available = available && !SameTag(closedRelease, latest);
        Note.IsVisible = available || footWorking || footStopped || launcherOut;
        NoteClose.IsVisible = !footWorking;
        NoteTitle.Text = footWorking ? job!.Work.Title[..job.Work.Title.LastIndexOf(" - ", StringComparison.Ordinal)]
            : footStopped ? stoppedJob!.Work.StoppedShort
            : available ? $"Hookshot {VersionName(latest!)} is out!"
            : launcherOut ? "Launcher update available!" : "";
        UpdateButton.IsVisible = available || launcherOut;
        UpdateButton.IsEnabled = !busy;
        UpdateButton.Content = available ? $"Update to {VersionName(latest!)}" : launcherOut ? $"Update to {VersionName(launcherUpdate!)}" : "Update";
        ToolTip.SetTip(UpdateButton, available
            ? $"Your game is rebuilt from your game files with {VersionName(latest!)}'s tools ({BuildTimes.About(typicalBuild)}). Your saves and settings stay."
            : launcherOut ? "Opens the release's page, where the new launcher is" : null);
        NoteLink.IsVisible = available || launcherOut;
        NoteLink.Tag = available ? ReleasePage(latest!) : launcherOut ? ReleasePage(launcherUpdate!) : null;
        FootBar.IsVisible = footWorking;
        FootStopped.IsVisible = footStopped;
        FootCancel.IsVisible = footWorking;
        FootLinks.IsVisible = footStopped;
        // (while it runs, what it is doing now and, once it can be told, the time left)
        FootNote.Text = footWorking ? NowLine(job!) : "";
        FootNote.IsVisible = FootNote.Text.Length > 0;
        // (why it stopped, in a few words; the whole of it, and that the game folder is as it was, under the pointer, in Show
        // details and in the error log)
        FootWhy.Text = footStopped ? LaunchWork.Brief(stoppedWhy!) : "";
        ToolTip.SetTip(FootWhy, footStopped ? stoppedWhy!.TrimEnd('.') + ". Nothing was changed in your game folder." : null);
        FootWhy.IsVisible = footStopped;
        if (footWorking) SetBar(FootFill, FootBarText, job!.Work.Fraction, job.Work.Percent);
        BuildDetails.Content = FootDetails.Content = detailsShown ? "Hide details" : "Show details";
    }

    /// (under a bar: what is going on now, and the time left once it can be told: "Compiling 7 of 516 files · about 3 minutes left")
    string NowLine(Job j) => j.Work.Left(typicalBuild, (DateTime.Now - j.Started).TotalSeconds) is { } left ? $"{j.Work.Now} · {left}" : j.Work.Now;

    static void SetBar(Border fill, TextBlock text, double f, string words)
    {
        var width = (fill.Parent as Control)?.Bounds.Width is > 0 and var w ? w : 235;
        fill.Width = Math.Round(width * Math.Clamp(f, 0, 1));
        text.Text = words;
    }

    // ---- Play ----

    void GoClick()
    {
        if (job != null) return;
        if (!folder.HasGame)
        {
            TabBuild.IsChecked = true;
            ShowMenu(true);
            return;
        }
        try
        {
            Builders.Play(folder.Path, folder.Version is { } v && Builders.Installed().TryGetValue(v, out var b) ? b : Builders.Newest(Builders.Installed()));
            Close();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _ = Dialog.Tell(this, "Hookshot", $"The game could not be started: {e.Message.TrimEnd('.')}. Your antivirus may have removed it.");
        }
    }

    void OpenFolder()
    {
        if (!folder.HasGame) return;
        try { Process.Start(new ProcessStartInfo(folder.Path) { UseShellExecute = true }); }
        catch (Exception) { }   // (no file manager to open it)
    }

    // ---- the build's choices ----

    /// (the game files are ready to build from: the folders, or a file that is the supported version, or one not checked as no
    /// builder is here yet to check it: the build checks it first)
    bool SourceReady => romfs != null && exefs != null || nup != null && !sourceChecking && sourceCheck is not { Ok: false };

    /// The release a first build is made with: the latest (the newest pre-release while Settings has them on), else with GitHub
    /// silent the newest builder here. Null: none to build with
    string? BuildTag()
    {
        var pre = PreTick.IsChecked == true;
        var newest = releases.Where(r => r.Download && (pre || !r.Pre)).Select(r => r.Tag).OrderByDescending(t => t, Order).FirstOrDefault();
        return newest ?? Builders.Newest(Builders.Installed())?.Tag;
    }

    /// The line under Build Game while nothing is under way: the game just built (once); the files being checked or why they
    /// cannot be used; else the version, how long, and how much room, in red when the drive has not got it
    (string, bool) IdleLine()
    {
        if (readyNote != null) return (readyNote, false);
        if (nup == null && romfs == null) return ("Choose your game file to build", false);
        if (sourceChecking) return ("Checking that it is the supported version…", false);
        if (sourceCheck is { Ok: false } c) return (c.Text, true);
        if (BuildTag() is not { } tag) return (checkedOnce ? "GitHub did not answer, and no builder is on this PC to build with. Try again soon." : "Looking for the latest release…", checkedOnce);
        var about = $"Builds {VersionName(tag)} · about 45 minutes · needs 45 GB";
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(outPath))!;
            var free = new DriveInfo(root).AvailableFreeSpace;
            return ($"{about}, {root.TrimEnd('\\', '/')} has {free >> 30} GB free", free < 45L << 30);
        }
        catch (Exception) { return (about, false); }
    }

    void SourceMenu()
    {
        var file = new MenuItem { Header = "Browse for a .nup file…" };
        file.Click += async (_, _) => await PickNup();
        var dirs = new MenuItem { Header = "Use romfs and exefs folders…" };
        dirs.Click += async (_, _) => await PickFolders();
        var menu = new ContextMenu { ItemsSource = new[] { file, dirs }, Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Open(SourceChange);
    }

    async Task PickNup()
    {
        var got = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose your game file",
            FileTypeFilter = new[] { new FilePickerFileType("Game file (*.nup)") { Patterns = new[] { "*.nup" } } },
        });
        if (got.FirstOrDefault()?.TryGetLocalPath() is { } path) UseNup(path);
    }

    async Task<string?> PickFolder(string title)
    {
        var got = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return got.FirstOrDefault()?.TryGetLocalPath();
    }

    /// The other way in: an unpacked game, its romfs and exefs folders (or one folder holding both)
    async Task PickFolders()
    {
        if (await PickFolder("Choose the folder with romfs and exefs (or the romfs folder)") is not { } both) return;
        if (Directory.Exists(Path.Combine(both, "romfs")) && Directory.Exists(Path.Combine(both, "exefs")))
        {
            await UseFolders(Path.Combine(both, "romfs"), Path.Combine(both, "exefs"));
            return;
        }
        if (await PickFolder("Now choose the exefs folder") is { } ex) await UseFolders(both, ex);
    }

    async Task UseFolders(string r, string x)
    {
        if (!File.Exists(Path.Combine(x, "main")))
        {
            await Dialog.Tell(this, "Hookshot", "That exefs folder has no file named main.");
            return;
        }
        (romfs, exefs, nup, sourceCheck, readyNote) = (r, x, null, null, null);
        SaveSettings();
        Render();
    }

    void UseNup(string path)
    {
        (nup, romfs, exefs, readyNote) = (Path.GetFullPath(path), null, null, null);
        SaveSettings();
        CheckSource();
    }

    async Task PickOut()
    {
        if (await PickFolder("Choose where the game goes") is not { } picked) return;
        outPath = picked;
        readyNote = null;
        SaveSettings();
        Render();
    }

    /// Game files dropped on the window: a build from them, the menu's Build tab open
    async void Dropped(object? sender, DragEventArgs e)
    {
        if (job != null || e.DataTransfer.TryGetFiles() is not { } items) return;
        TabBuild.IsChecked = true;
        ShowMenu(true);
        foreach (var path in items.Select(i => i.TryGetLocalPath()).OfType<string>())
        {
            if (path.EndsWith(".nup", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) UseNup(path);
            else if (Directory.Exists(Path.Combine(path, "romfs")) && Directory.Exists(Path.Combine(path, "exefs")))
                await UseFolders(Path.Combine(path, "romfs"), Path.Combine(path, "exefs"));
        }
    }

    /// The game file's version, as the newest builder here reads it (make_game.py --check). With no builder here yet, unchecked:
    /// the build checks it first
    async void CheckSource()
    {
        sourceCheck = null;
        Render();
        if (nup == null || Builders.Newest(Builders.Installed()) is not { } builder) return;
        var checkedFile = nup;
        sourceChecking = true;
        Render();
        var said = await Check(builder, checkedFile);
        if (checkedFile != nup) return;   // (another file chosen meanwhile: its own check says)
        sourceChecking = false;
        sourceCheck = said;
        Render();
    }

    static async Task<(bool Ok, string Text)> Check(Builder builder, string file)
    {
        try
        {
            using var p = Process.Start(Builders.MakeGame(builder, new[] { "--check", "--nup", file }))!;
            string? lastError = null;
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) lastError = e.Data; };
            p.BeginErrorReadLine();
            (bool, string)? result = null;
            while (await p.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("@@check ok ")) result = (true, line[11..]);
                else if (line.StartsWith("@@check bad ")) result = (false, line[12..]);
                else if (line.StartsWith("@@error ")) result = (false, line[8..]);
            }
            await p.WaitForExitAsync();
            return result ?? (false, lastError ?? $"The game file could not be read (exit code {p.ExitCode}).");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (false, "The builder could not be started to check the game file: " + e.Message);
        }
    }

    // ---- Settings ----

    void SaveSettings()
    {
        var s = BuilderSettings.Load(Builders.SettingsPath) ?? new BuilderSettings();
        s.Out = Builders.SavePath(outPath);
        (s.Nup, s.Romfs, s.Exefs) = (Builders.SavePath(nup), Builders.SavePath(romfs), Builders.SavePath(exefs));
        s.Dlss = DlssTick.IsChecked == true;
        s.Fsr = FsrTick.IsChecked == true;
        s.FsrLicense = fsrLicense;
        s.PrepareLater = PrepareTick.IsChecked != true;
        try { s.MemoryGb = BuildMemory.Selected(MemoryBox.SelectedItem); } catch (ArgumentException) { }
        s.Track = PreTick.IsChecked == true ? (s.Track is "nightly" ? "nightly" : "rc") : "release";
        try
        {
            Directory.CreateDirectory(Builders.Base);
            File.WriteAllText(Builders.SettingsPath, s.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // (kept for this window only)
    }

    const string DlssLicense =
        "NVIDIA DLSS is NVIDIA's, not Hookshot's. With it on, the build downloads NVIDIA's DLSS SDK (release v310.9.16) from " +
        "github.com/NVIDIA/DLSS, under NVIDIA's own license, the NVIDIA RTX SDKs license:\n\n" +
        "https://github.com/NVIDIA/DLSS/blob/9981c6350dc58c0ce21f024fd65e31e851c31689/LICENSE.txt\n\n" +
        "Its files stay on this PC (in the builder's ngx-sdk folder, and nvngx_dlss.dll in the game folder's ngx folder). " +
        "Hookshot never includes or shares them: never share a game folder built with them.\n\n" +
        "Do you accept NVIDIA's license?";

    const string FsrLicense =
        "AMD FSR 4 is AMD's, not Hookshot's. With it on, the build downloads AMD's FSR runtime for Vulkan (FidelityFX SDK " +
        "v2.4.0: amd_fidelityfx_loader_vk.dll and amd_fidelityfx_upscaler_vk.dll, signed by AMD) from " +
        "github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK, each file checked against its SHA-256, under AMD's FSR SDK license:\n\n" +
        "https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/blob/7539ae8b6568ad29ed78a753404351daf3b5cca7/Kits/FidelityFX/docs/license.md\n\n" +
        "Its files stay on this PC (in the builder's ffx-sdk folder, and the two DLLs with LICENSE-AMD-FSR-SDK.md in the game " +
        "folder).\n\nDo you accept AMD's license?";

    /// The DLSS switch: NVIDIA's license first, and it stays on only when that is accepted
    /// Settings' note when DLSS and FSR 4 no longer say how the game in the folder was built (FSR 4 by make_game.py's mark, DLSS by
    /// NVIDIA's library in its ngx folder), with Rebuild beside it. (Turning DLSS off is not one: NVIDIA's library stays, and the
    /// game's Upscaler menu chooses whether it is used)
    void ShowRebuildNote()
    {
        string? why = null;
        if (folder.HasGame && folder.Rebuilds && folder.Version is { } v && IsRelease(v))
        {
            var fsr = FsrTick.IsChecked == true && fsrLicense;
            // (in the game when built with it, the mark, and AMD's runtime beside it to load)
            var builtFsr = File.Exists(Path.Combine(folder.Path, ".hookshot_fsr")) && File.Exists(Path.Combine(folder.Path, "amd_fidelityfx_upscaler_vk.dll"));
            var ngx = Path.Combine(folder.Path, "ngx");
            var builtDlss = Directory.Exists(ngx) && Directory.EnumerateFiles(ngx).Any(f => Path.GetFileName(f) is "nvngx_dlss.dll" ||
                                                                                           Path.GetFileName(f).StartsWith("libnvidia-ngx-dlss.so"));
            if (fsr && !builtFsr) why = "Built without FSR 4";
            else if (!fsr && builtFsr) why = "Built with FSR 4";
            else if (DlssTick.IsChecked == true && !builtDlss) why = "Built without DLSS";
        }
        RebuildNote.Text = why ?? "";
        ToolTip.SetTip(RebuildNote, why == null ? null
            : $"{why}. Rebuild builds your game again from its game files with your settings now, {BuildTimes.About(typicalBuild)} on this PC. Your saves stay.");
        RebuildRow.IsVisible = why != null;
        RebuildLink.IsEnabled = job == null;
    }

    async Task DlssClick()
    {
        if (DlssTick.IsChecked == true && !await Dialog.Ask(this, "NVIDIA DLSS", DlssLicense, "Accept", "Decline")) DlssTick.IsChecked = false;
        SaveSettings();
        ShowRebuildNote();
    }

    /// The FSR 4 switch: AMD's license first, the same
    async Task FsrClick()
    {
        if (FsrTick.IsChecked == true)
        {
            fsrLicense = await Dialog.Ask(this, "AMD FSR 4", FsrLicense, "Accept", "Decline");
            if (!fsrLicense) FsrTick.IsChecked = false;
        }
        SaveSettings();
        ShowRebuildNote();
    }

    /// Settings' "Lower memory use" as the game folder's hookshot_env.txt has it (GameEnv.cs), and the cache file's size under it
    void ShowLowMem()
    {
        var game = folder.HasGame ? new DirectoryInfo(folder.Path) : null;
        LowMemTick.IsEnabled = game != null;
        LowMemTick.IsChecked = game != null && GameEnv.SkipsCache(game);
        var bytes = game != null ? GameEnv.CacheBytes(game) : -1;
        LowMemNote.Text = game == null ? "" : bytes > 0
            ? $"Skips the {BuildTimes.Size(bytes)} pipeline cache file: less memory, may stutter more the first time."
            : "Skips the pipeline cache file the game makes as it plays: less memory, may stutter more the first time.";
        LowMemNote.IsVisible = LowMemNote.Text.Length > 0;
        ShareButton.IsEnabled = game != null;
        ShowRebuildNote();
    }

    /// Save my pipeline list: the zip in Downloads, the report copied, its folder and the issue opened. Nothing is uploaded
    async Task ShareClick()
    {
        var installed = Builders.Installed();
        var builder = folder.Version is { } v && installed.TryGetValue(v, out var own) ? own : Builders.Newest(installed);
        if (!folder.HasGame || builder == null)
        {
            ShareNote.Text = "There is no builder on this PC to save it with yet.";
            return;
        }
        ShareButton.IsEnabled = false;
        ShareNote.Text = "Saving…";
        var (zip, report, count, error) = await Builders.SharePipelines(builder, folder.Path);
        ShareButton.IsEnabled = true;
        if (zip == null)
        {
            ShareNote.Text = error ?? "The pipeline list could not be saved.";
            ToolTip.SetTip(ShareNote, ShareNote.Text);   // (whole: the note is cut at three lines)
            return;
        }
        var copied = false;
        if (Clipboard is { } clipboard)
            try { await clipboard.SetTextAsync(report ?? ""); copied = true; } catch (Exception) { }
        foreach (var open in new[] { Path.GetDirectoryName(zip)!, Issue })
            try { Process.Start(new ProcessStartInfo(open) { UseShellExecute = true }); } catch (Exception) { }
        // (the menu's panel is narrow: the short of it there, the whole under the pointer)
        ShareNote.Text = $"Saved in {Path.GetFileName(Path.GetDirectoryName(zip))}: attach it to issue #2034" +
                         (copied ? " with the copied report." : ", with the report in it.");
        ToolTip.SetTip(ShareNote, $"Saved {Path.GetFileName(zip)} ({count} pipelines) in {Path.GetDirectoryName(zip)}. Attach it to a comment on " +
                                  $"{Issue}, then paste the report and say where you played.");
    }

    const string Issue = "https://github.com/NotRexed/Hookshot/issues/2034";

    void LowMemClick()
    {
        try { GameEnv.SetSkip(new DirectoryInfo(folder.Path), LowMemTick.IsChecked == true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
        ShowLowMem();   // (what the file says now, whether or not it could be written)
    }

    // ---- the work: a first build, or a release brought into the folder ----

    /// Build Game: the game built into the folder chosen, by the release BuildTag names (its builder got first when it is not here)
    async Task StartBuild()
    {
        if (job != null || !BuildGame.IsEnabled || BuildTag() is not { } tag) return;
        SaveSettings();
        int? workers, memoryGb;
        try
        {
            workers = BuildWorkers.Selected(WorkerBox.SelectedItem);
            memoryGb = BuildMemory.Selected(MemoryBox.SelectedItem);
        }
        catch (ArgumentException e)
        {
            await Dialog.Tell(this, "Hookshot", e.Message);
            return;
        }
        var source = romfs != null && exefs != null ? new List<string> { "--romfs", romfs, "--exefs", exefs } : new List<string> { "--nup", nup! };
        var language = (LanguageBox.SelectedItem as Language ?? SystemLanguage.Pick()).Code;
        var prepare = PrepareTick.IsChecked == true;
        var dlss = DlssTick.IsChecked == true;
        var fsr = FsrTick.IsChecked == true && fsrLicense;
        var target = outPath;
        await Run(new Job(tag, firstBuild: true, ahead: true, fetch: !Builders.Installed().ContainsKey(tag)), b =>
        {
            var args = new List<string>(source) { "--out", target, "--language", language };
            Options(b, args, dlss, fsr, prepare);
            return Builders.MakeGame(b, args, workers, memoryGb, dlss && Builders.Knows(b, "--fetch-ngx-sdk"), fsr && Builders.Knows(b, "--fsr"));
        }, target);
    }

    /// A release brought into the folder, which keeps the game files it was built from: its builder got first when it is not
    /// here, then its make_game.py --update builds the folder again. Asked first, as it is a rebuild and not a download
    async Task Bring(string tag, bool ask, bool rebuild = false)
    {
        if (job != null || !folder.HasGame) return;
        var ahead = folder.Version == null || !IsRelease(folder.Version) || Newer(tag, folder.Version);
        var name = VersionName(tag);
        var saved = BuilderSettings.Load(Builders.SettingsPath);
        var about = BuildTimes.About(typicalBuild);
        var rebuilt = $"Your game is rebuilt from your game files with {name}'s tools. That takes {about} on this PC, and Play waits until it is done. Your saves and settings stay.";
        var question = rebuild
            ? $"Rebuild {name}?\n\nYour game is built again from your game files with your settings now (FSR 4 {(saved?.Fsr == true && saved.FsrLicense ? "on" : "off")}, " +
              $"DLSS {(saved?.Dlss == true ? "on" : "off")}). That takes {about} on this PC, and Play waits until it is done. Your saves and settings stay."
            : ahead ? $"Update to {name}?\n\n{rebuilt}" : $"Switch back to {name}? It replaces {VersionName(folder.Version!)} in this folder.\n\n{rebuilt}";
        if (ask && !await Dialog.Ask(this, "Hookshot", question, rebuild ? "Rebuild" : ahead ? "Update" : "Switch back", "Not now")) return;
        int? memoryGb = saved?.MemoryGb is int gb && gb > 0 ? gb : null;
        var game = folder.Path;
        // (as the WPF launcher's update: the saved DLSS switch, or a folder built with DLSS whose license was accepted then, is
        // built with it; FSR 4 the same. Each said only to tools that know the word)
        // (a rebuild is for the settings now: what the folder was built with does not carry over, and FSR 4 switched off is taken
        // out, --no-fsr)
        var dlss = saved?.Dlss == true || (!rebuild && File.Exists(Path.Combine(game, "ngx", "nvngx_dlss.dll")) &&
                                           File.Exists(Path.Combine(Builders.NgxSdk, "LICENSE.txt")));
        var fsr = (saved?.Fsr == true && saved.FsrLicense) || (!rebuild && File.Exists(Path.Combine(game, ".hookshot_fsr")) &&
                  File.Exists(Path.Combine(game, "amd_fidelityfx_upscaler_vk.dll")) && File.Exists(Path.Combine(game, "LICENSE-AMD-FSR-SDK.md")));
        var noFsr = rebuild && !fsr && File.Exists(Path.Combine(game, ".hookshot_fsr"));
        var prepare = saved?.PrepareLater != true;
        NavPlay.IsChecked = true;   // (an update shows in the note, on the Play screen)
        await Run(new Job(tag, firstBuild: false, ahead, fetch: !Builders.Installed().ContainsKey(tag), rebuild), b =>
        {
            var args = new List<string> { "--update", game };
            Options(b, args, dlss, fsr, prepare);
            if (noFsr && Builders.Knows(b, "--no-fsr")) args.Add("--no-fsr");
            return Builders.MakeGame(b, args, null, memoryGb, dlss && Builders.Knows(b, "--fetch-ngx-sdk"), fsr && Builders.Knows(b, "--fsr"));
        }, game);
    }

    /// (make_game.py's options for the choices, each only when that release's make_game.py knows it: an older one stops on an
    /// argument it does not know)
    void Options(Builder b, List<string> args, bool dlss, bool fsr, bool prepare)
    {
        ffxAfter = false;
        if (OperatingSystem.IsWindows() && !SameDir(Builders.Work, Builders.Base) && Builders.Knows(b, "--work")) args.AddRange(new[] { "--work", Builders.Work });
        if (dlss && Builders.Knows(b, "--fetch-ngx-sdk") && Builders.Knows(b, "--accept-nvidia-license"))
            args.AddRange(new[] { "--fetch-ngx-sdk", "--accept-nvidia-license" });
        // (AMD FSR 4: --fsr to any make_game.py that takes it, the fetch words only to one that knows them. For one that does not,
        // v2.1.0 and before, the launcher puts AMD's runtime in the folder once it is built: FfxRuntime, ffxAfter)
        if (fsr && Builders.Knows(b, "--fsr")) args.Add("--fsr");
        if (fsr && Builders.Knows(b, "--fsr") && Builders.Knows(b, "--fetch-fsr-runtime") && Builders.Knows(b, "--accept-amd-license"))
            args.AddRange(new[] { "--fetch-fsr-runtime", "--accept-amd-license" });
        else if (fsr && Builders.Knows(b, "--fsr")) ffxAfter = true;
        if (!prepare && Builders.Knows(b, ShaderStep.Later)) args.Add(ShaderStep.Later);
    }

    /// (the build under way has FSR 4 and its tools do not fetch AMD's runtime: the launcher puts it in the folder at the end)
    bool ffxAfter;

    static bool SameDir(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    Func<Builder, ProcessStartInfo>? retryWith;
    string? retryFolder;

    async Task Run(Job j, Func<Builder, ProcessStartInfo> makeGame, string target)
    {
        (job, stoppedJob, stoppedWhy, readyNote, retryWith, retryFolder) = (j, null, null, null, makeGame, target);
        ShowDetails(false);
        var drawn = DateTime.MinValue;
        j.Said += line =>
        {
            if (detailsShown) AppendLog(line);
            // (the window drawn again for each step, and at most ten times a second for the rest: a compile says thousands of lines)
            if (line.StartsWith("@@") || (DateTime.Now - drawn).TotalMilliseconds > 100)
            {
                drawn = DateTime.Now;
                Render();
            }
        };
        FillChoices();
        // (the step's time under the bar moves while nothing is said)
        var tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Render());
        tick.Start();
        // (the game's own builder line points at this launcher: the game's update offer opens it, hos_update.c)
        string? failure;
        try { failure = await j.Run(b => WithSelf(makeGame(b))); }
        finally { tick.Stop(); }
        job = null;
        if (j.Work.Done != null && ffxAfter)
        {
            ffxAfter = false;
            var why = Builders.DummyReleases != null ? "not fetched with made-up releases (HOOKSHOT_DUMMY_RELEASES)"
                : await FfxRuntime.Ensure(Builders.FfxSdk) ?? FfxRuntime.CopyTo(Builders.FfxSdk, target);
            if (why != null) j.Log.AppendLine("AMD FSR 4: " + why);
        }
        if (j.Work.Done != null)
        {
            var built = GameFolder.Read(target);
            folder = built;
            var took = DateTime.Now - j.Started;
            var minutes = Math.Max(1, (int)took.TotalMinutes);
            readyNote = j.FirstBuild ? $"Your game is ready · built in {(took.TotalHours >= 1 ? $"{(int)took.TotalHours} h {took.Minutes} min" : minutes == 1 ? "a minute" : $"{minutes} minutes")}" : null;
            var keep = built.Version;
            Builders.RefreshStarter(target);
            _ = Task.Run(() => Builders.FreeAfterBuild(keep));
            if (j.FirstBuild) CheckSource();
            ShowLowMem();
            ShowDetails(false);
        }
        else if (failure != "cancelled") (stoppedJob, stoppedWhy) = (j, failure);
        else ShowDetails(false);
        FillChoices();
    }

    static ProcessStartInfo WithSelf(ProcessStartInfo si)
    {
        if (OperatingSystem.IsWindows() && Environment.ProcessPath is { } self) si.Environment["HOOKSHOT_BUILDER_EXE"] = self;
        return si;
    }

    /// The red button of a build or an update that stopped: the same again
    async Task Retry()
    {
        if (job != null || stoppedJob is not { } was || retryWith is not { } makeGame || retryFolder is not { } target) return;
        var again = new Job(was.Tag, was.FirstBuild, !was.Work.Bar.StartsWith("Switching"), fetch: !Builders.Installed().ContainsKey(was.Tag),
                            was.Work.IsRebuild);
        await Run(again, makeGame, target);
    }

    /// (Dismiss: the update that stopped put away, Check for updates in its place)
    /// The note's close button: a stop put away; a Hookshot release or the launcher's own update not shown again for that
    /// version while the window is open (a newer one shows it again; the release can still be brought from Releases)
    void CloseNote()
    {
        if (stoppedJob is { FirstBuild: false }) Dismiss();
        else
        {
            if (Available && !SameTag(closedRelease, latest)) closedRelease = latest;
            else if (launcherUpdate != null) closedLauncher = launcherUpdate;
            Render();
        }
    }
    string? closedRelease, closedLauncher;

    void Dismiss()
    {
        (stoppedJob, stoppedWhy) = (null, null);
        ShowDetails(false);
        FillChoices();
    }

    void ShowDetails(bool show)
    {
        detailsShown = show;
        Log.IsVisible = show;
        if (show)
        {
            var said = (job ?? stoppedJob)?.Log.ToString() ?? "";
            // (a stop: why, whole, first; the note and the Build page say it in a few words)
            if (job == null && stoppedJob != null && stoppedWhy != null)
                said = $"Why it stopped: {stoppedWhy.TrimEnd('.')}. Nothing was changed in your game folder.{Environment.NewLine}{Environment.NewLine}{said}";
            Log.Text = said.Length > 200_000 ? said[^200_000..] : said;
            Log.CaretIndex = Log.Text.Length;
        }
        Render();
    }

    void AppendLog(string line)
    {
        Log.Text += line + "\n";
        if (Log.Text.Length > 300_000) Log.Text = Log.Text[^200_000..];
        Log.CaretIndex = Log.Text.Length;
    }

    async Task CopyReport(Button button)
    {
        if (stoppedJob is not { } j || Clipboard is not { } clipboard) return;
        try
        {
            await clipboard.SetTextAsync(j.Report(stoppedWhy ?? "", folder.Version));
            button.Content = "Copied";
        }
        catch (Exception) { button.Content = "Could not copy"; }
        await Task.Delay(2400);
        button.Content = "Copy error log";
    }

    void LinkClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Tag: string page } || !page.StartsWith("https://")) return;
        try { Process.Start(new ProcessStartInfo(page) { UseShellExecute = true }); }
        catch (Exception) { }   // (no browser to open it)
    }
}

/// How long a build took on this PC lately (make_game.py's build_times.json, in the build files folder: the middle of the last
/// five), and that in words, as the WPF launcher says it (Launcher.cs, AboutTime)
static class BuildTimes
{
    public static double? Typical()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Builders.Work, "build_times.json")));
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
