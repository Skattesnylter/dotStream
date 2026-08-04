using DotStream.Icons;

namespace DotStream.App;

/// <summary>
/// Decides whether a running process is a given installed application.
///
/// The naive version of this truncated an identifier at its first dot, which turned
/// "Microsoft.Office.WINWORD.EXE.15" into "microsoft" - and any of VS Code's
/// "Microsoft.VisualStudio.Code.*" language servers then made Word look like it was
/// already running, so pressing its key opened its page instead of launching it.
///
/// Splitting into tokens and requiring a meaningful one to match fixes that: Word
/// carries "winword", which only appears when Word itself is running.
/// </summary>
public static class AppIdentity
{
    /// <summary>
    /// Tokens that say nothing about which application this is. A vendor prefix or a
    /// word like "service" appears in hundreds of places, and matching on one is how
    /// everything starts looking like everything else.
    /// </summary>
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "microsoft", "windows", "google", "adobe", "corp", "inc", "ltd", "llc",
        "app", "apps", "exe", "com", "net", "org", "the",
        "service", "services", "server", "host", "helper", "client", "agent",
        "launcher", "desktop", "manager", "update", "updater", "setup", "main"
    };

    public static bool Matches(InstalledApp app, string processName)
    {
        HashSet<string> wanted = Tokens(app.AppUserModelId, app.Name);
        if (wanted.Count == 0) return false;

        foreach (string token in Tokens(processName))
        {
            if (wanted.Contains(token)) return true;
        }

        return false;
    }

    // A prefix rule was tried here so that "AppleTV" would match
    // "AppleInc.AppleTVWin_nzyj5cx40ttqa!App", and measured against every installed
    // app and running process on a real machine: it took the number considered
    // running from 44 to 69. Among the new matches was Word against
    // OfficeClickToRun - which never stops running, so Word would have looked open
    // forever and never launched again. That is the exact bug the token rule was
    // written to fix. Packaged apps are identified by asking Windows instead; see
    // PackagedProcesses.

    /// <summary>
    /// Splits identifiers and names into comparable pieces. Anything generic, short
    /// or purely numeric is dropped - what is left is the part that identifies the
    /// program.
    /// </summary>
    public static HashSet<string> Tokens(params string?[] sources)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (string? source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;

            foreach (string raw in source.Split(
                         ['.', '\\', '/', '_', '!', ' ', '-', '(', ')', '[', ']', ':', ','],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string token = raw.ToLowerInvariant();

                if (token.Length < 3) continue;
                if (token.All(char.IsDigit)) continue;
                if (Generic.Contains(token)) continue;

                tokens.Add(token);
            }
        }

        return tokens;
    }
}
