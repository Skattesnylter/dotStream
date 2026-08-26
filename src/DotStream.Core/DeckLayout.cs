namespace DotStream.Core;

/// <summary>
/// Physical layout of the AJAZZ AKP153E.
///
/// The device is a 6x3 grid of 18 LCD cells:
///   - columns 0-4 hold the 15 physical keys      (protocol index 1-15  / 0x01-0x0F)
///   - column 5 holds 3 LCD cells with no switch  (protocol index 16-18 / 0x10-0x12)
///
/// Column 5 is what the vendor markets as the "side screen". It is not a separate
/// 854x480 panel - it is three ordinary cells addressed with the same BAT command
/// as the keys, which is why live info display costs us nothing extra.
///
/// Protocol index arrangement (note the reversed column order):
///
///     13  10  07  04  01 | 16
///     14  11  08  05  02 | 17
///     15  12  09  06  03 | 18
///
/// CONFIRMED ON HARDWARE, 04.08.2026. A numbered image was uploaded to each of the
/// eighteen cells on a real AKP153E and read back off the device; every position
/// matched, including the placement of 16-18 in column 5. This arrangement came from
/// the ZCube gist and pyajazz and was carried as a hypothesis for months. It was
/// right.
/// </summary>
public static class DeckLayout
{
    public const int Columns = 6;
    public const int Rows = 3;

    public const int FirstKey = 1;
    public const int LastKey = 15;
    public const int FirstInfoCell = 16;
    public const int LastInfoCell = 18;
    public const int CellCount = 18;

    /// <summary>Column index holding the three non-clickable info cells.</summary>
    public const int InfoColumn = 5;

    /// <summary>
    /// Native LCD resolution of a single cell, in pixels.
    ///
    /// MEASURED, 04.08.2026. Not 85, which the older protocol notes claimed, and not
    /// 126, which AJAZZ's own AKP153 user guide recommends for custom icons. A test
    /// pattern with a coloured band on each edge was resized live against the hardware
    /// until all four bands were equally thick, which happens at exactly 100.
    ///
    /// The size matters more than it looks. A cell is a persistent framebuffer and an
    /// upload only writes where the image reaches, so anything smaller leaves a ring of
    /// whatever was there before - which is why the deck showed a stubborn pale border
    /// and fragments of the vendor logo until this was corrected. Info cells 16-18
    /// measure the same as the keys.
    /// </summary>
    public const int CellPixels = 100;

    public static bool IsKey(int protocolIndex) =>
        protocolIndex is >= FirstKey and <= LastKey;

    public static bool IsInfoCell(int protocolIndex) =>
        protocolIndex is >= FirstInfoCell and <= LastInfoCell;

    public static bool IsValid(int protocolIndex) =>
        protocolIndex is >= FirstKey and <= LastInfoCell;

    /// <summary>Grid position (row, column) -&gt; protocol index.</summary>
    public static int ToProtocolIndex(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);

        return column == InfoColumn
            ? FirstInfoCell + row
            : (4 - column) * Rows + row + 1;
    }

    /// <summary>Protocol index -&gt; grid position (row, column).</summary>
    public static (int Row, int Column) FromProtocolIndex(int protocolIndex)
    {
        if (IsInfoCell(protocolIndex))
            return (protocolIndex - FirstInfoCell, InfoColumn);

        if (!IsKey(protocolIndex))
            throw new ArgumentOutOfRangeException(nameof(protocolIndex), protocolIndex, "Not a valid cell index (1-18).");

        int zeroBased = protocolIndex - 1;
        return (zeroBased % Rows, 4 - zeroBased / Rows);
    }

    public static IEnumerable<int> AllCells() => Enumerable.Range(FirstKey, CellCount);
    public static IEnumerable<int> Keys() => Enumerable.Range(FirstKey, LastKey - FirstKey + 1);
    public static IEnumerable<int> InfoCells() => Enumerable.Range(FirstInfoCell, LastInfoCell - FirstInfoCell + 1);
}
