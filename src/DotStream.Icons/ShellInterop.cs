using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DotStream.Icons;

internal enum Sigdn : uint
{
    NormalDisplay = 0x00000000,

    /// <summary>For items in AppsFolder this is the AppUserModelID.</summary>
    ParentRelativeParsing = 0x80018001,

    DesktopAbsoluteParsing = 0x80028000
}

[Flags]
internal enum Siigbf
{
    ResizeToFit = 0x00,
    BiggerSizeOk = 0x01,
    MemoryOnly = 0x02,
    IconOnly = 0x04,
    ThumbnailOnly = 0x08,
    InCacheOnly = 0x10,
    ScaleUp = 0x100
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize
{
    public int Width;
    public int Height;

    public NativeSize(int width, int height)
    {
        Width = width;
        Height = height;
    }
}

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr bindContext, in Guid handler, in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object result);

    void GetParent(out IShellItem parent);

    void GetDisplayName(Sigdn name, [MarshalAs(UnmanagedType.LPWStr)] out string displayName);

    void GetAttributes(uint mask, out uint attributes);

    void Compare(IShellItem other, uint hint, out int order);
}

[ComImport]
[Guid("70629033-e363-4a28-a567-0db78006e6d7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig]
    int Next(uint count,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IShellItem[] items,
        out uint fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumShellItems clone);
}

[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(NativeSize size, Siigbf flags, out IntPtr bitmap);
}

internal static class ShellInterop
{
    internal static readonly Guid FolderIdAppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    internal static readonly Guid BhidEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    internal static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    internal static readonly Guid IidEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");

    [DllImport("shell32.dll", PreserveSig = true)]
    internal static extern int SHGetKnownFolderItem(in Guid folderId, uint flags, IntPtr token,
        in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    internal static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext,
        in Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr handle, int size, ref NativeDibSection dib);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmapInfoHeader
    {
        public uint Size;
        public int Width;

        /// <summary>Negative means the DIB is stored top-down. This is the only
        /// reliable way to learn a bitmap's row order - BITMAP.bmHeight is always
        /// positive and tells you nothing.</summary>
        public int Height;

        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDibSection
    {
        public NativeBitmap Bitmap;
        public NativeBitmapInfoHeader Header;
        public uint Bitfield0;
        public uint Bitfield1;
        public uint Bitfield2;
        public IntPtr Section;
        public uint Offset;
    }

    /// <summary>
    /// Copies an HBITMAP produced by IShellItemImageFactory into a frozen BitmapSource.
    ///
    /// Deliberately not using Imaging.CreateBitmapSourceFromHBitmap: that path drops
    /// the alpha channel, which turns every transparent icon into a black square.
    /// </summary>
    internal static BitmapSource? ToBitmapSource(IntPtr hBitmap)
    {
        if (hBitmap == IntPtr.Zero) return null;

        var dib = new NativeDibSection();
        int dibSize = Marshal.SizeOf<NativeDibSection>();
        int written = GetObject(hBitmap, dibSize, ref dib);
        if (written == 0) return null;

        NativeBitmap native = dib.Bitmap;

        if (native.Bits == IntPtr.Zero || native.BitsPixel != 32 || native.Width <= 0 || native.Height <= 0)
            return null;

        // Row order. A DIB section reports it through BITMAPINFOHEADER.biHeight:
        // negative = top-down, positive = bottom-up. If GetObject could not fill in
        // the whole DIBSECTION we only got a BITMAP back, and the GDI default for
        // that is bottom-up.
        //
        // Shell icon handlers return both kinds. Copying the rows straight into a
        // top-down BitmapSource therefore flips roughly half of all icons - which
        // only shows up on asymmetric artwork, so it is easy to miss.
        bool topDown = written >= dibSize && dib.Header.Height < 0;

        int stride = native.WidthBytes;
        int byteCount = stride * native.Height;
        var pixels = new byte[byteCount];

        if (topDown)
        {
            Marshal.Copy(native.Bits, pixels, 0, byteCount);
        }
        else
        {
            for (int row = 0; row < native.Height; row++)
            {
                IntPtr sourceRow = native.Bits + (native.Height - 1 - row) * stride;
                Marshal.Copy(sourceRow, pixels, row * stride, stride);
            }
        }

        // Some shell handlers return a 32-bit DIB with the alpha channel left at zero.
        // Rendering that as Pbgra32 gives a fully invisible icon, so treat all-zero
        // alpha as "no alpha information" and force the image opaque.
        if (IsAlphaChannelEmpty(pixels))
        {
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 0xFF;
        }

        BitmapSource source = BitmapSource.Create(
            native.Width, native.Height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    private static bool IsAlphaChannelEmpty(byte[] pixels)
    {
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) return false;
        }

        return true;
    }
}
