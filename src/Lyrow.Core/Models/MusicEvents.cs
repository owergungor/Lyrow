namespace Lyrow.Core.Models;

/// <summary>
/// Event broadcast when the active music track metadata changes (e.g. song changed in Spotify/Browser).
/// </summary>
public sealed record MediaTrackChangedEvent(MediaTrackInfo Track);

/// <summary>
/// Event broadcast when the playback state (Play/Pause/Stop) or position changes.
/// </summary>
public sealed record MediaPlaybackStateChangedEvent(MediaTimelineInfo Timeline);

/// <summary>
/// Event broadcast when lyrics have been fetched or retrieved from cache.
/// </summary>
public sealed record LyricsLoadedEvent(SyncedLyrics Lyrics, string Source);

/// <summary>
/// Event broadcast when the current active lyric line advances to a new index.
/// </summary>
public sealed record ActiveLyricLineChangedEvent(int LineIndex, LyricLine? Line);

/// <summary>
/// Event broadcast when lyrics lookup fails or is rate limited.
/// </summary>
public sealed record LyricsFetchFailedEvent(string ErrorMessage, bool IsRateLimited, int RetryAfterSeconds);
