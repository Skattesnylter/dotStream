namespace DotStream.Core;

/// <summary>
/// One cell on a page: how it looks right now, and what happens when pressed.
///
/// The visual is a function rather than a value so a button can reflect live state
/// (play vs pause, muted vs not) without anything having to push updates into it.
/// Re-rendering is cheap and the controller's hash check throws away the result
/// when nothing actually changed.
/// </summary>
public sealed class DeckButton
{
    public required Func<CellVisual> Visual { get; init; }

    /// <summary>Null for a decorative cell - info cells always have null.</summary>
    public Func<Task>? OnPress { get; init; }

    /// <summary>
    /// What a long press does, if anything different.
    ///
    /// The device reports press and release separately, which turns fifteen keys into
    /// thirty actions without a single folder. It costs something, though: a button
    /// with a hold action cannot fire on the way down, because until the finger lifts
    /// nobody knows which of the two was meant. Leave this null and the press stays
    /// instant.
    /// </summary>
    public Func<Task>? OnHold { get; init; }

    /// <summary>
    /// Fires again, and again, while the key is held. For volume and anything else
    /// where the alternative is pressing fifteen times.
    ///
    /// Mutually exclusive with <see cref="OnHold"/> in practice: a key cannot both
    /// repeat while held and mean something different when held.
    /// </summary>
    public bool RepeatWhileHeld { get; init; }

    /// <summary>
    /// What this button was built from - an InstalledApp, an action id. Lets the
    /// editor work out what a cell holds without a parallel bookkeeping map.
    /// </summary>
    public object? Tag { get; init; }

    public static DeckButton Static(CellVisual visual, Func<Task>? onPress = null) =>
        new() { Visual = () => visual, OnPress = onPress };
}
