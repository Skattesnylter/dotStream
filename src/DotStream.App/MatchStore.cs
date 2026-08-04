using System.IO;
using System.Text.Json;

namespace DotStream.App;

/// <summary>
/// One way of recognising an application, taught by pointing at it.
///
/// All three are recorded because none of them is reliable alone. A packaged app has an
/// identifier and a desktop program does not; a process name is stable but shared by
/// every Electron application that never renamed itself; a window title is often the
/// only thing that distinguishes two windows of the same program.
/// </summary>
public sealed record MatchRule(string? AppUserModelId, string? ProcessName, string? Title)
{
    public bool Matches(ForegroundApp foreground)
    {
        if (!string.IsNullOrEmpty(AppUserModelId))
            return string.Equals(AppUserModelId, foreground.AppUserModelId, StringComparison.OrdinalIgnoreCase);

        return !string.IsNullOrEmpty(ProcessName)
               && string.Equals(ProcessName, foreground.ProcessName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What to call this in a menu - the title if there is one, else the process.</summary>
    public string Describe() =>
        !string.IsNullOrWhiteSpace(Title) ? Title!
        : !string.IsNullOrWhiteSpace(ProcessName) ? ProcessName!
        : AppUserModelId ?? "that window";
}

/// <summary>
/// Which window belongs to which page, when working it out automatically has failed.
///
/// It will fail. Media Player runs as "Microsoft.Media.Player", is installed as
/// "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" and is displayed as
/// "Mediespiller" - three names that resemble each other not at all, and the next
/// application will be wrong in some new way. Nobody using this can patch the matching
/// code, so they need a way to say "this window, this page" by pointing at it.
///
/// Kept out of the profile deliberately. A profile is meant to be exported and shared;
/// which process a page follows on one machine is not something to carry to another.
/// </summary>
public sealed class MatchStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private Dictionary<string, MatchRule> _rules = new(StringComparer.OrdinalIgnoreCase);

    public static string FilePath => Path.Combine(AppSelectionStore.DirectoryPath, "matches.json");

    public static MatchStore Load()
    {
        var store = new MatchStore();

        try
        {
            if (!File.Exists(FilePath)) return store;

            Dictionary<string, MatchRule>? loaded =
                JsonSerializer.Deserialize<Dictionary<string, MatchRule>>(File.ReadAllText(FilePath));

            if (loaded is not null)
                store._rules = new Dictionary<string, MatchRule>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // A corrupt file costs the overrides, not the app.
        }

        return store;
    }

    public MatchRule? Get(string pageId) => _rules.GetValueOrDefault(pageId);

    /// <summary>The page taught to follow this window, if any.</summary>
    public string? PageFor(ForegroundApp foreground)
    {
        foreach ((string pageId, MatchRule rule) in _rules)
        {
            if (rule.Matches(foreground)) return pageId;
        }

        return null;
    }

    public void Set(string pageId, MatchRule rule)
    {
        _rules[pageId] = rule;
        Save();
    }

    public void Clear(string pageId)
    {
        if (_rules.Remove(pageId)) Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSelectionStore.DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_rules, Options));
        }
        catch
        {
            // Losing an override is not worth taking the app down for.
        }
    }
}
