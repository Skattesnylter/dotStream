using System.Diagnostics;
using DotStream.Icons;

namespace DotStream.App;

/// <summary>
/// "Is this app already running?" - the signal that decides whether pressing an app
/// key launches it or drills into its page.
///
/// Process names are a blunt instrument, but combined with the media-session check
/// in MediaHub they cover both Win32 and packaged apps well enough. Results are
/// cached briefly because this runs on every repaint of the home page.
/// </summary>
public static class RunningApps
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);
    private static readonly Lock Gate = new();

    private static HashSet<string> _names = [];
    private static DateTime _capturedUtc = DateTime.MinValue;

    public static bool IsRunning(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // A packaged app can be answered exactly, so no heuristic is involved.
        if (PackagedProcesses.IsRunning(app.AppUserModelId)) return true;

        HashSet<string> wanted = AppIdentity.Tokens(app.AppUserModelId, app.Name);
        if (wanted.Count == 0) return false;

        foreach (string token in Snapshot())
        {
            if (wanted.Contains(token)) return true;
        }

        return false;
    }

    /// <summary>Every meaningful token across every running process name.</summary>
    private static HashSet<string> Snapshot()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _capturedUtc < CacheLifetime)
                return _names;

            var names = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    using (process)
                    {
                        names.UnionWith(AppIdentity.Tokens(process.ProcessName));
                    }
                }
            }
            catch
            {
                // Enumeration can fail on locked-down processes; a partial list is fine.
            }

            _names = names;
            _capturedUtc = DateTime.UtcNow;
            return _names;
        }
    }
}
