using System.Windows;
using System.Windows.Threading;

namespace DotStream.App;

/// <summary>
/// Dials in the true cell geometry by eye, against the deck on the desk.
///
/// This exists because the size cannot be looked up. The protocol notes said 85x85,
/// AJAZZ's own manual says 126x126, and the hardware measured 100x100 - and several
/// VID/PID pairs ship under the same product name, so the next variant may well differ
/// again. A cell is a persistent framebuffer, which is what makes a wrong value so
/// confusing: too small leaves a ring of whatever was there before, too large crops,
/// and neither reports an error. Sliders settle it in about ten seconds.
///
/// The window owns no hardware. It hands values to the caller, which already has the
/// transport open - which is the whole reason this beats the standalone tool it
/// replaces, where dotStream had to be closed first to free the device.
/// </summary>
public partial class CalibrationWindow : Window
{
    private readonly Func<int, int, bool, Task> _preview;
    private readonly int _originalSize;
    private readonly int _originalRotation;

    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(120) };

    /// <param name="preview">
    /// Draws the deck at a given size, rotation and pattern setting. Called while
    /// dragging, so it has to be cheap enough to run repeatedly.
    /// </param>
    public CalibrationWindow(string deviceName, int size, int rotation, Func<int, int, bool, Task> preview)
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);

        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _originalSize = size;
        _originalRotation = rotation;

        DeviceLabel.Text = deviceName;

        SizeSlider.Value = size;
        RotationSlider.Value = rotation;

        // Sliders fire on every pixel of travel, and each one is eighteen uploads.
        // Waiting for the hand to stop keeps the deck responsive instead of queueing
        // a repaint per pixel.
        _settle.Tick += (_, _) => { _settle.Stop(); _ = PushAsync(); };

        Loaded += (_, _) => _ = PushAsync();
    }

    /// <summary>The chosen size, valid once the dialog returns true.</summary>
    public int CellPixels => (int)SizeSlider.Value;

    /// <summary>The chosen rotation, valid once the dialog returns true.</summary>
    public int CellRotation => (int)RotationSlider.Value;

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        _settle.Stop();
        _settle.Start();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        SizeSlider.Value = DotStream.Core.DeckLayout.CellPixels;
        RotationSlider.Value = 270;
    }

    private async Task PushAsync()
    {
        int size = CellPixels;
        int rotation = CellRotation;
        bool pattern = PatternCheck.IsChecked == true;

        SizeLabel.Text = $"Cell size — {size} x {size}";
        RotationLabel.Text = $"Rotation — {rotation}°";

        try
        {
            await _preview(size, rotation, pattern);
            Readout.Text = $"{size}x{size}   rotation {rotation}°   {(pattern ? "measuring pattern" : "your own keys")}";
        }
        catch (Exception ex)
        {
            Readout.Text = ex.Message;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Cancel has no handler of its own: IsCancel already closes the window with a
    /// false result, and the caller repaints the deck either way. A handler that did
    /// its own awaiting came back to a window that had closed underneath it and threw
    /// on DialogResult - the button worked, then reported failure.
    ///
    /// Stopping the timer here is what stops a slider nudged just before closing from
    /// firing a preview into a dialog that no longer exists.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        _settle.Stop();
        base.OnClosed(e);
    }
}
