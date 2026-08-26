using System.Windows.Media;
using DotStream.Core;
using DotStream.Media;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>
/// One entry in the palette the user drags from.
///
/// <see cref="Create"/> is a factory rather than a finished button because a button
/// often needs to close over the page it lands on - "back" has to know where it is.
/// </summary>
public sealed class ActionDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required Func<DeckNavigator, DeckButton> Create { get; init; }

    /// <summary>Still appearance, used for the palette thumbnail.</summary>
    public required CellVisual Preview { get; init; }

    public override string ToString() => Name;
}

/// <summary>
/// The built-in actions. Deliberately small - this is the seed of the profile
/// schema, so anything added here is something we are committing to serialising
/// later.
/// </summary>
public sealed class ActionCatalog
{
    private static readonly Color MediaBackground = Color.FromRgb(0x10, 0x1A, 0x14);
    private static readonly Color MediaAccent = Color.FromRgb(0x5B, 0xE5, 0x8F);
    private static readonly Color SystemBackground = Color.FromRgb(0x14, 0x14, 0x1A);
    private static readonly Color NavigationBackground = Color.FromRgb(0x1A, 0x14, 0x14);

    private readonly MediaHub _media;

    public ActionCatalog(MediaHub media)
    {
        _media = media;
        Actions = Build();
    }

    public IReadOnlyList<ActionDefinition> Actions { get; }

    public ActionDefinition? ById(string id) =>
        Actions.FirstOrDefault(a => a.Id == id);

    private IReadOnlyList<ActionDefinition> Build() =>
    [
        new ActionDefinition
        {
            Id = "nav.back",
            Name = "Back",
            Category = "Navigation",
            Preview = Glyph(Glyphs.Back, NavigationBackground, Colors.White, "Back"),
            Create = navigator => new DeckButton
            {
                Tag = "nav.back",
                Visual = () => Glyph(Glyphs.Back, NavigationBackground, Colors.White, "Back"),
                OnPress = () => { navigator.Pop(); return Task.CompletedTask; }
            }
        },

        new ActionDefinition
        {
            Id = "media.playpause",
            Name = "Play / Pause",
            Category = "Media",
            Preview = Glyph(Glyphs.Play, MediaBackground, MediaAccent, "Play"),
            Create = _ => new DeckButton
            {
                Tag = "media.playpause",
                // Reads live state, so the key shows what pressing it will do next.
                Visual = () => _media.Snapshot?.IsPlaying == true
                    ? Glyph(Glyphs.Pause, MediaBackground, MediaAccent, "Pause")
                    : Glyph(Glyphs.Play, MediaBackground, MediaAccent, "Play"),
                OnPress = () => _media.TogglePlayPauseAsync()
            }
        },

        new ActionDefinition
        {
            Id = "media.next",
            Name = "Next track",
            Category = "Media",
            Preview = Glyph(Glyphs.Next, MediaBackground, Colors.White, "Next"),
            Create = _ => new DeckButton
            {
                Tag = "media.next",
                Visual = () => Glyph(Glyphs.Next, MediaBackground, Colors.White, "Next"),
                OnPress = () => _media.NextAsync()
            }
        },

        new ActionDefinition
        {
            Id = "media.previous",
            Name = "Previous track",
            Category = "Media",
            Preview = Glyph(Glyphs.Previous, MediaBackground, Colors.White, "Prev"),
            Create = _ => new DeckButton
            {
                Tag = "media.previous",
                Visual = () => Glyph(Glyphs.Previous, MediaBackground, Colors.White, "Prev"),
                OnPress = () => _media.PreviousAsync()
            }
        },

        // A video player with one file open reports next and previous as unavailable -
        // there is no next track - so seeking is the pair that actually applies. The
        // distances match Media Player's own two buttons.
        new ActionDefinition
        {
            Id = "media.back10",
            Name = "Back 10 seconds",
            Category = "Media",
            Preview = Glyph(SeekBack, MediaBackground, Colors.White, "10 s"),
            Create = _ => new DeckButton
            {
                Tag = "media.back10",
                Visual = () => Glyph(SeekBack, MediaBackground, Colors.White, "10 s"),
                OnPress = () => _media.SeekByAsync(TimeSpan.FromSeconds(-10))
            }
        },

        new ActionDefinition
        {
            Id = "media.forward30",
            Name = "Forward 30 seconds",
            Category = "Media",
            Preview = Glyph(SeekForward, MediaBackground, Colors.White, "30 s"),
            Create = _ => new DeckButton
            {
                Tag = "media.forward30",
                Visual = () => Glyph(SeekForward, MediaBackground, Colors.White, "30 s"),
                OnPress = () => _media.SeekByAsync(TimeSpan.FromSeconds(30))
            }
        },

        new ActionDefinition
        {
            Id = "media.artwork",
            Name = "Now playing",
            Category = "Media",
            Preview = NowPlaying(null),
            Create = _ => new DeckButton
            {
                Tag = "media.artwork",
                Visual = () => NowPlaying(_media.Snapshot),
                OnPress = () => _media.TogglePlayPauseAsync()
            }
        },

        new ActionDefinition
        {
            Id = "system.volume.up",
            Name = "Volume up",
            Category = "System",
            Preview = Glyph(Glyphs.VolumeUp, SystemBackground, Colors.White, "Vol +"),
            Create = _ => new DeckButton
            {
                Tag = "system.volume.up",
                // Holding it keeps turning it up, which is the whole point of a volume
                // key. Fifteen separate presses to cross the range is not a control.
                RepeatWhileHeld = true,
                Visual = () => Glyph(Glyphs.VolumeUp, SystemBackground, Colors.White, "Vol +"),
                OnPress = () => { SystemVolume.Up(); return Task.CompletedTask; }
            }
        },

        new ActionDefinition
        {
            Id = "system.volume.down",
            Name = "Volume down",
            Category = "System",
            Preview = Glyph(Glyphs.VolumeDown, SystemBackground, Colors.White, "Vol -"),
            Create = _ => new DeckButton
            {
                Tag = "system.volume.down",
                RepeatWhileHeld = true,
                Visual = () => Glyph(Glyphs.VolumeDown, SystemBackground, Colors.White, "Vol -"),
                OnPress = () => { SystemVolume.Down(); return Task.CompletedTask; }
            }
        },

        new ActionDefinition
        {
            Id = "input.hotkey",
            Name = "Add a Hotkey",
            Category = "Input",
            Preview = Glyph(Glyphs.Plus, Color.FromRgb(0x14, 0x12, 0x0B), Color.FromRgb(0xFF, 0xC9, 0x6B), "Ctrl+"),
            // Configured per key: the window asks which combination when dropped.
            Create = _ => new DeckButton
            {
                Tag = "input.hotkey",
                Visual = () => Glyph(Glyphs.Plus, Color.FromRgb(0x14, 0x12, 0x0B),
                    Color.FromRgb(0xFF, 0xC9, 0x6B), "Ctrl+")
            }
        },

        new ActionDefinition
        {
            Id = "input.text",
            Name = "Type some text",
            Category = "Input",
            Preview = Glyph(Glyphs.Comment, Color.FromRgb(0x0B, 0x14, 0x0E),
                Color.FromRgb(0x8B, 0xE2, 0xA8), "Text"),
            // Configured per key: the window asks what to type when dropped.
            Create = _ => new DeckButton
            {
                Tag = "input.text",
                Visual = () => Glyph(Glyphs.Comment, Color.FromRgb(0x0B, 0x14, 0x0E),
                    Color.FromRgb(0x8B, 0xE2, 0xA8), "Text")
            }
        },

        new ActionDefinition
        {
            Id = "input.run",
            Name = "Run a program",
            Category = "Input",
            Preview = Glyph(Glyphs.Play, Color.FromRgb(0x14, 0x0B, 0x18),
                Color.FromRgb(0xC9, 0x8B, 0xE2), "Run"),
            // Configured per key: the window asks what to run when dropped.
            Create = _ => new DeckButton
            {
                Tag = "input.run",
                Visual = () => Glyph(Glyphs.Play, Color.FromRgb(0x14, 0x0B, 0x18),
                    Color.FromRgb(0xC9, 0x8B, 0xE2), "Run")
            }
        },

        new ActionDefinition
        {
            Id = "input.link",
            Name = "Open a link",
            Category = "Input",
            Preview = Glyph(Glyphs.Link, Color.FromRgb(0x0B, 0x11, 0x18),
                Color.FromRgb(0x6F, 0xA8, 0xDC), "Link"),
            // Configured per key: the window asks for the address when dropped.
            Create = _ => new DeckButton
            {
                Tag = "input.link",
                Visual = () => Glyph(Glyphs.Link, Color.FromRgb(0x0B, 0x11, 0x18),
                    Color.FromRgb(0x6F, 0xA8, 0xDC), "Link")
            }
        },

        new ActionDefinition
        {
            Id = "mcp.call",
            Name = "Call an MCP tool",
            Category = "Integration",
            Preview = Glyph(Glyphs.Plus, Color.FromRgb(0x0B, 0x14, 0x18), WidgetTheme.StreamCyan, "MCP"),
            // Configured per key rather than created ready to run - the window asks
            // for a server and a tool when this is dropped on a key.
            Create = _ => new DeckButton
            {
                Tag = "mcp.call",
                Visual = () => Glyph(Glyphs.Plus, Color.FromRgb(0x0B, 0x14, 0x18), WidgetTheme.StreamCyan, "MCP")
            }
        },

        new ActionDefinition
        {
            Id = "system.volume.mute",
            Name = "Mute",
            Category = "System",
            Preview = Glyph(Glyphs.Mute, SystemBackground, Colors.White, "Mute"),
            Create = _ => new DeckButton
            {
                Tag = "system.volume.mute",
                Visual = () => Glyph(Glyphs.Mute, SystemBackground, Colors.White, "Mute"),
                OnPress = () => { SystemVolume.ToggleMute(); return Task.CompletedTask; }
            }
        }
    ];

    // Fluent has a left and a right arrow drawn as a matched pair; Glyphs has only the
    // left one, and a rewind key beside a forward key drawn in a different hand looks
    // wrong. Falls back to the hand-drawn set if the generated file ever loses them.
    private static readonly Geometry SeekBack = FluentIcons.Get("back") ?? Glyphs.Back;
    private static readonly Geometry SeekForward = FluentIcons.Get("forward") ?? Glyphs.Next;

    private static CellVisual Glyph(Geometry glyph, Color background, Color colour, string label) => new()
    {
        Background = background,
        Glyph = glyph,
        GlyphColor = colour,
        IconScale = 0.62,
        Label = label,
        LabelColor = Color.FromRgb(0xB4, 0xB4, 0xBE),
        LabelSize = 12,
        LabelPosition = LabelPosition.Bottom,
        ReservedLabelLines = 1
    };

    private static CellVisual NowPlaying(MediaSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new CellVisual
            {
                Background = MediaBackground,
                Label = "Nothing playing",
                LabelColor = Color.FromRgb(0x7C, 0x7C, 0x88),
                LabelSize = 11,
                LabelPosition = LabelPosition.Bottom,
                ReservedLabelLines = 1
            };
        }

        return new CellVisual
        {
            Background = Colors.Black,
            Icon = snapshot.Thumbnail,
            IconScale = 1.0,
            Label = snapshot.Title,
            LabelSize = 11,
            LabelPosition = LabelPosition.Bottom,
            ReservedLabelLines = 1,
            Dimmed = !snapshot.IsPlaying
        };
    }
}
