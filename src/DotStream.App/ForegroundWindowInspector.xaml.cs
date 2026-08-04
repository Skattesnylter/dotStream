using System.Text;
using System.Windows;
using DotStream.Icons;

namespace DotStream.App;

/// <summary>
/// What dotStream saw when you last switched away, and what it made of it.
///
/// "It does not follow my app" was previously unanswerable - the matching happens
/// several layers down and reports nothing. Every value here is one the matcher used,
/// so a page that will not open can be diagnosed by looking rather than by guessing,
/// and the text can be pasted into a bug report by someone who cannot read the code.
/// </summary>
public partial class ForegroundWindowInspector : Window
{
    private readonly Func<ForegroundApp?> _last;
    private readonly Func<ForegroundApp, InstalledApp?> _match;
    private readonly Func<ForegroundApp, string?> _taught;

    public ForegroundWindowInspector(
        Func<ForegroundApp?> last,
        Func<ForegroundApp, InstalledApp?> match,
        Func<ForegroundApp, string?> taught)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _last = last;
        _match = match;
        _taught = taught;

        Refresh();
    }

    /// <summary>Called by the host whenever the foreground changes, so this stays live.</summary>
    public void Refresh()
    {
        if (_last() is not { } app)
        {
            Details.Text = "Nothing yet.\n\nSwitch to another application, then come back.";
            Hint.Text = "";
            return;
        }

        var text = new StringBuilder();

        text.AppendLine($"window title  {Or(app.Title)}");
        text.AppendLine($"process       {Or(app.ProcessName)}   (pid {app.ProcessId})");
        text.AppendLine($"identifier    {Or(app.AppUserModelId, "none - this is a desktop program")}");
        text.AppendLine();

        if (_taught(app) is { } taughtPage)
        {
            text.AppendLine($"taught rule   opens \"{taughtPage}\"");
            Hint.Text = "A rule you taught is being used.";
        }
        else if (_match(app) is { } matched)
        {
            text.AppendLine($"recognised    {matched.Name}");
            text.AppendLine($"              {matched.AppUserModelId}");
            Hint.Text = "Recognised automatically - its page opens if one exists.";
        }
        else
        {
            text.AppendLine("recognised    nothing");
            text.AppendLine();
            text.AppendLine("No installed application matched this window, so no page");
            text.AppendLine("will open for it. Open the page you want, right-click a");
            text.AppendLine("key, and choose to open it when this window comes to the");
            text.AppendLine("front.");
            Hint.Text = "Not recognised - teach it from the page's right-click menu.";
        }

        Details.Text = text.ToString().TrimEnd();
    }

    private static string Or(string? value, string fallback = "(none)") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Details.Text);
            Hint.Text = "Copied.";
        }
        catch
        {
            // Another process can hold the clipboard open; not worth a dialog.
            Hint.Text = "The clipboard was busy - try again.";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
