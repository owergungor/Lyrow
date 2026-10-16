using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

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
