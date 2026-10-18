using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App.Platform;

/// <summary>
/// Caches decoded WinUI 3 BitmapImage artwork instances asynchronously
/// to eliminate UI stalls and repeated disk/memory decodes.
/// </summary>
public sealed class ArtworkBitmapManager
{
    private readonly IArtworkService _artworkService;
    private readonly ConcurrentDictionary<string, byte[]> _rawBytesCache = new(StringComparer.OrdinalIgnoreCase);

    public ArtworkBitmapManager(IArtworkService artworkService)
    {
        _artworkService = artworkService;
    }

    /// <summary>
    /// Asynchronously retrieves or decodes the album artwork BitmapImage for a track.
    /// Returns null if unavailable or corrupt.
    /// </summary>
    public async Task<BitmapImage?> GetArtworkBitmapAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        if (track.IsEmpty) return null;

        string key = ArtworkService.CreateCacheKey(track.Artist, track.Title);

        byte[]? rawBytes;
        if (!_rawBytesCache.TryGetValue(key, out rawBytes))
        {
            rawBytes = await _artworkService.GetArtworkBytesAsync(track, ct).ConfigureAwait(false);
            if (rawBytes != null && rawBytes.Length > 0)
            {
                _rawBytesCache[key] = rawBytes;
            }
        }

        if (rawBytes == null || rawBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(rawBytes);
                await writer.StoreAsync();
            }

            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]?> GetArtworkBytesAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        return await _artworkService.GetArtworkBytesAsync(track, ct).ConfigureAwait(false);
    }

    public void Invalidate(string artist, string title)
    {
        string key = ArtworkService.CreateCacheKey(artist, title);
        _rawBytesCache.TryRemove(key, out _);
        _artworkService.Invalidate(artist, title);
    }

    public void Clear()
    {
        _rawBytesCache.Clear();
        _artworkService.ClearCache();
    }
}
