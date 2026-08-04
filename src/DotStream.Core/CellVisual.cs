using System.Windows;
using System.Windows.Media;

namespace DotStream.Core;

public enum LabelPosition
{
    None,
    Top,
    Bottom
}

/// <summary>
/// Declarative description of what a single cell should look like. Deliberately
/// data-only: the renderer turns this into pixels, and two identical CellVisuals
/// always produce identical pixels, which is what makes dirty-tracking work.
/// </summary>
public sealed record CellVisual
{
    public static CellVisual Blank { get; } = new();

    public Color Background { get; init; } = Colors.Black;

    /// <summary>When set, background is a vertical gradient from Background to this.</summary>
    public Color? BackgroundGradientTo { get; init; }

    public ImageSource? Icon { get; init; }

    /// <summary>
    /// Vector glyph, drawn centred and scaled to <see cref="IconScale"/>. Used for
    /// transport controls and similar - no image assets to ship, and it stays sharp
    /// at any cell resolution.
    /// </summary>
    public Geometry? Glyph { get; init; }

    public Color GlyphColor { get; init; } = Colors.White;

    /// <summary>
    /// A letterform used as the icon - a bold B, an italic I, an underlined U.
    ///
    /// For text formatting the letter is a better icon than any drawing of one, which
    /// is why every word processor's toolbar has used it for thirty years. Drawn at
    /// icon size and styled, not as a label.
    /// </summary>
    public string? IconLetter { get; init; }

    public FontWeight IconLetterWeight { get; init; } = FontWeights.Normal;

    public FontStyle IconLetterStyle { get; init; } = FontStyles.Normal;

    /// <summary>Underline or strikethrough on the letterform.</summary>
    public TextDecorationCollection? IconLetterDecorations { get; init; }

    /// <summary>
    /// Icon or glyph size as a fraction of the artwork area - which is the cell minus
    /// whatever the label reserves, not the whole cell.
    /// </summary>
    public double IconScale { get; init; } = 0.85;

    public string? Label { get; init; }
    public Color LabelColor { get; init; } = Colors.White;
    public double LabelSize { get; init; } = 12;
    public LabelPosition LabelPosition { get; init; } = LabelPosition.Bottom;

    /// <summary>
    /// How many lines of label to reserve room for, whether or not they are used.
    ///
    /// Set this to 2 on keys that sit next to each other in a grid: a one-line name
    /// and a two-line name then still produce identically sized artwork, so the deck
    /// reads as one set. Leave it at 1 where a cell stands alone.
    /// </summary>
    public int ReservedLabelLines { get; init; } = 1;

    /// <summary>Large centred value, e.g. "43%" on an info cell.</summary>
    public string? BigText { get; init; }
    public Color BigTextColor { get; init; } = Color.FromRgb(0x4D, 0xD9, 0xE8);

    /// <summary>Multiplier on the big value's size, for readings that need more room.</summary>
    public double BigTextScale { get; init; } = 1.0;

    /// <summary>0..1 - draws a circular gauge ring. Null = no ring.</summary>
    public double? GaugeFraction { get; init; }
    public Color GaugeColor { get; init; } = Color.FromRgb(0x4D, 0xD9, 0xE8);

    /// <summary>Darkens the cell, e.g. for a disabled or inactive action.</summary>
    public bool Dimmed { get; init; }
}
