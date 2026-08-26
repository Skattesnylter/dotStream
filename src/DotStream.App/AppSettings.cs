using System.IO;
using System.Text.Json;
using DotStream.Core;

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

    /// <summary>
    /// The size of one cell in device pixels.
    ///
    /// 100 is measured on the 0300:3010 AKP153E, and is a default rather than a fact:
    /// several VID/PID pairs ship under this name and nobody has measured them all. A
    /// cell is a persistent framebuffer, so a wrong value here does not fail cleanly -
    /// too small leaves a ring of whatever was there before, too large crops. That is
    /// what the calibration window is for.
    /// </summary>
    public int CellPixels { get; set; } = DeckLayout.CellPixels;

    /// <summary>
    /// Degrees the image is turned before upload. 270 on the measured device, with no
    /// mirroring. A variant with its panel mounted the other way needs a different
    /// quarter turn, which is why this is settable rather than a constant.
    /// </summary>
    public int CellRotation { get; set; } = 270;

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
