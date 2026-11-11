namespace Hookshot.Launcher;

/// What a game folder holds, as the launcher shows it: the game (bin/hookshot(.exe)), the release it was built as (hookshot_version.txt's
/// version= line), and whether it keeps the game files a rebuild starts from (.hookshot_exefs and romfs: it can be built as another release)
sealed record GameFolder(string Path, bool HasGame, string? Version, bool Rebuilds)
{
    public static GameFolder Read(string path)
    {
        if (path.Length == 0 || !Directory.Exists(path)) return new(path, false, null, false);
        var game = File.Exists(System.IO.Path.Combine(path, "bin", OperatingSystem.IsWindows() ? "hookshot.exe" : "hookshot"));
        string? version = null;
        try
        {
            foreach (var line in File.ReadLines(System.IO.Path.Combine(path, "hookshot_version.txt")))
                if (line.StartsWith("version=")) { version = line[8..].Trim(); break; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var rebuilds = Directory.Exists(System.IO.Path.Combine(path, ".hookshot_exefs")) && Directory.Exists(System.IO.Path.Combine(path, "romfs"));
        return new(path, game, version, rebuilds);
    }
}
