using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace DotStream.Icons;

/// <summary>
/// Reads icons out of a .dll, .exe or .ico on this machine.
///
/// This is how Windows' own "Change Icon" dialog works, and the distinction matters:
/// the icons in SHELL32.dll are Microsoft's artwork and shipping copies of them would
/// be redistribution. Reading them from the user's own installation at runtime is not
/// - nothing leaves the machine it was already on, and only a path and an index are
/// stored.
/// </summary>
public static class IconFile
{
    /// <summary>How many icons the file contains. Zero when it has none or is unreadable.</summary>
    public static int Count(string path)
    {
        try
        {
            int count = PrivateExtractIcons(path, 0, 0, 0, null, null, 0, 0);
            return Math.Max(0, count);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Extracts one icon at the requested size. Returns null when the index is out of
    /// range or the file holds nothing usable.
    /// </summary>
    public static BitmapSource? Extract(string path, int index, int size = 64)
    {
        var handles = new IntPtr[1];
        var ids = new int[1];

        try
        {
            if (PrivateExtractIcons(path, index, size, size, handles, ids, 1, 0) < 1) return null;
            if (handles[0] == IntPtr.Zero) return null;

            try
            {
                return FromIcon(handles[0]);
            }
            finally
            {
                DestroyIcon(handles[0]);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Every icon in the file, in order, skipping any that fail to load.</summary>
    public static IReadOnlyList<(int Index, BitmapSource Image)> ExtractAll(string path, int size = 64, int limit = 400)
    {
        var icons = new List<(int, BitmapSource)>();
        int count = Math.Min(Count(path), limit);

        for (int i = 0; i < count; i++)
        {
            if (Extract(path, i, size) is { } image) icons.Add((i, image));
        }

        return icons;
    }

    private static BitmapSource? FromIcon(IntPtr icon)
    {
        if (!GetIconInfo(icon, out IconInfo info)) return null;

        try
        {
            // The colour bitmap carries the pixels; the mask is only needed for the
            // ancient 1-bit path, which ShellInterop's alpha handling covers anyway.
            return ShellInterop.ToBitmapSource(info.ColourBitmap);
        }
        finally
        {
            if (info.ColourBitmap != IntPtr.Zero) ShellInterop.DeleteObject(info.ColourBitmap);
            if (info.MaskBitmap != IntPtr.Zero) ShellInterop.DeleteObject(info.MaskBitmap);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColourBitmap;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int PrivateExtractIcons(
        string file, int index, int width, int height,
        IntPtr[]? icons, int[]? ids, int count, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
