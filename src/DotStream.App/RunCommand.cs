using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using DotStream.Rendering;

namespace DotStream.App;

/// <summary>
/// A program, script or document a key starts.
///
/// The third of the everyday actions, after the hotkey and the text macro. A hotkey
/// needs the target application to be in front; this does not care what has focus,
/// which is what makes it the right tool for "build", "deploy", "open the log".
///
/// Deliberately not reachable over MCP. An agent can propose hotkeys and nothing
/// else: a key that types Ctrl+S is a different kind of thing from a key that runs an
/// executable, and only one of them should be suggestible by software.
/// </summary>
public sealed record RunBinding(string Path, string Label, string Icon = "")
{
    public string Arguments { get; init; } = "";

    /// <summary>Empty means the directory the target itself lives in.</summary>
    public string WorkingDirectory { get; init; } = "";

    /// <summary>
    /// Off means the process is started directly. On uses the shell, which is what
    /// makes URLs, documents and anything with a file association work - but it also
    /// means the argument string is handled by the shell rather than passed through.
    /// </summary>
    public bool UseShell { get; init; } = true;

    /// <summary>Hides the console window a script would otherwise flash up.</summary>
    public bool Hidden { get; init; }

    /// <summary>See <see cref="HotkeyBinding.IconFile"/>.</summary>
    public string IconFile { get; init; } = "";

    public int IconIndex { get; init; }

    [JsonIgnore]
    public DeckIcon? ResolvedIcon => IconLibrary.ByName(Icon) ?? IconLibrary.Suggest(Label);

    [JsonIgnore]
    public BitmapSource? FileImage => IconCache.Get(IconFile, IconIndex);

    [JsonIgnore]
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Label) ? System.IO.Path.GetFileNameWithoutExtension(Path) : Label;

    /// <summary>
    /// Starts it. Returns what to put on the status line, so the caller does not have
    /// to know how the process was launched.
    /// </summary>
    public string Start()
    {
        if (string.IsNullOrWhiteSpace(Path)) return "Nothing to run - no path set.";

        var info = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(Path))
        {
            UseShellExecute = UseShell,
            CreateNoWindow = Hidden
        };

        if (Arguments.Length > 0) info.Arguments = Environment.ExpandEnvironmentVariables(Arguments);

        // A script that reads a relative path finds nothing if the working directory is
        // wherever dotStream happens to have been started from.
        string working = WorkingDirectory.Length > 0
            ? Environment.ExpandEnvironmentVariables(WorkingDirectory)
            : System.IO.Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(Path)) ?? "";

        if (working.Length > 0 && Directory.Exists(working)) info.WorkingDirectory = working;

        if (Hidden && !UseShell) info.WindowStyle = ProcessWindowStyle.Hidden;

        try
        {
            Process.Start(info);
            return $"Started {DisplayLabel}.";
        }
        catch (Exception ex)
        {
            return $"Could not start {DisplayLabel}: {ex.Message}";
        }
    }
}
