using System.Windows.Media.Imaging;

namespace DotStream.Core;

/// <summary>
/// A rendered cell image plus a hash of its pixels.
///
/// The image is always upright and unrotated. Any device-specific orientation
/// transform belongs in the transport, so the simulator and the real device share
/// one rendering path.
/// </summary>
public sealed class RenderedCell
{
    public BitmapSource Image { get; }

    /// <summary>Hash of the raw pixels. Equal hash =&gt; nothing to upload.</summary>
    public string Hash { get; }

    public RenderedCell(BitmapSource image, string hash)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrEmpty(hash);

        if (!image.IsFrozen)
            throw new ArgumentException("Image must be frozen so it can cross threads.", nameof(image));

        Image = image;
        Hash = hash;
    }
}
