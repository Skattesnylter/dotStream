using System.Windows.Media.Imaging;

namespace DotStream.Media;

/// <summary>
/// What is playing right now, as of the last refresh. Immutable so it can be read
/// from a render pass without locking.
/// </summary>
public sealed record MediaSnapshot
{
    public required string SourceAppId { get; init; }

    public string? Title { get; init; }
    public string? Artist { get; init; }

    public bool IsPlaying { get; init; }

    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }

    /// <summary>Album art, frozen. Null when the source provides none.</summary>
    public BitmapSource? Thumbnail { get; init; }

    public double Progress => Duration > TimeSpan.Zero
        ? Math.Clamp(Position / Duration, 0, 1)
        : 0;

    public string DisplayLine => string.IsNullOrWhiteSpace(Artist)
        ? Title ?? ""
        : $"{Artist} - {Title}";
}
