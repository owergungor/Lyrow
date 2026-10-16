namespace Lyrow.Core.Models;

/// <summary>
/// Playback status of active media session.
/// </summary>
public enum MediaPlaybackStatus
{
    Unknown,
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused
}

/// <summary>
/// Snapshot of metadata describing the currently playing music track.
/// </summary>
public sealed record MediaTrackInfo(
    string Title,
    string Artist,
    string Album,
    TimeSpan Duration,
    string? TrackId = null,
    string? AlbumArtUri = null,
    byte[]? ThumbnailData = null)
{
    public static readonly MediaTrackInfo Empty = new(
        Title: string.Empty,
        Artist: string.Empty,
        Album: string.Empty,
        Duration: TimeSpan.Zero);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Artist);
}

/// <summary>
/// Playback timeline position and playback status.
/// </summary>
public sealed record MediaTimelineInfo(
    TimeSpan Position,
    TimeSpan Duration,
    MediaPlaybackStatus Status,
    DateTime LastUpdatedUtc)
{
    public static readonly MediaTimelineInfo Zero = new(
        Position: TimeSpan.Zero,
        Duration: TimeSpan.Zero,
        Status: MediaPlaybackStatus.Stopped,
        LastUpdatedUtc: DateTime.UtcNow);

    /// <summary>
    /// Calculates smoothly interpolated current position by adding elapsed real time if Playing.
    /// </summary>
    public TimeSpan GetInterpolatedPosition()
    {
        if (Status != MediaPlaybackStatus.Playing || Position <= TimeSpan.Zero)
        {
            return Position;
        }

        var elapsed = DateTime.UtcNow - LastUpdatedUtc;
        var estimated = Position + elapsed;

        if (Duration > TimeSpan.Zero && estimated > Duration)
        {
            return Duration;
        }

        return estimated >= TimeSpan.Zero ? estimated : TimeSpan.Zero;
    }
}

/// <summary>
/// A single synchronized lyric line with timestamp and text.
/// </summary>
public sealed record LyricLine(
    TimeSpan Timestamp,
    string Text,
    int LineIndex)
{
    public override string ToString() => $"[{Timestamp:mm\\:ss\\.ff}] {Text}";
}

/// <summary>
/// Represents full synchronized or plain lyrics for a track.
/// </summary>
public sealed class SyncedLyrics
{
    public static readonly SyncedLyrics Empty = new(
        title: string.Empty,
        artist: string.Empty,
        isSynced: false,
        lines: Array.Empty<LyricLine>(),
        plainLyrics: null);

    public SyncedLyrics(
        string title,
        string artist,
        bool isSynced,
        IReadOnlyList<LyricLine> lines,
        string? plainLyrics = null,
        TimeSpan? duration = null)
    {
        Title = title;
        Artist = artist;
        IsSynced = isSynced;
        Lines = lines;
        PlainLyrics = plainLyrics;
        Duration = duration;
    }

    public string Title { get; }
    public string Artist { get; }
    public bool IsSynced { get; }
    public IReadOnlyList<LyricLine> Lines { get; }
    public string? PlainLyrics { get; }
    public TimeSpan? Duration { get; }

    /// <summary>
    /// Efficiently finds the active lyric line index for the given playback position using Binary Search (O(log N)).
    /// Returns -1 if the position is prior to the first lyric timestamp or if no lines exist.
    /// </summary>
    public int FindActiveLineIndex(TimeSpan position)
    {
        if (Lines.Count == 0) return -1;
        if (position < Lines[0].Timestamp) return -1;

        int low = 0;
        int high = Lines.Count - 1;
        int candidate = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            if (Lines[mid].Timestamp <= position)
            {
                candidate = mid;
                low = mid + 1; // Try to find a later matching timestamp
            }
            else
            {
                high = mid - 1;
            }
        }

        return candidate;
    }
}

/// <summary>
/// Result wrapper returned by lyrics lookup clients and caches.
/// </summary>
public sealed record LyricsSearchResult(
    bool Found,
    SyncedLyrics? Lyrics,
    string Source,
    string? Error = null);
