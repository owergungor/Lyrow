using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media.Imaging;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App.Platform;

/// <summary>
/// Caches decoded Avalonia Bitmap artwork instances asynchronously off the UI thread
/// to ensure 0 ms UI thread blocking and eliminate repeated JPEG/PNG decodes.
/// </summary>
public sealed class ArtworkBitmapManager
{
    private readonly IArtworkService _artworkService;
    private readonly ConcurrentDictionary<string, Bitmap?> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);

    public ArtworkBitmapManager(IArtworkService artworkService)
    {
        _artworkService = artworkService;
    }

    /// <summary>
    /// Asynchronously retrieves or decodes the album artwork Bitmap for a track.
    /// Returns null if unavailable or corrupt.
    /// </summary>
    public async Task<Bitmap?> GetArtworkBitmapAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        if (track.IsEmpty) return null;

        string key = ArtworkService.CreateCacheKey(track.Artist, track.Title);
        if (_bitmapCache.TryGetValue(key, out var cachedBitmap))
        {
            return cachedBitmap;
        }

        // 1. Fetch raw bytes from ArtworkService or track
        byte[]? rawBytes = await _artworkService.GetArtworkBytesAsync(track, ct).ConfigureAwait(false);
        if (rawBytes == null || rawBytes.Length == 0)
        {
            return null;
        }

        // 2. Decode on thread pool to avoid blocking UI thread
        Bitmap? decoded = await Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(rawBytes);
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }, ct).ConfigureAwait(false);

        if (decoded != null)
        {
            _bitmapCache[key] = decoded;
        }

        return decoded;
    }

    public void Invalidate(string artist, string title)
    {
        string key = ArtworkService.CreateCacheKey(artist, title);
        _bitmapCache.TryRemove(key, out var removed);
        removed?.Dispose();
        _artworkService.Invalidate(artist, title);
    }

    public void Clear()
    {
        foreach (var kvp in _bitmapCache)
        {
            kvp.Value?.Dispose();
        }
        _bitmapCache.Clear();
        _artworkService.ClearCache();
    }
}
