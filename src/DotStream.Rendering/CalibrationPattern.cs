using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DotStream.Rendering;

/// <summary>
/// A test image whose job is to make the cell size readable at a glance.
///
/// "It looks a bit off" is not a measurement. A coloured band on each edge is: which
/// bands you can see, and how thick they look against each other, says whether the
/// image is cropped and on which sides. The size is right when all four are present
/// and equal - which is how 100x100 was established, after the notes claimed 85 and
/// the manual claimed 126.
/// </summary>
public static class CalibrationPattern
{
    /// <summary>
    /// Draws the pattern at a given size, labelled with the cell number.
    ///
    /// Returned frozen, so it can cross threads like any other rendered cell.
    /// </summary>
    public static BitmapSource Render(int size, int protocolIndex)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);

        // Deep enough to be unmistakable, shallow enough that four of them do not meet
        // in the middle on a small cell.
        int band = Math.Max(3, size / 12);

        var visual = new DrawingVisual();

        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x18)), null,
                new Rect(0, 0, size, size));

            // Red on top because the bezel is light: a white outline disappeared into
            // it, which cost a round of measurements before anyone noticed.
            dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, size, band));
            dc.DrawRectangle(Brushes.Gold, null, new Rect(0, 0, band, size));
            dc.DrawRectangle(Brushes.Lime, null, new Rect(size - band, 0, band, size));
            dc.DrawRectangle(Brushes.DodgerBlue, null, new Rect(0, size - band, size, band));

            var cross = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x40, 0xFF)), 1);
            double mid = size / 2.0;

            dc.DrawLine(cross, new Point(mid, band), new Point(mid, size - band));
            dc.DrawLine(cross, new Point(band, mid), new Point(size - band, mid));

            var label = new FormattedText(
                protocolIndex.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size / 4.0,
                Brushes.White,
                1.0);

            dc.DrawText(label, new Point(mid - label.Width / 2, mid - label.Height / 2));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }
}
