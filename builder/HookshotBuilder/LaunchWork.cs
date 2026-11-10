using System.Globalization;
using System.Text.RegularExpressions;

namespace HookshotBuilder;

/// What the launcher says while it brings a version (Paper: "v2 - Updating"): the lines of the programs that do it, read into
/// one number and two lines of words. Those programs are hidden and not the launcher's own: the setup, in its engine mode,
/// installs that version's builder beside the others (builder/setup/setup.c: @@setup, @@percent), and then that version's own
/// make_game.py builds the folder (@@stage, @@progress, @@plan, @@file, @@eta, @@done). Nothing here touches the window, the
/// disk or a process.
///
/// A version's tools may say things this launcher does not know (a stage added later, a line worded differently): what is not
/// understood moves nothing and is kept in the details, and the number never goes back.
sealed class LaunchWork
{
    /// (how much of the whole each part is: getting the builder, when it has to be downloaded, then make_game.py's stages as
    /// the builder's own window weighs them)
    const double Fetch = 0.12;
    static readonly Dictionary<string, (double From, double Size, string Says)> Stages = new()
    {
        ["read"] = (0, 0.05, "Reading the game files kept in the folder"),
        ["lift"] = (0.05, 0.15, "Translating the game to PC code"),
        ["compile"] = (0.20, 0.76, "Compiling changed files"),
        ["finish"] = (0.96, 0.04, "Swapping in the new game"),
    };
    static readonly Regex PartOf = new(@"[Pp]art (\d+) of (\d+)");

    readonly string name, verb;
    readonly bool fetch;
    string? stage;          // (make_game.py's stage now; null while the builder is still being got)
    int part = 1, parts = 1;
    int changed = -1, compiled;
    double eta = -1;
    string says;

    /// name: the version, as written ("v2.2.0"); ahead: it is ahead of the one the folder holds (an update), else a switch back;
    /// fetch: its builder is not on this PC and is downloaded first
    public LaunchWork(string name, bool ahead, bool fetch)
    {
        (this.name, this.fetch) = (name, fetch);
        verb = ahead ? "Updating to" : "Switching to";
        says = fetch ? "Getting its builder" : "Starting";
    }

    public double Fraction { get; private set; }
    /// (under the button: "Updating to v2.2.0 - 42%")
    public string Title => $"{verb} {name} - {(int)(Fraction * 100)}%";
    /// (the line under that: "Compiling changed files (7 of 516) · about 3 minutes left")
    public string Detail
    {
        get
        {
            if (stage != "compile") return says;
            var files = changed > 0 ? $" ({compiled} of {changed})" : "";
            return says + files + (eta >= 0 ? " · " + TimeLeft(eta) : "");
        }
    }
    /// The game folder, once make_game.py said it is done (the build succeeded whatever comes after)
    public string? Done { get; private set; }
    /// Why it stopped, as the program that stopped said it
    public string? Error { get; private set; }

    /// What the stopped state says under the button's "Retry": the version, and the reason in one line
    public string StoppedTitle => $"The {(verb.StartsWith("Updating") ? "update" : "switch")} to {name} stopped";

    public void Line(string line)
    {
        if (line.StartsWith("@@error ")) Error = line[8..].Trim();
        else if (line.StartsWith("@@done ")) Done = line[7..].Trim();
        else if (line.StartsWith("@@setup "))
        {
            // ("Downloading part 2 of 7 (310 MB)...", "Part 3 of 7 unchanged: kept from the installed version")
            var text = line[8..].Trim().TrimEnd('.');
            if (PartOf.Match(text) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var i) && int.TryParse(m.Groups[2].Value, out var n) && n > 0)
            {
                (part, parts) = (Math.Clamp(i, 1, n), n);
                Reach(Fetch * (part - 1) / parts);
            }
            if (text.Length > 0) says = "Getting its builder: " + char.ToLowerInvariant(text[0]) + text[1..];
        }
        else if (line.StartsWith("@@percent "))
        {
            if (stage == null && int.TryParse(line[10..], out var p)) Reach(Fetch * (part - 1 + Math.Clamp(p, 0, 100) / 100.0) / parts);
        }
        else if (line.StartsWith("@@installed ")) Reach(Fetch);
        else if (line.StartsWith("@@stage "))
        {
            stage = line[8..].Trim().Split(' ')[0];
            if (Stages.TryGetValue(stage, out var s))
            {
                says = s.Says;
                Reach(Whole(s.From));
            }
            else says = "Working";   // (a stage this launcher has no words for)
        }
        else if (line.StartsWith("@@progress "))
        {
            if (stage != null && Stages.TryGetValue(stage, out var s)
                && double.TryParse(line[11..], NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                Reach(Whole(s.From + s.Size * Math.Clamp(f, 0, 1)));
        }
        else if (line.StartsWith("@@plan "))
        {
            var p = line.Split(' ');
            if (p.Length == 3 && int.TryParse(p[1], out var c)) (changed, compiled) = (c, 0);
        }
        else if (line.StartsWith("@@file "))
        {
            var p = line.Split(' ');
            if (p.Length == 4 && p[3] is "new" or "again") compiled++;
        }
        else if (line.StartsWith("@@eta "))
        {
            if (double.TryParse(line[6..], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) eta = seconds;
        }
    }

    /// (make_game.py's share of the whole: all of it when nothing is downloaded first)
    double Whole(double f) => fetch ? Fetch + (1 - Fetch) * f : f;
    void Reach(double f) => Fraction = Math.Max(Fraction, Math.Clamp(f, 0, 1));

    public static string TimeLeft(double seconds)
    {
        if (seconds < 45) return "less than a minute left";
        if (seconds < 90) return "about a minute left";
        var minutes = (int)Math.Round(seconds / 60);
        return minutes >= 60 ? $"about {minutes / 60} h {minutes % 60} min left" : $"about {minutes} minutes left";
    }
}
