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
    /// What this button was built from - an InstalledApp, an action id. Lets the
    /// editor work out what a cell holds without a parallel bookkeeping map.
    /// </summary>
    public object? Tag { get; init; }

    public static DeckButton Static(CellVisual visual, Func<Task>? onPress = null) =>
        new() { Visual = () => visual, OnPress = onPress };
}
