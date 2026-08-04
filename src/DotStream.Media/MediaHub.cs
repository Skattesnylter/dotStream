using System.IO;
using System.Windows.Media.Imaging;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DotStream.Media;

/// <summary>
/// Reads and controls whatever is currently playing, through Windows' own media
/// transport session API.
///
/// Chosen over the Spotify Web API on purpose: no OAuth, no application
/// registration, no rate limits, and it works identically for Spotify, Tidal, VLC
/// and a YouTube tab. In exchange we only get what the session exposes, which is
/// exactly the transport surface a deck key needs.
/// </summary>
public sealed class MediaHub
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private MediaSnapshot? _snapshot;

    // Album art fetch is comparatively expensive, so it is only redone when the
    // track actually changes.
    private string? _thumbnailKey;
    private BitmapSource? _thumbnail;

    public MediaSnapshot? Snapshot => _snapshot;

    public bool IsAvailable => _manager is not null;

    public async Task InitialiseAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch
        {
            // No media platform available. Everything below degrades to no-ops.
            _manager = null;
        }
    }

    public async Task RefreshAsync()
    {
        GlobalSystemMediaTransportControlsSession? session = CurrentSession();

        if (session is null)
        {
            _snapshot = null;
            return;
        }

        try
        {
            GlobalSystemMediaTransportControlsSessionMediaProperties properties =
                await session.TryGetMediaPropertiesAsync();

            GlobalSystemMediaTransportControlsSessionPlaybackInfo playback = session.GetPlaybackInfo();
            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline = session.GetTimelineProperties();

            string key = session.SourceAppUserModelId + "|" + properties.Title + "|" + properties.Artist;

            if (key != _thumbnailKey)
            {
                _thumbnail = await LoadThumbnailAsync(properties.Thumbnail);
                _thumbnailKey = key;
            }

            _snapshot = new MediaSnapshot
            {
                SourceAppId = session.SourceAppUserModelId,
                Title = properties.Title,
                Artist = properties.Artist,
                IsPlaying = playback.PlaybackStatus ==
                            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Position = timeline.Position,
                Duration = timeline.EndTime - timeline.StartTime,
                Thumbnail = _thumbnail
            };
        }
        catch
        {
            // Sessions disappear mid-call when an app closes. Keep the last good one.
        }
    }

    /// <summary>
    /// True when something matching this app is currently a media session - which is
    /// also the most reliable "is it running" signal we have that works for both
    /// Win32 and packaged apps.
    /// </summary>
    public bool HasSessionFor(string appUserModelId, string? displayName = null)
    {
        if (_manager is null) return false;

        string wanted = Stem(appUserModelId);
        if (wanted.Length == 0) return false;

        try
        {
            foreach (GlobalSystemMediaTransportControlsSession session in _manager.GetSessions())
            {
                string candidate = Stem(session.SourceAppUserModelId);
                if (candidate.Length == 0) continue;

                if (candidate == wanted ||
                    candidate.Contains(wanted, StringComparison.Ordinal) ||
                    wanted.Contains(candidate, StringComparison.Ordinal))
                    return true;

                if (displayName is not null &&
                    Stem(displayName) is { Length: > 0 } name &&
                    (candidate.Contains(name, StringComparison.Ordinal) ||
                     name.Contains(candidate, StringComparison.Ordinal)))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public Task TogglePlayPauseAsync() => Invoke(s => s.TryTogglePlayPauseAsync());

    public Task NextAsync() => Invoke(s => s.TrySkipNextAsync());

    public Task PreviousAsync() => Invoke(s => s.TrySkipPreviousAsync());

    /// <summary>
    /// Jumps a fixed distance through whatever is playing. Negative goes back.
    ///
    /// Done by reading the position and setting a new one rather than by asking the
    /// application to fast-forward: <c>TryFastForwardAsync</c> means whatever the app
    /// decided it means - a scan in one, a fixed jump of its own choosing in another -
    /// while an absolute position is exactly the distance asked for. Clamped to the
    /// seekable range so the end of a track is a stop rather than an error.
    /// </summary>
    public async Task SeekByAsync(TimeSpan offset)
    {
        GlobalSystemMediaTransportControlsSession? session = CurrentSession();
        if (session is null) return;

        try
        {
            if (!session.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled) return;

            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline =
                session.GetTimelineProperties();

            TimeSpan target = timeline.Position + offset;

            if (target < timeline.MinSeekTime) target = timeline.MinSeekTime;
            if (timeline.MaxSeekTime > timeline.MinSeekTime && target > timeline.MaxSeekTime)
                target = timeline.MaxSeekTime;

            await session.TryChangePlaybackPositionAsync(target.Ticks);
            await RefreshAsync();
        }
        catch
        {
            // The session went away between lookup and call.
        }
    }

    private async Task Invoke(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> operation)
    {
        GlobalSystemMediaTransportControlsSession? session = CurrentSession();
        if (session is null) return;

        try
        {
            await operation(session);
            await RefreshAsync();
        }
        catch
        {
            // The session went away between lookup and call.
        }
    }

    /// <summary>
    /// The application a media key should address, when it is sitting on that
    /// application's page.
    ///
    /// Without this, transport goes to whichever session Windows considers current, which
    /// is not the page you are looking at: pressing Next on a Media Player page skipped
    /// a Spotify track, because Spotify was what Windows had in front. Null means no
    /// preference - the current session, as before.
    /// </summary>
    public string? PreferredSource { get; set; }

    private GlobalSystemMediaTransportControlsSession? CurrentSession()
    {
        try
        {
            if (_manager is null) return null;

            if (PreferredSource is { Length: > 0 } wanted && SessionFor(wanted) is { } preferred)
                return preferred;

            return _manager.GetCurrentSession();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The session belonging to an application, matched the same forgiving way as
    /// <see cref="HasSessionFor"/> - a packaged app reports the identifier it was
    /// installed under, but a desktop one often reports its executable instead.
    /// </summary>
    private GlobalSystemMediaTransportControlsSession? SessionFor(string appUserModelId)
    {
        string wanted = Stem(appUserModelId);
        if (wanted.Length == 0 || _manager is null) return null;

        foreach (GlobalSystemMediaTransportControlsSession session in _manager.GetSessions())
        {
            string candidate = Stem(session.SourceAppUserModelId);
            if (candidate.Length == 0) continue;

            if (candidate == wanted ||
                candidate.Contains(wanted, StringComparison.Ordinal) ||
                wanted.Contains(candidate, StringComparison.Ordinal))
                return session;
        }

        return null;
    }

    private static async Task<BitmapSource?> LoadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null) return null;

        try
        {
            using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
            if (stream.Size == 0) return null;

            var buffer = new byte[stream.Size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(buffer);

            var image = new BitmapImage();
            using (var memory = new MemoryStream(buffer))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = memory;
                image.EndInit();
            }

            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reduces an AppUserModelID to something comparable. AppsFolder hands out
    /// "Spotify.exe" or "SpotifyAB.SpotifyMusic_zpd...!Spotify" depending on whether
    /// the install is Win32 or packaged; media sessions report their own variant.
    /// Comparing stems matches across both.
    /// </summary>
    private static string Stem(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        int bang = value.IndexOf('!');
        if (bang > 0) value = value[..bang];

        int slash = value.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) value = value[(slash + 1)..];

        int dot = value.IndexOf('.');
        if (dot > 0) value = value[..dot];

        int underscore = value.IndexOf('_');
        if (underscore > 0) value = value[..underscore];

        return value.Trim().ToLowerInvariant();
    }
}
