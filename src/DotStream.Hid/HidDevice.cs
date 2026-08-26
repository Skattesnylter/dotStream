using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DotStream.Hid;

/// <summary>
/// One HID collection, found and opened.
///
/// The device exposes several collections and only the vendor-defined one accepts
/// the protocol. Opening the first one enumerated appears to succeed and then does
/// nothing at all, which is the failure mode that stalled at least one other project
/// on this hardware.
/// </summary>
public sealed record HidCollection(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    int InputReportLength,
    int OutputReportLength,
    string? Serial,
    string? Product);

/// <summary>
/// Finding and opening HID collections. Everything Windows-specific lives here so
/// <see cref="HidTransport"/> can read as protocol rather than as interop.
/// </summary>
public static class HidDevice
{
    /// <summary>The vendor-defined usage page the deck's protocol lives on.</summary>
    public const ushort VendorUsagePage = 0xFFA0;

    /// <summary>
    /// Every HID collection currently attached.
    ///
    /// Deliberately unfiltered. Four VID/PID pairs are known to be in circulation for
    /// this hardware and a unit bought anywhere may report a fifth, so the caller
    /// matches on the usage page and lets the user confirm rather than hardcoding an
    /// identifier that will be wrong for someone.
    /// </summary>
    public static IReadOnlyList<HidCollection> Enumerate()
    {
        var found = new List<HidCollection>();

        Guid hid = Guid.Empty;
        HidD_GetHidGuid(ref hid);

        IntPtr set = SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == InvalidHandle) return found;

        try
        {
            for (int i = 0; ; i++)
            {
                var iface = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref iface)) break;

                string path = PathOf(set, ref iface);
                if (path.Length == 0) continue;

                // Opened with no access rights at all: enough to read the descriptors,
                // and it does not fight anything that already holds the device.
                IntPtr probe = CreateFile(path, 0, FileShareRead | FileShareWrite,
                                          IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

                if (probe == InvalidHandle) continue;

                try
                {
                    var attributes = new HiddAttributes { Size = Marshal.SizeOf<HiddAttributes>() };
                    if (!HidD_GetAttributes(probe, ref attributes)) continue;
                    if (!HidD_GetPreparsedData(probe, out IntPtr preparsed)) continue;

                    HidpCaps caps = default;
                    HidP_GetCaps(preparsed, ref caps);
                    HidD_FreePreparsedData(preparsed);

                    found.Add(new HidCollection(
                        path,
                        attributes.VendorID,
                        attributes.ProductID,
                        caps.UsagePage,
                        caps.InputReportByteLength,
                        caps.OutputReportByteLength,
                        Descriptor(probe, HidD_GetSerialNumberString),
                        Descriptor(probe, HidD_GetProductString)));
                }
                finally { CloseHandle(probe); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }

        return found;
    }

    /// <summary>
    /// The deck, if one is attached: a vendor-page collection that also accepts
    /// output reports. The keyboard collection on the same device shares its VID and
    /// PID, so the usage page is what tells them apart.
    /// </summary>
    public static HidCollection? FindDeck() =>
        Enumerate().FirstOrDefault(c => c.UsagePage == VendorUsagePage && c.OutputReportLength > 0);

    /// <summary>Opens a stream for writing, or reading, or both.</summary>
    public static SafeHandle Open(string path, bool read, bool write)
    {
        uint access = (read ? GenericRead : 0) | (write ? GenericWrite : 0);

        IntPtr handle = CreateFile(path, access, FileShareRead | FileShareWrite,
                                   IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle == InvalidHandle)
            throw new IOException($"Could not open {path}: Win32 error {Marshal.GetLastWin32Error()}.");

        return new HidHandle(handle);
    }

    private static string PathOf(IntPtr set, ref SpDeviceInterfaceData iface)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out int needed, IntPtr.Zero);

        IntPtr detail = Marshal.AllocHGlobal(needed);

        try
        {
            // cbSize is the size of the fixed part of the structure, not of the
            // buffer: 8 on 64-bit, 6 on 32-bit. Passing the buffer size fails.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

            return SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, needed, out _, IntPtr.Zero)
                ? Marshal.PtrToStringUni(detail + 4) ?? ""
                : "";
        }
        finally { Marshal.FreeHGlobal(detail); }
    }

    private static string? Descriptor(IntPtr handle, Func<IntPtr, StringBuilder, int, bool> get)
    {
        var text = new StringBuilder(128);
        return get(handle, text, text.Capacity * 2) ? text.ToString() : null;
    }

    private sealed class HidHandle : SafeHandle
    {
        public HidHandle(IntPtr handle) : base(InvalidHandle, true) => SetHandle(handle);

        public override bool IsInvalid => handle == InvalidHandle || handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    private static readonly IntPtr InvalidHandle = new(-1);

    private const int DigcfPresent = 0x02, DigcfDeviceInterface = 0x10;
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    private const uint FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorID, ProductID, VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage, UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(ref Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(IntPtr handle, ref HiddAttributes attributes);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr preparsed);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr preparsed);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr preparsed, ref HidpCaps caps);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetSerialNumberString(IntPtr handle, StringBuilder text, int length);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    private static extern bool HidD_GetProductString(IntPtr handle, StringBuilder text, int length);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr window, int flags);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SpDeviceInterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, int size, out int needed, IntPtr info);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool WriteFile(SafeHandle handle, byte[] buffer, int count, out int written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadFile(SafeHandle handle, byte[] buffer, int count, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
