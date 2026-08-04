using System.Windows;
using System.Windows.Media;
using DotStream.Core;

namespace DotStream.Rendering;

/// <summary>
/// A named icon: either a vector glyph or a styled letterform.
///
/// Both kinds exist because both are right in different places. A pair of scissors is
/// the icon for Cut. For Bold, the icon is a bold B - drawing a picture of one would
/// be worse, which is why no word processor has ever tried.
/// </summary>
public sealed record DeckIcon(string Name, string Category)
{
    public Geometry? Glyph { get; init; }

    public string? Letter { get; init; }
    public FontWeight LetterWeight { get; init; } = FontWeights.Normal;
    public FontStyle LetterStyle { get; init; } = FontStyles.Normal;
    public TextDecorationCollection? LetterDecorations { get; init; }

    public double Scale { get; init; } = 0.52;

    /// <summary>Applies this icon to a visual, leaving everything else alone.</summary>
    public CellVisual ApplyTo(CellVisual visual, Color colour) => visual with
    {
        Glyph = Glyph,
        GlyphColor = colour,
        IconLetter = Letter,
        IconLetterWeight = LetterWeight,
        IconLetterStyle = LetterStyle,
        IconLetterDecorations = LetterDecorations,
        IconScale = Scale
    };
}

/// <summary>
/// The icons a key can wear, by name.
///
/// Named rather than positional so an agent proposing a page can say "bold" and mean
/// it, and so a profile stays readable. <see cref="Suggest"/> also guesses from a
/// label, which means most keys get a sensible icon without anyone choosing one.
/// </summary>
public static class IconLibrary
{
    /// <summary>
    /// Fluent first, then the hand-drawn set for anything Fluent has no name for.
    ///
    /// Microsoft's own icons are MIT licensed, which is the whole reason they can be
    /// shipped - unlike the ones in SHELL32.dll, which are read from the user's
    /// machine at runtime instead. They also happen to be drawn on a 24x24 grid, the
    /// same one this renderer uses.
    /// </summary>
    public static IReadOnlyList<DeckIcon> All { get; } = Build();

    private static IReadOnlyList<DeckIcon> Build()
    {
        var icons = new List<DeckIcon>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Letterforms win over any drawing of them: for Bold the icon is a bold B.
        foreach (DeckIcon letterform in Letterforms())
        {
            icons.Add(letterform);
            taken.Add(letterform.Name);
        }

        foreach (string name in FluentIcons.Names)
        {
            if (!taken.Add(name)) continue;
            if (FluentIcons.Get(name) is not { } geometry) continue;

            icons.Add(new DeckIcon(name, CategoryOf(name)) { Glyph = geometry, Scale = 0.56 });
        }

        foreach (DeckIcon fallback in HandDrawn())
        {
            if (taken.Add(fallback.Name)) icons.Add(fallback);
        }

        return icons;
    }

    private static string CategoryOf(string name) => name switch
    {
        "save" or "open" or "new" or "copy" or "cut" or "paste" or "undo" or "redo"
            or "find" or "replace" or "delete" or "print" or "comment" or "link"
            or "attach" or "image" or "chart" => "Edit",

        "align-left" or "align-centre" or "align-right" or "list" or "numbered-list"
            or "indent" or "table" or "sum" or "filter" or "sort" or "zoom" => "Layout",

        "play" or "pause" or "next" or "previous" or "volume-up" or "volume-down"
            or "mute" or "record" or "camera" or "microphone" or "screenshot" => "Media",

        _ => "General"
    };

    // Methods, not initialised properties. Static initialisers run in declaration
    // order, so a property declared below All would still be null when Build() ran -
    // which is exactly what happened, and it took the whole app down with it.
    private static IReadOnlyList<DeckIcon> Letterforms() =>
    [
        // Letterforms - the icon is the letter.
        new("bold", "Format") { Letter = "B", LetterWeight = FontWeights.Bold, Scale = 0.62 },
        new("italic", "Format") { Letter = "I", LetterStyle = FontStyles.Italic, Scale = 0.62 },
        new("underline", "Format")
        {
            Letter = "U", Scale = 0.62,
            LetterDecorations = Frozen(TextDecorations.Underline)
        },
        new("strikethrough", "Format")
        {
            Letter = "ab", Scale = 0.50,
            LetterDecorations = Frozen(TextDecorations.Strikethrough)
        },
        new("font", "Format") { Letter = "A", Scale = 0.62 },
        new("grow", "Format") { Letter = "A↑", Scale = 0.44 },
        new("shrink", "Format") { Letter = "A↓", Scale = 0.44 },
        new("case", "Format") { Letter = "Aa", Scale = 0.44 },
        new("superscript", "Format") { Letter = "x²", Scale = 0.46 },
        new("subscript", "Format") { Letter = "x₂", Scale = 0.46 }
    ];

    /// <summary>
    /// The set drawn before Fluent was available. Kept as a fallback for anything
    /// Fluent has no matching name for, and because a couple of them read better at
    /// 85 pixels than their Fluent equivalent.
    /// </summary>
    private static IReadOnlyList<DeckIcon> HandDrawn() =>
    [
        new("save", "Edit") { Glyph = Glyphs.Save },
        new("open", "Edit") { Glyph = Glyphs.Open },
        new("new", "Edit") { Glyph = Glyphs.NewFile },
        new("copy", "Edit") { Glyph = Glyphs.Copy },
        new("cut", "Edit") { Glyph = Glyphs.Cut },
        new("paste", "Edit") { Glyph = Glyphs.Paste },
        new("undo", "Edit") { Glyph = Glyphs.Undo },
        new("redo", "Edit") { Glyph = Glyphs.Redo },
        new("find", "Edit") { Glyph = Glyphs.Find },
        new("delete", "Edit") { Glyph = Glyphs.Delete },
        new("print", "Edit") { Glyph = Glyphs.Print },
        new("comment", "Edit") { Glyph = Glyphs.Comment },
        new("link", "Edit") { Glyph = Glyphs.Link },

        // Layout and data.
        new("align-left", "Layout") { Glyph = Glyphs.AlignLeft },
        new("align-centre", "Layout") { Glyph = Glyphs.AlignCentre },
        new("align-right", "Layout") { Glyph = Glyphs.AlignRight },
        new("list", "Layout") { Glyph = Glyphs.BulletList },
        new("table", "Layout") { Glyph = Glyphs.Table },
        new("sum", "Layout") { Glyph = Glyphs.Sum },
        new("filter", "Layout") { Glyph = Glyphs.Filter },
        new("zoom", "Layout") { Glyph = Glyphs.ZoomIn },

        // Status and system.
        new("check", "Status") { Glyph = Glyphs.Check },
        new("cross", "Status") { Glyph = Glyphs.Cross },
        new("star", "Status") { Glyph = Glyphs.Star },
        new("lock", "Status") { Glyph = Glyphs.Lock },
        new("refresh", "Status") { Glyph = Glyphs.Refresh },
        new("settings", "Status") { Glyph = Glyphs.Settings },
        new("back", "Status") { Glyph = Glyphs.Back },
        new("plus", "Status") { Glyph = Glyphs.Plus },

        // Media.
        new("play", "Media") { Glyph = Glyphs.Play },
        new("pause", "Media") { Glyph = Glyphs.Pause },
        new("next", "Media") { Glyph = Glyphs.Next },
        new("previous", "Media") { Glyph = Glyphs.Previous },
        new("volume-up", "Media") { Glyph = Glyphs.VolumeUp },
        new("volume-down", "Media") { Glyph = Glyphs.VolumeDown },
        new("mute", "Media") { Glyph = Glyphs.Mute }
    ];

    public static DeckIcon? ByName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : All.FirstOrDefault(i => i.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Guesses an icon from a key's label, so a page proposed as plain text still
    /// arrives looking like something. Exact name first, then known words.
    /// </summary>
    public static DeckIcon? Suggest(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        if (ByName(label) is { } exact) return exact;

        string text = label.ToLowerInvariant();

        foreach ((string word, string icon) in Hints)
        {
            if (text.Contains(word, StringComparison.Ordinal)) return ByName(icon);
        }

        return null;
    }

    /// <summary>
    /// Ordered: the first match wins, so more specific words come first. "paste
    /// special" must not be caught by "paste" before "special" has been considered.
    /// </summary>
    private static readonly (string Word, string Icon)[] Hints =
    [
        // Before the generic words: "conditional formatting" contains "format", which
        // would otherwise be caught by the settings gear further down.
        ("conditional", "highlight"), ("highlight", "highlight"), ("paint", "paint"),

        ("bold", "bold"), ("italic", "italic"), ("underline", "underline"),
        ("strike", "strikethrough"), ("superscript", "superscript"), ("subscript", "subscript"),
        ("grow", "grow"), ("larger", "grow"), ("shrink", "shrink"), ("smaller", "shrink"),
        ("case", "case"), ("font", "font"),
        ("save", "save"), ("open", "open"), ("new ", "new"),
        ("copy", "copy"), ("cut", "cut"), ("paste", "paste"),
        ("undo", "undo"), ("redo", "redo"),
        ("find", "find"), ("search", "find"), ("replace", "find"),
        ("delete", "delete"), ("remove", "delete"), ("clear", "delete"),
        ("print", "print"), ("comment", "comment"), ("link", "link"),
        ("left", "align-left"), ("cent", "align-centre"), ("right", "align-right"),
        ("list", "list"), ("bullet", "list"), ("table", "table"),
        ("merge", "merge-cells"), ("wrap", "text-wrap"),
        ("sum", "sum"), ("total", "sum"), ("filter", "filter"), ("sort", "filter"),
        ("zoom", "zoom"), ("fill", "list"),
        ("lock", "lock"), ("protect", "lock"), ("refresh", "refresh"), ("reload", "refresh"),
        ("setting", "settings"), ("option", "settings"), ("format", "settings"),
        ("play", "play"), ("pause", "pause"), ("next", "next"), ("prev", "previous"),
        ("volume", "volume-up"), ("mute", "mute"),
        ("back", "back"), ("star", "star"), ("favourite", "star"), ("favorite", "star")
    ];

    private static TextDecorationCollection Frozen(TextDecorationCollection decorations)
    {
        TextDecorationCollection copy = decorations.Clone();
        copy.Freeze();
        return copy;
    }
}
