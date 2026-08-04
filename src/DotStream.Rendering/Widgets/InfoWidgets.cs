using System.Globalization;
using System.Windows.Media;
using DotStream.Core;

namespace DotStream.Rendering.Widgets;

/// <summary>
/// The built-in info cells. Everything here reads from <see cref="SystemMetrics"/>,
/// which means everything here works without a driver, a package or admin rights.
/// </summary>
public static class InfoWidgets
{
    public static IReadOnlyList<IInfoWidget> All { get; } =
    [
        new CpuWidget(),
        new RamWidget(),
        new GpuWidget(),
        new VideoMemoryWidget(),
        new DiskWidget(),
        new NetworkWidget(),
        new ClockWidget(),
        new UptimeWidget()
    ];

    public static IInfoWidget? ById(string id) =>
        All.FirstOrDefault(w => w.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private const string Invariant = "0.0";

    internal static CellVisual Gauge(WidgetTheme theme, double fraction, string value, string label,
        double valueScale = 1.0) => new()
    {
        Background = theme.Background,
        GaugeFraction = fraction,
        GaugeColor = theme.Accent,
        BigText = value,
        BigTextColor = theme.Value,
        BigTextScale = valueScale,
        Label = label,
        LabelColor = theme.Label,
        LabelSize = 10,
        LabelPosition = LabelPosition.Bottom
    };

    internal static CellVisual Text(WidgetTheme theme, string value, string label, double valueScale = 1.0) => new()
    {
        Background = theme.Background,
        BigText = value,
        BigTextColor = theme.Value,
        BigTextScale = valueScale,
        Label = label,
        LabelColor = theme.Label,
        LabelSize = 10,
        LabelPosition = LabelPosition.Bottom
    };

    internal static string Fixed(double value, string format = Invariant) =>
        value.ToString(format, CultureInfo.InvariantCulture);
}

public sealed class CpuWidget : IInfoWidget
{
    public string Id => "cpu";
    public string Name => "CPU load";
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public WidgetTheme DefaultTheme { get; } =
        WidgetTheme.FromAccent(WidgetTheme.StreamCyan, Color.FromRgb(0x0B, 0x12, 0x16));

    public CellVisual Render(WidgetTheme theme)
    {
        double load = SystemMetrics.CpuLoad();
        return InfoWidgets.Gauge(theme, load, (int)Math.Round(load * 100) + "%", "CPU");
    }
}

public sealed class RamWidget : IInfoWidget
{
    public string Id => "ram";
    public string Name => "Memory";
    public TimeSpan Interval => TimeSpan.FromSeconds(2);

    public WidgetTheme DefaultTheme { get; } =
        WidgetTheme.FromAccent(Color.FromRgb(0x9B, 0x8C, 0xFF), Color.FromRgb(0x0D, 0x0B, 0x16));

    public CellVisual Render(WidgetTheme theme)
    {
        MemoryReading memory = SystemMetrics.Memory();

        return InfoWidgets.Gauge(theme, memory.Fraction,
            InfoWidgets.Fixed(memory.UsedGiB),
            "of " + InfoWidgets.Fixed(memory.TotalGiB, "0") + " GB");
    }
}

public sealed class GpuWidget : IInfoWidget
{
    public string Id => "gpu";
    public string Name => "GPU load";
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public WidgetTheme DefaultTheme { get; } =
        WidgetTheme.FromAccent(Color.FromRgb(0x6D, 0xE2, 0x8B), Color.FromRgb(0x08, 0x14, 0x0C));

    public CellVisual Render(WidgetTheme theme)
    {
        if (!SystemMetrics.GpuAvailable)
            return InfoWidgets.Text(theme, "n/a", "GPU", 0.8);

        double load = SystemMetrics.GpuLoad();
        return InfoWidgets.Gauge(theme, load, (int)Math.Round(load * 100) + "%", "GPU");
    }
}

public sealed class VideoMemoryWidget : IInfoWidget
{
    public string Id => "vram";
    public string Name => "Video memory";
    public TimeSpan Interval => TimeSpan.FromSeconds(2);

    public WidgetTheme DefaultTheme { get; } =
        WidgetTheme.FromAccent(Color.FromRgb(0xFF, 0xA6, 0x5C), Color.FromRgb(0x16, 0x0E, 0x08));

    public CellVisual Render(WidgetTheme theme)
    {
        if (!SystemMetrics.GpuAvailable)
            return InfoWidgets.Text(theme, "n/a", "VRAM", 0.8);

        double used = SystemMetrics.VideoMemoryGiB();
        double total = DxgiAdapters.TotalVideoMemoryGiB();

        // Usage comes from PDH, capacity from DXGI - PDH does not report the adapter's
        // size. Without a total there is no honest denominator, so fall back to the
        // bare figure rather than inventing one.
        return total > 0
            ? InfoWidgets.Gauge(theme, Math.Clamp(used / total, 0, 1),
                InfoWidgets.Fixed(used),
                "of " + InfoWidgets.Fixed(total, "0") + " GB")
            : InfoWidgets.Text(theme, InfoWidgets.Fixed(used), "GB VRAM");
    }
}

public sealed class DiskWidget : IInfoWidget
{
    public string Id => "disk";
    public string Name => "Disk free";
    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public WidgetTheme DefaultTheme { get; } =
        WidgetTheme.FromAccent(Color.FromRgb(0x7F, 0xC7, 0xFF), Color.FromRgb(0x08, 0x10, 0x18));

    public CellVisual Render(WidgetTheme theme)
    {
        DiskReading disk = SystemMetrics.SystemDisk();

        return InfoWidgets.Gauge(theme, disk.Fraction,
            InfoWidgets.Fixed(disk.FreeGiB, disk.FreeGiB >= 100 ? "0" : "0.0"),
            "GB free");
    }
}

public sealed class NetworkWidget : IInfoWidget
{
    public string Id => "net";
    public string Name => "Network";
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public WidgetTheme DefaultTheme { get; } =
        WidgetTheme.FromAccent(Color.FromRgb(0xFF, 0x8F, 0xC4), Color.FromRgb(0x16, 0x08, 0x10));

    public CellVisual Render(WidgetTheme theme)
    {
        NetworkReading network = SystemMetrics.Network();
        double down = network.DownKiBps;

        string value = down >= 1024
            ? InfoWidgets.Fixed(down / 1024) + "M"
            : InfoWidgets.Fixed(down, "0");

        return InfoWidgets.Text(theme, value,
            "KB/s  " + InfoWidgets.Fixed(network.UpKiBps, "0") + " up");
    }
}

public sealed class ClockWidget : IInfoWidget
{
    public string Id => "clock";
    public string Name => "Clock";
    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public WidgetTheme DefaultTheme { get; } =
        WidgetTheme.FromAccent(WidgetTheme.StreamCyan, Color.FromRgb(0x0A, 0x10, 0x12));

    public CellVisual Render(WidgetTheme theme)
    {
        DateTime now = DateTime.Now;

        return InfoWidgets.Text(theme,
            now.ToString("HH:mm", CultureInfo.InvariantCulture),
            now.ToString("ddd dd MMM", CultureInfo.CurrentCulture));
    }
}

public sealed class UptimeWidget : IInfoWidget
{
    public string Id => "uptime";
    public string Name => "Uptime";
    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public WidgetTheme DefaultTheme { get; } =
        WidgetTheme.FromAccent(Color.FromRgb(0xC0, 0xC6, 0xD0), Color.FromRgb(0x0E, 0x0E, 0x12));

    public CellVisual Render(WidgetTheme theme)
    {
        TimeSpan uptime = SystemMetrics.Uptime;

        string value = uptime.TotalDays >= 1
            ? InfoWidgets.Fixed(uptime.TotalDays, "0") + "d"
            : InfoWidgets.Fixed(uptime.TotalHours, "0") + "h";

        string label = uptime.TotalDays >= 1
            ? uptime.Hours + " h up"
            : uptime.Minutes + " min up";

        return InfoWidgets.Text(theme, value, label);
    }
}
