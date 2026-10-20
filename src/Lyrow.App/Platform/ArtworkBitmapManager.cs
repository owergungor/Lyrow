using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Media.Imaging;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App.Platform;

/// <summary>
/// Caches decoded WinUI 3 BitmapImage artwork instances and extracts true RGBA pixel palettes
/// asynchronously on background threads to eliminate UI stalls.
/// </summary>
public sealed class ArtworkBitmapManager
{
    private readonly IArtworkService _artworkService;
    private readonly ConcurrentDictionary<string, byte[]> _rawBytesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, GlowPalette> _paletteCache = new(StringComparer.OrdinalIgnoreCase);

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

        byte[]? rawBytes = track.ThumbnailData;
        if (rawBytes != null && rawBytes.Length > 0 && ArtworkService.IsValidImageData(rawBytes))
        {
            _rawBytesCache[key] = rawBytes;
            _artworkService.CacheArtwork(track.Artist, track.Title, rawBytes);
        }
        else if (!_rawBytesCache.TryGetValue(key, out rawBytes))
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
            stream.Seek(0);

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
        if (track.IsEmpty) return null;

        string key = ArtworkService.CreateCacheKey(track.Artist, track.Title);
        if (track.ThumbnailData != null && track.ThumbnailData.Length > 0 && ArtworkService.IsValidImageData(track.ThumbnailData))
        {
            _rawBytesCache[key] = track.ThumbnailData;
            return track.ThumbnailData;
        }

        if (_rawBytesCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        return await _artworkService.GetArtworkBytesAsync(track, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Decodes compressed PNG/JPEG/WebP artwork on a background thread using Windows WIC BitmapDecoder
    /// and extracts a vibrant 4-edge GlowPalette from the real pixels.
    /// </summary>
    public async Task<GlowPalette> ExtractArtworkPaletteAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        if (track.IsEmpty) return GlowPalette.Default;

        string key = ArtworkService.CreateCacheKey(track.Artist, track.Title);
        if (_paletteCache.TryGetValue(key, out var cachedPalette))
        {
            return cachedPalette;
        }

        var rawBytes = await GetArtworkBytesAsync(track, ct).ConfigureAwait(false);
        if (rawBytes == null || rawBytes.Length == 0)
        {
            return GlowPalette.Default;
        }

        var palette = await Task.Run(async () =>
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(rawBytes);
                    await writer.StoreAsync();
                }
                stream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(stream);
                var transform = new BitmapTransform
                {
                    ScaledWidth = 32,
                    ScaledHeight = 32,
                    InterpolationMode = BitmapInterpolationMode.Linear
                };

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] rgbaPixels = pixelData.DetachPixelData();
                if (rgbaPixels != null && rgbaPixels.Length >= 64)
                {
                    return ArtworkColorExtractor.ExtractPaletteFromRgbaPixels(rgbaPixels);
                }
            }
            catch
            {
                // Fallback to direct byte sampler if WIC decoder cannot parse synthetic bytes
            }

            return ArtworkColorExtractor.ExtractPalette(rawBytes);
        }, ct).ConfigureAwait(false);

        _paletteCache[key] = palette;
        return palette;
    }

    public void Invalidate(string artist, string title)
    {
        string key = ArtworkService.CreateCacheKey(artist, title);
        _rawBytesCache.TryRemove(key, out _);
        _paletteCache.TryRemove(key, out _);
        _artworkService.Invalidate(artist, title);
    }

    public void Clear()
    {
        _rawBytesCache.Clear();
        _paletteCache.Clear();
        _artworkService.ClearCache();
    }
}
