using System.Windows;
using System.Windows.Threading;

namespace DotStream.App;

public partial class App : Application
{
    /// <summary>
    /// Started with --tray, so it should go straight to the tray without showing the
    /// window. Set by the Run entry, so signing in does not throw a window at you.
    /// </summary>
    public static bool StartHidden { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StartHidden = e.Args.Any(arg =>
            arg.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("/tray", StringComparison.OrdinalIgnoreCase));

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
