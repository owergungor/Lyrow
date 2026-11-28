using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// Individual timing-aware lyrics provider participating in v5.0 multi-provider resolution.
/// </summary>
public interface ILyricsSyncProvider
{
    LyricsProviderId ProviderId { get; }

    Task<LyricsTimingData?> FetchTimingDataAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregator provider (such as LiriQo) that returns multiple upstream-attributed tracks
/// in a single HTTP round-trip so Lyrow never emits duplicate requests.
/// </summary>
public interface IMultiTrackLyricsProvider
{
    string AggregatorName { get; }

    Task<IReadOnlyList<LyricsTimingData>> FetchAllUpstreamTracksAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Multi-provider lyrics orchestrator supporting v5.0 synchronization modes, priority resolution,
/// strict capability validation, and fallback recommendation.
/// </summary>
public interface IMultiProviderLyricsService : ILyricsClient
{
    LyricsResolutionResult? LastResolution { get; }

    Task<LyricsResolutionResult> ResolveLyricsAsync(
        string trackName,
        string artistName,
        LyricsSyncMode requestedMode,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries providers in controlled parallel with per-provider timeouts, immediately emitting
    /// the first usable lyrics result via <paramref name="onInterimResult"/> and seamlessly upgrading
    /// if a higher-precision timing payload arrives later. Never downgrades precision.
    /// </summary>
    Task<LyricsResolutionResult> ResolveLyricsProgressiveAsync(
        string trackName,
        string artistName,
        LyricsSyncMode requestedMode,
        Func<LyricsResolutionResult, Task>? onInterimResult = null,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service abstraction for fetching lyrics from online providers (e.g. LRCLIB).
/// </summary>
public interface ILyricsClient
{
    /// <summary>
    /// Fetches synchronized or plain lyrics for a track, handling network timeouts, 404, and rate limits gracefully.
    /// </summary>
    Task<LyricsSearchResult> FetchLyricsAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Persistent or in-memory cache for storing and retrieving synchronized lyrics.
/// </summary>
public interface ILyricsCache
{
    /// <summary>Retrieves cached lyrics for the specified track and artist, or null if miss.</summary>
    Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default);

    /// <summary>Stores lyrics in local cache.</summary>
    Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default);

    /// <summary>Evicts all cached lyrics.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
