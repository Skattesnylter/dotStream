using System.Globalization;
using System.Windows.Media;

namespace DotStream.Core;

/// <summary>
/// The four colours a widget cell is built from.
///
/// Held per placed widget rather than per widget type: two people will not agree on
/// what a CPU gauge should look like, and someone running two of them will want to
/// tell them apart.
/// </summary>
public sealed record WidgetTheme
{
    public required Color Background { get; init; }

    /// <summary>The ring that sweeps with the value.</summary>
    public required Color Accent { get; init; }

    /// <summary>The large number.</summary>
    public required Color Value { get; init; }

    /// <summary>The small caption under it.</summary>
    public required Color Label { get; init; }

    /// <summary>dotStream's own accent - the cyan in the wordmark.</summary>
    public static Color StreamCyan { get; } = Color.FromRgb(0x4D, 0xD9, 0xE8);

    public static WidgetTheme FromAccent(Color accent, Color background) => new()
    {
        Background = background,
        Accent = accent,
        Value = accent,
        Label = Lighten(accent, 0.55)
    };

    private static Color Lighten(Color color, double amount) => Color.FromRgb(
        (byte)Math.Clamp(color.R + (255 - color.R) * amount, 0, 255),
        (byte)Math.Clamp(color.G + (255 - color.G) * amount, 0, 255),
        (byte)Math.Clamp(color.B + (255 - color.B) * amount, 0, 255));
}

/// <summary>Colours as "#RRGGBB", so a profile stays readable and hand-editable.</summary>
public static class ColorCodec
{
    public static string ToHex(Color color) =>
        "#" + color.R.ToString("X2", CultureInfo.InvariantCulture)
            + color.G.ToString("X2", CultureInfo.InvariantCulture)
            + color.B.ToString("X2", CultureInfo.InvariantCulture);

    public static Color? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;

        string value = hex.Trim().TrimStart('#');
        if (value.Length != 6) return null;

        return byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
               && byte.TryParse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
               && byte.TryParse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b)
            ? Color.FromRgb(r, g, b)
            : null;
    }
}
