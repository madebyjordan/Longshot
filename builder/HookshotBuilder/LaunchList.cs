using System.Text.RegularExpressions;

namespace HookshotBuilder;

/// A release as GitHub lists it: its tag, whether it is a pre-release, and whether it holds the builder to download (the setup
/// and its list, or the whole zip). One without can only be played or built here when it is already on this PC
sealed record ReleaseInfo(string Tag, bool Pre, bool Download);

enum LaunchKind { Release, Pre, Dev }

/// One version in the launcher's bar: a release or a pre-release (played in the launch folder, which holds one of them at a
/// time and is updated or built again to another first), or a dev build in a folder of its own beside it (nx2/version.py
/// names it "pre-1.1.0 6353e9b"; scripts/build-game.ps1 makes one and writes its hookshot_build.txt)
sealed class LaunchChoice
{
    public LaunchKind Kind { get; init; }
    public string Label { get; init; } = "";   // (in the bar: "v2.1.0 - Latest", "v2.2.0-rc.1", "pre-2.1.1 53ac43d")
    public string Title { get; init; } = "";   // (under the button: "v2.2.0-rc.1 - Pre-release", "pre-2.1.1 53ac43d - Dev build")
    public string? Tag { get; init; }          // (a release's tag, v1.0.0)
    public string? Folder { get; init; }       // (a dev build's folder)
    public string? Build { get; init; }        // (a dev build's name, "pre-1.1.0 6353e9b")
    public bool Here { get; init; }            // (on this PC: the release the launch folder holds, or a dev build)
    public bool Reach { get; init; }           // (not here, and the button brings it: its builder is installed, or its release has one)
    public bool Picked { get; set; }
    public bool Stale { get; set; }            // (a dev build that Update would bring forward: DevState, DevBuild.cs)
    public string Key => Tag != null ? "release:" + Tag : "dev:" + Folder;

    // (the row, MainWindow.xaml's VersionRow: no mark means it is here, the arrow that the button brings it, dim that nothing can)
    public bool Ticked => Picked && Here && !Stale;
    public bool Arrow => (!Here && Reach) || Stale;
    public bool Dim => !Here && !Reach && !Picked;
    public string? Tip => Folder ?? (Here ? null : Reach ? "Not on this PC yet" : "Not on this PC, and its release has no builder to download");
}

/// The bar's three lists (MainWindow.xaml, as drawn in Paper's "v2 - Version List by channel"): what a version is comes
/// first, whether it is on this PC is the mark at its right. Nothing here touches the window, the disk or the network
static class LaunchList
{
    /// (the two newest releases stay in sight; the rest fold into one row when that hides two or more)
    const int Newest = 2;

    public sealed record Lists(List<LaunchChoice> All, List<LaunchChoice> Releases, int Older, bool Folded,
                               List<LaunchChoice> Pre, List<LaunchChoice> Dev, LaunchChoice? Picked);

    /// github: GitHub's releases; latest: its latest release's tag; built: the release the launch folder holds; builders: the
    /// releases whose builder is installed here; dev: the dev builds beside the folder; rebuilds: the folder keeps its game files
    /// (it can be built as another release); pick: the key of the version picked; open: the older releases are unfolded.
    /// Older: how many releases the fold row stands for (0: no fold row); Folded: they are out of the list now
    public static Lists Make(IReadOnlyList<ReleaseInfo> github, string? latest, string? built, ICollection<string> builders,
                             IReadOnlyList<(string Dir, string Build)> dev, bool rebuilds, string? pick, bool open)
    {
        var known = new Dictionary<string, ReleaseInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in github) known[VersionName(r.Tag)] = r;
        // (on this PC and no longer, or not yet, among GitHub's: the folder's own release, an installed builder's)
        foreach (var t in built != null ? builders.Append(built) : builders)
            if (IsRelease(t)) known.TryAdd(VersionName(t), new ReleaseInfo(t, IsPre(t), false));

        var all = new List<LaunchChoice>();
        foreach (var r in known.Values.OrderByDescending(r => ReleaseOrder(r.Tag)).ThenBy(r => r.Pre))
        {
            var here = SameTag(r.Tag, built);
            var name = VersionName(r.Tag);
            var newest = SameTag(r.Tag, latest);
            all.Add(new LaunchChoice
            {
                Kind = r.Pre ? LaunchKind.Pre : LaunchKind.Release,
                Tag = r.Tag,
                Label = newest ? name + " - Latest" : name,
                Title = newest ? name + " - Latest" : r.Pre ? name + " - Pre-release" : name,
                Here = here,
                Reach = !here && rebuilds && (r.Download || builders.Any(b => SameTag(b, r.Tag))),
            });
        }
        foreach (var (dir, build) in dev)
            all.Add(new LaunchChoice { Kind = LaunchKind.Dev, Folder = dir, Build = build, Label = build, Title = build + " - Dev build", Here = true });

        // (a key holds a folder or a tag: neither minds capitals, and the folder may come written another way)
        var picked = all.FirstOrDefault(c => string.Equals(c.Key, pick, StringComparison.OrdinalIgnoreCase))
                     ?? all.FirstOrDefault(c => SameTag(c.Tag, built))
                     ?? all.FirstOrDefault(c => c.Here)
                     ?? all.FirstOrDefault(c => c.Kind == LaunchKind.Release)
                     ?? all.FirstOrDefault();
        if (picked != null) picked.Picked = true;

        var releases = all.Where(c => c.Kind == LaunchKind.Release).ToList();
        var stay = releases.Where((c, i) => i < Newest || c.Here || c.Picked).ToList();
        var older = releases.Count - stay.Count;
        if (older < 2) (stay, older) = (releases, 0);   // (a row that says "1 older release" takes the place of the release itself)
        // (a pre-release is shown while it is ahead of the latest release: once that is out it is an old test build. The one
        // the folder holds, or the one picked, always is)
        var ahead = latest != null ? ReleaseOrder(latest) : null;
        var pre = all.Where(c => c.Kind == LaunchKind.Pre && (c.Here || c.Picked || ahead == null || ReleaseOrder(c.Tag!) > ahead)).ToList();
        return new Lists(all, open ? releases : stay, older, older > 0 && !open, pre, all.Where(c => c.Kind == LaunchKind.Dev).ToList(), picked);
    }

    /// (v1.0.0, 1.0.0, v2.2.0-rc.1: a release's tag, not a dev build's name or a branch's)
    public static bool IsRelease(string tag) => Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$");
    /// (a tag GitHub has not told us about: the part after the numbers says it is a pre-release, as Semantic Versioning has it)
    public static bool IsPre(string tag) => tag.Contains('-');
    public static bool SameTag(string? a, string? b) =>
        a != null && b != null && string.Equals(a.TrimStart('v', 'V'), b.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);
    public static Version ReleaseOrder(string tag) =>
        Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var v) ? v : new Version(0, 0);
    /// (as the launcher writes a release: v1.0.0)
    public static string VersionName(string tag) => "v" + tag.TrimStart('v', 'V');
    /// (a is ahead of b: a later release, or the release itself after its own pre-release)
    public static bool Newer(string a, string b) =>
        ReleaseOrder(a) != ReleaseOrder(b) ? ReleaseOrder(a) > ReleaseOrder(b) : !IsPre(a) && IsPre(b);
}
