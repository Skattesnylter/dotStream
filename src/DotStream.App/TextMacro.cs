using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>
/// A block of text a key types out.
///
/// The everyday half of what a deck is for: an email address, a signature, a support
/// reply, a licence header, the same three-line SQL query you write forty times a week.
/// It needs no cooperation from the target application, exactly like a hotkey.
/// </summary>
public sealed record TextMacroBinding(string Text, string Label, string Icon = "")
{
    /// <summary>See <see cref="HotkeyBinding.IconFile"/> - the artwork stays where it was installed.</summary>
    public string IconFile { get; init; } = "";

    public int IconIndex { get; init; }

    /// <summary>
    /// Whether to press Enter at the end. Off by default: a macro that submits the form
    /// it just filled in is a different and more dangerous thing than one that types.
    /// </summary>
    public bool PressEnter { get; init; }

    [JsonIgnore]
    public DeckIcon? ResolvedIcon => IconLibrary.ByName(Icon) ?? IconLibrary.Suggest(Label);

    [JsonIgnore]
    public BitmapSource? FileImage => IconCache.Get(IconFile, IconIndex);

    [JsonIgnore]
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Preview : Label;

    /// <summary>The first line, shortened - what to show when there is no label.</summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            string first = Text.Split('\n')[0].Trim();
            return first.Length <= 18 ? first : first[..15] + "...";
        }
    }
}

/// <summary>
/// Types text by synthesising characters rather than key presses.
///
/// Sending the key that bears "@" would produce whatever that key means on the layout
/// in use, which on a Norwegian keyboard is not "@" at all. A Unicode input carries the
/// character itself, so the macro types what it says regardless of layout - and works
/// for accented letters and emoji, which have no key to press.
/// </summary>
public static class TextMacro
{
    /// <summary>
    /// Characters between pauses. Sending thousands of inputs in one call is accepted
    /// by Windows and then dropped by applications that cannot keep up; a small batch
    /// with a breath between is slower to describe than to watch.
    /// </summary>
    private const int BatchSize = 40;

    private const int BatchPauseMs = 8;

    private const ushort VkReturn = 0x0D;
    private const ushort VkTab = 0x09;

    public static async Task SendAsync(string? text, bool pressEnter = false)
    {
        if (string.IsNullOrEmpty(text)) return;

        var batch = new List<Win32Input.Input>(BatchSize * 2);

        foreach (char character in text)
        {
            switch (character)
            {
                // A newline in the box means Enter, not a character. Carriage returns
                // arrive alongside it from a multi-line text box and would otherwise be
                // typed twice.
                case '\r':
                    continue;

                case '\n':
                    batch.Add(Win32Input.Key(VkReturn, down: true));
                    batch.Add(Win32Input.Key(VkReturn, down: false));
                    break;

                case '\t':
                    batch.Add(Win32Input.Key(VkTab, down: true));
                    batch.Add(Win32Input.Key(VkTab, down: false));
                    break;

                default:
                    batch.Add(Win32Input.Character(character, down: true));
                    batch.Add(Win32Input.Character(character, down: false));
                    break;
            }

            if (batch.Count < BatchSize * 2) continue;

            Win32Input.Send(batch);
            batch.Clear();
            await Task.Delay(BatchPauseMs);
        }

        if (batch.Count > 0) Win32Input.Send(batch);

        if (!pressEnter) return;

        await Task.Delay(BatchPauseMs);
        Win32Input.Send([Win32Input.Key(VkReturn, true), Win32Input.Key(VkReturn, false)]);
    }
}
