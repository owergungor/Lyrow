using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// Service managing asynchronous retrieval, caching, and lifecycle of album artwork.
/// </summary>
public interface IArtworkService
{
    /// <summary>
    /// Asynchronously retrieves artwork bytes for a track, using in-memory cache when available.
    /// Returns null if no artwork is available or if data is corrupt.
    /// </summary>
    Task<byte[]?> GetArtworkBytesAsync(MediaTrackInfo track, CancellationToken ct = default);

    /// <summary>
    /// Explicitly caches artwork bytes for a given artist and track title.
    /// </summary>
    void CacheArtwork(string artist, string title, byte[] artworkBytes);

    /// <summary>
    /// Checks whether artwork for the given track is already present in cache.
    /// </summary>
    bool IsCached(string artist, string title);

    /// <summary>
    /// Removes artwork for a specific track from the cache.
    /// </summary>
    void Invalidate(string artist, string title);

    /// <summary>
    /// Clears all cached artwork.
    /// </summary>
    void ClearCache();
}
