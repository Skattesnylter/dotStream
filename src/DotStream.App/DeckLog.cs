using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DotStream.App;

public enum LogDirection
{
    /// <summary>Something arrived - a key press, an agent request.</summary>
    In,

    /// <summary>Something left - a reply, a launched app, an outbound call.</summary>
    Out,

    /// <summary>Neither: state changes, warnings, failures.</summary>
    Note
}

public sealed record LogEntry(DateTime When, LogDirection Direction, string Source, string Text)
{
    public string Time => When.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public string Arrow => Direction switch
    {
        LogDirection.In => "▸",   // in from the outside
        LogDirection.Out => "◂",  // out from us
        _ => "·"
    };

    public Brush Colour => Direction switch
    {
        LogDirection.In => InBrush,
        LogDirection.Out => OutBrush,
        _ => NoteBrush
    };

    private static readonly Brush InBrush = Frozen(Color.FromRgb(0x6D, 0xE2, 0x8B));
    private static readonly Brush OutBrush = Frozen(Color.FromRgb(0x4D, 0xD9, 0xE8));
    private static readonly Brush NoteBrush = Frozen(Color.FromRgb(0x88, 0x88, 0x94));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// A running record of what the deck did and who asked for it.
///
/// Worth having the moment an agent can drive the hardware: when a key lights up on
/// its own, the useful question is which request caused it. Directions are separated
/// so an agent's request and our reply do not read as the same kind of event.
///
/// Bounded, and appended on the UI thread so the view can bind straight to it.
/// </summary>
public static class DeckLog
{
    private const int Capacity = 600;

    public static ObservableCollection<LogEntry> Entries { get; } = [];

    public static void In(string source, string text) => Add(LogDirection.In, source, text);

    public static void Out(string source, string text) => Add(LogDirection.Out, source, text);

    public static void Note(string source, string text) => Add(LogDirection.Note, source, text);

    private static void Add(LogDirection direction, string source, string text)
    {
        var entry = new LogEntry(DateTime.Now, direction, source, Flatten(text));

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Append(entry));
            return;
        }

        Append(entry);
    }

    private static void Append(LogEntry entry)
    {
        Entries.Add(entry);

        while (Entries.Count > Capacity) Entries.RemoveAt(0);
    }

    private static string Flatten(string text)
    {
        string flat = string.Join("  ",
            text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return flat.Length > 400 ? flat[..400] + "..." : flat;
    }
}
