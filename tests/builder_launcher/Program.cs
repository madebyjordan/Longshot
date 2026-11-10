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

        Console.WriteLine($"launcher list: {checks} checks passed");
        return 0;
    }
}
