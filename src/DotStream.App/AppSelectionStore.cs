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
