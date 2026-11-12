using System.Net.Http;
using System.Text.Json;
using HookshotBuilder;

namespace Hookshot.Launcher;

/// Hookshot's published releases, newest first, as the WPF launcher reads them (builder/HookshotBuilder/Updater.cs, ReleaseList): each
/// one's tag, whether it is a pre-release, whether it holds a builder to download (the setup and its list, or the whole zip), and the
/// latest release's tag (not a pre-release). Empty and null when GitHub did not answer: the launcher shows what is on this PC
static class ReleaseFeed
{
    const string Releases = "https://api.github.com/repos/NotRexed/Hookshot/releases";
    const string Setup = "HookshotBuilder.exe", SetupList = "hookshot-setup.txt", LinuxList = "hookshot-linux.txt";

    public static async Task<(List<ReleaseInfo> Releases, string? Latest)> Get()
    {
        // (development) HOOKSHOT_LATEST_TAG=v1.2.0-rc.1,v1.1.0,v1.0.0?: these instead of GitHub's, newest first. One with a part after its
        // numbers is a pre-release, the first without is the latest, one ending in ? has no builder to download; none: no answer
        if (Environment.GetEnvironmentVariable("HOOKSHOT_LATEST_TAG") is { Length: > 0 } fake)
        {
            var made = fake == "none" ? new List<ReleaseInfo>()
                : fake.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Select(t => new ReleaseInfo(t.TrimEnd('?'), LaunchList.IsPre(t), !t.EndsWith('?'), !t.EndsWith('?'))).ToList();
            return (made, made.FirstOrDefault(r => !r.Pre)?.Tag);
        }
        var found = new List<ReleaseInfo>();
        string? latest = null;
        bool nightlyHere = false, nightlyList = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Hookshot");
            using var request = new HttpRequestMessage(HttpMethod.Get, Releases + "?per_page=30");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (found, null);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.GetProperty("draft").GetBoolean() || r.GetProperty("tag_name").GetString() is not { Length: > 0 } tag) continue;
                var names = new HashSet<string?>();
                if (r.TryGetProperty("assets", out var assets))
                    foreach (var asset in assets.EnumerateArray()) names.Add(asset.GetProperty("name").GetString());
                // (the rolling nightly: listed under the version it is now, read from its own setup list below)
                if (tag == "nightly")
                {
                    nightlyList = names.Contains(Setup) && names.Contains(SetupList);
                    nightlyHere = OperatingSystem.IsWindows() ? nightlyList : names.Contains(LinuxList);
                    continue;
                }
                if (!LaunchList.IsRelease(tag)) continue;   // (only versions: other names are not listed)
                var pre = r.GetProperty("prerelease").GetBoolean();
                // (what this launcher installs a release's builder from: on Windows the setup and its list, Builders.Install's engine
                // mode; on Linux the list linux_update.py reads, and the AppImage for a first builder. A release with only the zip
                // was the WPF builder's own update)
                var list = names.Contains(Setup) && names.Contains(SetupList);
                var here = OperatingSystem.IsWindows() ? list
                    : names.Contains(LinuxList) && names.Any(n => n != null && n.StartsWith("Hookshot") && !n.StartsWith("Hookshot-Launcher") && n.EndsWith("x86_64.AppImage"));
                found.Add(new ReleaseInfo(tag, pre, here, list));
                if (latest == null && !pre) latest = tag;
            }
            // (the nightly's version, git describe's v2.2.0-4-g1a2b3c4: the "tag" line of its setup list, which the setup checks when
            // it installs it by that name, from the rolling nightly release, builder/setup/setup.c nightly_tag)
            if (nightlyHere && await NightlyTag(http) is { } nightly && found.All(f => !LaunchList.SameTag(f.Tag, nightly)))
                found.Add(new ReleaseInfo(nightly, true, true, nightlyList));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException
                                       or KeyNotFoundException) { }
        return (found, latest);
    }

    /// (the version the nightly is now: its setup list's "tag" line; null when it cannot be read or is not a version)
    static async Task<string?> NightlyTag(HttpClient http)
    {
        try
        {
            var list = await http.GetStringAsync("https://github.com/NotRexed/Hookshot/releases/download/nightly/" +
                                                 (OperatingSystem.IsWindows() ? SetupList : LinuxList));
            var tag = list.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("tag "))?[4..].Trim();
            return tag != null && LaunchList.IsRelease(tag) ? tag : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException) { return null; }
    }
}
