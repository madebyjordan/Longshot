namespace HookshotBuilder;

/// What a dev build's folder says of itself: hookshot_build.txt, written by scripts/build-game.ps1 when it builds the folder
/// from a checkout of the source. commit: the commit built; changed: how many files of the checkout were changed and not
/// committed then; source: the checkout. One written before these were kept has the name alone
sealed record DevInfo(string Name, string? Commit, int Changed, string? Source, string? Branch)
{
    /// null unless the lines are a dev build's (channel=dev)
    public static DevInfo? Parse(IEnumerable<string> lines)
    {
        var said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var at = line.IndexOf('=');
            if (at > 0) said[line[..at].Trim()] = line[(at + 1)..].Trim();
        }
        if (said.GetValueOrDefault("channel") != "dev") return null;
        var commit = said.GetValueOrDefault("commit");
        return new DevInfo(said.GetValueOrDefault("name") is { Length: > 0 } name ? name : "dev",
                           commit is { Length: >= 7 } && commit.All(Uri.IsHexDigit) ? commit : null,
                           int.TryParse(said.GetValueOrDefault("changed"), out var n) && n > 0 ? n : 0,
                           said.GetValueOrDefault("source") is { Length: > 0 } source ? source : null,
                           said.GetValueOrDefault("branch") is { Length: > 0 } branch ? branch : null);
    }
}

/// What the checkout a dev build was made from says now (Launcher.cs asks git). head: its commit; changed: its files changed
/// and not committed; behind: the commits GitHub's dev has that it has not (-1: could not be asked); sinceBuild: its commits
/// since the one the build was made from (-1: not known); launcherBehind: the builder's own source differs from what the
/// launcher that is running was built from; canUpdate: it has the script that brings all of it up to date
/// (scripts/dev-update.ps1)
sealed record SourceNow(string Head, int Changed, int Behind, int SinceBuild, bool LauncherBehind, bool CanUpdate);

/// What the launcher says of a dev build, so that nobody has to remember what it was built from or whether it is the
/// newest: one line under the button, whether that line is news, and whether the button says Update. Nothing here touches
/// the window, the disk or git
static class DevState
{
    public sealed record Says(string Line, bool News, bool Update, string Tip);

    static string Count(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";
    static string Short(string commit) => commit.Length > 7 ? commit[..7] : commit;

    /// folder: the dev build's folder, as it is named ("game-dev"); now: null when its checkout is not on this PC, or git
    /// is not
    public static Says Of(DevInfo info, SourceNow? now, string folder)
    {
        var where = $"In {folder}, with its own saves and settings";
        var built = info.Commit == null ? "" : $"Built from {Short(info.Commit)}"
                  + (info.Branch is { } b && b != "dev" ? $" ({b})" : "")
                  + (info.Changed > 0 ? $" with {Count(info.Changed, "uncommitted change")}" : "") + ". ";
        if (now == null) return new Says(info.Changed > 0 ? $"{where} · built with {Count(info.Changed, "uncommitted change")}" : where, false, false, built + where + ".");

        var tip = built + (info.Source != null ? $"Source: {info.Source}, at {Short(now.Head)}"
                           + (now.Changed > 0 ? $" with {Count(now.Changed, "uncommitted change")}" : "") + ". " : "") + where + ".";
        // (the build is not what the source is now: commits since it, or another commit altogether)
        var stale = now.SinceBuild > 0 || (info.Commit != null && now.SinceBuild < 0 && !now.Head.StartsWith(info.Commit, StringComparison.OrdinalIgnoreCase)
                                                                 && !info.Commit.StartsWith(now.Head, StringComparison.OrdinalIgnoreCase));
        // (GitHub's dev is ahead and the source can go there: nothing of it is uncommitted)
        var pull = now.Behind > 0 && now.Changed == 0;
        var update = now.CanUpdate && (pull || stale || now.LauncherBehind);
        string line;
        if (now.Behind > 0 && now.Changed > 0)
            line = $"{Count(now.Behind, "commit")} behind dev · the source has {Count(now.Changed, "uncommitted change")}: commit them to update";
        else if (now.Behind > 0) line = $"{Count(now.Behind, "commit")} behind dev";
        else if (stale) line = now.SinceBuild > 0 ? $"The source has {Count(now.SinceBuild, "newer commit")} than this build" : "The source is not at the commit this was built from";
        else if (now.LauncherBehind) line = "This build is up to date · the launcher is behind its source";
        else
        {
            line = now.Behind < 0 ? "GitHub's dev could not be checked" : "Up to date with dev";
            if (info.Changed > 0) line += $" · built with {Count(info.Changed, "uncommitted change")}";
            else if (now.Changed > 0) line += $" · the source has {Count(now.Changed, "uncommitted change")}";
            return new Says(line, false, update, tip);
        }
        if (!now.CanUpdate) line += " (its source cannot update itself yet: bring it forward once by hand)";
        return new Says(line, true, update, tip);
    }
}
