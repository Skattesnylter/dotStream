using System.IO;
using System.Reflection;
using System.Windows;

namespace DotStream.App;

/// <summary>
/// Shows a document that ships inside the executable.
///
/// The licence and credits are embedded rather than opened from disk on purpose:
/// loose files next to the exe break the moment anyone moves it, and a
/// self-contained publish may not carry them at all. Apache-2.0 section 4(d) asks
/// for the notices to be readable wherever third-party notices normally appear -
/// in a desktop app, that is here.
/// </summary>
public partial class TextViewerWindow : Window
{
    public const string LicenceResource = "dotStream.LICENSE.txt";
    public const string CreditsResource = "dotStream.CREDITS.md";

    public TextViewerWindow(string title, string heading, string resourceName)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        Title = title;
        HeadingLabel.Text = heading;
        Body.Text = ReadResource(resourceName);
    }

    private static string ReadResource(string resourceName)
    {
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                string available = string.Join("\n  ", assembly.GetManifestResourceNames());
                return $"Resource '{resourceName}' is not embedded in this build.\n\nAvailable:\n  {available}";
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"Could not load {resourceName}.\n\n{ex.Message}";
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Body.Text);
        }
        catch
        {
            // The clipboard is occasionally locked by another process; not worth
            // interrupting the user over.
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
