using HookshotBuilder;
using Hookshot.Launcher;

// The launcher's side of the hand-over (launcher/Hookshot/Handover.cs, docs/launcher-upgrade.md) against the contract's sample
// files: the real Handover.cs, GameFolder.cs and BuilderSettings.cs; Builders is a stand-in that names a scratch settings file.
// Arguments: the fixtures folder, then a folder holding an import file the Linux upgrader itself wrote (linux_upgrade.write_import).
static class Program
{
    static int checks, failures;

    static void Check(bool condition, string message)
    {
        checks++;
        if (condition) return;
        failures++;
        Console.Error.WriteLine("FAIL: " + message);
    }

    static int Main(string[] args)
    {
        var fixtures = args[0];
        var scratch = Directory.CreateTempSubdirectory("launcher-handover-").FullName;
        try
        {
            Imports(fixtures);
            Applying(fixtures, scratch);
            FromTheLinuxBuilder(fixtures);
            Taking(scratch);
            AtStart(fixtures, scratch);
            CommandLine();
            if (args.Length > 1) FromTheUpgrader(args[1]);
        }
        finally { Directory.Delete(scratch, true); }
        Console.WriteLine($"Launcher hand-over checks: {checks - failures} passed, {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    static string Read(string fixtures, string name) => File.ReadAllText(Path.Combine(fixtures, name));

    static void Imports(string fixtures)
    {
        var w = Handover.Parse(Read(fixtures, "import-windows.json"));
        Check(w != null, "The Windows sample import was refused.");
        if (w == null) return;
        Check(w.Folder == @"C:\Users\player\Games\Hookshot" && w.Version == "v2.3.0-rc.2" && w.Track == "rc" && w.Default,
              "The Windows sample's folder, version, track or default was not read.");
        var s = w.Settings;
        Check(s.Out == w.Folder && s.Nup == @"C:\Users\player\Downloads\game.nup" && s.Romfs == null && s.Exefs == null,
              "The Windows sample's game files were not read.");
        Check(s.Dlss && s.DlssLicense && !s.Fsr && !s.FsrLicense && !s.PrepareLater && s.MemoryGb == 12 && s.Track == "rc",
              "The Windows sample's build settings were not read.");
        Check(w.Language == "fr-FR" && w.Work == @"D:\HookshotWork", "The Windows sample's language or work folder was not read.");

        var l = Handover.Parse(Read(fixtures, "import-linux.json"));
        Check(l != null, "The Linux sample import was refused.");
        if (l == null) return;
        Check(l.Folder == "/home/player/Games/Hookshot" && l.Version == "v2.3.0-4-g1a2b3c4" && l.Track == "nightly",
              "The Linux sample's folder, version or track was not read.");
        Check(l.Settings.Nup == null && l.Settings.Romfs == "/home/player/dump/romfs" && l.Settings.Exefs == "/home/player/dump/exefs",
              "The Linux sample's game folders were not read.");
        Check(!l.Settings.Dlss && l.Settings.DlssLicense && l.Settings.PrepareLater && l.Settings.MemoryGb == null && l.Work == null,
              "The Linux sample's build settings were not read.");

        Check(Handover.Parse(Read(fixtures, "import-windows.json").Replace("\"format\": 1", "\"format\": 2")) == null,
              "An import of a later format was taken.");
        Check(Handover.Parse("{\"format\": 1, \"folder\": \"Games/Hookshot\"}") == null, "An import with a relative folder was taken.");
        Check(Handover.Parse("{\"format\": 1}") == null, "An import with no folder was taken.");
        Check(Handover.Parse("{\"format\": 1, \"folder\": \"C:Games\"}") == null, "An import with a drive-relative folder was taken.");
        Check(Handover.Parse("{\"format\": 1, \"folder\": \"D:/Games\"}") != null && Handover.Parse("{\"format\": 1, \"folder\": \"/g\"}") != null,
              "An absolute folder of the other system was refused (the sample files are read on both).");
        Check(Handover.Parse("not json") == null, "A file that is not JSON was taken.");
        var bare = Handover.Parse("{\"format\": 1, \"folder\": \"/g\", \"version\": \"v2.2.0\"}");
        Check(bare != null && bare.Track == "release" && !bare.Settings.PrepareLater && bare.Default,
              "An import with only a folder and version did not take the defaults (release, shaders prepared).");

        foreach (var (version, track) in new[] { ("v2.3.0", "release"), ("v2.3.0-rc.2", "rc"), ("v2.3.0-4-g1a2b3c4", "nightly"),
                                                  ("v2.3.0-rc.1-3-g1a2b3c4", "nightly"), ("nightly-12-1a2b3c4d", "nightly"), (null, "release") })
            Check(Handover.TrackOf(version) == track, $"{version ?? "no version"} is not on the {track} track.");
    }

    /// The Windows sample applied to a launcher with no settings yet: the Builder's own saved settings come out (the sample
    /// builder-settings.json the import was made from), and its work folder is taken
    static void Applying(string fixtures, string scratch)
    {
        var dir = Path.Combine(scratch, "apply");
        Directory.CreateDirectory(dir);
        var settings = Path.Combine(dir, "builder-settings.json");
        var work = Path.Combine(dir, "work.txt");
        if (Handover.Parse(Read(fixtures, "import-windows.json")) is not { } import)
        {
            Check(false, "The Windows sample import was refused, so it could not be applied.");
            return;
        }
        Check(Handover.Apply(new[] { import }, settings, work), "Nothing was written for an import.");
        var got = BuilderSettings.Load(settings);
        var want = System.Text.Json.JsonSerializer.Deserialize(Read(fixtures, "builder-settings.json"), BuilderSettingsJson.Default.BuilderSettings);
        Check(got != null && want != null && got.ToJson() == want.ToJson(),
              $"The settings taken from the import are not the Builder's own: {got?.ToJson()} against {want?.ToJson()}.");
        Check(File.Exists(work) && File.ReadAllText(work).Trim() == @"D:\HookshotWork", "The import's work folder was not taken.");

        // (a launcher with settings of its own keeps them; only a default folder that holds no game gives way)
        var own = new BuilderSettings { Out = Path.Combine(dir, "nothing here"), Nup = "/mine.nup", MemoryGb = 6, Track = "release" };
        File.WriteAllText(settings, own.ToJson());
        Handover.Apply(new[] { import }, settings, null);
        got = BuilderSettings.Load(settings)!;
        Check(got.Out == import.Folder && got.Nup == "/mine.nup" && got.MemoryGb == 6 && got.Track == "release",
              "With settings of its own, the launcher did not keep them and take only the import's folder.");
        var game = Path.Combine(dir, "a game");
        Directory.CreateDirectory(Path.Combine(game, "bin"));
        File.WriteAllText(Path.Combine(game, "bin", OperatingSystem.IsWindows() ? "hookshot.exe" : "hookshot"), "");
        File.WriteAllText(settings, new BuilderSettings { Out = game }.ToJson());
        Handover.Apply(new[] { import }, settings, null);
        Check(BuilderSettings.Load(settings)!.Out == game, "The launcher's own default folder, a game, was replaced by the import's.");
        Check(!Handover.Apply(Array.Empty<Handover.Import>(), settings, null), "No import wrote the settings anyway.");
    }

    /// The Linux Builder's builder.ini read as if it had been handed over (a first start with nothing dropped): the sample
    /// import-linux.json, made from the same builder.ini
    static void FromTheLinuxBuilder(string fixtures)
    {
        var cfg = Handover.ReadIni(Read(fixtures, "builder.ini"));
        var got = Handover.FromIni(cfg, null, "v2.3.0-4-g1a2b3c4", null, oldLicense: false);
        var want = Handover.Parse(Read(fixtures, "import-linux.json"))!;
        Check(got != null && got.Folder == want.Folder && got.Version == want.Version && got.Track == want.Track &&
              got.Language == want.Language && got.Settings.ToJson() == want.Settings.ToJson(),
              $"builder.ini did not read as the sample import: {got?.Settings.ToJson()} against {want.Settings.ToJson()}.");
        cfg.Remove("nvidia_license");
        Check(Handover.FromIni(cfg, null, null, null, oldLicense: false)?.Settings.DlssLicense == false &&
              Handover.FromIni(cfg, null, null, null, oldLicense: true)?.Settings.DlssLicense == true,
              "NVIDIA's license accepted in the old SDK folder was not counted.");
        cfg["kind"] = "nup";
        var nup = Handover.FromIni(cfg, null, null, "en-GB", false)!;
        Check(nup.Settings.Nup == "/home/player/Downloads/old.nup" && nup.Settings.Romfs == null && nup.Language == "de-DE",
              "kind=nup did not give the nup alone, or builder.ini's language did not come first.");
        cfg.Remove("language");
        Check(Handover.FromIni(cfg, null, null, "en-GB", false)!.Language == "en-GB", "The folder's language.txt was not the fallback.");
        cfg.Remove("out");
        Check(Handover.FromIni(cfg, null, null, null, false) == null, "A builder.ini with no install folder made an import.");
        Check(Handover.ReadIni("a=1\r\nb=x=y\nnot a line\n=z\n") is var d && d["a"] == "1" && d["b"] == "x=y" && d.Count == 2,
              "builder.ini's lines were not read as the Builder reads them.");
    }

    /// import/: each file read and deleted; one that cannot be read kept as .bad; one still being written left alone
    static void Taking(string scratch)
    {
        var dir = Path.Combine(scratch, "import");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.json"), "{\"format\": 1, \"folder\": \"/games/one\"}");
        File.WriteAllText(Path.Combine(dir, "b.json"), "{\"format\": 2, \"folder\": \"/games/two\"}");
        File.WriteAllText(Path.Combine(dir, "c.json.part"), "{\"format\": 1, \"folder\": \"/games/three\"}");
        File.WriteAllText(Path.Combine(dir, "d.json"), "{");
        var got = Handover.Take(dir);
        Check(got.Count == 1 && got[0].Folder == "/games/one", "import/ did not give the one good file.");
        Check(!File.Exists(Path.Combine(dir, "a.json")), "An import taken was not deleted.");
        Check(File.Exists(Path.Combine(dir, "b.json.bad")) && File.Exists(Path.Combine(dir, "d.json.bad")),
              "An import that could not be taken was not kept as .bad.");
        Check(File.Exists(Path.Combine(dir, "c.json.part")), "An import still being written was touched.");
        Check(Handover.Take(Path.Combine(scratch, "no such folder")).Count == 0, "A missing import folder was not empty.");
    }

    /// At a start: HOOKSHOT_LAUNCHER_HOME's import/ taken into the launcher's settings before anything is shown
    static void AtStart(string fixtures, string scratch)
    {
        var home = Path.Combine(scratch, "home ü");
        Environment.SetEnvironmentVariable("HOOKSHOT_LAUNCHER_HOME", home);
        Builders.Base = Path.Combine(scratch, "builders");
        Check(Handover.Home == home && Handover.ImportDir == Path.Combine(home, "import"), "HOOKSHOT_LAUNCHER_HOME was not the home.");
        Directory.CreateDirectory(Handover.ImportDir);
        File.WriteAllText(Path.Combine(Handover.ImportDir, Handover.FileName(@"C:\Users\player\Games\Hookshot")), Read(fixtures, "import-windows.json"));
        Handover.AtStart();
        Check(BuilderSettings.Load(Builders.SettingsPath) is { Out: @"C:\Users\player\Games\Hookshot", MemoryGb: 12 },
              "The import dropped in the home was not in the launcher's settings after its start.");
        Check(Directory.GetFiles(Handover.ImportDir).Length == 0, "The import was left in import/.");
        Check(Handover.FileName("/a") == Handover.FileName("/a") && Handover.FileName("/a") != Handover.FileName("/b") &&
              Handover.FileName("/a").StartsWith("builder-") && Handover.FileName("/a").Length == "builder-".Length + 16 + 5,
              "An import file's name is not one a folder, as the upgrader names it.");
        Environment.SetEnvironmentVariable("HOOKSHOT_LAUNCHER_HOME", null);
        var unset = Handover.Home;
        Check(OperatingSystem.IsWindows() ? unset.EndsWith(@"\HookshotLauncher") : unset.EndsWith("/hookshot/launcher"),
              $"The default home is not the contract's: {unset}.");
    }

    static void CommandLine()
    {
        Handover.Start S(params string[] a) => Handover.ReadArgs(a);
        Check(S("--launch", "/g") == new Handover.Start("/g", false, null, false), "--launch <folder> was not read.");
        Check(S("--update", "/g", "--tag", "v3.0.0", "--fetched") == new Handover.Start("/g", true, "v3.0.0", false),
              "--update <folder> --tag <tag> --fetched was not read.");
        Check(S("--update", "/g") == new Handover.Start("/g", true, null, false), "--update <folder> with no tag was not read.");
        Check(S("--play") == new Handover.Start(null, false, null, true) && S("--play", "/g") == new Handover.Start("/g", false, null, true),
              "--play [<folder>] was not read.");
        Check(S("--update", "--tag", "v3.0.0") == new Handover.Start(null, false, null, false) && S() == new Handover.Start(null, false, null, false),
              "--update with no folder, or nothing, was not an ordinary start.");
    }

    /// An import the Linux upgrader wrote itself (linux_upgrade.import_for + write_import, the driver ran them): taken whole
    static void FromTheUpgrader(string dir)
    {
        var got = Handover.Take(dir);
        Check(got.Count == 1, "The Linux upgrader's own import file was not taken.");
        if (got.Count != 1) return;
        Check(got[0].Version == "v2.3.0-4-g1a2b3c4" && got[0].Track == "nightly" && got[0].Settings.DlssLicense &&
              got[0].Settings.PrepareLater && got[0].Settings.Romfs != null && got[0].Language == "de-DE",
              "The Linux upgrader's import did not read as it was written.");
    }
}

namespace Hookshot.Launcher
{
    /// (the stand-in: where the launcher keeps its settings, a scratch folder)
    static class Builders
    {
        public static string Base { get; set; } = Path.Combine(Path.GetTempPath(), "launcher-handover-unset");
        public static string SettingsPath => Path.Combine(Base, "builder-settings.json");
    }
}
