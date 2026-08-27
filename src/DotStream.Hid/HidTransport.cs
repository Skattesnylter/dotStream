using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DotStream.Core;

namespace DotStream.Hid;

/// <summary>
/// The real AKP153E over USB HID.
///
/// Every constant here was measured against hardware rather than taken from the
/// protocol notes, because two of them turned out to be wrong for this variant. See
/// docs/PROTOCOL.md for what was verified and when.
/// </summary>
public sealed class HidTransport : IDeckTransport
{
    private readonly HidCollection _collection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    private SafeHandle? _write;
    private SafeHandle? _read;
    private Task? _reader;

    public HidTransport(HidCollection collection) =>
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));

    /// <summary>Opens the deck if one is attached, otherwise null.</summary>
    public static HidTransport? TryOpen() =>
        HidDevice.FindDeck() is { } deck ? new HidTransport(deck) : null;

    /// <summary>
    /// What to call the attached deck.
    ///
    /// Not the device's own product string, which on the measured unit reads
    /// "HOTSPOTEKUSB HID DEMO" - a factory placeholder the vendor never replaced. It
    /// looks like a fault in the window, and it is not ours to apologise for.
    ///
    /// Not "AKP153E" either, tempting as it is: decks are found by vendor usage page
    /// rather than by VID/PID, so anything answering that description gets opened and
    /// naming it would be a claim this cannot back. The identifier pair says which
    /// variant it really is, which is the part worth reporting in a bug.
    /// </summary>
    public string Name => $"Deck  ({_collection.VendorId:X4}:{_collection.ProductId:X4})";

    /// <summary>
    /// The device's own product string, for diagnostics. Shown on hover and written to
    /// the console when connecting, because it is exactly what somebody comparing two
    /// variants needs and exactly what nobody wants on screen all day.
    /// </summary>
    public string ProductName =>
        _collection.Product is { Length: > 0 } product ? product : "(no product string)";

    public bool IsConnected { get; private set; }

    /// <summary>
    /// Degrees to turn each image before upload. 270 with no mirroring on the measured
    /// device: an unrotated image arrives on its side, which is how the first test
    /// pattern came out with its markers pointing the wrong way.
    ///
    /// Settable because the panel's mounting is a property of the variant, not of the
    /// protocol, and several variants ship under this name.
    /// </summary>
    public int Rotation { get; set; } = 270;

    public event EventHandler<DeckKeyEventArgs>? KeyPressed;

    /// <summary>Raised on release as well as press, which the device reports explicitly.</summary>
    public event EventHandler<DeckKeyEventArgs>? KeyReleased;

    /// <summary>
    /// The deck went away.
    ///
    /// Measured: pulling the cable does not take the application down - the read
    /// blocks, then fails, and writes start throwing. What it does do without this
    /// event is leave a deck that stays dark after being plugged back in, with no
    /// indication why. Somebody has to be told so somebody can go looking for it again.
    /// </summary>
    public event EventHandler? Disconnected;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        // Two handles rather than one. A blocking read and a write on the same handle
        // can sit on top of each other, and reads block indefinitely by design - there
        // is nothing to read until somebody presses a key.
        _write = HidDevice.Open(_collection.Path, read: false, write: true);
        _read = HidDevice.Open(_collection.Path, read: true, write: false);

        IsConnected = true;

        Send("DIS");
        _reader = Task.Run(ReadLoop, CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task SetBrightnessAsync(int percent, CancellationToken ct = default)
    {
        Send("LIG", (byte)Math.Clamp(percent, 0, 100));
        return Task.CompletedTask;
    }

    public async Task SetCellAsync(int protocolIndex, RenderedCell cell, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cell);

        if (!DeckLayout.IsValid(protocolIndex))
            throw new ArgumentOutOfRangeException(nameof(protocolIndex), protocolIndex, "Not a valid cell.");

        await SendImageAsync(protocolIndex, Encode(cell.Image), ct);
    }

    /// <summary>
    /// Puts an already-encoded JPEG on a cell.
    ///
    /// Exposed because the encoding is the part still being calibrated: the panel is
    /// larger than the 85x85 the notes claim, and until the true size is settled a
    /// tool needs to send images this layer has not decided the shape of.
    /// </summary>
    public async Task SendImageAsync(int protocolIndex, byte[] jpeg, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        await _writeLock.WaitAsync(ct);

        try
        {
            // Size big-endian, then the 1-based cell index.
            Send("BAT", (byte)(jpeg.Length >> 8), (byte)(jpeg.Length & 0xFF), (byte)protocolIndex);

            int payload = _collection.OutputReportLength - 1;

            for (int sent = 0; sent < jpeg.Length; sent += payload)
            {
                var frame = new byte[_collection.OutputReportLength];
                Array.Copy(jpeg, sent, frame, 1, Math.Min(payload, jpeg.Length - sent));
                Write(frame);
            }

            Send("STP");
        }
        finally { _writeLock.Release(); }
    }

    public async Task ClearCellAsync(int protocolIndex, CancellationToken ct = default) =>
        await ClearAsync(0x00, (byte)protocolIndex, ct);

    public async Task ClearAllAsync(CancellationToken ct = default) =>
        await ClearAsync(0x00, 0xFF, ct);

    /// <summary>
    /// Sends a clear and commits it.
    ///
    /// The STP is the point. A clear on its own sits in the device without taking
    /// effect, which hid itself for a long time: every clear in the application was
    /// followed by a repaint, and the first image's own STP committed the clear along
    /// with it. Clearing on shutdown was the first clear with nothing after it, and it
    /// did nothing at all.
    ///
    /// Under the write lock for the same reason image uploads are: a clear landing
    /// between a BAT and its payload would be read as part of the image.
    /// </summary>
    private async Task ClearAsync(byte tag, byte target, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);

        try
        {
            Send("CLE", tag, target);
            Send("STP");
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>
    /// Sends HAN, which the protocol notes list as Sleep.
    ///
    /// UNVERIFIED. That line came from the sources the notes were built on, not from
    /// this hardware, and the last two constants taken on trust the same way were both
    /// wrong. It is sent on shutdown only, where the worst case is a deck that needs
    /// unplugging - and ConnectAsync opens with DIS, which is display init and the most
    /// likely thing to wake a sleeping panel.
    ///
    /// No STP after it. CLE needed one, LIG did not, so committing is per-command
    /// rather than universal and guessing again would be repeating the mistake.
    /// </summary>
    public Task SleepAsync(CancellationToken ct = default)
    {
        Send("HAN");
        return Task.CompletedTask;
    }

    /// <summary>Puts the vendor's own logo back, which is the only way to undo a clear.</summary>
    public Task RestoreVendorLogoAsync(CancellationToken ct = default)
    {
        Send("CLE", 0x44, 0x43);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds and writes one command frame.
    ///
    /// Packet layout is "CRT", two zero bytes, a three-character ASCII opcode, two
    /// more zeros, then the payload. The report-ID byte in front shifts all of it by
    /// one, which is the detail that costs an evening if you get it wrong.
    /// </summary>
    private void Send(string opcode, params byte[] payload)
    {
        var frame = new byte[_collection.OutputReportLength];

        Encoding.ASCII.GetBytes("CRT").CopyTo(frame, 1);
        Encoding.ASCII.GetBytes(opcode).CopyTo(frame, 6);

        for (int i = 0; i < payload.Length && 11 + i < frame.Length; i++)
            frame[11 + i] = payload[i];

        Write(frame);
    }

    private void Write(byte[] frame)
    {
        if (_write is null) throw new InvalidOperationException("Not connected.");

        // The whole report has to go, every time. Windows rejects a write that is not
        // exactly OutputReportByteLength - it does not pad for you.
        if (HidDevice.WriteFile(_write, frame, frame.Length, out _, IntPtr.Zero)) return;

        int error = Marshal.GetLastWin32Error();

        // A write to a device that has been pulled fails like any other write. Saying so
        // once is what lets the layer above stop trying and start looking.
        if (IsConnected)
        {
            IsConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        throw new IOException($"Write failed: Win32 error {error}.");
    }

    /// <summary>
    /// Key events, read until disposal.
    ///
    /// Frames carry an "ACK\0\0OK" header, the cell index at byte 10 and the state at
    /// byte 11. Press and release are explicit, so no edge has to be synthesised by
    /// comparing successive frames - the earlier protocol notes were wrong about that,
    /// in our favour.
    /// </summary>
    private void ReadLoop()
    {
        var buffer = new byte[_collection.InputReportLength];

        while (!_stopping.IsCancellationRequested && _read is not null)
        {
            if (!HidDevice.ReadFile(_read, buffer, buffer.Length, out int read, IntPtr.Zero))
            {
                // Distinguishing "unplugged" from "we are shutting down" matters: only
                // one of them should send anybody looking for the device again.
                if (!_stopping.IsCancellationRequested)
                {
                    IsConnected = false;
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            if (read < 12) continue;

            int index = buffer[10];
            bool down = buffer[11] == 0x01;

            if (!DeckLayout.IsKey(index)) continue;

            var args = new DeckKeyEventArgs(index);

            if (down) KeyPressed?.Invoke(this, args);
            else KeyReleased?.Invoke(this, args);
        }
    }

    /// <summary>
    /// JPEG for the device: rotated 270 degrees, quality 90.
    ///
    /// The rotation is not a guess. Sent upright, an asymmetric glyph arrives on the
    /// deck turned 90 degrees clockwise, so the image is pre-turned the other way. The
    /// notes described this as "90 degrees plus horizontal and vertical mirroring",
    /// which is the same thing said the long way round.
    /// </summary>
    private byte[] Encode(BitmapSource image)
    {
        BitmapSource oriented = Rotation == 0
            ? image
            : new TransformedBitmap(image, new RotateTransform(Rotation));

        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(oriented));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        IsConnected = false;

        // Closing the read handle is what breaks the blocking read; there is no
        // polite way to cancel it.
        _read?.Dispose();
        _read = null;

        if (_reader is not null)
        {
            try { await _reader.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (TimeoutException) { }
        }

        _write?.Dispose();
        _write = null;

        _stopping.Dispose();
        _writeLock.Dispose();
    }
}
