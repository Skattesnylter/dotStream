using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace DotStream.App;

public sealed record FontOption(string Name, FontFamily Family, bool IsUserFont);

/// <summary>
/// The label rendering choices, as a set of options that can be cycled at runtime.
///
/// These are exposed as controls rather than settled in code because none of them has
/// a right answer that can be found on a monitor. The judgement has to be made on the
/// actual 85px LCD, under its own plastic cap, at the angle it sits on a desk. A
/// switch costs almost nothing and will be needed again when the hardware lands - and
/// again by anyone whose eyes differ from mine.
///
/// Choices are stored by name, not by index. The font list grows when the user drops
/// files into their fonts folder, and an index would then quietly point at something
/// else.
/// </summary>
public sealed class TextLab
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static readonly (string Name, FontWeight Weight)[] Weights =
    [
        ("Regular", FontWeights.Normal),
        ("Medium", FontWeights.Medium),
        ("SemiBold", FontWeights.SemiBold),
        ("Bold", FontWeights.Bold)
    ];

    public static readonly (string Name, TextFormattingMode Mode)[] Formatting =
    [
        ("Ideal", TextFormattingMode.Ideal),
        ("Display", TextFormattingMode.Display)
    ];

    public static readonly (string Name, TextRenderingMode Mode)[] Rendering =
    [
        ("Grayscale", TextRenderingMode.Grayscale),
        ("ClearType", TextRenderingMode.ClearType),
        ("Aliased", TextRenderingMode.Aliased)
    ];

    private IReadOnlyList<FontOption> _fonts = [];

    public string FontName { get; set; } = "Atkinson";
    public string WeightName { get; set; } = "SemiBold";
    public string FormattingName { get; set; } = "Ideal";
    public string RenderingName { get; set; } = "Grayscale";
    public double LabelSize { get; set; } = 10;

    public static string FilePath => Path.Combine(AppSelectionStore.DirectoryPath, "text.json");

    /// <summary>Drop .ttf or .otf files here and they appear after the next start.</summary>
    public static string UserFontsPath => Path.Combine(AppSelectionStore.DirectoryPath, "fonts");

    [JsonIgnore] public IReadOnlyList<FontOption> Fonts => _fonts;

    [JsonIgnore]
    public FontOption Font =>
        _fonts.FirstOrDefault(f => f.Name.Equals(FontName, StringComparison.OrdinalIgnoreCase))
        ?? _fonts[0];

    [JsonIgnore]
    public (string Name, FontWeight Weight) Weight =>
        Weights.FirstOrDefault(w => w.Name == WeightName, Weights[2]);

    [JsonIgnore]
    public (string Name, TextFormattingMode Mode) Format =>
        Formatting.FirstOrDefault(f => f.Name == FormattingName, Formatting[0]);

    [JsonIgnore]
    public (string Name, TextRenderingMode Mode) Render =>
        Rendering.FirstOrDefault(r => r.Name == RenderingName, Rendering[0]);

    public static TextLab Load()
    {
        TextLab lab;

        try
        {
            lab = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<TextLab>(File.ReadAllText(FilePath)) ?? new TextLab()
                : new TextLab();
        }
        catch
        {
            lab = new TextLab();
        }

        lab._fonts = DiscoverFonts();
        return lab;
    }

    public void CycleFont() => FontName = Next(_fonts.Select(f => f.Name).ToList(), FontName);

    public void CycleWeight() => WeightName = Next(Weights.Select(w => w.Name).ToList(), WeightName);

    public void CycleFormatting() => FormattingName = Next(Formatting.Select(f => f.Name).ToList(), FormattingName);

    public void CycleRendering() => RenderingName = Next(Rendering.Select(r => r.Name).ToList(), RenderingName);

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSelectionStore.DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Non-fatal.
        }
    }

    /// <summary>
    /// Built-in families first, then whatever the user has put in their fonts folder.
    ///
    /// Scanned at startup rather than watched: a font file being copied in is not
    /// atomic, and re-resolving typefaces while the deck is rendering buys nothing.
    /// </summary>
    private static IReadOnlyList<FontOption> DiscoverFonts()
    {
        var options = new List<FontOption>
        {
            new("Verdana", new FontFamily("Verdana"), false),
            new("Atkinson", new FontFamily(
                new Uri("pack://application:,,,/"), "./Resources/fonts/#Atkinson Hyperlegible"), false),
            new("Segoe Small", new FontFamily("Segoe UI Variable Small, Segoe UI"), false),
            new("Tahoma", new FontFamily("Tahoma"), false)
        };

        try
        {
            if (Directory.Exists(UserFontsPath))
            {
                var seen = options.Select(o => o.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (FontFamily family in
                         System.Windows.Media.Fonts.GetFontFamilies(new Uri(UserFontsPath + Path.DirectorySeparatorChar)))
                {
                    string name = FamilyName(family);
                    if (name.Length == 0 || !seen.Add(name)) continue;

                    options.Add(new FontOption(name, family, true));
                }
            }
        }
        catch
        {
            // A malformed font file must not stop the app from starting.
        }

        return options;
    }

    /// <summary>Font family sources look like "file:///C:/.../#Family Name".</summary>
    private static string FamilyName(FontFamily family)
    {
        string source = family.Source ?? "";
        int hash = source.LastIndexOf('#');
        return hash >= 0 ? source[(hash + 1)..].Trim() : source.Trim();
    }

    private static string Next(IReadOnlyList<string> names, string current)
    {
        if (names.Count == 0) return current;

        int index = -1;
        for (int i = 0; i < names.Count; i++)
        {
            if (!names[i].Equals(current, StringComparison.OrdinalIgnoreCase)) continue;
            index = i;
            break;
        }

        return names[(index + 1) % names.Count];
    }
}
