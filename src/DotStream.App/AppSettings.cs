using System.IO;
using System.Text.Json;

namespace DotStream.App;

/// <summary>
/// General settings that are not the deck layout, the palette or label rendering -
/// those already have their own files.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Follow the focused application and open its page automatically.</summary>
    public bool FollowForegroundApp { get; set; } = true;

    /// <summary>
    /// How long manual navigation holds the deck before automatic switching may take
    /// over again. Refreshed by every key press, so an active page stays put.
    /// </summary>
    public int PinSeconds { get; set; } = 30;

    public int Brightness { get; set; } = 80;

    /// <summary>
    /// Off by default. An agent being able to put a question on a physical key is a
    /// capability the user should switch on knowingly, not inherit from an installer.
    /// </summary>
    public bool McpEnabled { get; set; }

    public int McpPort { get; set; } = 8787;

    public static string FilePath => Path.Combine(AppSelectionStore.DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // Defaults are a perfectly good outcome.
        }

        return new AppSettings();
    }

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
}
