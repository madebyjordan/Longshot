using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

    internal MainWindow(GameFolder folder, Preview preview, string? updateTag = null)
    {
        var saved = BuilderSettings.Load(Builders.SettingsPath) ?? new BuilderSettings();
        // (opened with no folder: the game built last, when it is still there)
        if (folder.Path.Length == 0 && saved.Out is { Length: > 0 } last && GameFolder.Read(last) is { HasGame: true } before) folder = before;
        this.folder = folder;
        this.updateTag = updateTag;
        InitializeComponent();
        Chrome();
        olderOpen = preview.Older;
        typicalBuild = BuildTimes.Typical();

        // ---- the saved choices ----
        outPath = folder.Path.Length > 0 ? folder.Path
            : saved.Out is { Length: > 2 } o ? o : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Hookshot");
        if (saved.Romfs is { } r && saved.Exefs is { } x && Directory.Exists(r) && Directory.Exists(x)) (romfs, exefs) = (r, x);
        else if (saved.Nup is { } n && File.Exists(n)) nup = n;
        PrepareTick.IsChecked = !saved.PrepareLater;
        DlssTick.IsChecked = saved.Dlss;
        fsrLicense = saved.FsrLicense;
        FsrTick.IsChecked = saved.Fsr && saved.FsrLicense;
        PreTick.IsChecked = preview.Pre || (saved.Track is { } t && t != "release");
        LanguageBox.ItemsSource = Strings.All;
        LanguageBox.SelectedItem = Strings.Default();
        WorkerBox.ItemsSource = BuildWorkers.Choices(Environment.ProcessorCount).ToList();
        WorkerBox.SelectedIndex = 0;
        var memory = BuildMemory.Choices(BuildMemory.TotalGb()).ToList();
        MemoryBox.ItemsSource = memory;
        MemoryBox.SelectedItem = BuildMemory.Saved(memory, saved.MemoryGb);

        // ---- what the buttons do ----
        MenuButton.Click += (_, _) => ShowMenu(true);
        CloseMenu.Click += (_, _) => ShowMenu(false);
        foreach (var tab in new[] { TabBuild, TabReleases, TabSettings }) tab.IsCheckedChanged += (_, _) => ShowPage();
        FoldRow.Click += (_, _) => { olderOpen = !olderOpen; FillChoices(); };
        PreTick.Click += (_, _) => { SaveSettings(); FillChoices(); };
        PrepareTick.Click += (_, _) => SaveSettings();
        DlssTick.Click += async (_, _) => await DlssClick();
        FsrTick.Click += async (_, _) => await FsrClick();
        MemoryBox.SelectionChanged += (_, _) => SaveSettings();
        LowMemTick.Click += (_, _) => LowMemClick();
        CheckButton.Click += async (_, _) => await CheckReleases(say: true);
        UpdateButton.Click += async (_, _) => await UpdateClick();
        UpdateGo.Click += async (_, _) => await UpdateClick();
        Go.Click += (_, _) => GoClick();
        Version.Click += (_, _) => OpenFolder();
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
        FootDismiss.Click += (_, _) => Dismiss();
        AddHandler(Button.ClickEvent, LinkClick);
        AddHandler(Button.ClickEvent, RowClick);
        AddHandler(DragDrop.DropEvent, Dropped);
        DragDrop.SetAllowDrop(this, true);
        KeyDown += KeyPressed;
        Closing += (_, e) => { if (job != null && !closeAsked) { e.Cancel = true; _ = AskClose(); } };

        // (no game yet: the menu opens on Build; with one, on Releases)
        (folder.HasGame ? TabReleases : TabBuild).IsChecked = true;
        if (preview.Tab != null) (preview.Tab == "build" ? TabBuild : preview.Tab == "settings" ? TabSettings : TabReleases).IsChecked = true;
        ShowMenu(preview.Menu || !folder.HasGame || updateTag != null);
        ShowPage();
        ShowLowMem();
        CheckSource();
        FillChoices();
        Ready = Start();
    }

    /// (the releases asked for; then the game's own offer, when it opened this: that release brought, as it was asked there)
    async Task Start()
    {
        await CheckReleases(say: false);
        if (updateTag != null && folder.HasGame && job == null) await Bring(updateTag, ask: false);
    }

    /// (development, Shot.cs: a button pressed with no screen, and how far the work under way is)
    internal Task Act(string what)
    {
        switch (what)
        {
            case "build": return StartBuild();
            case "update": return UpdateClick();
            case "details": ShowDetails(true); return Task.CompletedTask;
            default: return Task.CompletedTask;
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
        if (e.Key == Key.Escape && Bar.IsVisible)
        {
            if (detailsShown) ShowDetails(false);
            else ShowMenu(false);
            e.Handled = true;
        }
    }

    // ---- the menu ----

    void ShowMenu(bool open)
    {
        Bar.IsVisible = open;
        MenuButton.IsVisible = !open;
        if (!open) ShowDetails(false);
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
        // (one list, newest first: the pre-releases among the releases while Settings has them on, and always the one in the folder)
        var pre = PreTick.IsChecked == true;
        ReleaseList.ItemsSource = lists.Pre.Where(c => pre || c.Here || c.Picked || c.Busy).Concat(lists.Releases).OrderByDescending(c => c.Tag!, Order).ToList();
        FoldRow.IsVisible = lists.Older > 0;
        FoldText.Text = lists.Folded ? $"{lists.Older} older releases" : "Fewer releases";
        FoldMark.RenderTransform = new ScaleTransform(1, lists.Folded ? 1 : -1);   // (down while they are folded, up to fold them again)
        ReleasesNote.Text = checkedOnce && releases.Count == 0 ? "GitHub did not answer: only the versions on this PC are listed." : "";
        ReleasesNote.IsVisible = ReleasesNote.Text.Length > 0;
        Render();
    }

    /// A release's row pressed: one not here is brought into the folder (asked first)
    async void RowClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Tag: LaunchChoice c } || job != null) return;
        if (!c.Here && c.Reach && c.Tag != null) await Bring(c.Tag, ask: true);
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
        MenuDot.IsVisible = NewerOut();
        UpdateGo.IsVisible = available && folder.HasGame;
        UpdateGoTo.Text = available ? "to " + VersionName(latest!) : "";
        ToolTip.SetTip(UpdateGo, available ? $"Update to {VersionName(latest!)}: your game is rebuilt from your game files ({BuildTimes.About(typicalBuild)}). Your saves and settings stay." : null);

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
        var (line, bad) = buildWorking ? (job!.Work.Detail, false) : idle ? IdleLine() : ("", false);
        BuildLine.Text = line;
        BuildLine.Foreground = new SolidColorBrush(Color.Parse(bad ? "#FF8373" : "#B8B8B8"));
        BuildLine.IsVisible = line.Length > 0;
        BuildWhy.Text = buildStopped ? stoppedWhy : "";
        ToolTip.SetTip(BuildWhy, BuildWhy.Text);
        BuildWhy.IsVisible = buildStopped;
        if (buildWorking) SetBar(BuildFill, BuildBarText, job!.Work.Fraction, $"Building... {(int)(job.Work.Fraction * 100)}%");

        // ---- the foot ----
        CheckButton.IsVisible = !footWorking && !footStopped && !available;
        CheckButton.IsEnabled = !busy && !checking;
        UpdateButton.IsVisible = available;
        UpdateButton.IsEnabled = !busy;
        FootBar.IsVisible = footWorking;
        FootStopped.IsVisible = footStopped;
        FootStopped.Content = footStopped && stoppedJob!.Work.Bar.StartsWith("Switching") ? "Switch stopped" : "Update stopped";
        FootCancel.IsVisible = footWorking;
        FootLinks.IsVisible = footStopped;
        FootLine.Text = available ? $"{VersionName(latest!)} Latest" : "";
        FootLine.IsVisible = available;
        // (that installing a version is a rebuild of the game, and how long; while it runs, the step and the time left)
        FootNote.Text = available ? $"Rebuilds your game from your game files · {BuildTimes.About(typicalBuild)}"
            : footWorking ? job!.Work.Note(typicalBuild, (DateTime.Now - job.Started).TotalSeconds) : "";
        FootNote.IsVisible = FootNote.Text.Length > 0;
        FootWhy.Text = footStopped ? stoppedWhy!.TrimEnd('.') + ". Nothing was changed in your game folder." : "";
        ToolTip.SetTip(FootWhy, FootWhy.Text);
        FootWhy.IsVisible = footStopped;
        if (footWorking) SetBar(FootFill, FootBarText, job!.Work.Fraction, job.Work.Bar);
        BuildDetails.Content = FootDetails.Content = detailsShown ? "Hide details" : "Show details";
    }

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
        s.Out = outPath;
        (s.Nup, s.Romfs, s.Exefs) = (nup, romfs, exefs);
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
    async Task DlssClick()
    {
        if (DlssTick.IsChecked == true && !await Dialog.Ask(this, "NVIDIA DLSS", DlssLicense, "Accept", "Decline")) DlssTick.IsChecked = false;
        SaveSettings();
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
    }

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
        var language = (LanguageBox.SelectedItem as Language ?? Strings.Default()).Code;
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
    async Task Bring(string tag, bool ask)
    {
        if (job != null || !folder.HasGame) return;
        var ahead = folder.Version == null || !IsRelease(folder.Version) || Newer(tag, folder.Version);
        var name = VersionName(tag);
        var rebuild = $"Your game is rebuilt from your game files with {name}'s tools. That takes {BuildTimes.About(typicalBuild)} on this PC, and Play waits until it is done. Your saves and settings stay.";
        var question = ahead ? $"Update to {name}?\n\n{rebuild}" : $"Switch back to {name}? It replaces {VersionName(folder.Version!)} in this folder.\n\n{rebuild}";
        if (ask && !await Dialog.Ask(this, "Hookshot", question, ahead ? "Update" : "Switch back", "Not now")) return;
        var saved = BuilderSettings.Load(Builders.SettingsPath);
        int? memoryGb = saved?.MemoryGb is int gb && gb > 0 ? gb : null;
        var game = folder.Path;
        // (as the WPF launcher's update: the saved DLSS switch, or a folder built with DLSS whose license was accepted then, is
        // built with it; FSR 4 the same. Each said only to tools that know the word)
        var dlss = saved?.Dlss == true || (File.Exists(Path.Combine(game, "ngx", "nvngx_dlss.dll")) && File.Exists(Path.Combine(Builders.NgxSdk, "LICENSE.txt")));
        var fsr = (saved?.Fsr == true && saved.FsrLicense) || (File.Exists(Path.Combine(game, ".hookshot_fsr")) &&
                  File.Exists(Path.Combine(game, "amd_fidelityfx_upscaler_vk.dll")) && File.Exists(Path.Combine(game, "LICENSE-AMD-FSR-SDK.md")));
        var prepare = saved?.PrepareLater != true;
        TabReleases.IsChecked = true;
        ShowMenu(true);
        await Run(new Job(tag, firstBuild: false, ahead, fetch: !Builders.Installed().ContainsKey(tag)), b =>
        {
            var args = new List<string> { "--update", game };
            Options(b, args, dlss, fsr, prepare);
            return Builders.MakeGame(b, args, null, memoryGb, dlss && Builders.Knows(b, "--fetch-ngx-sdk"), fsr && Builders.Knows(b, "--fsr"));
        }, game);
    }

    /// (make_game.py's options for the choices, each only when that release's make_game.py knows it: an older one stops on an
    /// argument it does not know)
    static void Options(Builder b, List<string> args, bool dlss, bool fsr, bool prepare)
    {
        if (OperatingSystem.IsWindows() && !SameDir(Builders.Work, Builders.Base) && Builders.Knows(b, "--work")) args.AddRange(new[] { "--work", Builders.Work });
        if (dlss && Builders.Knows(b, "--fetch-ngx-sdk") && Builders.Knows(b, "--accept-nvidia-license"))
            args.AddRange(new[] { "--fetch-ngx-sdk", "--accept-nvidia-license" });
        if (fsr && Builders.Knows(b, "--fsr") && Builders.Knows(b, "--fetch-fsr-runtime") && Builders.Knows(b, "--accept-amd-license"))
            args.AddRange(new[] { "--fsr", "--fetch-fsr-runtime", "--accept-amd-license" });
        if (!prepare && Builders.Knows(b, ShaderStep.Later)) args.Add(ShaderStep.Later);
    }

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
        if (j.Work.Done != null)
        {
            var built = GameFolder.Read(target);
            folder = built;
            var took = DateTime.Now - j.Started;
            var minutes = Math.Max(1, (int)took.TotalMinutes);
            readyNote = j.FirstBuild ? $"Your game is ready · built in {(took.TotalHours >= 1 ? $"{(int)took.TotalHours} h {took.Minutes} min" : minutes == 1 ? "a minute" : $"{minutes} minutes")}" : null;
            var keep = built.Version;
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
        var again = new Job(was.Tag, was.FirstBuild, !was.Work.Bar.StartsWith("Switching"), fetch: !Builders.Installed().ContainsKey(was.Tag));
        await Run(again, makeGame, target);
    }

    /// (Dismiss: the update that stopped put away, Check for updates in its place)
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
