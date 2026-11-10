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
    bool launchChecking, launchChecked, menuOpen, olderOpen, stepsHalfWay;
    enum Go { Play, Bring, None }                // (Bring: the folder is updated or built again to the version picked first)
    Go launchGo;

    // (while a version is brought the launcher stays: the programs that do it are hidden, and it says how far they are)
    enum LaunchState { Idle, Working, Stopped }
    LaunchState launchState;
    LaunchWork? work;                  // (what is being brought, and how far it is: LaunchWork.cs)
    LaunchChoice? workTarget;
    Process? enginePull;               // (the setup in its engine mode: that version's builder installed beside this one)
    Runner? launchRunner;              // (that version's own make_game.py)
    bool workCancelled, shotWorking, cancelHalfWay;
    string workStopped = "";           // (why it stopped)
    readonly System.Text.StringBuilder workLog = new();
    static readonly Brush StoppedInk = Frozen(0xE2, 0x77, 0x6B);

    internal bool LauncherBusy => launchState == LaunchState.Working;

    LaunchChoice? Picked => launchLists?.Picked;

    /// The launcher of a game folder, when the window opens as it (Longshot.exe in the folder, or the game built last)
    async void ShowLauncher(string folder)
    {
        // (development) HOOKSHOT_UI_PREVIEW=versions,older,max,pick=release:v1.1.0,go,pages (any of them): the menu open from the
        // start, its older releases unfolded, the window maximised, that version picked, the button pressed, "Build from
        // game files" pressed; cancel: Cancel pressed when what the button started is 40% done; steps: the menu opened then
        var preview = (Environment.GetEnvironmentVariable("HOOKSHOT_UI_PREVIEW") ?? "").Split(',', StringSplitOptions.TrimEntries);
        olderOpen = preview.Contains("older");
        cancelHalfWay = preview.Contains("cancel");
        stepsHalfWay = preview.Contains("steps");
        EnterLauncher(folder, preview.FirstOrDefault(p => p.StartsWith("pick="))?[5..], check: false);
        if (preview.Contains("versions")) ShowMenu(true, atOnce: true);
        if (preview.Contains("max")) WindowState = WindowState.Maximized;
        await CheckReleases(say: false);
        // (development) HOOKSHOT_UI_SHOT=<file.png>: the window as drawn, once the releases are in, and out (the window opens away
        // from the screen and takes no keys, MainWindow's constructor). With go: drawn half way (<file>-working.png) and when
        // what the button started has ended (WorkEnded)
        if (preview.Contains("go"))
        {
            LauncherGoClick(this, new RoutedEventArgs());
            if (launchState != LaunchState.Idle) return;
        }
        if (preview.Contains("pages"))   // ("Build from game files" pressed: the builder's own pages, in this window)
        {
            LauncherBuildClick(this, new RoutedEventArgs());
            await Task.Delay(2500);
        }
        if (Shot != null)
        {
            await Task.Delay(500);
            SaveShot(Shot);
            Application.Current.Shutdown();
        }
    }

    /// The screen becomes the launcher of that folder: at the start, and when a build here has made the game (MainWindow.xaml.cs,
    /// ShowReady) or was left for the game that is there. What is on this PC shows at once; GitHub's releases when they come
    void EnterLauncher(string folder, string? pick = null, bool check = true)
    {
        launchFolder = folder;
        updating = false;
        if (OutPath.Text != folder) OutPath.Text = folder;
        launchBuilt = GameVersion(folder) ?? ReadVersion(folder);
        launchPick = pick ?? LastChoice(folder);
        launchState = LaunchState.Idle;
        ShowFace(Face.Launcher);
        FillChoices();
        if (check && !launchChecked) _ = CheckReleases(say: false);
    }

    static string? Shot => Environment.GetEnvironmentVariable("HOOKSHOT_UI_SHOT") is { Length: > 0 } file ? file : null;

    void SaveShot(string file)
    {
        UpdateLayout();
        var picture = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        picture.Render(this);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(picture));
        using (var to = File.Create(file)) png.Save(to);
        // (beside it, what the picture shows in words, for a check that reads no picture: tests/builder_launcher/rig)
        var menu = !menuOpen ? "closed" : MenuSteps.Visibility == Visibility.Visible ? "steps" : MenuOptions.Visibility == Visibility.Visible ? "options" : "versions";
        File.WriteAllLines(System.IO.Path.ChangeExtension(file, ".txt"), new[]
        {
            $"face={face}", $"state={launchState}", $"button={(LauncherGo.Visibility == Visibility.Visible ? LauncherGoText.Text : "")}",
            $"enabled={LauncherGo.IsEnabled}", $"under={LauncherVersion.Content}", $"line={LauncherStatus.Text}", $"menu={menu}",
            $"ready={ReadyBlock.Visibility == Visibility.Visible}", $"files={SourceText.Text}", $"folder={OutPath.Text}",
            $"note={string.Concat(FilesNote.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text))}",
            $"steps={string.Join(" | ", rows.Values.Select(r => r.Says))}",
        });
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
        if (face != Face.Launcher) return;   // (a build's own screen: MainWindow.xaml.cs says what the button does)
        ShowPills();
        // (right after a build, while nothing else is going on: the shortcuts and what the build keeps, above the button)
        ReadyBlock.Visibility = readyNote != null && launchState == LaunchState.Idle ? Visibility.Visible : Visibility.Collapsed;
        if (launchState != LaunchState.Idle)
        {
            ShowBring();
            return;
        }
        LauncherGo.Visibility = Visibility.Visible;
        LauncherProgress.Visibility = Visibility.Collapsed;
        LauncherVersion.Foreground = Brushes.White;
        (LauncherStatus.ToolTip, LauncherStatus.IsHitTestVisible, LauncherStatus.Cursor) = (null, false, null);
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
        // (this once, the game the folder holds: that it was just built, and in how long)
        if (readyNote != null && c is { Here: true, Folder: null } && !news) (text, LauncherStatus.ToolTip, LauncherStatus.IsHitTestVisible) = (readyNote, readyTip, true);
        LauncherStatus.Text = text;
        LauncherStatus.Foreground = news ? NewsInk : QuietInk;
        MenuDot.Visibility = NewerOut() ? Visibility.Visible : Visibility.Collapsed;   // (the dot on Menu: a newer release is in there)
    }

    /// The button: the build's own word on a build's screen (MainWindow.xaml.cs), else what the launcher does for the version picked
    void LauncherGoClick(object sender, RoutedEventArgs e)
    {
        switch (face)
        {
            case Face.Files: StartBuild(sender, e); return;
            case Face.Problem: RetryProblem(sender, e); return;
            case Face.Build: return;
        }
        if (launchState == LaunchState.Working) return;
        if (launchState == LaunchState.Stopped)   // (the button says Retry)
        {
            if (workTarget != null) StartWork(workTarget);
            return;
        }
        if (Picked is not { } c) return;
        SaveChoice(launchFolder, c.Key);
        switch (launchGo)
        {
            case Go.Play:
                StartGame(c.Folder ?? launchFolder);
                Application.Current.Shutdown();
                break;
            case Go.Bring when BuilderHere(c.Tag!) != null || EngineSetup() != null:
                StartWork(c);
                break;
            case Go.Bring:
                // (a setup from before it could install a version beside this one: the builder's own update, which takes this
                // builder's place with that version's. The latest release as before; any other by its own tag)
                readyNote = null;
                Updater.UpdateFolder(this, launchFolder, c.Tag, fetched: false, exact: !SameTag(c.Tag, latestTag));
                break;
        }
    }

    // ---- a version brought: the launcher stays, and hidden programs do it (Paper: "v2 - Updating", "v2 - Update stopped") ----

    /// The setup beside this builder, when it has the engine mode (builder/setup/setup.c, --engine). One from before would take
    /// the word for an ordinary install: it would open its window, make that version the one in use and remove this one
    static string? EngineSetup()
    {
        try
        {
            var setup = System.IO.Path.Combine(AppContext.BaseDirectory, "LongshotBuilder-Setup.exe");
            if (!File.Exists(setup)) return null;
            var has = File.ReadAllBytes(setup).AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("--engine")) >= 0;
            return has ? setup : null;
        }
        catch (Exception) { return null; }
    }

    /// The version picked brought into the launch folder: its builder installed beside this one when it is not on the PC (the
    /// setup, hidden), then the folder built with that builder's own tools (its make_game.py, hidden). The window is the
    /// launcher's throughout
    async void StartWork(LaunchChoice c)
    {
        var tag = c.Tag!;
        var tools = BuilderHere(tag);
        var ahead = launchBuilt == null || !IsRelease(launchBuilt) || Newer(tag, launchBuilt);
        (workTarget, work, workCancelled, workStopped, shotWorking) = (c, new LaunchWork(VersionName(tag), ahead, fetch: tools == null), false, "", false);
        workLog.Clear();
        launchState = LaunchState.Working;
        readyNote = null;
        ShowDetails(false);
        // (its steps, in the menu: the builder got first when it is not here; the shaders' step when that version has one and
        // the player has it on. One downloaded is a version that has it)
        MakeRows(update: true, shaders: BuilderSettings.PrepareNow(SettingsPath) && (tools == null || KnowsShaderStep(tools)), download: tools == null);
        StepsTitle.Text = $"{(ahead ? "Updating to" : "Switching to")} {VersionName(tag)}";
        ShowMenu(false);
        ShowGo();
        if (tools == null)
        {
            var why = await PullEngine(tag);
            if (why == null && (tools = BuilderHere(tag)) == null)
                why = $"The builder of {VersionName(tag)} is not there after it was installed. Your antivirus may have removed it.";
            if (why != null)
            {
                WorkEnded(why);
                return;
            }
        }
        int? workers = null;
        try { workers = BuildWorkers.Selected(WorkerBox.SelectedItem); } catch (ArgumentException) { }
        // (as the builder's own update does it, UpdateBuild: a folder built with NVIDIA DLSS, its license accepted then, is built
        // with it again; the build files where the player chose)
        var dlss = File.Exists(System.IO.Path.Combine(launchFolder, "ngx", "nvngx_dlss.dll")) &&
                   File.Exists(System.IO.Path.Combine(Storage.NgxSdk, "LICENSE.txt"));
        var args = new List<string> { "--update", launchFolder };
        if (Storage.WorkChosen) args.AddRange(new[] { "--work", Storage.Work });
        if (dlss) args.AddRange(new[] { "--fetch-ngx-sdk", "--accept-nvidia-license" });
        // (the shaders' step, left to the game's first start when the player unticked it: said only to tools that know the
        // word. A version from before the step has none to leave, and would stop on an argument it does not know)
        if (!BuilderSettings.PrepareNow(SettingsPath) && KnowsShaderStep(tools!)) args.Add(ShaderStep.Later);
        launchRunner = new Runner(tools!, args, workers, dlss ? Storage.NgxSdk : null);
        launchRunner.Line += l => Dispatcher.BeginInvoke(() => WorkLine(l));
        launchRunner.Finished += failure => Dispatcher.BeginInvoke(() => WorkEnded(failure));
        launchRunner.Start();
    }

    /// (that version's make_game.py takes --no-prepare-shaders: read from its text, as each version runs its own tools)
    static bool KnowsShaderStep(Tools tools)
    {
        try { return File.ReadAllText(System.IO.Path.Combine(tools.Root, "nx2", "make_game.py")).Contains(ShaderStep.Later); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// The setup in its engine mode: that release installed beside the others, nothing started, nothing removed. null: done
    async Task<string?> PullEngine(string tag)
    {
        try
        {
            var si = new ProcessStartInfo(EngineSetup()!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var a in new[] { "--engine", "--tag", tag }) si.ArgumentList.Add(a);
            using var p = Process.Start(si)!;
            enginePull = p;
            while (await p.StandardOutput.ReadLineAsync() is { } line) WorkLine(line);
            await p.WaitForExitAsync();
            enginePull = null;
            if (workCancelled) return "cancelled";
            return p.ExitCode == 0 ? null : work?.Error ?? "The setup stopped without saying why.";
        }
        catch (Exception e)
        {
            enginePull = null;
            return workCancelled ? "cancelled" : "The setup could not be started: " + e.Message;
        }
    }

    void WorkLine(string line)
    {
        if (work == null) return;
        work.Line(line);
        workLog.AppendLine(line);
        if (workLog.Length > 600_000) workLog.Remove(0, workLog.Length - 400_000);
        if (detailsShown)
        {
            Log.AppendText(line + "\n");
            Log.ScrollToEnd();
        }
        // (the menu's steps: the builder got, then what its make_game.py says of its own)
        if (line.StartsWith("@@setup ") && rows.TryGetValue("download", out var got))
        {
            if (current == null) { current = "download"; got.Begin(); }
            got.Small(line[8..].Trim());
        }
        else if (line.StartsWith("@@installed ") && rows.TryGetValue("download", out got)) { got.Small("Installed beside the one in use"); got.Done(); current = null; }
        else StepsLine(line);
        ShowBring();
        if (Shot != null && !shotWorking && work.Fraction >= 0.4)   // (development: the working state drawn once, on the way)
        {
            shotWorking = true;
            if (stepsHalfWay) ShowMenu(true, atOnce: true);
            SaveShot(System.IO.Path.ChangeExtension(Shot, null) + "-working.png");
            if (stepsHalfWay) ShowMenu(false, atOnce: true);
        }
        if (cancelHalfWay && work.Fraction >= 0.4)
        {
            cancelHalfWay = false;
            CancelLaunchWork();
        }
    }

    /// failure: null (it ended well), "cancelled", or why it stopped
    void WorkEnded(string? failure)
    {
        (launchRunner, enginePull) = (null, null);
        if (work?.Done != null)
        {
            // (the folder holds the version picked now: what the launcher shows is read again from it)
            launchBuilt = GameVersion(launchFolder) ?? ReadVersion(launchFolder);
            if (workTarget?.Tag is { } tag) NextBuilder(tag);
            if (Shot == null) Finder.RefreshShortcuts(launchFolder);   // (the player's shortcuts, onto the folder's launcher again)
            // (as after any build: the build files go but its compiled objects, so the next version compiles only what changed,
            // and a version's builder that is no longer the one in use)
            var freed = Storage.KeepBuildFiles ? Task.CompletedTask : Task.Run(() => Storage.Free(afterBuild: true));
            if (Shot != null) freed.Wait(20000);   // (development: it is out as soon as it is drawn)
            launchState = LaunchState.Idle;
        }
        else if (failure == "cancelled" || workCancelled) launchState = LaunchState.Idle;
        else
        {
            workStopped = failure ?? work?.Error ?? "The build tools stopped without saying why.";
            launchState = LaunchState.Stopped;
            if (current != null && rows.TryGetValue(current, out var row)) row.Stopped();
        }
        if (launchState == LaunchState.Idle)
        {
            ShowDetails(false);
            ShowMenu(false);   // (it held the steps: the versions are in it again)
            FillChoices();
        }
        else ShowGo();
        if (Shot != null)   // (development: drawn as it ended, and out)
        {
            SaveShot(Shot);
            Application.Current.Shutdown();
        }
    }

    /// After a release ahead of the builder in use was brought: its builder, which came with it, is the one in use from the next
    /// start (versions\current.txt, which the game folder's Longshot.exe reads; what Storage.Free keeps). The launcher never
    /// goes back to an older one this way, and not to a pre-release's
    static void NextBuilder(string tag)
    {
        try
        {
            if (IsPre(tag) || BuilderHere(tag) is not { } tools) return;
            var current = System.IO.Path.Combine(Storage.Base, "versions", "current.txt");
            var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(tools.Root.TrimEnd('\\'))));
            var now = File.ReadAllText(current).Trim();
            if (folder is { Length: > 0 } && IsRelease(now) && Newer(tag, now)) File.WriteAllText(current, folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // (no current.txt: not a setup's install)
    }

    /// The window while a version is brought (the strip fills from the left, in the button's place) and when that stopped
    void ShowBring()
    {
        if (work == null) return;
        var working = launchState == LaunchState.Working;
        LauncherGo.Visibility = working ? Visibility.Hidden : Visibility.Visible;
        LauncherProgress.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        LauncherVersion.IsHitTestVisible = false;
        if (working)
        {
            ProgressClip.Rect = new Rect(0, 0, LauncherProgress.Width * work.Fraction, LauncherProgress.Height);
            LauncherVersion.Content = work.Title;
            LauncherVersion.Foreground = Brushes.White;
            LauncherStatus.Text = work.Detail;
            LauncherStatus.Foreground = QuietInk;
            (LauncherStatus.ToolTip, LauncherStatus.IsHitTestVisible, LauncherStatus.Cursor) = (null, false, null);
            return;
        }
        LauncherGoText.Text = "Retry";
        LauncherGo.IsEnabled = true;
        LauncherVersion.Content = work.StoppedTitle;
        LauncherVersion.Foreground = StoppedInk;
        // (the folder is swapped only once the new game is complete, make_game.py: one that stopped left it as it was. The line
        // is one line: a long reason has it to itself, cut where it ends and whole under the pointer and in Copy details.
        // Pressed, it shows what the programs said)
        const string kept = "Nothing was changed in your game folder.";
        var why = workStopped.TrimEnd('.') + ".";
        LauncherStatus.Text = why.Length + kept.Length <= 96 ? why + " " + kept : why;
        LauncherStatus.Foreground = QuietInk;
        LauncherStatus.ToolTip = why + " " + kept;
        (LauncherStatus.IsHitTestVisible, LauncherStatus.Cursor) = (true, Cursors.Hand);
        LauncherCopy.Content = "Copy details";
    }

    bool Working => face == Face.Build || (face == Face.Launcher && launchState == LaunchState.Working);
    bool Stopped => face == Face.Problem || (face == Face.Launcher && launchState == LaunchState.Stopped);

    /// The buttons at the edges: Menu at the top left while the menu is closed; at the bottom left Cancel while a version is
    /// brought or the game is built, Back and Copy details when that stopped, and Back from a build's choices to the launcher
    /// they were opened from
    void ShowPills()
    {
        if (MenuButton == null) return;
        MenuButton.Visibility = menuOpen ? Visibility.Collapsed : Visibility.Visible;
        LauncherCancel.Visibility = Working ? Visibility.Visible : Visibility.Collapsed;
        LauncherStopped.Visibility = Stopped ? Visibility.Visible : Visibility.Collapsed;
        FilesBack.Visibility = face == Face.Files && launchFolder.Length > 0 && File.Exists(GameExe(launchFolder)) ? Visibility.Visible : Visibility.Collapsed;
        // (the menu's rows stay clear of them)
        MenuScroll.Margin = new Thickness(0, 48, 0, Working || Stopped || FilesBack.Visibility == Visibility.Visible ? 90 : 0);
    }

    void LauncherCancelClick(object sender, RoutedEventArgs e)
    {
        if (face == Face.Build) CancelBuild(sender, e);
        else CancelLaunchWork();
    }

    void StoppedBackClick(object sender, RoutedEventArgs e)
    {
        if (face == Face.Problem) Back(sender, e);
        else LauncherBackToIdleClick(sender, e);
    }

    void StoppedCopyClick(object sender, RoutedEventArgs e)
    {
        if (face == Face.Problem) CopyForReport(sender, e);
        else LauncherCopyClick(sender, e);
    }

    internal void CancelLaunchWork()
    {
        if (launchState != LaunchState.Working) return;
        workCancelled = true;
        launchRunner?.Cancel();
        try { enginePull?.Kill(entireProcessTree: true); } catch (Exception) { }
    }

    void LauncherBackToIdleClick(object sender, RoutedEventArgs e)
    {
        launchState = LaunchState.Idle;
        ShowDetails(false);
        ShowMenu(false);
        FillChoices();
    }

    /// (what stopped, for a bug report: the versions, the reason and the last of what the hidden programs said)
    void LauncherCopyClick(object sender, RoutedEventArgs e)
    {
        var said = workLog.ToString();
        var report = $"Longshot launcher: {work?.StoppedTitle}\nBuilder {Tools.Find()?.Version ?? "from source"}, the folder holds {launchBuilt ?? "no release"}\n" +
                     $"{workStopped}\n\n{(said.Length > 6000 ? said[^6000..] : said)}";
        try
        {
            Clipboard.SetText(report);
            LauncherCopy.Content = "Copied";
        }
        catch (Exception) { LauncherCopy.Content = "Could not copy"; }   // (the clipboard held by another program)
    }

    // ---- a build from game files, from the launcher and back ----

    /// "Build from game files", at the foot of the menu's versions: the build's choices on the screen (other game files, where
    /// the game goes; its options in the menu), with Back at the bottom left
    async void LauncherBuildClick(object sender, RoutedEventArgs e)
    {
        if (launchState != LaunchState.Idle) return;
        readyNote = null;
        sourcePicked = false;
        await OpenFiles(first: false);
    }

    void BackToLauncherClick(object sender, RoutedEventArgs e)
    {
        if (runner != null) return;
        EnterLauncher(launchFolder);
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

    // ---- the menu: a bar from the left (Paper: "Master Menu"), opened by Menu and closed by Close menu ----

    void MenuClick(object sender, RoutedEventArgs e) => ShowMenu(true);

    void MenuCloseClick(object sender, RoutedEventArgs e) => ShowMenu(false);

    /// (a press in the window that no button took: the menu closes)
    void LauncherBackClick(object sender, MouseButtonEventArgs e) => ShowMenu(false);

    /// (a press in the menu itself, between its rows: it stays)
    void LauncherBarClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    /// What the menu holds goes with the moment: the steps while a version is brought or the game is built, a build's options
    /// while its choices are on the screen (or when there is no game whose versions to show), else the versions
    void ShowMenu(bool open, bool atOnce = false)
    {
        if (VersionBar == null) return;
        if (open)
        {
            var options = !Working && (face == Face.Files || launchFolder.Length == 0);
            MenuSteps.Visibility = Working ? Visibility.Visible : Visibility.Collapsed;
            MenuOptions.Visibility = options ? Visibility.Visible : Visibility.Collapsed;
            MenuVersions.Visibility = !Working && !options ? Visibility.Visible : Visibility.Collapsed;
        }
        if (open == menuOpen) return;
        menuOpen = open;
        ShowPills();
        VersionBarSlide.BeginAnimation(TranslateTransform.XProperty, null);
        VersionBar.BeginAnimation(OpacityProperty, null);
        if (open) VersionBar.Visibility = Visibility.Visible;
        else if (atOnce) VersionBar.Visibility = Visibility.Collapsed;
        if (atOnce) return;
        var time = TimeSpan.FromMilliseconds(open ? 200 : 150);
        var away = -Math.Max(240, VersionBar.ActualWidth);
        var slide = new DoubleAnimation(open ? away : 0, open ? 0 : away, time)
        {
            EasingFunction = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        // (opened again before it was away: it stays)
        if (!open) slide.Completed += (_, _) => { if (!menuOpen) VersionBar.Visibility = Visibility.Collapsed; };
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
