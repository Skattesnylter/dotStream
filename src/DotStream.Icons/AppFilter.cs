namespace DotStream.Icons;

/// <summary>
/// shell:AppsFolder is the union of everything the Start menu can launch, which
/// includes help files, uninstallers, setup wrappers, control panel applets and
/// management consoles. What people actually want on a deck key is apps and games.
///
/// This is a heuristic, not a truth. It is therefore always exposed behind a
/// user-visible toggle rather than applied silently - hiding something the user
/// installed on purpose, with no way to get it back, would be worse than a bit
/// of noise in the list.
/// </summary>
public static class AppFilter
{
    /// <summary>Shortcuts pointing at documents or system consoles, not programs.</summary>
    private static readonly string[] NonProgramExtensions =
    [
        ".chm", ".hlp", ".txt", ".rtf", ".pdf", ".url", ".htm", ".html",
        ".msc", ".cpl", ".ini", ".log", ".xml"
    ];

    /// <summary>
    /// Matched against the display name, case-insensitively. Kept to phrases that
    /// are unambiguous about being an accessory rather than the program itself.
    /// </summary>
    private static readonly string[] AccessoryPhrases =
    [
        "help file", "help topics", "documentation", "read me", "readme",
        "release notes", "user guide", "user manual", "getting started",
        "uninstall", "avinstaller",
        "setup", "installer", "oppsett",
        "bug report", "crash report", "error report", "diagnostic", "diagnostics",
        "troubleshoot", "feilsøk",
        "repair tool", "recovery tool", "cleanup tool", "clean up",
        "command prompt", "powershell", "ledetekst",
        "registry editor", "registerredigering",
        "odbc", "system configuration", "systemkonfigurasjon",
        "event viewer", "loggbok",
        "task scheduler", "oppgaveplanlegging",
        "character map", "tegnkart",
        "control panel", "kontrollpanel",
        "windows tools", "administrative tools", "administrasjonsverktøy",
        "license", "lisens", "eula",
        "website", "web site", "hjemmeside", "support",
        "modify ", "change "
    ];

    public static bool IsLikelyUserApp(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Packaged apps are always real apps - Store and MSIX installs never show up
        // here as help files, so they skip the heuristics entirely. A Steam game is
        // the same case, and more so: the list it came from contains nothing else.
        if (app.IsPackaged || app.SteamAppId is not null) return true;

        string id = app.AppUserModelId;

        foreach (string extension in NonProgramExtensions)
        {
            if (id.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        string name = app.Name;

        foreach (string phrase in AccessoryPhrases)
        {
            if (name.Contains(phrase, StringComparison.CurrentCultureIgnoreCase))
                return false;
        }

        return true;
    }

    public static IEnumerable<InstalledApp> UserApps(IEnumerable<InstalledApp> apps) =>
        apps.Where(IsLikelyUserApp);
}
