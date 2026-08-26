using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>
/// A key that opens a web page, a file or a folder.
///
/// Technically a narrow case of <see cref="RunBinding"/> with the shell doing the
/// work, and kept separate anyway. Telling somebody to configure "run a program" in
/// order to open a bookmark is the kind of thing that makes software feel like it was
/// written for the person who wrote it.
/// </summary>
public sealed record LinkBinding(string Target, string Label, string Icon = "")
{
    /// <summary>See <see cref="HotkeyBinding.IconFile"/>.</summary>
    public string IconFile { get; init; } = "";

    public int IconIndex { get; init; }

    [JsonIgnore]
    public DeckIcon? ResolvedIcon => IconLibrary.ByName(Icon) ?? IconLibrary.Suggest(Label) ?? IconLibrary.ByName("link");

    [JsonIgnore]
    public BitmapSource? FileImage => IconCache.Get(IconFile, IconIndex);

    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Pretty : Label;

    /// <summary>The target with the noise stripped, for when there is no label.</summary>
    [JsonIgnore]
    public string Pretty
    {
        get
        {
            string text = Target.Trim();

            foreach (string prefix in (string[])["https://", "http://", "www."])
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    text = text[prefix.Length..];

            text = text.TrimEnd('/');
            int slash = text.IndexOf('/');
            if (slash > 0) text = text[..slash];

            return text.Length <= 18 ? text : text[..15] + "...";
        }
    }

    public string Open()
    {
        string target = Environment.ExpandEnvironmentVariables(Target.Trim());
        if (target.Length == 0) return "Nothing to open - no address set.";

        // A bare "example.com" is what people type. Without a scheme the shell treats
        // it as a file path and reports that it cannot find it.
        if (!target.Contains("://") && !target.StartsWith('\\') && target.IndexOf(':') != 1)
            target = "https://" + target;

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return $"Opened {DisplayLabel}.";
        }
        catch (Exception ex)
        {
            return $"Could not open {DisplayLabel}: {ex.Message}";
        }
    }
}
