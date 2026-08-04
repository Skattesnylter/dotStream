using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DotStream.App;

public sealed record ForegroundApp(
    IntPtr Window, int ProcessId, string ProcessName, string Title, string? AppUserModelId = null);

/// <summary>
/// Reports which application the user just switched to.
///
/// A hook rather than a poll: EVENT_SYSTEM_FOREGROUND fires once, when it happens.
/// Polling GetForegroundWindow would either miss quick switches or burn a timer
/// forever to catch something that changes a few times a minute.
///
/// WINEVENT_OUTOFCONTEXT means the callback arrives on this thread's message loop,
/// so handlers already run on the UI thread. Note the delegate is held in a field:
/// the hook keeps only an unmanaged pointer, and letting the collector take it turns
/// the next event into a crash.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;

    private readonly WinEventProc _callback;
    private IntPtr _hook;

    public ForegroundWatcher()
    {
        _callback = OnWinEvent;

        // SKIPOWNPROCESS so that clicking dotStream's own window is not itself an
        // app switch - otherwise the deck would reset every time you touched it.
        _hook = SetWinEventHook(
            EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
    }

    public event EventHandler<ForegroundApp>? Changed;

    public bool IsRunning => _hook != IntPtr.Zero;

    /// <summary>
    /// The last application window that had focus before dotStream took it.
    ///
    /// A hotkey has to reach the program the user was actually working in. On real
    /// hardware that is automatic - pressing a physical key does not move focus - but
    /// in the simulator, clicking a key focuses this window instead, and the keystroke
    /// would land here.
    /// </summary>
    public IntPtr LastForegroundWindow { get; private set; }

    /// <summary>
    /// The same window, described. Because the hook skips this process, this is always
    /// some other application - which is what makes "follow the app I was just in"
    /// possible at all: by the time the user reaches a menu here, dotStream is in front,
    /// and the app they meant is not.
    /// </summary>
    public ForegroundApp? LastApp { get; private set; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    /// <summary>The app in front right now, for establishing state at startup.</summary>
    public static ForegroundApp? Current() => Describe(GetForegroundWindow());

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr window,
        int objectId, int childId, uint thread, uint time)
    {
        // Only the window itself, not its scrollbars, menus or carets.
        const int objidWindow = 0;
        if (objectId != objidWindow || childId != 0 || window == IntPtr.Zero) return;

        if (Describe(window) is not { } app) return;

        LastForegroundWindow = window;
        LastApp = app;
        Changed?.Invoke(this, app);
    }

    private static ForegroundApp? Describe(IntPtr window)
    {
        if (window == IntPtr.Zero) return null;
        if (!IsRealAppWindow(window)) return null;

        GetWindowThreadProcessId(window, out int processId);
        if (processId == 0) return null;

        processId = Unwrap(window, processId);

        string name;

        try
        {
            using Process process = Process.GetProcessById(processId);
            name = process.ProcessName;
        }
        catch
        {
            // Protected or already-exited processes cannot be opened. Without a name
            // there is nothing to match on, so there is nothing to report.
            return null;
        }

        var title = new StringBuilder(256);
        GetWindowText(window, title, title.Capacity);

        return new ForegroundApp(window, processId, name, title.ToString(), PackagedProcesses.AumidOf(processId));
    }

    /// <summary>
    /// Finds the process that really owns a window hosted by ApplicationFrameHost.
    ///
    /// A packaged app's frame belongs to ApplicationFrameHost, not to the app: bringing
    /// Media Player to the front reports "ApplicationFrameHost", which identifies
    /// nothing and matches no installed application. The app itself owns a child window,
    /// and that child runs in the real process - "Microsoft.Media.Player", carrying the
    /// identifier "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic".
    /// </summary>
    private static int Unwrap(IntPtr window, int hostProcessId)
    {
        int found = hostProcessId;

        EnumChildWindows(window, (child, _) =>
        {
            GetWindowThreadProcessId(child, out int childProcessId);

            if (childProcessId == 0 || childProcessId == hostProcessId) return true;

            found = childProcessId;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    /// <summary>
    /// Filters out everything that is not a window a person would call an app.
    ///
    /// Switching between two applications produces more than one foreground event:
    /// tool windows, shell surfaces and short-lived frames all take the foreground on
    /// the way. Acting on those makes the deck flick to a page and straight back off
    /// it again.
    /// </summary>
    private static bool IsRealAppWindow(IntPtr window)
    {
        const int gwlExStyle = -20;
        const long wsExToolWindow = 0x00000080;

        if (!IsWindowVisible(window)) return false;
        if ((GetWindowLongPtr(window, gwlExStyle).ToInt64() & wsExToolWindow) != 0) return false;

        // A titleless top-level window is the taskbar, a tooltip host or similar.
        return GetWindowTextLength(window) > 0;
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;

        UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window,
        int objectId, int childId, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
