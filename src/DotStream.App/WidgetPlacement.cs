using DotStream.Core;
using DotStream.Rendering.Widgets;

namespace DotStream.App;

/// <summary>
/// A widget sitting on a particular cell, with the colours chosen for it there.
///
/// Mutable on purpose: editing colours should repaint the cell, not rebuild the
/// button and everything holding a reference to it.
/// </summary>
public sealed class WidgetPlacement
{
    public required IInfoWidget Widget { get; init; }

    public required WidgetTheme Theme { get; set; }
}
