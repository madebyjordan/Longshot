using HookshotBuilder;

// The launcher's bar of versions (builder/HookshotBuilder/LaunchList.cs): which list a version is in, its mark, and what is
// picked. No window, no game: dotnet run --project tests/builder_launcher/BuilderLauncherTests.csproj
static class Program
{
    static int checks;

    static void Require(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }

    static ReleaseInfo R(string tag, bool download = true) => new(tag, LaunchList.IsPre(tag), download);
    static string Names(IEnumerable<LaunchChoice> list) => string.Join(" | ", list.Select(c => c.Label));
    static readonly (string, string)[] NoDev = Array.Empty<(string, string)>();
    static readonly string[] NoBuilders = Array.Empty<string>();

    static int Main()
    {
        // ---- the drawing (Paper, "v2 - Version List by channel"): v2.1.0 in the folder and the latest, a pre-release ahead
        // of it, thirteen releases before it, a dev build beside the folder ----
        var github = new List<ReleaseInfo> { R("v2.2.0-rc.1"), R("v2.1.0") };
        for (var i = 14; i >= 2; i--) github.Add(R($"v2.0.{i}"));
        var dev = new[] { (@"C:\Games\game-dev", "pre-2.1.1 53ac43d") };
        var l = LaunchList.Make(github, "v2.1.0", "v2.1.0", NoBuilders, dev, rebuilds: true, pick: null, open: false);
        Require(Names(l.Releases) == "v2.1.0 - Latest | v2.0.14", "the two newest releases stay in sight: " + Names(l.Releases));
        Require(l.Older == 12 && l.Folded, $"twelve releases fold into one row: {l.Older}, {l.Folded}");
        Require(Names(l.Pre) == "v2.2.0-rc.1" && Names(l.Dev) == "pre-2.1.1 53ac43d", "one pre-release, one dev build");
        Require(l.Picked?.Tag == "v2.1.0" && l.Picked.Ticked && !l.Picked.Arrow, "the folder's release is picked and ticked");
        Require(l.Picked!.Title == "v2.1.0 - Latest", "the latest says so under the button");
        var before = l.Releases[1];
        Require(!before.Here && before.Reach && before.Arrow && !before.Dim && !before.Ticked, "a release not here has the arrow");
        Require(l.Pre[0].Arrow && l.Pre[0].Title == "v2.2.0-rc.1 - Pre-release", "a pre-release not here: the arrow, and its kind under the button");
        Require(l.Dev[0].Here && !l.Dev[0].Arrow && !l.Dev[0].Ticked && l.Dev[0].Title == "pre-2.1.1 53ac43d - Dev build", "a dev build is here, unmarked");

        // (unfolded: every release, the row still there to fold them again)
        l = LaunchList.Make(github, "v2.1.0", "v2.1.0", NoBuilders, dev, true, null, open: true);
        Require(l.Releases.Count == 14 && l.Older == 12 && !l.Folded, $"unfolded: {l.Releases.Count} releases, {l.Older}, {l.Folded}");

        // (the pre-release picked: it has the accent's place, keeps its arrow, and the folder's release loses its tick)
        l = LaunchList.Make(github, "v2.1.0", "v2.1.0", NoBuilders, dev, true, "release:v2.2.0-rc.1", false);
        Require(l.Picked?.Tag == "v2.2.0-rc.1" && l.Picked.Arrow && !l.Picked.Ticked, "the pre-release picked keeps its arrow");
        Require(!l.Releases[0].Ticked && !l.Releases[0].Arrow && l.Releases[0].Here, "the folder's release: here, not picked, no mark");

        // (a folded release picked comes into sight, and the fold is one shorter)
        l = LaunchList.Make(github, "v2.1.0", "v2.1.0", NoBuilders, dev, true, "release:v2.0.5", false);
        Require(Names(l.Releases) == "v2.1.0 - Latest | v2.0.14 | v2.0.5" && l.Older == 11, "a picked older release is shown: " + Names(l.Releases));

        // (a dev build picked)
        l = LaunchList.Make(github, "v2.1.0", "v2.1.0", NoBuilders, dev, true, @"dev:C:\Games\game-dev", false);
        Require(l.Picked?.Kind == LaunchKind.Dev && l.Picked.Ticked, "a dev build picked is ticked");
        // (the same folder written in small letters, as launcher.json's own names are: still the one picked)
        l = LaunchList.Make(github, "v2.1.0", "v2.1.0", NoBuilders, dev, true, @"dev:c:\games\GAME-dev", false);
        Require(l.Picked?.Kind == LaunchKind.Dev, "a pick is found whatever its capitals");

        // ---- a newer release is out: it is first in Releases with the arrow, the folder's below it, still picked ----
        github = new List<ReleaseInfo> { R("v2.2.0"), R("v2.2.0-rc.1"), R("v2.1.0"), R("v2.0.14") };
        l = LaunchList.Make(github, "v2.2.0", "v2.1.0", NoBuilders, NoDev, true, null, false);
        Require(Names(l.Releases) == "v2.2.0 - Latest | v2.1.0 | v2.0.14" && l.Older == 0, "one older release is not folded: " + Names(l.Releases));
        Require(l.Releases[0].Arrow && l.Picked?.Tag == "v2.1.0", "the new release has the arrow; the folder's stays picked");
        Require(l.Pre.Count == 0, "a pre-release of a release that is out is not listed");
        Require(LaunchList.Newer("v2.2.0", "v2.1.0") && LaunchList.Newer("v2.2.0", "v2.2.0-rc.1") && !LaunchList.Newer("v2.2.0-rc.1", "v2.2.0")
                && !LaunchList.Newer("v2.1.0", "v2.1.0") && LaunchList.Newer("v2.2.0-rc.1", "v2.1.0"), "what is ahead of what");
        // (the folder holds that old pre-release: it is listed, as what is here)
        l = LaunchList.Make(github, "v2.2.0", "v2.2.0-rc.1", NoBuilders, NoDev, true, null, false);
        Require(Names(l.Pre) == "v2.2.0-rc.1" && l.Pre[0].Ticked, "the pre-release the folder holds is listed and picked");

        // ---- what can be brought ----
        // (Longshot today: one release, with nothing to download; the folder holds it)
        l = LaunchList.Make(new[] { R("v1.0.0", download: false) }, "v1.0.0", "v1.0.0", new[] { "v1.0.0" }, NoDev, true, null, false);
        Require(Names(l.Releases) == "v1.0.0 - Latest" && l.Picked!.Ticked && l.Pre.Count == 0 && l.Dev.Count == 0 && l.Older == 0, "one release, here");
        // (a release with no builder to download is dim; one whose builder is installed here is not; nothing is, in a folder
        // that does not keep its game files)
        github = new List<ReleaseInfo> { R("v1.2.0"), R("v1.1.0", download: false), R("v1.0.0", download: false) };
        l = LaunchList.Make(github, "v1.2.0", "v1.2.0", new[] { "v1.0.0" }, NoDev, true, null, false);
        Require(l.Releases[1].Dim && !l.Releases[1].Arrow, "no builder to download, none here: dim");
        Require(l.Releases[2].Arrow && !l.Releases[2].Dim, "its builder is installed here: the arrow");
        l = LaunchList.Make(github, "v1.2.0", "v1.1.0", NoBuilders, NoDev, rebuilds: false, null, false);
        Require(l.Releases[0].Dim && !l.Releases[0].Arrow, "a folder without its game files: nothing can be brought");
        // (the same release written with and without its v is one release; GitHub's word on it wins)
        l = LaunchList.Make(new[] { R("v1.1.0") }, "v1.1.0", "1.1.0", new[] { "1.1.0" }, NoDev, true, null, false);
        Require(l.Releases.Count == 1 && l.Releases[0].Here && l.Releases[0].Label == "v1.1.0 - Latest", "v1.1.0 and 1.1.0 are one");
        // (nothing known at all: GitHub did not answer and the folder has no version)
        l = LaunchList.Make(Array.Empty<ReleaseInfo>(), null, null, NoBuilders, NoDev, false, null, false);
        Require(l.All.Count == 0 && l.Picked == null, "nothing to list");
        // (GitHub did not answer: what is on this PC is still listed, a pre-release the folder holds by its name)
        l = LaunchList.Make(Array.Empty<ReleaseInfo>(), null, "v1.2.0-rc.1", NoBuilders, dev, true, null, false);
        Require(Names(l.Pre) == "v1.2.0-rc.1" && l.Releases.Count == 0 && l.Dev.Count == 1 && l.Picked!.Kind == LaunchKind.Pre, "offline: the folder's pre-release and the dev build");

        Require(LaunchList.IsRelease("v1.0.0") && LaunchList.IsRelease("2.0.14") && LaunchList.IsRelease("v2.2.0-rc.1")
                && !LaunchList.IsRelease("pre-1.1.0 6353e9b") && !LaunchList.IsRelease("dev 86148ce") && !LaunchList.IsRelease("main"), "what a release's tag is");

        var lists = checks;
        Work();
        var work = checks - lists;
        Dev();
        Console.WriteLine($"launcher list: {lists} checks passed; launcher work: {work} checks passed; dev builds: {checks - lists - work} checks passed");
        return 0;
    }

    /// What the launcher says of a dev build (builder/HookshotBuilder/DevBuild.cs): from what its folder says it was built from
    /// (scripts/build-game.ps1) and what its source says now
    static void Dev()
    {
        // ---- the folder's own file ----
        var full = "0123456789abcdef0123456789abcdef01234567";
        var info = DevInfo.Parse(new[] { "name=pre-1.1.0 0123456", "channel=dev", "commit=" + full, "branch=dev", "changed=0", @"source=C:\Longshot\worktrees\dev", "built=2026-11-11T09:00:00Z" })!;
        Require(info.Name == "pre-1.1.0 0123456" && info.Commit == full && info.Changed == 0 && info.Source == @"C:\Longshot\worktrees\dev" && info.Branch == "dev", "a dev build's file, read");
        var old = DevInfo.Parse(new[] { "name=pre-1.1.0 6353e9b", "channel=dev" })!;
        Require(old.Commit == null && old.Source == null && old.Changed == 0, "one written before the commit was kept has its name alone");
        Require(DevInfo.Parse(new[] { "name=v1.0.0" }) == null && DevInfo.Parse(new[] { "name=x", "channel=release" }) == null, "not a dev build's");
        Require(DevInfo.Parse(new[] { "channel=dev", "commit=not a commit", "changed=many" }) is { Commit: null, Changed: 0, Name: "dev" }, "what is not a commit or a number is not taken for one");

        SourceNow Now(int changed = 0, int behind = 0, int since = 0, bool launcher = false, bool can = true, string? head = null) => new(head ?? full, changed, behind, since, launcher, can);
        DevState.Says Of(DevInfo i, SourceNow? n) => DevState.Of(i, n, "game-dev");

        // ---- no checkout on this PC (or no git): as before, nothing offered ----
        var s = Of(info, null);
        Require(s.Line == "In game-dev, with its own saves and settings" && !s.Update && !s.News, "no source here: " + s.Line);

        // ---- the newest ----
        s = Of(info, Now());
        Require(s.Line == "Up to date with dev" && !s.Update && !s.News, "the newest: " + s.Line);
        Require(s.Tip.Contains("Built from 0123456.") && s.Tip.Contains(@"Source: C:\Longshot\worktrees\dev, at 0123456"), "what it was built from, under the pointer: " + s.Tip);
        s = Of(info, Now(behind: -1));
        Require(s.Line == "GitHub's dev could not be checked" && !s.Update, "GitHub not reached is not 'up to date': " + s.Line);

        // ---- GitHub's dev is ahead ----
        s = Of(info, Now(behind: 3));
        Require(s.Line == "3 commits behind dev" && s.Update && s.News, "behind, and the button brings it: " + s.Line);
        Require(Of(info, Now(behind: 1)).Line == "1 commit behind dev", "one commit is one commit");
        s = Of(info, Now(behind: 3, changed: 14));
        Require(s.Line == "3 commits behind dev · the source has 14 uncommitted changes: commit them to update" && !s.Update && s.News,
                "behind, with work not committed in the source: said, and nothing is pulled over it: " + s.Line);
        s = Of(info, Now(behind: 3, changed: 14, since: 2));
        Require(s.Update, "but what the source already has that the build has not is still built");

        // ---- the source moved on since the build ----
        s = Of(info, Now(since: 2));
        Require(s.Line == "The source has 2 newer commits than this build" && s.Update && s.News, s.Line);
        s = Of(old with { Commit = "abcdef0" }, Now(since: -1));
        Require(s.Update && s.Line == "The source is not at the commit this was built from", "a commit the source does not know: " + s.Line);
        Require(!Of(info with { Commit = "0123456" }, Now(since: -1)).Update, "a short commit that is the source's own is the same commit");

        // ---- uncommitted work, with nothing behind ----
        s = Of(info with { Changed = 14 }, Now(changed: 14));
        Require(s.Line == "Up to date with dev · built with 14 uncommitted changes" && !s.Update && !s.News, s.Line);
        Require(s.Tip.Contains("Built from 0123456 with 14 uncommitted changes."), s.Tip);
        s = Of(info, Now(changed: 2));
        Require(s.Line == "Up to date with dev · the source has 2 uncommitted changes" && !s.Update, s.Line);

        // ---- the launcher itself ----
        s = Of(info, Now(launcher: true));
        Require(s.Line == "This build is up to date · the launcher is behind its source" && s.Update && s.News, s.Line);

        // ---- a source from before it could update itself: said, not offered ----
        s = Of(info, Now(behind: 3, can: false));
        Require(!s.Update && s.Line.StartsWith("3 commits behind dev (its source cannot update itself yet"), s.Line);

        // ---- the strip and the two lines while it is brought up to date (LaunchWork.cs, dev) ----
        var w = new LaunchWork("the dev build", ahead: true, fetch: false, dev: true);
        Require(w.Title == "Updating the dev build - 0%" && w.Heading == "Updating the dev build", w.Title);
        foreach (var line in new[] { "@@stage source Bringing the source up to date", "the source was brought forward 3 commit(s), to abcdef0" }) w.Line(line);
        Require(w.Fraction == 0 && w.Detail == "Bringing the source up to date", "the source takes a moment and no share: " + w.Detail);
        foreach (var line in new[] { "@@stage compile", "@@progress 0.5" }) w.Line(line);
        Require(Math.Abs(w.Fraction - 0.9 * (0.20 + 0.35)) < 1e-9, $"the game's build has nine tenths: {w.Fraction}");
        foreach (var line in new[] { "@@stage shaders", "@@progress 1" }) w.Line(line);
        Require(Math.Abs(w.Fraction - 0.9) < 1e-9, $"built, with the launcher to come: {w.Fraction}");
        foreach (var line in new[] { "@@stage launcher Building the launcher", "@@progress 0.8" }) w.Line(line);
        Require(Math.Abs(w.Fraction - 0.98) < 1e-9 && w.Detail == "Building the launcher", $"the launcher has the last tenth: {w.Fraction}");
        w.Line(@"@@done C:\Longshot\game-dev");
        Require(w.Fraction == 1 && w.Done == @"C:\Longshot\game-dev", "done");
        Require(new LaunchWork("the dev build", true, false, dev: true).StoppedTitle == "The update of the dev build stopped", "and when it stopped");
    }

    /// What the launcher says while it brings a version (builder/HookshotBuilder/LaunchWork.cs), from the lines of the hidden
    /// programs that do it: the setup's engine mode (builder/setup/setup.c), then that version's make_game.py
    static void Work()
    {
        static void Feed(LaunchWork w, params string[] lines) { foreach (var l in lines) w.Line(l); }

        // ---- a version whose builder is downloaded first ----
        var w = new LaunchWork("v2.2.0", ahead: true, fetch: true);
        Require(w.Title == "Updating to v2.2.0 - 0%" && w.Detail == "Getting its builder", "before a line: " + w.Title + " / " + w.Detail);
        Feed(w, "@@setup Looking for the Longshot Builder of v2.2.0...", "@@setup Downloading part 1 of 4 (310 MB)...", "@@percent 50");
        Require(w.Fraction is > 0.014 and < 0.016, $"half of the first of four parts is an eighth of the builder's share: {w.Fraction}");
        Require(w.Detail == "Getting its builder: downloading part 1 of 4 (310 MB)", "the setup's words, in the launcher's sentence: " + w.Detail);
        Feed(w, "@@percent 100", "@@setup Unpacking part 1 of 4...", "@@setup Part 2 of 4 unchanged: kept from the installed version");
        Require(w.Fraction is > 0.029 and < 0.031, $"the second part begun: {w.Fraction}");
        Feed(w, "@@percent 20");   // (a late line of the part before must not move it back)
        var before = w.Fraction;
        Feed(w, "@@setup Downloading part 1 of 4 (310 MB)...", "@@percent 0");
        Require(w.Fraction >= before, "the number never goes back");
        Feed(w, "@@installed v2.2.0");
        Require(Math.Abs(w.Fraction - 0.12) < 1e-9, $"the builder is here: its whole share, {w.Fraction}");
        Feed(w, "@@stage read", "@@stage lift", "@@progress 0.5");
        Require(Math.Abs(w.Fraction - (0.12 + 0.88 * (0.05 + 0.15 * 0.5))) < 1e-9, $"make_game.py's stages fill the rest: {w.Fraction}");
        Require(w.Detail == "Translating the game to PC code", w.Detail);
        Feed(w, "@@percent 99");
        Require(Math.Abs(w.Fraction - (0.12 + 0.88 * 0.125)) < 1e-9, "the setup's percent means nothing once the build runs");
        Feed(w, "@@stage compile", "@@plan 8 516", "@@file a.c 3 new", "@@file b.c 4 again", "@@file c.c 1 kept", "@@progress 0.25", "@@eta 190");
        Require(w.Detail == "Compiling changed files (2 of 8) · about 3 minutes left", w.Detail);
        Require(w.Title == $"Updating to v2.2.0 - {(int)((0.12 + 0.88 * (0.20 + 0.70 * 0.25)) * 100)}%", w.Title);
        Require(w.Done == null && w.Error == null, "not done, no error");
        // (the shaders' step, where the version has it: the game's own lines, passed on by make_game.py, and its plain one
        // when it left them to the first start)
        Feed(w, "@@stage finish", "@@stage shaders Preparing the shaders", "@@progress 0.5");
        Require(Math.Abs(w.Fraction - (0.12 + 0.88 * 0.96)) < 1e-9 && w.Detail == "Preparing the shaders", $"the shaders' step has its share: {w.Fraction}, {w.Detail}");
        Feed(w, "the shaders were not prepared now (no usable Vulkan here): the game prepares them at its first start");
        Require(w.Done == null && w.Error == null, "shaders left to the first start are not a failure");
        Feed(w, @"@@done C:\Games\Longshot");
        Require(w.Done == @"C:\Games\Longshot" && w.Fraction == 1, "done, and the folder it says");

        // ---- one whose builder is here: the build is the whole of it; a switch back says so ----
        w = new LaunchWork("v2.0.14", ahead: false, fetch: false);
        Require(w.Title == "Switching to v2.0.14 - 0%" && w.Detail == "Starting", w.Title + " / " + w.Detail);
        Feed(w, "@@stage compile", "@@progress 0.5");
        Require(Math.Abs(w.Fraction - (0.20 + 0.35)) < 1e-9, $"no share kept for a download: {w.Fraction}");
        Require(w.Detail == "Compiling changed files", "no count and no time until the tools give them: " + w.Detail);
        Feed(w, "@@eta 20");
        Require(w.Detail == "Compiling changed files · less than a minute left", w.Detail);
        Feed(w, "@@error The compiler stopped on src_4.c.");
        Require(w.Error == "The compiler stopped on src_4.c." && w.Done == null && w.StoppedTitle == "The switch to v2.0.14 stopped", "why it stopped, and what the launcher calls it");
        Require(new LaunchWork("v2.2.0", true, true).StoppedTitle == "The update to v2.2.0 stopped", "an update that stopped");

        // ---- tools that say what this launcher does not know (a later version's, an older one's) ----
        w = new LaunchWork("v3.0.0", ahead: true, fetch: false);
        Feed(w, "@@stage lift", "@@progress 1", "@@stage polish", "@@progress 0.5", "@@whatever 12", "plain words from a compiler", "@@progress nonsense");
        Require(Math.Abs(w.Fraction - 0.20) < 1e-9 && w.Detail == "Working", $"an unknown stage moves nothing and has plain words: {w.Fraction}, {w.Detail}");
        Feed(w, "@@stage finish");
        Require(Math.Abs(w.Fraction - 0.90) < 1e-9, $"a version with no shaders' step is at its last step here: {w.Fraction}");
        Feed(w, "@@done X");
        Require(w.Fraction == 1 && w.Done == "X", "and the build still ends");
        Require(LaunchWork.TimeLeft(100) == "about 2 minutes left" && LaunchWork.TimeLeft(60) == "about a minute left" && LaunchWork.TimeLeft(4000) == "about 1 h 7 min left", "time left, in words");
    }
}
