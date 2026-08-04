using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DotStream.Core;

namespace DotStream.Rendering;

/// <summary>
/// Turns a <see cref="CellVisual"/> into pixels.
///
/// Output is always upright at the cell's native resolution. The device-specific
/// orientation transform (the AKP153 wants its JPEGs rotated) lives in the
/// transport, not here - that way the simulator shows what a human would expect
/// and there is exactly one rendering path.
///
/// Must be called on an STA thread: RenderTargetBitmap requires it.
/// </summary>
public sealed class CellRenderer
{
    /// <summary>
    /// Line spacing as a multiple of the font size. Verdana has a large x-height, so
    /// the default single spacing sets two lines almost touching.
    /// </summary>
    private const double LineSpacing = 1.45;


    private readonly int _size;

    public CellRenderer(int size = DeckLayout.CellPixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        _size = size;
    }

    public int Size => _size;

    /// <summary>
    /// How glyphs meet the pixel grid. See <see cref="Format"/>.
    ///
    /// Ideal by default: measured against Display, Display wins on stroke integrity
    /// but loses on spacing, and side by side the spacing turned out to matter more.
    /// </summary>
    public TextFormattingMode FormattingMode { get; set; } = TextFormattingMode.Ideal;

    /// <summary>Font family list, most preferred first. Swappable for comparison.</summary>
    public FontFamily LabelFontFamily { get; set; } = new("Verdana, Segoe UI Variable Small, Tahoma");

    /// <summary>
    /// Label weight. The most useful knob at small sizes: as the em size drops, stems
    /// get thinner than a pixel and antialias away, which is why letters start looking
    /// eaten before they look small. A heavier weight buys back the coverage.
    /// </summary>
    public FontWeight LabelWeight { get; set; } = FontWeights.SemiBold;

    /// <summary>
    /// Grayscale, ClearType or Aliased. ClearType adds colour fringes that survive
    /// JPEG compression to the device; Aliased removes antialiasing entirely, which on
    /// a small panel can read better than half-lit pixels.
    /// </summary>
    public TextRenderingMode RenderingMode { get; set; } = TextRenderingMode.Grayscale;

    public RenderedCell Render(CellVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        var drawing = new DrawingVisual();

        // Icons arrive at 256px and land on an 85px cell. The default scaling mode
        // makes that look cheap; Fant resampling is what keeps small artwork legible.
        RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.HighQuality);

        // Text at 10-11px is the hardest thing on a cell this size.
        //
        // Ideal formatting, not Display: Display snaps every glyph to a whole pixel,
        // which at 11px steals or adds a pixel between narrow letters and makes the
        // spacing visibly uneven - "AutoHotkey" comes out looking misspelled. Display
        // was worth it while the simulator resampled 85px to 92px; now that it draws
        // 1:1, correct advance widths matter more than pixel-aligned stems.
        //
        // Grayscale rather than ClearType because subpixel rendering produces colour
        // fringes that survive JPEG compression and look like artefacts on the LCD.
        TextOptions.SetTextFormattingMode(drawing, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(drawing, RenderingMode);

        using (DrawingContext dc = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, _size, _size);

            Brush background = visual.BackgroundGradientTo is { } gradientTo
                ? new LinearGradientBrush(visual.Background, gradientTo, 90)
                : new SolidColorBrush(visual.Background);
            background.Freeze();
            dc.DrawRectangle(background, null, bounds);

            // Lay the label out first. Artwork is then fitted into whatever is left,
            // rather than being centred in the whole cell and hoping the two do not
            // meet - which is exactly how a label that fits still ended up hidden
            // behind an icon.
            bool hasLabel = !string.IsNullOrEmpty(visual.Label) && visual.LabelPosition != LabelPosition.None;
            FormattedText? label = hasLabel ? FitLabel(visual) : null;
            Rect content = ContentArea(visual, label);

            if (visual.GaugeFraction is { } fraction)
                DrawGauge(dc, fraction, visual.GaugeColor);

            if (visual.Icon is { } icon)
                DrawIcon(dc, icon, visual, content);

            if (visual.Glyph is { } glyph)
                DrawGlyph(dc, glyph, visual, content);

            if (!string.IsNullOrEmpty(visual.IconLetter))
                DrawIconLetter(dc, visual, content);

            if (!string.IsNullOrEmpty(visual.BigText))
                DrawBigText(dc, visual, content);

            if (label is not null)
                DrawLabel(dc, visual, label);

            if (visual.Dimmed)
            {
                var veil = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
                veil.Freeze();
                dc.DrawRectangle(veil, null, bounds);
            }
        }

        var bitmap = new RenderTargetBitmap(_size, _size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();

        return new RenderedCell(bitmap, HashPixels(bitmap));
    }

    private void DrawGauge(DrawingContext dc, double fraction, Color color)
    {
        fraction = Math.Clamp(fraction, 0, 1);

        double thickness = _size * 0.09;
        double radius = (_size - thickness) / 2 - _size * 0.07;
        var centre = new Point(_size / 2.0, _size / 2.0);

        var trackPen = new Pen(new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B)), thickness);
        trackPen.Freeze();
        dc.DrawEllipse(null, trackPen, centre, radius, radius);

        if (fraction <= 0.001) return;

        var start = new Point(centre.X, centre.Y - radius);
        var geometry = new StreamGeometry();

        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(start, false, false);

            if (fraction >= 0.999)
            {
                // A single arc cannot express 360 degrees; use two half-turns.
                var opposite = new Point(centre.X, centre.Y + radius);
                g.ArcTo(opposite, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                g.ArcTo(start, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
            else
            {
                double sweepDegrees = fraction * 360.0;
                double radians = (sweepDegrees - 90) * Math.PI / 180.0;
                var end = new Point(centre.X + radius * Math.Cos(radians), centre.Y + radius * Math.Sin(radians));
                g.ArcTo(end, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
            }
        }

        geometry.Freeze();

        var pen = new Pen(new SolidColorBrush(color), thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();

        dc.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// Where the artwork goes.
    ///
    /// The label is drawn over the artwork, not beside it, so the icon keeps the full
    /// cell - on 85 pixels there is not enough room to give the label its own band
    /// without shrinking the icon to something unrecognisable. All that is left is a
    /// small nudge away from the label edge so the two are not dead centred on each
    /// other; legibility is handled on the glyphs themselves.
    /// </summary>
    /// <summary>
    /// The area artwork gets, once the label has claimed its band.
    ///
    /// The band is reserved from the declared line count, not from how many lines the
    /// text actually needs - so artwork stays the same size whether a name wraps or
    /// not, and text never lands on top of an icon. Overlaying the two and relying on
    /// a scrim or an outline for contrast does not survive this cell size: at ~10px
    /// the gaps between letters are about a pixel wide, and anything drawn around the
    /// glyphs to separate them from the artwork fills those gaps in instead.
    /// </summary>
    private Rect ContentArea(CellVisual visual, FormattedText? label)
    {
        if (label is null) return new Rect(0, 0, _size, _size);

        int lines = Math.Max(1, visual.ReservedLabelLines);
        double band = Math.Min(visual.LabelSize * LineSpacing * lines + _size * 0.035, _size * 0.55);

        return visual.LabelPosition == LabelPosition.Top
            ? new Rect(0, band, _size, _size - band)
            : new Rect(0, 0, _size, _size - band);
    }

    private static void DrawIcon(DrawingContext dc, ImageSource icon, CellVisual visual, Rect content)
    {
        double target = Math.Min(content.Width, content.Height) * Math.Clamp(visual.IconScale, 0.05, 1.0);

        dc.DrawImage(icon, new Rect(
            content.X + (content.Width - target) / 2,
            content.Y + (content.Height - target) / 2,
            target, target));
    }

    private static void DrawGlyph(DrawingContext dc, Geometry glyph, CellVisual visual, Rect content)
    {
        Rect bounds = glyph.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

        double target = Math.Min(content.Width, content.Height) * Math.Clamp(visual.IconScale, 0.05, 1.0);
        double scale = target / Math.Max(bounds.Width, bounds.Height);

        var transform = new TransformGroup();
        transform.Children.Add(new TranslateTransform(
            -(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2)));
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(
            content.X + content.Width / 2, content.Y + content.Height / 2));
        transform.Freeze();

        var brush = new SolidColorBrush(visual.GlyphColor);
        brush.Freeze();

        dc.PushTransform(transform);
        dc.DrawGeometry(brush, null, glyph);
        dc.Pop();
    }

    /// <summary>
    /// A letterform standing in for an icon - the B of bold, the I of italic. Scaled
    /// to the artwork area like a glyph would be, not sized like a label.
    /// </summary>
    private void DrawIconLetter(DrawingContext dc, CellVisual visual, Rect content)
    {
        double size = Math.Min(content.Width, content.Height) * Math.Clamp(visual.IconScale, 0.05, 1.0);

        var brush = new SolidColorBrush(visual.GlyphColor);
        brush.Freeze();

        var text = new FormattedText(
            visual.IconLetter!,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(LabelFontFamily, visual.IconLetterStyle, visual.IconLetterWeight, FontStretches.Normal),
            size,
            brush,
            numberSubstitution: null,
            FormattingMode,
            pixelsPerDip: 1.0);

        if (visual.IconLetterDecorations is { } decorations)
            text.SetTextDecorations(decorations);

        dc.DrawText(text, new Point(
            content.X + (content.Width - text.Width) / 2,
            content.Y + (content.Height - text.Height) / 2));
    }

    private void DrawBigText(DrawingContext dc, CellVisual visual, Rect content)
    {
        FormattedText text = Format(
            visual.BigText!, _size * 0.235 * Math.Clamp(visual.BigTextScale, 0.3, 2.0),
            visual.BigTextColor, FontWeights.Bold);
        text.TextAlignment = TextAlignment.Center;
        text.MaxTextWidth = _size;

        dc.DrawText(text, new Point(0, content.Y + (content.Height - text.Height) / 2));
    }

    private void DrawLabel(DrawingContext dc, CellVisual visual, FormattedText text)
    {
        bool atTop = visual.LabelPosition == LabelPosition.Top;

        double y = atTop
            ? _size * 0.03
            : _size - text.Height - _size * 0.03;

        // Snapped to a whole pixel so the baseline lands on a row. Both ends of the
        // glyphs have to be on the grid - an integral cap height buys nothing if the
        // baseline itself starts on a half pixel.
        var origin = new Point(2, Math.Round(y));

        // No shadow, outline or scrim. The reserved band means the label always sits
        // on flat background, so nothing has to be drawn around the glyphs - which is
        // what kept closing up the one-pixel gaps between letters.
        var fill = new SolidColorBrush(visual.LabelColor);
        fill.Freeze();
        text.SetForegroundBrush(fill);

        dc.DrawText(text, origin);
    }

    /// <summary>
    /// Lays out the label at the requested size, stepping down only when it will not
    /// otherwise fit.
    ///
    /// Shrinking the font globally to rescue the two longest names would make every
    /// other key worse, and 11px is already small on an 85px LCD. So the size given
    /// in the CellVisual is a maximum, not a fixed value, and only the labels that
    /// need the room pay for it. Ellipsis remains as the last resort.
    /// </summary>
    private FormattedText FitLabel(CellVisual visual)
    {
        // A narrow range on purpose. Letting every label find its own best size makes
        // each key legible but the deck as a whole look like fourteen different
        // typefaces. Capping the drop at 15% costs the odd ellipsis and buys a set
        // that reads as one thing.
        const double absoluteMinimum = 8.0;
        const double maximumShrink = 0.85;
        const double step = 0.5;

        int maximumLines = Math.Max(1, visual.ReservedLabelLines);

        double size = visual.LabelSize;
        double floor = Math.Max(absoluteMinimum, size * maximumShrink);

        FormattedText text;

        while (true)
        {
            text = Format(visual.Label!, size, visual.LabelColor, LabelWeight);
            text.TextAlignment = TextAlignment.Center;
            text.MaxTextWidth = _size - 4;
            text.LineHeight = size * LineSpacing;

            int lines = (int)Math.Round(text.Height / text.LineHeight);
            if (lines <= maximumLines || size <= floor) break;

            size -= step;
        }

        text.MaxLineCount = maximumLines;
        text.Trimming = TextTrimming.CharacterEllipsis;
        return text;
    }

    private void DrawLabelScrim(DrawingContext dc, double textHeight, bool atTop)
    {
        double height = Math.Min(_size, textHeight + _size * 0.20);

        // Opaque end against the label, fading away from it.
        var brush = atTop
            ? new LinearGradientBrush(Color.FromArgb(210, 0, 0, 0), Color.FromArgb(0, 0, 0, 0),
                new Point(0, 0), new Point(0, 1))
            : new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(210, 0, 0, 0),
                new Point(0, 0), new Point(0, 1));
        brush.Freeze();

        dc.DrawRectangle(brush, null, atTop
            ? new Rect(0, 0, _size, height)
            : new Rect(0, _size - height, _size, height));
    }

    /// <summary>
    /// Note the explicit TextFormattingMode.
    ///
    /// TextOptions.SetTextFormattingMode on the DrawingVisual does nothing here: it is
    /// an attached property inherited by framework elements, and DrawingContext.DrawText
    /// renders whatever the FormattedText itself was built with. Setting it on the
    /// visual looks like it works and silently does not.
    ///
    /// The mode is a property rather than a constant because there is no right answer
    /// to pick here. Display snaps stems to the pixel grid, which keeps the top
    /// strokes of B, C, S and 2 solid, but it also rounds every advance width to a
    /// whole pixel, so letter spacing goes uneven. Ideal keeps the spacing and lets
    /// the tops fray. Which one reads better is a judgement, and it has to be made
    /// again on the actual LCD - so it stays switchable.
    /// </summary>
    private FormattedText Format(string value, double emSize, Color color, FontWeight weight)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        return new FormattedText(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(LabelFontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            emSize,
            brush,
            numberSubstitution: null,
            FormattingMode,
            pixelsPerDip: 1.0);
    }

    private static string HashPixels(BitmapSource bitmap)
    {
        int stride = bitmap.PixelWidth * 4;
        var buffer = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(buffer, stride, 0);
        return Convert.ToHexString(SHA256.HashData(buffer));
    }
}
