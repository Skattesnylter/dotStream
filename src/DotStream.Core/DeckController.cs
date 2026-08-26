namespace DotStream.Core;

/// <summary>
/// Sits between "what should the deck look like" and the transport.
///
/// Three jobs, all of them forced on us by the hardware:
///
///  1. Dirty-tracking. A cell whose pixels did not change is never uploaded.
///     Without this, a 1 Hz CPU widget would push an identical image every
///     second forever.
///
///  2. Coalescing. Only the newest pending image per cell survives. If the CPU
///     widget ticks three times while the queue is busy, two of those uploads
///     were already obsolete.
///
///  3. Priority. Keys and info cells share one USB pipe and every upload is
///     ACK-serialised, so a batch of info-cell refreshes can sit in front of the
///     visual feedback for a key the user just pressed. High priority jumps
///     the queue.
/// </summary>
public sealed class DeckController : IAsyncDisposable
{
    private readonly IDeckTransport _transport;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, PendingUpload> _pending = new();
    private readonly string?[] _lastHash = new string?[DeckLayout.CellCount + 1];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    public DeckController(IDeckTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _worker = Task.Run(WorkerAsync);
    }

    public IDeckTransport Transport => _transport;

    /// <summary>Raised when an upload throws. The worker keeps running.</summary>
    public event EventHandler<Exception>? UploadFailed;

    public event EventHandler<DeckKeyEventArgs>? KeyReleased
    {
        add => _transport.KeyReleased += value;
        remove => _transport.KeyReleased -= value;
    }

    public event EventHandler<DeckKeyEventArgs>? KeyPressed
    {
        add => _transport.KeyPressed += value;
        remove => _transport.KeyPressed -= value;
    }

    /// <summary>
    /// Queue a cell image. No-op when the pixels match what the device already
    /// shows. Set <paramref name="highPriority"/> for anything the user is
    /// waiting to see - key feedback, page switches.
    /// </summary>
    public void Update(int protocolIndex, RenderedCell cell, bool highPriority = false)
    {
        if (!DeckLayout.IsValid(protocolIndex))
            throw new ArgumentOutOfRangeException(nameof(protocolIndex), protocolIndex, "Not a valid cell index (1-18).");
        ArgumentNullException.ThrowIfNull(cell);

        lock (_gate)
        {
            if (_lastHash[protocolIndex] == cell.Hash && !_pending.ContainsKey(protocolIndex))
                return;

            _pending[protocolIndex] = new PendingUpload(cell, highPriority);
        }

        _signal.Release();
    }

    /// <summary>Forget what the device is showing, so the next Update always uploads.</summary>
    public void Invalidate(int protocolIndex)
    {
        lock (_gate) _lastHash[protocolIndex] = null;
    }

    public void InvalidateAll()
    {
        lock (_gate) Array.Clear(_lastHash);
    }

    private async Task WorkerAsync()
    {
        CancellationToken ct = _cts.Token;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (TryDequeue(out int index, out PendingUpload job))
            {
                try
                {
                    await _transport.SetCellAsync(index, job.Cell, ct).ConfigureAwait(false);
                    lock (_gate) _lastHash[index] = job.Cell.Hash;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    UploadFailed?.Invoke(this, ex);
                }
            }
        }
    }

    private bool TryDequeue(out int index, out PendingUpload job)
    {
        lock (_gate)
        {
            index = -1;
            job = default;

            foreach ((int key, PendingUpload value) in _pending)
            {
                if (!value.HighPriority) continue;
                index = key;
                job = value;
                break;
            }

            if (index < 0)
            {
                foreach ((int key, PendingUpload value) in _pending)
                {
                    index = key;
                    job = value;
                    break;
                }
            }

            if (index < 0) return false;

            _pending.Remove(index);
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected
        }

        _cts.Dispose();
        _signal.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    private readonly record struct PendingUpload(RenderedCell Cell, bool HighPriority);
}
