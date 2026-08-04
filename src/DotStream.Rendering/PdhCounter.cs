using System.Runtime.InteropServices;

namespace DotStream.Rendering;

/// <summary>
/// A performance counter read through PDH directly.
///
/// Not System.Diagnostics.PerformanceCounter: that lives in a NuGet package, and the
/// whole project so far has no package dependencies. PDH is in the OS and the interop
/// is a page of code.
///
/// GPU load and VRAM are the reason this exists. Windows exposes both as counters
/// (\GPU Engine and \GPU Adapter Memory) with no driver, no vendor SDK and no
/// elevation - unlike temperatures, which have no public API at all.
/// </summary>
public sealed class PdhCounter : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private const int PdhMoreData = unchecked((int)0x800007D2);

    private readonly IntPtr _query;
    private readonly IntPtr _counter;
    private bool _primed;

    private PdhCounter(IntPtr query, IntPtr counter)
    {
        _query = query;
        _counter = counter;
    }

    public bool IsValid => _query != IntPtr.Zero;

    /// <summary>
    /// Opens a counter path, which may contain wildcards. Returns null when the
    /// counter does not exist on this machine - an older Windows, or no GPU.
    /// </summary>
    public static PdhCounter? Open(string path)
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out IntPtr query) != 0)
            return null;

        if (PdhAddEnglishCounter(query, path, IntPtr.Zero, out IntPtr counter) != 0)
        {
            PdhCloseQuery(query);
            return null;
        }

        return new PdhCounter(query, counter);
    }

    /// <summary>
    /// Sum of every instance matching the path.
    ///
    /// Wildcards are the point: a machine has one GPU Engine instance per engine per
    /// process, and the useful number is their total. The first call after opening
    /// primes the counter and returns 0 - rate counters need two samples.
    /// </summary>
    public double ReadSum()
    {
        if (_query == IntPtr.Zero) return 0;
        if (PdhCollectQueryData(_query) != 0) return 0;

        if (!_primed)
        {
            _primed = true;
            return 0;
        }

        uint bufferSize = 0;
        uint itemCount = 0;

        int status = PdhGetFormattedCounterArray(
            _counter, PdhFmtDouble | PdhFmtNoCap100, ref bufferSize, ref itemCount, IntPtr.Zero);

        if (status != PdhMoreData || itemCount == 0) return 0;

        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);

        try
        {
            if (PdhGetFormattedCounterArray(
                    _counter, PdhFmtDouble | PdhFmtNoCap100, ref bufferSize, ref itemCount, buffer) != 0)
                return 0;

            double total = 0;
            int stride = Marshal.SizeOf<PdhFormattedCounterItem>();

            for (int i = 0; i < itemCount; i++)
            {
                var item = Marshal.PtrToStructure<PdhFormattedCounterItem>(buffer + i * stride);
                if (!double.IsNaN(item.Value) && !double.IsInfinity(item.Value))
                    total += item.Value;
            }

            return total;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterItem
    {
        public IntPtr Name;
        public uint Status;
        private readonly uint _padding;
        public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArray(
        IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr items);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);
}
