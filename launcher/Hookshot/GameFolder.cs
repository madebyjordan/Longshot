namespace Hookshot.Launcher;

/// What a game folder holds, as the launcher shows it: the game (bin/hookshot(.exe)), and the release it was built as
/// (hookshot_version.txt's version= line)
sealed record GameFolder(string Path, bool HasGame, string? Version)
{
    public static GameFolder Read(string path)
    {
        if (path.Length == 0 || !Directory.Exists(path)) return new(path, false, null);
        var game = File.Exists(System.IO.Path.Combine(path, "bin", OperatingSystem.IsWindows() ? "hookshot.exe" : "hookshot"));
        string? version = null;
        try
        {
            foreach (var line in File.ReadLines(System.IO.Path.Combine(path, "hookshot_version.txt")))
                if (line.StartsWith("version=")) { version = line[8..].Trim(); break; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new(path, game, version);
    }
}
