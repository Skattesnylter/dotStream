using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DotStream.App;

/// <summary>
/// The application user model IDs of every running packaged (MSIX) process.
///
/// Guessing at this from process names does not work. The Apple TV app is installed as
/// "AppleInc.AppleTVWin_nzyj5cx40ttqa!App" and runs as "AppleTV"; no token rule joins
/// those two without also joining things that have nothing to do with each other - a
/// prefix rule tried here matched Word against OfficeClickToRun, which runs constantly,
/// so Word would have looked open forever.
///
/// Windows already knows the answer exactly, so ask it. Win32 processes simply have no
/// identity of this kind and fall back to the token match in <see cref="AppIdentity"/>.
/// </summary>
public static class PackagedProcesses
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(2);
    private static readonly Lock Gate = new();

    private static HashSet<string> _identifiers = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _capturedUtc = DateTime.MinValue;

    /// <summary>Whether a packaged application with this identifier is running.</summary>
    public static bool IsRunning(string? appUserModelId)
    {
        // No "!" means it is not a packaged application identifier, so there is nothing
        // here to compare against and the caller should use its own test.
        if (string.IsNullOrWhiteSpace(appUserModelId) || !appUserModelId.Contains('!')) return false;

        lock (Gate)
        {
            if (DateTime.UtcNow - _capturedUtc >= CacheLifetime) Capture();
            return _identifiers.Contains(appUserModelId);
        }
    }

    private static void Capture()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    if (AumidOf(process.Id) is { } id) found.Add(id);
                }
            }
        }
        catch
        {
            // Enumeration can fail part way through; a partial list is still useful.
        }

        _identifiers = found;
        _capturedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// The identifier a process was packaged under, or null for an ordinary Win32
    /// program - which has no such identity and must be matched some other way.
    /// </summary>
    public static string? AumidOf(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;

        try
        {
            uint length = 0;

            // First call sizes the buffer. Anything other than "too small" means this
            // process has no package identity - which is the common case.
            if (GetApplicationUserModelId(handle, ref length, null) != ErrorInsufficientBuffer) return null;

            var buffer = new StringBuilder((int)length);
            return GetApplicationUserModelId(handle, ref length, buffer) == 0 ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const int ErrorInsufficientBuffer = 122;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder? id);
}
