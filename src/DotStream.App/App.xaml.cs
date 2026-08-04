using System.Windows;
using System.Windows.Threading;

namespace DotStream.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
    }

    /// <summary>
    /// Keeps one broken window from taking the process with it.
    ///
    /// A null reference while building a dialog killed the whole app - and with it a
    /// deck that was otherwise working fine. Anything reached from a click should be
    /// allowed to fail on its own; the failure is logged to the console where it can
    /// be read, rather than only surviving in the Windows event log.
    /// </summary>
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DeckLog.Note("error", e.Exception.GetType().Name + ": " + e.Exception.Message);

        MessageBox.Show(
            e.Exception.Message + "\n\nThe rest of dotStream is still running. "
            + "View > Console has the details.",
            "Something went wrong",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }
}
