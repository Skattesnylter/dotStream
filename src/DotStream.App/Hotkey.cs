using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace DotStream.App;

/// <summary>
/// A key combination sent to whatever window has focus.
///
/// The most useful action there is, because it needs no cooperation from the target:
/// every program already has shortcuts, and this hands them to a physical key. It is
/// also what makes a page for Word or Excel worth having - there is no media session
/// to talk to, but there is always Ctrl+S.
///
/// Stored and parsed as text like "Ctrl+Shift+S" so a profile stays readable, and so
/// an agent proposing a page can write what it means.
/// </summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        var text = new StringBuilder();

        if (Modifiers.HasFlag(ModifierKeys.Control)) text.Append("Ctrl+");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) text.Append("Alt+");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) text.Append("Shift+");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) text.Append("Win+");

        // A modifier-only step prints as "Alt", not "Alt+".
        if (Key == Key.None) return text.ToString().TrimEnd('+');

        return text.Append(Friendly(Key)).ToString();
    }

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        ModifierKeys modifiers = ModifierKeys.None;
        Key key = Key.None;

        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; continue;
                case "alt": modifiers |= ModifierKeys.Alt; continue;
                case "shift": modifiers |= ModifierKeys.Shift; continue;
                case "win" or "windows" or "meta" or "cmd": modifiers |= ModifierKeys.Windows; continue;
            }

            if (TryParseKey(raw, out Key parsed)) key = parsed;
        }

        // A modifier on its own is a real step in a ribbon sequence: "Alt, H, M, C"
        // starts by tapping and releasing Alt to raise the KeyTips. It is only
        // meaningless as a hotkey in its own right.
        if (key == Key.None) return modifiers == ModifierKeys.None ? null : new Hotkey(modifiers, Key.None);

        return new Hotkey(modifiers, key);
    }

    private static bool TryParseKey(string text, out Key key)
    {
        // Names people actually write, before falling back to the enum's own spelling.
        switch (text.ToLowerInvariant())
        {
            case "=" or "equals" or "plus": key = Key.OemPlus; return true;
            case "-" or "minus": key = Key.OemMinus; return true;
            case "," or "comma": key = Key.OemComma; return true;
            case "." or "period" or "dot": key = Key.OemPeriod; return true;
            case "/" or "slash": key = Key.OemQuestion; return true;
            case ";" or "semicolon": key = Key.OemSemicolon; return true;
            case "'" or "quote": key = Key.OemQuotes; return true;
            case "[": key = Key.OemOpenBrackets; return true;
            case "]": key = Key.OemCloseBrackets; return true;
            case "\\" or "backslash": key = Key.OemBackslash; return true;
            case "`" or "backtick" or "tilde": key = Key.OemTilde; return true;
            case "esc": key = Key.Escape; return true;
            case "del": key = Key.Delete; return true;
            case "ins": key = Key.Insert; return true;
            case "pgup": key = Key.PageUp; return true;
            case "pgdn" or "pgdown": key = Key.PageDown; return true;
            case "enter" or "return": key = Key.Return; return true;
            case "space" or "spacebar": key = Key.Space; return true;
        }

        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = Key.D0 + (text[0] - '0');
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && key != Key.None;
    }

    private static string Friendly(Key key) => key switch
    {
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemBackslash => "\\",
        Key.OemTilde => "`",
        Key.Return => "Enter",
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        _ => key.ToString()
    };

    /// <summary>
    /// Presses and releases the combination. Modifiers go down in order and come back
    /// up in reverse, which is what applications expect - releasing Ctrl before the
    /// letter it modifies produces a plain keystroke instead of a shortcut.
    /// </summary>
    public void Send()
    {
        var sequence = new List<ushort>();

        if (Modifiers.HasFlag(ModifierKeys.Control)) sequence.Add(VkControl);
        if (Modifiers.HasFlag(ModifierKeys.Alt)) sequence.Add(VkMenu);
        if (Modifiers.HasFlag(ModifierKeys.Shift)) sequence.Add(VkShift);
        if (Modifiers.HasFlag(ModifierKeys.Windows)) sequence.Add(VkWin);

        var target = (ushort)KeyInterop.VirtualKeyFromKey(Key);
        if (target == 0 && sequence.Count == 0) return;

        var inputs = new List<Win32Input.Input>();

        foreach (ushort modifier in sequence) inputs.Add(Win32Input.Key(modifier, down: true));

        // A modifier-only step is a tap: down and straight back up, with nothing in
        // between. That is what raises the ribbon's KeyTips.
        if (target != 0)
        {
            inputs.Add(Win32Input.Key(target, down: true));
            inputs.Add(Win32Input.Key(target, down: false));
        }

        for (int i = sequence.Count - 1; i >= 0; i--)
            inputs.Add(Win32Input.Key(sequence[i], down: false));

        Win32Input.Send(inputs);
    }

    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkWin = 0x5B;
}

/// <summary>
/// A macro: several combinations pressed one after another, written with commas.
///
/// Ribbon commands are reached this way and cannot be reached any other way - Merge
/// and Centre in Excel is "Alt, H, M, C", four separate taps that walk a menu, not one
/// chord. A single <see cref="Hotkey"/> cannot express it, and there is no Ctrl+
/// shortcut to fall back on.
///
/// Stored in the same field as an ordinary hotkey, so a profile stays readable and an
/// agent proposing a page can write "Alt, H, M, C" and mean exactly that.
/// </summary>
/// <summary>
/// One step: either a combination to press or a string to type. Exactly one is set.
/// </summary>
public sealed record SequenceStep(Hotkey? Key, string? Text)
{
    // Round-trips: what this prints is what Parse reads back.
    public override string ToString() => Text is null ? Key?.ToString() ?? "" : "\"" + Text + "\"";
}

public static class KeySequence
{
    /// <summary>
    /// Long enough for a ribbon to open and paint its KeyTips, short enough that a
    /// four-step macro still feels instant. Sending the whole sequence at once does
    /// not work: the menu the second step is aiming at does not exist yet.
    /// </summary>
    public const int StepDelayMs = 90;

    public static IReadOnlyList<SequenceStep> Parse(string? text)
    {
        var steps = new List<SequenceStep>();
        if (string.IsNullOrWhiteSpace(text)) return steps;

        foreach (string raw in Split(text))
        {
            string step = raw.Trim();

            // A quoted step is typed rather than pressed. Command palettes are reached
            // no other way: "Developer: Reload Window" has no shortcut of its own, and
            // the only route to it is to open the palette, type the name and confirm.
            if (step.Length >= 2 && step[0] == '"' && step[^1] == '"')
            {
                steps.Add(new SequenceStep(null, step[1..^1]));
                continue;
            }

            if (Hotkey.Parse(step) is { } parsed) steps.Add(new SequenceStep(parsed, null));
        }

        return steps;
    }

    /// <summary>
    /// Splits on commas, except where the comma is the key being pressed or sits inside
    /// quoted text. "Ctrl+," is one step, "Alt, H" is two, and a comma inside "Hello,
    /// world" belongs to the text.
    /// </summary>
    private static IEnumerable<string> Split(string text)
    {
        var current = new StringBuilder();
        bool quoted = false;

        foreach (char c in text)
        {
            if (c == '"') quoted = !quoted;

            if (c == ',' && !quoted && current.Length > 0 && current[^1] != '+')
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) yield return current.ToString();
    }

    public static async Task SendAsync(IReadOnlyList<SequenceStep> steps)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            if (i > 0) await Task.Delay(StepDelayMs);

            if (steps[i].Text is { } typed) await TextMacro.SendAsync(typed);
            else steps[i].Key?.Send();
        }
    }

    public static string Describe(IEnumerable<SequenceStep> steps) => string.Join(", ", steps);
}
