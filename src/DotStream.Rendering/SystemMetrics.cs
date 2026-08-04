using System.IO;
using System.Runtime.InteropServices;

namespace DotStream.Rendering;

public readonly record struct MemoryReading(double Fraction, double UsedGiB, double TotalGiB);

public readonly record struct DiskReading(double Fraction, double FreeGiB, double TotalGiB);

public readonly record struct NetworkReading(double DownKiBps, double UpKiBps);

/// <summary>
/// Everything the widgets can read, without a NuGet package, a driver or elevation.
///
/// What is deliberately absent: temperatures, fan speeds and power draw. Windows has
/// no public API for them - the ACPI thermal zone is empty on most desktops - and the
/// only route is a library that loads a kernel driver and needs admin rights. That
/// would cost the per-user, no-UAC install, so it stays out until someone asks for it
/// explicitly.
/// </summary>
public static class SystemMetrics
{
    private static long _previousIdle, _previousKernel, _previousUser;

    private static readonly Lock Gate = new();
    private static PdhCounter? _gpuUtilisation;
    private static PdhCounter? _gpuMemory;
    private static bool _gpuOpened;

    private static ulong _previousReceived, _previousSent;
    private static long _previousNetworkTicks;

    /// <summary>Total CPU load, 0..1, measured since the previous call.</summary>
    public static double CpuLoad()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
            return 0;

        long deltaIdle = idle - _previousIdle;
        long deltaKernel = kernel - _previousKernel;
        long deltaUser = user - _previousUser;

        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        // Kernel time already includes idle time, so kernel + user is the total.
        long total = deltaKernel + deltaUser;
        if (total <= 0) return 0;

        return Math.Clamp(1.0 - (double)deltaIdle / total, 0, 1);
    }

    public static MemoryReading Memory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

        if (!GlobalMemoryStatusEx(ref status))
            return new MemoryReading(0, 0, 0);

        const double bytesPerGiB = 1024d * 1024d * 1024d;
        double total = status.TotalPhys / bytesPerGiB;
        double used = (status.TotalPhys - status.AvailPhys) / bytesPerGiB;

        return new MemoryReading(status.MemoryLoad / 100.0, used, total);
    }

    /// <summary>Combined 3D engine utilisation across all GPUs, 0..1.</summary>
    public static double GpuLoad()
    {
        EnsureGpuCounters();
        return _gpuUtilisation is null ? 0 : Math.Clamp(_gpuUtilisation.ReadSum() / 100.0, 0, 1);
    }

    /// <summary>Dedicated video memory in use, in GiB. Total is not exposed by PDH.</summary>
    public static double VideoMemoryGiB()
    {
        EnsureGpuCounters();
        return _gpuMemory is null ? 0 : _gpuMemory.ReadSum() / (1024d * 1024d * 1024d);
    }

    public static bool GpuAvailable
    {
        get
        {
            EnsureGpuCounters();
            return _gpuUtilisation is not null;
        }
    }

    public static DiskReading SystemDisk()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

            if (!GetDiskFreeSpaceEx(root, out ulong available, out ulong total, out _) || total == 0)
                return new DiskReading(0, 0, 0);

            const double bytesPerGiB = 1024d * 1024d * 1024d;
            return new DiskReading(1.0 - (double)available / total, available / bytesPerGiB, total / bytesPerGiB);
        }
        catch
        {
            return new DiskReading(0, 0, 0);
        }
    }

    /// <summary>
    /// Throughput across all up interfaces since the previous call. Reads the adapter
    /// byte counters directly, so no counter warm-up is needed.
    /// </summary>
    public static NetworkReading Network()
    {
        ulong received = 0, sent = 0;

        try
        {
            foreach (System.Net.NetworkInformation.NetworkInterface adapter in
                     System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                System.Net.NetworkInformation.IPInterfaceStatistics stats = adapter.GetIPStatistics();
                received += (ulong)Math.Max(0, stats.BytesReceived);
                sent += (ulong)Math.Max(0, stats.BytesSent);
            }
        }
        catch
        {
            return new NetworkReading(0, 0);
        }

        long ticks = Environment.TickCount64;
        long elapsed = ticks - _previousNetworkTicks;

        ulong previousReceived = _previousReceived;
        ulong previousSent = _previousSent;

        _previousReceived = received;
        _previousSent = sent;
        _previousNetworkTicks = ticks;

        // First call, or counters went backwards after an adapter reset.
        if (elapsed <= 0 || previousReceived == 0 || received < previousReceived || sent < previousSent)
            return new NetworkReading(0, 0);

        double seconds = elapsed / 1000.0;
        return new NetworkReading(
            (received - previousReceived) / 1024.0 / seconds,
            (sent - previousSent) / 1024.0 / seconds);
    }

    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private static void EnsureGpuCounters()
    {
        lock (Gate)
        {
            if (_gpuOpened) return;
            _gpuOpened = true;

            _gpuUtilisation = PdhCounter.Open(@"\GPU Engine(*engtype_3D)\Utilization Percentage");
            _gpuMemory = PdhCounter.Open(@"\GPU Adapter Memory(*)\Dedicated Usage");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string directoryName, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
