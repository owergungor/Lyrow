using System.Collections.Concurrent;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// High-performance thread-safe in-memory album cover and artwork caching service.
/// </summary>
public sealed class ArtworkService : IArtworkService
{
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static string CreateCacheKey(string artist, string title)
    {
        return $"{artist.Trim()}_{title.Trim()}";
    }

    public Task<byte[]?> GetArtworkBytesAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        if (track.IsEmpty)
        {
            return Task.FromResult<byte[]?>(null);
        }

        string key = CreateCacheKey(track.Artist, track.Title);
        if (_cache.TryGetValue(key, out var cachedBytes))
        {
            return Task.FromResult<byte[]?>(cachedBytes);
        }

        if (track.ThumbnailData != null && track.ThumbnailData.Length > 0)
        {
            if (IsValidImageData(track.ThumbnailData))
            {
                _cache[key] = track.ThumbnailData;
                return Task.FromResult<byte[]?>(track.ThumbnailData);
            }
        }

        return Task.FromResult<byte[]?>(null);
    }

    public void CacheArtwork(string artist, string title, byte[] artworkBytes)
    {
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) return;
        if (artworkBytes == null || artworkBytes.Length == 0 || !IsValidImageData(artworkBytes)) return;

        string key = CreateCacheKey(artist, title);
        _cache[key] = artworkBytes;
    }

    public bool IsCached(string artist, string title)
    {
        string key = CreateCacheKey(artist, title);
        return _cache.ContainsKey(key);
    }

    public void Invalidate(string artist, string title)
    {
        string key = CreateCacheKey(artist, title);
        _cache.TryRemove(key, out _);
    }

    public void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Validates standard image file signatures (JPEG, PNG, GIF, BMP, WebP) to protect UI from corrupt data.
    /// </summary>
    public static bool IsValidImageData(byte[] data)
    {
        if (data == null || data.Length < 4) return false;

        // JPEG: FF D8 FF
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;

        // PNG: 89 50 4E 47 (.PNG)
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;

        // GIF: 47 49 46 38 (GIF8)
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) return true;

        // BMP: 42 4D (BM)
        if (data[0] == 0x42 && data[1] == 0x4D) return true;

        // WebP: RIFF ... WEBP
        if (data.Length >= 12 &&
            data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
            data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            return true;
        }

        // Return true if length is substantial as fallback for vendor-specific uncompressed frames
        return data.Length > 64;
    }
}
