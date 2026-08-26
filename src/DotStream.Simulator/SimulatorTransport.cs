using System.Windows.Threading;
using DotStream.Core;

namespace DotStream.Simulator;

/// <summary>
/// Drives a <see cref="DeckSimulatorControl"/> through the same interface the real
/// device will use.
///
/// It deliberately fakes the USB cost of an upload. An 85x85 JPEG at ~q90 is roughly
/// 5 kB, which is ~11 packets of 512 bytes, and every image is ACK-serialised - call
/// it 15 ms per cell. Simulating that means queueing and priority bugs show up now,
/// on the simulator, instead of on hardware that has not arrived yet.
/// </summary>
public sealed class SimulatorTransport : IDeckTransport
{
    private readonly DeckSimulatorControl _view;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SimulatorTransport(DeckSimulatorControl view)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _dispatcher = view.Dispatcher;

        _view.CellPressed += (_, index) => KeyPressed?.Invoke(this, new DeckKeyEventArgs(index));
        _view.CellReleased += (_, index) => KeyReleased?.Invoke(this, new DeckKeyEventArgs(index));
    }

    /// <summary>Fake per-cell upload cost. Set to zero to disable.</summary>
    public TimeSpan SimulatedUploadTime { get; set; } = TimeSpan.FromMilliseconds(15);

    public string Name => "Simulator (virtual AKP153E)";

    public bool IsConnected { get; private set; }

    public event EventHandler<DeckKeyEventArgs>? KeyPressed;

    /// <summary>
    /// Mouse-up on a key. Real hardware reports both edges, so the simulator does too -
    /// otherwise a long press works on the desk and does nothing on screen, and someone
    /// setting up their deck before it arrives would be building against a lie.
    /// </summary>
    public event EventHandler<DeckKeyEventArgs>? KeyReleased;

    /// <summary>Never raised: there is no cable to pull on a window.</summary>
#pragma warning disable CS0067
    public event EventHandler? Disconnected;
#pragma warning restore CS0067

    public Task ConnectAsync(CancellationToken ct = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task SetBrightnessAsync(int percent, CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() => _view.SetBrightness(percent)).Task;

    public async Task SetCellAsync(int protocolIndex, RenderedCell cell, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cell);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (SimulatedUploadTime > TimeSpan.Zero)
                await Task.Delay(SimulatedUploadTime, ct).ConfigureAwait(false);

            await _dispatcher.InvokeAsync(() => _view.SetCell(protocolIndex, cell.Image)).Task.ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task ClearCellAsync(int protocolIndex, CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(() => _view.ClearCell(protocolIndex)).Task;

    public Task ClearAllAsync(CancellationToken ct = default) =>
        _dispatcher.InvokeAsync(_view.ClearAll).Task;

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
