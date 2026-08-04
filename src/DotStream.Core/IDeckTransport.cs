namespace DotStream.Core;

public sealed class DeckKeyEventArgs : EventArgs
{
    public int ProtocolIndex { get; }

    public DeckKeyEventArgs(int protocolIndex) => ProtocolIndex = protocolIndex;
}

/// <summary>
/// Everything that touches the physical device. Two implementations:
/// SimulatorTransport (virtual deck in a window) and - once the hardware
/// arrives - HidTransport.
///
/// Implementations are expected to serialise their own writes: the AKP153
/// firmware ACKs each image upload and corrupts the target cell if a new
/// upload starts before the ACK arrives.
/// </summary>
public interface IDeckTransport : IAsyncDisposable
{
    string Name { get; }

    bool IsConnected { get; }

    /// <summary>
    /// Raised when a key is pressed. Info cells (16-18) have no switch under them
    /// and never raise this.
    /// </summary>
    event EventHandler<DeckKeyEventArgs>? KeyPressed;

    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>0-100. Firmware clamps above 100.</summary>
    Task SetBrightnessAsync(int percent, CancellationToken ct = default);

    Task SetCellAsync(int protocolIndex, RenderedCell cell, CancellationToken ct = default);

    Task ClearCellAsync(int protocolIndex, CancellationToken ct = default);

    Task ClearAllAsync(CancellationToken ct = default);
}
