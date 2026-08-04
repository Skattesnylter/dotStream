namespace DotStream.Core;

/// <summary>
/// A full screen of the deck: up to 15 keys plus, optionally, the three info cells.
///
/// A page that does not define an info cell leaves it to the global widgets, so a
/// sub-page can take over cell 16 for a progress ring while cell 18 keeps showing
/// the clock.
/// </summary>
public sealed class DeckPage
{
    public required string Id { get; init; }

    public string? Title { get; init; }

    /// <summary>
    /// How often the key cells should be re-rendered. Null for a static page - it
    /// is painted once when pushed and then left alone.
    /// </summary>
    public TimeSpan? RefreshInterval { get; init; }

    public Dictionary<int, DeckButton> Cells { get; } = new();

    public DeckPage Set(int protocolIndex, DeckButton button)
    {
        if (!DeckLayout.IsValid(protocolIndex))
            throw new ArgumentOutOfRangeException(nameof(protocolIndex), protocolIndex, "Not a valid cell index (1-18).");

        Cells[protocolIndex] = button;
        return this;
    }

    /// <summary>Places a button by grid position, which is how humans think about it.</summary>
    public DeckPage SetAt(int row, int column, DeckButton button) =>
        Set(DeckLayout.ToProtocolIndex(row, column), button);

    public DeckButton? Get(int protocolIndex) =>
        Cells.GetValueOrDefault(protocolIndex);
}
