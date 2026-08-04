using System.Windows.Input;

namespace DotStream.App;

/// <summary>
/// Commands of our own, so a shortcut does not have to borrow an unrelated built-in
/// one. Binding Ctrl+L to ApplicationCommands.Print works until somebody wonders why
/// printing opens a log.
/// </summary>
public static class DeckCommands
{
    public static RoutedUICommand ShowConsole { get; } = new(
        "Console", nameof(ShowConsole), typeof(DeckCommands),
        [new KeyGesture(Key.L, ModifierKeys.Control)]);
}
