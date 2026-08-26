using System.IO;
using System.Text.Json;
using DotStream.Icons;

namespace DotStream.App;

/// <summary>
/// Which applications the user wants to see in the palette, persisted between runs.
///
/// A null <see cref="Selected"/> means "never customised" - in that state the
/// heuristic in <see cref="AppFilter"/> decides, so a fresh install shows something
/// sensible without anyone having to tick 180 boxes first.
/// </summary>
public sealed class AppSelectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dotStream");

    public static string FilePath => Path.Combine(DirectoryPath, "selection.json");

    /// <summary>AppUserModelIds the user chose. Null until they customise it.</summary>
    public HashSet<string>? Selected { get; private set; }

    public bool IsCustomised => Selected is not null;

    public static AppSelectionStore Load()
    {
        var store = new AppSelectionStore();

        try
        {
            if (!File.Exists(FilePath)) return store;

            string json = File.ReadAllText(FilePath);
            string[]? ids = JsonSerializer.Deserialize<string[]>(json);

            if (ids is not null)
                store.Selected = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // A corrupt selection file should not stop the app from starting; falling
            // back to the heuristic is a perfectly good outcome.
        }

        return store;
    }

    public void Set(IEnumerable<string> appUserModelIds)
    {
        Selected = new HashSet<string>(appUserModelIds, StringComparer.OrdinalIgnoreCase);
        Save();
    }

    /// <summary>
    /// Takes in a source of apps that did not exist when the user last chose, so the
    /// new entries are visible instead of silently absent.
    ///
    /// The selection is a list of what to show, which means anything appearing after it
    /// was saved is hidden by default - correct for an app the user already declined,
    /// wrong for a whole category they were never asked about. Steam games are that
    /// second case. The check is whether *any* of them are named in the file: if some
    /// are, the user has since made a real choice about this source and it is theirs to
    /// keep, so nothing happens. That makes this a one-time adoption rather than a rule
    /// that keeps overriding them.
    /// </summary>
    public void AdoptNewSource(IEnumerable<InstalledApp> apps)
    {
        if (Selected is null) return; // never customised; the heuristic covers it

        var newcomers = apps.Select(a => a.AppUserModelId).ToList();
        if (newcomers.Count == 0) return;
        if (newcomers.Any(Selected.Contains)) return;

        foreach (string id in newcomers) Selected.Add(id);

        Save();
    }

    /// <summary>Applies the selection, or the heuristic when nothing was ever chosen.</summary>
    public IEnumerable<InstalledApp> Apply(IEnumerable<InstalledApp> apps) =>
        Selected is null
            ? AppFilter.UserApps(apps)
            : apps.Where(a => Selected.Contains(a.AppUserModelId));

    private void Save()
    {
        if (Selected is null) return;

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Selected.ToArray(), SerializerOptions));
        }
        catch
        {
            // Non-fatal: the selection simply will not survive a restart.
        }
    }
}
