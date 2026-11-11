using HookshotBuilder;

namespace Hookshot.Launcher;

/// The game language closest to the system's own, as the WPF builder picks it (Strings.Default). The launcher runs with invariant
/// globalization (no ICU to ship on Linux), where .NET's culture is always the invariant one, so the system's language is read
/// directly: Windows' user locale, or LC_ALL, LC_MESSAGES and LANG on Linux ("fr_FR.UTF-8")
static class SystemLanguage
{
    public static Language Pick()
    {
        var name = (Name() ?? "").Split('.', '@')[0].Replace('_', '-');
        var pick = Strings.All.FirstOrDefault(l => l.Code.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (pick != null) return pick;
        if (name.StartsWith("zh"))
            return Strings.All.First(l => l.Code == (name.Contains("TW") || name.Contains("HK") || name.Contains("Hant") ? "zh-Hant" : "zh-Hans"));
        if (name.StartsWith("es-") && !name.Equals("es-ES", StringComparison.OrdinalIgnoreCase)) return Strings.All.First(l => l.Code == "es-419");
        if (name.StartsWith("pt")) return Strings.All.First(l => l.Code == "pt-BR");
        var family = name.Split('-')[0];
        return Strings.All.FirstOrDefault(l => l.Code.Equals(family, StringComparison.OrdinalIgnoreCase)) ?? Strings.All[0];
    }

    static string? Name()
    {
        if (OperatingSystem.IsWindows())
        {
            // (the user's locale, as Windows' settings keep it: "fr-FR")
            try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\International", "LocaleName", null) as string; }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return null; }
        }
        foreach (var v in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
            if (Environment.GetEnvironmentVariable(v) is { Length: > 0 } s && s != "C" && s != "POSIX") return s;
        return null;
    }
}
