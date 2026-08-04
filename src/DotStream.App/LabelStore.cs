using System.IO;
using System.Text.Json;

namespace DotStream.App;

/// <summary>
/// Custom key labels, stored against the thing on the key rather than against the
/// key itself.
///
/// The identity is the AppUserModelId for an application, or the catalogue id for an
/// action. That is deliberate: rename Spotify to "Musikk", move it to another key,
/// remove it entirely and add it back six months later - the name you chose is still
/// there. Storing the override inside the deck layout would lose it the moment the
/// key was cleared.
/// </summary>
public sealed class LabelStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private Dictionary<string, string> _labels = new(StringComparer.OrdinalIgnoreCase);

    public static string FilePath => Path.Combine(AppSelectionStore.DirectoryPath, "labels.json");

    public static LabelStore Load()
    {
        var store = new LabelStore();

        try
        {
            if (!File.Exists(FilePath)) return store;

            Dictionary<string, string>? loaded =
                JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));

            if (loaded is not null)
                store._labels = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // A corrupt file costs the custom names, not the app.
        }

        return store;
    }

    /// <summary>The user's label for this identity, or null to use the default.</summary>
    public string? Get(string? identity) =>
        identity is not null && _labels.TryGetValue(identity, out string? label) ? label : null;

    public void Set(string identity, string? label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        if (string.IsNullOrWhiteSpace(label))
            _labels.Remove(identity);
        else
            _labels[identity] = label.Trim();

        Save();
    }

    public bool Has(string? identity) => identity is not null && _labels.ContainsKey(identity);

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSelectionStore.DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_labels, Options));
        }
        catch
        {
            // Non-fatal: the names simply will not survive a restart.
        }
    }
}
