using DotStream.Core;

namespace DotStream.Rendering.Widgets;

/// <summary>
/// Drives one of the three info cells in column 5.
///
/// A widget only describes itself as a <see cref="CellVisual"/>; it never touches the
/// device. The controller's dirty-tracking means a widget can be polled every second
/// and still cost nothing when its value has not visibly changed.
/// </summary>
public interface IInfoWidget
{
    /// <summary>Stable identifier, used in the saved profile.</summary>
    string Id { get; }

    /// <summary>Shown in the palette.</summary>
    string Name { get; }

    /// <summary>How often <see cref="Render"/> should be called.</summary>
    TimeSpan Interval { get; }

    /// <summary>Colours used until the user picks their own.</summary>
    WidgetTheme DefaultTheme { get; }

    CellVisual Render(WidgetTheme theme);
}
