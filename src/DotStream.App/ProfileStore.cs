using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotStream.App.Mcp;
using DotStream.Core;
using DotStream.Icons;

namespace DotStream.App;

/// <summary>Widget colours, as "#RRGGBB" so a profile stays hand-editable.</summary>
public sealed record ThemeRecord
{
    public required string Background { get; init; }
    public required string Accent { get; init; }
    public required string Value { get; init; }
    public required string Label { get; init; }

    public static ThemeRecord From(WidgetTheme theme) => new()
    {
        Background = ColorCodec.ToHex(theme.Background),
        Accent = ColorCodec.ToHex(theme.Accent),
        Value = ColorCodec.ToHex(theme.Value),
        Label = ColorCodec.ToHex(theme.Label)
    };

    /// <summary>Falls back to the widget's own defaults for anything unreadable.</summary>
    public WidgetTheme ToTheme(WidgetTheme fallback) => new()
    {
        Background = ColorCodec.Parse(Background) ?? fallback.Background,
        Accent = ColorCodec.Parse(Accent) ?? fallback.Accent,
        Value = ColorCodec.Parse(Value) ?? fallback.Value,
        Label = ColorCodec.Parse(Label) ?? fallback.Label
    };
}

/// <summary>What a cell holds, in a form that survives a restart.</summary>
public sealed record CellRecord
{
    /// <summary>"app", "action" or "widget".</summary>
    public required string Kind { get; init; }

    /// <summary>AppUserModelId for an app, catalogue id for an action or widget.</summary>
    public required string Value { get; init; }

    /// <summary>Widgets only. Null means the widget's own default colours.</summary>
    public ThemeRecord? Theme { get; init; }
}

public sealed record PageRecord
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public bool Dynamic { get; init; }
    public Dictionary<string, CellRecord> Cells { get; init; } = [];

    /// <summary>
    /// Hotkeys built for this page but not necessarily placed on a key.
    ///
    /// A page is a set of things an application can do plus an arrangement of them.
    /// Keeping the library separate means building the set and laying it out are two
    /// steps, and removing a key does not throw away the work of defining it.
    /// </summary>
    public List<HotkeyBinding> Hotkeys { get; init; } = [];
}

public sealed record ProfileRecord
{
    public int Version { get; init; } = 1;
    public List<PageRecord> Pages { get; init; } = [];
}

/// <summary>
/// Persists the deck layout.
///
/// A <see cref="DeckButton"/> holds closures, which cannot be serialised - so what
/// goes to disk is the declarative form each button already carries in its Tag: an
/// AppUserModelId, or an action id from the catalogue. On load the closures are
/// rebuilt from those two lookups, which also means a profile stays valid when the
/// implementation of an action changes.
/// </summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string FilePath => Path.Combine(AppSelectionStore.DirectoryPath, "profile.json");

    public static bool Exists => File.Exists(FilePath);

    public static ProfileRecord? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<ProfileRecord>(File.ReadAllText(FilePath), Options)
                : null;
        }
        catch
        {
            // A corrupt profile must not stop the app starting; a fresh default deck
            // is a better outcome than a crash loop.
            return null;
        }
    }

    public static void Save(IEnumerable<DeckPage> pages,
        IReadOnlyDictionary<string, List<HotkeyBinding>>? libraries = null)
    {
        var record = new ProfileRecord();

        foreach (DeckPage page in pages)
        {
            var pageRecord = new PageRecord
            {
                Id = page.Id,
                Title = page.Title,
                Dynamic = page.RefreshInterval is not null
            };

            if (libraries?.TryGetValue(page.Id, out List<HotkeyBinding>? library) == true)
                pageRecord.Hotkeys.AddRange(library);

            foreach ((int index, DeckButton button) in page.Cells)
            {
                CellRecord? cell = button.Tag switch
                {
                    // Agent pages are transient by design and never persisted.
                    "agent.option" or "agent.key" => null,
                    InstalledApp app => new CellRecord { Kind = "app", Value = app.AppUserModelId },
                    McpBinding binding => new CellRecord
                    {
                        Kind = "mcp",
                        Value = JsonSerializer.Serialize(binding, Options)
                    },
                    HotkeyBinding hotkey => new CellRecord
                    {
                        Kind = "hotkey",
                        Value = JsonSerializer.Serialize(hotkey, Options)
                    },
                    TextMacroBinding macro => new CellRecord
                    {
                        Kind = "text",
                        Value = JsonSerializer.Serialize(macro, Options)
                    },
                    WidgetPlacement widget => new CellRecord
                    {
                        Kind = "widget",
                        Value = widget.Widget.Id,
                        Theme = ThemeRecord.From(widget.Theme)
                    },
                    string actionId => new CellRecord { Kind = "action", Value = actionId },
                    _ => null
                };

                if (cell is not null)
                    pageRecord.Cells[index.ToString()] = cell;
            }

            record.Pages.Add(pageRecord);
        }

        try
        {
            Directory.CreateDirectory(AppSelectionStore.DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(record, Options));
        }
        catch
        {
            // Non-fatal: the layout simply will not survive a restart.
        }
    }

    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch
        {
            // Nothing useful to do about it.
        }
    }
}
