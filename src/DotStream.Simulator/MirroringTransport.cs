using DotStream.Core;

namespace DotStream.Simulator;

/// <summary>
/// Drives the real deck and the on-screen one at the same time, and keeps working when
/// the real one is pulled out.
///
/// Without the mirror the window goes black the moment hardware is plugged in, because
/// the simulator was never a fallback that also happened to draw - it *was* what drew.
/// Mirroring keeps the picture on screen honest, which matters while building a page:
/// you should not have to look away from the editor to see what you just changed.
///
/// A press from either side counts. Clicking the on-screen deck still works with the
/// hardware attached, which is worth more than it sounds when the deck is behind a
/// monitor or you are working somewhere else.
/// </summary>
public sealed class MirroringTransport : IDeckTransport
{
    private readonly SimulatorTransport _screen;
    private readonly Func<IDeckTransport?> _find;
    private readonly Action<IDeckTransport>? _configure;
    private readonly SemaphoreSlim _swap = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    private IDeckTransport? _device;
    private int _brightness = 80;

    /// <param name="find">
    /// Looks for the hardware. Called again after a disconnect, which is why this is a
    /// factory rather than an instance: the old handles are dead and a returning device
    /// is a new device as far as Windows is concerned.
    /// </param>
    /// <param name="configure">
    /// Applies whatever settings the hardware transport needs - cell rotation, in
    /// practice. A callback rather than a property because this project has no business
    /// knowing what a HID transport is: it draws decks, real or on screen.
    /// </param>
    /// <param name="device">
    /// The deck, or null if none is attached yet. Null is a normal state, not an error:
    /// starting dotStream before plugging the deck in is the ordinary case for anyone
    /// who has it set to run at login, and the window is useful on its own regardless.
    /// Either way this watches for hardware and picks it up when it appears.
    /// </param>
    public MirroringTransport(
        IDeckTransport? device,
        SimulatorTransport screen,
        Func<IDeckTransport?> find,
        Action<IDeckTransport>? configure = null)
    {
        _device = device;
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        _find = find ?? throw new ArgumentNullException(nameof(find));
        _configure = configure;

        if (_device is not null) _configure?.Invoke(_device);

        // The on-screen copy costs nothing to draw, so it should not pretend to.
        _screen.SimulatedUploadTime = TimeSpan.Zero;

        _screen.KeyPressed += (_, e) => KeyPressed?.Invoke(this, e);
        _screen.KeyReleased += (_, e) => KeyReleased?.Invoke(this, e);

        if (_device is not null) Subscribe(_device);
        else _ = WatchForDeviceAsync();
    }

    /// <summary>Raised when the hardware comes back, so the caller can repaint it.</summary>
    public event EventHandler? Reconnected;

    /// <summary>
    /// Re-applies the caller's device settings to the hardware currently attached.
    ///
    /// Needed because a reconnect builds a *fresh* transport, which comes back at its
    /// own defaults - a calibration made before the cable was pulled would otherwise
    /// vanish without saying so.
    /// </summary>
    public void ReconfigureDevice()
    {
        if (_device is { } device) _configure?.Invoke(device);
    }

    public string Name => _device?.Name ?? "No deck - window only, watching for one";

    public bool IsConnected => _device?.IsConnected ?? false;

    public event EventHandler<DeckKeyEventArgs>? KeyPressed;

    public event EventHandler<DeckKeyEventArgs>? KeyReleased;

    public event EventHandler? Disconnected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_device is not null) await _device.ConnectAsync(ct);
        await _screen.ConnectAsync(ct);
    }

    public async Task SetBrightnessAsync(int percent, CancellationToken ct = default)
    {
        _brightness = percent;

        await _screen.SetBrightnessAsync(percent, ct);
        await ToDeviceAsync(d => d.SetBrightnessAsync(percent, ct));
    }

    public async Task SetCellAsync(int protocolIndex, RenderedCell cell, CancellationToken ct = default)
    {
        // Screen first: it is instant, and it means the window never lags behind the
        // hardware even while a slow upload is in flight.
        await _screen.SetCellAsync(protocolIndex, cell, ct);
        await ToDeviceAsync(d => d.SetCellAsync(protocolIndex, cell, ct));
    }

    public async Task ClearCellAsync(int protocolIndex, CancellationToken ct = default)
    {
        await _screen.ClearCellAsync(protocolIndex, ct);
        await ToDeviceAsync(d => d.ClearCellAsync(protocolIndex, ct));
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await _screen.ClearAllAsync(ct);
        await ToDeviceAsync(d => d.ClearAllAsync(ct));
    }

    /// <summary>Hardware only. There is no panel behind the on-screen deck.</summary>
    public Task SleepAsync(CancellationToken ct = default) =>
        ToDeviceAsync(d => d.SleepAsync(ct));

    /// <summary>
    /// Sends to the hardware if there is any, and swallows the failure if it vanished
    /// between the check and the write.
    ///
    /// Swallowing is right here. A cable pulled mid-repaint would otherwise surface as
    /// an error for every one of eighteen cells, when the only thing worth saying is
    /// that the deck is gone - which the disconnect event already says once.
    /// </summary>
    private async Task ToDeviceAsync(Func<IDeckTransport, Task> action)
    {
        IDeckTransport? device = _device;
        if (device is null) return;

        try
        {
            await action(device);
        }
        catch (Exception)
        {
            // The transport raises Disconnected itself; nothing more to do here.
        }
    }

    private void Subscribe(IDeckTransport device)
    {
        device.KeyPressed += OnDeviceKeyPressed;
        device.KeyReleased += OnDeviceKeyReleased;
        device.Disconnected += OnDeviceDisconnected;
    }

    private void Unsubscribe(IDeckTransport device)
    {
        device.KeyPressed -= OnDeviceKeyPressed;
        device.KeyReleased -= OnDeviceKeyReleased;
        device.Disconnected -= OnDeviceDisconnected;
    }

    private void OnDeviceKeyPressed(object? sender, DeckKeyEventArgs e) => KeyPressed?.Invoke(this, e);

    private void OnDeviceKeyReleased(object? sender, DeckKeyEventArgs e) => KeyReleased?.Invoke(this, e);

    private void OnDeviceDisconnected(object? sender, EventArgs e)
    {
        _ = DropAndWaitForItAsync();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Lets go of the dead device, then waits for one to turn up.
    /// </summary>
    private async Task DropAndWaitForItAsync()
    {
        await _swap.WaitAsync();

        try
        {
            if (_device is { } dead)
            {
                Unsubscribe(dead);
                _device = null;

                try { await dead.DisposeAsync(); } catch { /* it is already gone */ }
            }
        }
        finally { _swap.Release(); }

        await WatchForDeviceAsync();
    }

    /// <summary>
    /// Looks for a deck until one turns up.
    ///
    /// Runs both when a deck was pulled out and when there never was one, because those
    /// are the same situation from here: a deck that appears is always a new device -
    /// the old handles refer to something Windows has forgotten - so this enumerates
    /// from scratch rather than reopening anything. Two seconds between attempts is far
    /// below anyone's patience for plugging in a cable, and costs nothing meanwhile.
    /// </summary>
    private async Task WatchForDeviceAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(2), _stopping.Token); }
            catch (OperationCanceledException) { return; }

            IDeckTransport? found;

            try { found = _find(); }
            catch { continue; }

            if (found is null) continue;

            try
            {
                await found.ConnectAsync(_stopping.Token);

                _configure?.Invoke(found);
                await found.SetBrightnessAsync(_brightness, _stopping.Token);
            }
            catch
            {
                try { await found.DisposeAsync(); } catch { }
                continue;
            }

            await _swap.WaitAsync();

            try
            {
                _device = found;
                Subscribe(found);
            }
            finally { _swap.Release(); }

            // The cells are blank until somebody repaints them, and only the caller
            // knows what should be on them.
            Reconnected?.Invoke(this, EventArgs.Empty);
            return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        await _screen.DisposeAsync();

        if (_device is { } device)
        {
            Unsubscribe(device);
            try { await device.DisposeAsync(); } catch { }
        }

        _stopping.Dispose();
        _swap.Dispose();
    }
}
