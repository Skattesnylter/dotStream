using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace DotStream.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        Assembly assembly = Assembly.GetExecutingAssembly();

        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // The SDK appends the source revision id (+<sha>) to the informational version.
        int plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];

        VersionLabel.Text = "Version " + version;
        RuntimeLabel.Text = ".NET " + Environment.Version;
    }

    private void OnShowLicence(object sender, RoutedEventArgs e) =>
        new TextViewerWindow("Apache License 2.0", "dotStream is licensed under the Apache License, Version 2.0",
            TextViewerWindow.LicenceResource) { Owner = this }.ShowDialog();

    private void OnShowCredits(object sender, RoutedEventArgs e) =>
        new TextViewerWindow("Credits", "Credits and third-party notices",
            TextViewerWindow.CreditsResource) { Owner = this }.ShowDialog();

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSelectionStore.DirectoryPath);
            Process.Start(new ProcessStartInfo(AppSelectionStore.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open the settings folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
