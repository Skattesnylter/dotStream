using System.Windows.Media;

namespace DotStream.Rendering;

/// <summary>
/// Vector glyphs, authored on a 24x24 grid.
///
/// Deliberately not an icon font or a set of PNGs: the renderer scales geometry to
/// whatever the cell resolution turns out to be, and there is nothing to ship or keep
/// in sync. All geometries are frozen and safe to share across threads.
///
/// Notably absent are B, I and U. For text formatting the letter itself is the icon -
/// see <see cref="DotStream.Core.CellVisual.IconLetter"/>.
/// </summary>
public static class Glyphs
{
    // ---- transport and system -------------------------------------------------

    public static Geometry Play { get; } = Make("M8,5 L19,12 L8,19 Z");
    public static Geometry Pause { get; } = Make("M7,5 H10.5 V19 H7 Z M13.5,5 H17 V19 H13.5 Z");
    public static Geometry Next { get; } = Make("M5,5 L14,12 L5,19 Z M16,5 H19 V19 H16 Z");
    public static Geometry Previous { get; } = Make("M19,5 L10,12 L19,19 Z M5,5 H8 V19 H5 Z");
    public static Geometry Back { get; } = Make("M20,11 H8.8 L13.4,6.4 L12,5 L5,12 L12,19 L13.4,17.6 L8.8,13 H20 Z");
    public static Geometry Plus { get; } = Make("M10.5,5 H13.5 V10.5 H19 V13.5 H13.5 V19 H10.5 V13.5 H5 V10.5 H10.5 Z");

    public static Geometry VolumeUp { get; } = Make(
        "M3,9 H7 L12,4.5 V19.5 L7,15 H3 Z M17,8 H19 V11 H22 V13 H19 V16 H17 V13 H14 V11 H17 Z");

    public static Geometry VolumeDown { get; } = Make("M3,9 H7 L12,4.5 V19.5 L7,15 H3 Z M14,11 H22 V13 H14 Z");

    public static Geometry Mute { get; } = Make(
        "M3,9 H7 L12,4.5 V19.5 L7,15 H3 Z M15,9 L17,11 L19,9 L20.5,10.5 L18.5,12.5 L20.5,14.5 " +
        "L19,16 L17,14 L15,16 L13.5,14.5 L15.5,12.5 L13.5,10.5 Z");

    // ---- editing --------------------------------------------------------------

    public static Geometry Save { get; } = Make(
        "M4,3 H17 L21,7 V21 H4 Z M8,3 H16 V9 H8 Z M13,4.5 H15 V8 H13 Z M7,13 H18 V21 H7 Z");

    public static Geometry Open { get; } = Make("M2,5 H10 L12,7.5 H22 V20 H2 Z M2,9 H22 V11 H2 Z");

    public static Geometry Copy { get; } = Make(
        "M8,2 H19 V17 H8 Z M10,4 V15 H17 V4 Z M4,6 H6 V20 H16 V22 H4 Z");

    public static Geometry Cut { get; } = Make(
        "M6.5,2 L12,11 L17.5,2 L19.3,3 L13.2,13.2 L16.5,18.7 A3.2,3.2 0 1,1 14.7,19.7 L12,15.2 " +
        "L9.3,19.7 A3.2,3.2 0 1,1 7.5,18.7 L10.8,13.2 L4.7,3 Z " +
        "M6,20.2 A1.4,1.4 0 1,0 6.01,20.2 M18,20.2 A1.4,1.4 0 1,0 18.01,20.2");

    public static Geometry Paste { get; } = Make(
        "M9,2 H15 V4 H19 V22 H5 V4 H9 Z M10,3.4 V5.4 H14 V3.4 Z M8,9 H16 V11 H8 Z M8,13 H16 V15 H8 Z");

    public static Geometry Undo { get; } = Make(
        "M6,10 H15 A6,6 0 0,1 15,22 H9 V19.4 H15 A3.4,3.4 0 0,0 15,12.6 H6 V17 L0.5,11.3 L6,5.6 Z");

    public static Geometry Redo { get; } = Make(
        "M18,10 H9 A6,6 0 0,0 9,22 H15 V19.4 H9 A3.4,3.4 0 0,1 9,12.6 H18 V17 L23.5,11.3 L18,5.6 Z");

    public static Geometry Find { get; } = Make(
        "M10,2 A8,8 0 1,1 9.99,2 M10,4.6 A5.4,5.4 0 1,0 10.01,4.6 M15.6,15.6 L22,22 L20.2,23.8 L13.8,17.4 Z");

    public static Geometry Delete { get; } = Make(
        "M9,2 H15 V4 H21 V6.4 H3 V4 H9 Z M5,8 H19 V22 H5 Z M8.5,10.5 H10.5 V19.5 H8.5 Z M13.5,10.5 H15.5 V19.5 H13.5 Z");

    public static Geometry Print { get; } = Make(
        "M7,2 H17 V7 H7 Z M3,9 H21 V18 H17 V13 H7 V18 H3 Z M7,15 H17 V22 H7 Z");

    public static Geometry NewFile { get; } = Make(
        "M5,2 H14 L19,7 V22 H5 Z M13,3 V8 H18 M8,13 H16 V15 H8 Z M11,11 H13 V17 H11 Z");

    public static Geometry Comment { get; } = Make("M2,3 H22 V17 H13 L8,22 V17 H2 Z");

    public static Geometry Link { get; } = Make(
        "M9.5,7 H7 A5,5 0 0,0 7,17 H9.5 V14.6 H7 A2.6,2.6 0 0,1 7,9.4 H9.5 Z " +
        "M14.5,7 H17 A5,5 0 0,1 17,17 H14.5 V14.6 H17 A2.6,2.6 0 0,0 17,9.4 H14.5 Z M7.5,10.8 H16.5 V13.2 H7.5 Z");

    // ---- layout ---------------------------------------------------------------

    public static Geometry AlignLeft { get; } = Make("M3,4 H21 V6.4 H3 Z M3,9 H15 V11.4 H3 Z M3,14 H21 V16.4 H3 Z M3,19 H15 V21.4 H3 Z");
    public static Geometry AlignCentre { get; } = Make("M3,4 H21 V6.4 H3 Z M6,9 H18 V11.4 H6 Z M3,14 H21 V16.4 H3 Z M6,19 H18 V21.4 H6 Z");
    public static Geometry AlignRight { get; } = Make("M3,4 H21 V6.4 H3 Z M9,9 H21 V11.4 H9 Z M3,14 H21 V16.4 H3 Z M9,19 H21 V21.4 H9 Z");

    public static Geometry BulletList { get; } = Make(
        "M3,4.2 A1.6,1.6 0 1,0 3.01,4.2 M3,11.2 A1.6,1.6 0 1,0 3.01,11.2 M3,18.2 A1.6,1.6 0 1,0 3.01,18.2 " +
        "M8,4 H21 V6.4 H8 Z M8,11 H21 V13.4 H8 Z M8,18 H21 V20.4 H8 Z");

    public static Geometry Table { get; } = Make(
        "M2,4 H22 V20 H2 Z M2,9 H22 V11 H2 Z M2,14 H22 V16 H2 Z M8,4 H10 V20 H8 Z M14,4 H16 V20 H14 Z");

    public static Geometry Sum { get; } = Make(
        "M5,3 H19 V6 H10.5 L15.5,12 L10.5,18 H19 V21 H5 V18.5 L11,12 L5,5.5 Z");

    public static Geometry Filter { get; } = Make("M2,4 H22 L14,13 V21 L10,19 V13 Z");

    public static Geometry ZoomIn { get; } = Make(
        "M10,2 A8,8 0 1,1 9.99,2 M10,4.6 A5.4,5.4 0 1,0 10.01,4.6 M15.6,15.6 L22,22 L20.2,23.8 L13.8,17.4 Z " +
        "M9,6.5 H11 V9 H13.5 V11 H11 V13.5 H9 V11 H6.5 V9 H9 Z");

    // ---- status ---------------------------------------------------------------

    public static Geometry Check { get; } = Make("M9.4,17.2 L4.2,12 L2.4,13.8 L9.4,20.8 L21.6,8.6 L19.8,6.8 Z");
    public static Geometry Cross { get; } = Make("M5,6.8 L6.8,5 L12,10.2 L17.2,5 L19,6.8 L13.8,12 L19,17.2 L17.2,19 L12,13.8 L6.8,19 L5,17.2 L10.2,12 Z");
    public static Geometry Star { get; } = Make("M12,2 L15.1,8.6 L22,9.5 L17,14.5 L18.3,21.5 L12,18.2 L5.7,21.5 L7,14.5 L2,9.5 L8.9,8.6 Z");
    public static Geometry Lock { get; } = Make("M7,10 V7 A5,5 0 0,1 17,7 V10 H19 V22 H5 V10 Z M9.4,10 H14.6 V7 A2.6,2.6 0 0,0 9.4,7 Z");
    public static Geometry Refresh { get; } = Make(
        "M12,4 A8,8 0 1,1 4.6,15 H7.4 A5.4,5.4 0 1,0 12,6.6 V10 L6.5,5.3 L12,0.6 Z");
    public static Geometry Settings { get; } = Make(
        "M12,8.4 A3.6,3.6 0 1,0 12.01,8.4 M10.4,1 H13.6 L14.1,4 L16.4,5 L18.9,3.3 L21.2,5.6 L19.5,8.1 " +
        "L20.5,10.4 L23.5,10.9 V14.1 L20.5,14.6 L19.5,16.9 L21.2,19.4 L18.9,21.7 L16.4,20 L14.1,21 " +
        "L13.6,24 H10.4 L9.9,21 L7.6,20 L5.1,21.7 L2.8,19.4 L4.5,16.9 L3.5,14.6 L0.5,14.1 V10.9 " +
        "L3.5,10.4 L4.5,8.1 L2.8,5.6 L5.1,3.3 L7.6,5 L9.9,4 Z");

    private static Geometry Make(string path)
    {
        Geometry geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }
}
