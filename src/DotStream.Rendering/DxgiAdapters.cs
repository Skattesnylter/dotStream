using System.Runtime.InteropServices;

namespace DotStream.Rendering;

/// <summary>
/// Total dedicated video memory, read from DXGI.
///
/// PDH reports how much VRAM is in use but never the adapter's capacity, so a usage
/// figure on its own has no denominator. DXGI's adapter description carries the
/// physical amount, which is the number a gauge needs.
///
/// Not WMI: Win32_VideoController.AdapterRAM is a 32-bit field and wraps above 4 GB,
/// so it reports nonsense on any modern card. Not the registry either - the value is
/// there but the key layout is a driver implementation detail.
///
/// Queried once and cached. Cards do not change size while the app is running.
/// </summary>
public static class DxgiAdapters
{
    private static readonly Lock Gate = new();
    private static double? _totalGiB;

    /// <summary>
    /// Dedicated video memory across all hardware adapters, in GiB. Zero when DXGI is
    /// unavailable or every adapter is software-rendered.
    /// </summary>
    public static double TotalVideoMemoryGiB()
    {
        lock (Gate)
        {
            _totalGiB ??= Query();
            return _totalGiB.Value;
        }
    }

    private static double Query()
    {
        object? factoryObject = null;

        try
        {
            Guid iid = typeof(IDxgiFactory1).GUID;
            if (CreateDXGIFactory1(ref iid, out factoryObject) != 0 || factoryObject is not IDxgiFactory1 factory)
                return 0;

            ulong total = 0;

            for (uint index = 0; ; index++)
            {
                if (factory.EnumAdapters1(index, out IDxgiAdapter1? adapter) != 0 || adapter is null)
                    break;

                try
                {
                    if (adapter.GetDesc1(out AdapterDesc1 desc) != 0) continue;

                    // Skip the Microsoft Basic Render Driver, which claims memory it
                    // does not have.
                    const uint softwareAdapter = 0x2;
                    if ((desc.Flags & softwareAdapter) != 0) continue;

                    total += (ulong)desc.DedicatedVideoMemory;
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }

            return total / (1024d * 1024d * 1024d);
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (factoryObject is not null) Marshal.ReleaseComObject(factoryObject);
        }
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nint DedicatedVideoMemory;
        public nint DedicatedSystemMemory;
        public nint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    // The unused members exist only to keep the vtable slots lined up; they are never
    // called, so their signatures do not matter as long as the count and order do.
    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();

        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();

        [PreserveSig]
        int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDxgiAdapter1? adapter);

        [PreserveSig]
        int IsCurrent();
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDxgiAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();

        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();

        [PreserveSig]
        int GetDesc1(out AdapterDesc1 description);
    }
}
