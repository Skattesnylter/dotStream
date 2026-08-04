using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DotStream.App;

/// <summary>
/// WPF does not follow the app's own colours into the non-client area, so a dark
/// window still gets a light title bar. DWM has to be told separately.
/// </summary>
internal static class WindowTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>
    /// Call from a window's constructor. Applies immediately if the handle already
    /// exists, otherwise waits for it.
    /// </summary>
    public static void UseDarkTitleBar(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (new WindowInteropHelper(window).Handle is var handle && handle != IntPtr.Zero)
        {
            Apply(handle);
            return;
        }

        window.SourceInitialized += OnSourceInitialized;

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            Apply(new WindowInteropHelper(window).Handle);
        }
    }

    private static void Apply(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;

        int enabled = 1;

        // Unsupported on older Windows 10 builds; failure is not worth reporting.
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }
}
