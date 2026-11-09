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
    public const int MaxCacheEntries = 50;
    private readonly IArtworkService _artworkService;
    private readonly ConcurrentDictionary<string, byte[]> _rawBytesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, GlowPalette> _paletteCache = new(StringComparer.OrdinalIgnoreCase);

    public ArtworkBitmapManager(IArtworkService artworkService)
    {
        _artworkService = artworkService;
    }

    /// <summary>
    /// Asynchronously retrieves or decodes the album artwork BitmapImage for a track.
    /// IMPORTANT: Never use ConfigureAwait(false) inside this method because WinUI 3 BitmapImage
    /// must be instantiated and bound on the UI Dispatcher thread (avoiding 0x8001010E RPC_E_WRONG_THREAD).
    /// </summary>
    public async Task<BitmapImage?> GetArtworkBitmapAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        if (track.IsEmpty) return null;

        string key = ArtworkService.CreateCacheKey(track.Artist, track.Title);

        byte[]? rawBytes = track.ThumbnailData;
        if (rawBytes != null && rawBytes.Length > 0 && ArtworkService.IsValidImageData(rawBytes))
        {
            if (ArtworkService.IsLowResolutionThumbnail(rawBytes) || ArtworkService.IsWebpImageData(rawBytes))
            {
                // Attempt to upgrade low-resolution or WebP browser thumbnail (e.g. YouTube Music Web / Apple Music Web)
                var upgraded = await _artworkService.GetArtworkBytesAsync(track, ct);
                if (upgraded != null && upgraded.Length > 0 && ArtworkService.IsValidImageData(upgraded) &&
                    (ArtworkService.IsWebpImageData(rawBytes) || upgraded.Length >= rawBytes.Length))
                {
                    rawBytes = upgraded;
                }
            }

            // If we have a cached version with greater or equal byte size, avoid overwriting with a lower-res thumbnail
            if (!_rawBytesCache.TryGetValue(key, out var existing) || existing == null || existing.Length <= rawBytes.Length)
            {
                _rawBytesCache[key] = rawBytes;
                PruneCache(_rawBytesCache, key);
                _artworkService.CacheArtwork(track.Artist, track.Title, rawBytes);
            }
            else
            {
                rawBytes = existing;
            }
        }
        else if (!_rawBytesCache.TryGetValue(key, out rawBytes) || rawBytes == null || rawBytes.Length == 0)
        {
            rawBytes = await _artworkService.GetArtworkBytesAsync(track, ct);
            if (rawBytes != null && rawBytes.Length > 0)
            {
                _rawBytesCache[key] = rawBytes;
                PruneCache(_rawBytesCache, key);
            }
        }

        if (rawBytes == null || rawBytes.Length == 0)
        {
            return null;
        }

        try
        {
            byte[] bytesToDecode = rawBytes;
            if (ArtworkService.IsWebpImageData(bytesToDecode))
            {
                var pngBytes = await TranscodeToPngBytesAsync(bytesToDecode);
                if (pngBytes != null && pngBytes.Length > 0)
                {
                    bytesToDecode = pngBytes;
                    _rawBytesCache[key] = pngBytes;
                    _artworkService.CacheArtwork(track.Artist, track.Title, pngBytes);
                }
            }

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytesToDecode);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);

            bool animationsEnabled = AreSystemAnimationsEnabled();
            var image = new BitmapImage
            {
                AutoPlay = animationsEnabled
            };

            try
            {
                await image.SetSourceAsync(stream);
            }
            catch (Exception decodeEx)
            {
                // Fallback for browser WebP / custom frames: transcode via WIC BitmapDecoder -> PNG bytes on background thread
                LocalDiagnosticLogger.LogWarning($"[ArtworkBitmapManager] Direct BitmapImage.SetSourceAsync failed ({decodeEx.GetType().Name} 0x{decodeEx.HResult:X8}: {decodeEx.Message}); attempting WIC PNG transcode for '{track.Artist} - {track.Title}'.");
                var fallbackPng = await TranscodeToPngBytesAsync(rawBytes);
                if (fallbackPng == null || fallbackPng.Length == 0)
                {
                    throw;
                }

                using var pngStream = new InMemoryRandomAccessStream();
                using (var pngWriter = new DataWriter(pngStream.GetOutputStreamAt(0)))
                {
                    pngWriter.WriteBytes(fallbackPng);
                    await pngWriter.StoreAsync();
                    pngWriter.DetachStream();
                }
                pngStream.Seek(0);
                await image.SetSourceAsync(pngStream);
            }

            if (!animationsEnabled)
            {
                try { image.Stop(); } catch { }
            }
            return image;
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogWarning($"[ArtworkBitmapManager] Failed to decode artwork bitmap for '{track.Artist} - {track.Title}' ({ex.GetType().Name} 0x{ex.HResult:X8}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Transcodes any WIC-supported image buffer (including browser WebP thumbnails) into standard PNG bytes
    /// on a background thread so WinUI 3 BitmapImage can decode it without format or thread-affinity issues.
    /// </summary>
    public static async Task<byte[]?> TranscodeToPngBytesAsync(byte[]? rawBytes)
    {
        if (rawBytes == null || rawBytes.Length == 0) return null;

        return await Task.Run(async () =>
        {
            try
            {
                using var inputStream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(inputStream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(rawBytes);
                    await writer.StoreAsync();
                    writer.DetachStream();
                }
                inputStream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(inputStream);
                using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                using var pngStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, pngStream);
                encoder.SetSoftwareBitmap(softwareBitmap);
                await encoder.FlushAsync();
                pngStream.Seek(0);

                uint size = (uint)pngStream.Size;
                if (size == 0) return null;
                byte[] output = new byte[size];
                using var reader = new DataReader(pngStream.GetInputStreamAt(0));
                await reader.LoadAsync(size);
                reader.ReadBytes(output);
                reader.DetachStream();
                return output;
            }
            catch
            {
                return null;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks the Windows OS user preference for animations (Reduced Motion).
    /// If user has enabled Reduced Motion, animations are disabled and artwork freezes on first frame.
    /// </summary>
    public static bool AreSystemAnimationsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            }
        }
        catch { }
        return true;
    }

    public async Task<byte[]?> GetArtworkBytesAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        if (track.IsEmpty) return null;

        string key = ArtworkService.CreateCacheKey(track.Artist, track.Title);
        if (track.ThumbnailData != null && track.ThumbnailData.Length > 0 && ArtworkService.IsValidImageData(track.ThumbnailData))
        {
            if (!_rawBytesCache.TryGetValue(key, out var existing) || existing == null || existing.Length <= track.ThumbnailData.Length)
            {
                _rawBytesCache[key] = track.ThumbnailData;
                return track.ThumbnailData;
            }
            return existing;
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
        if (track.ThumbnailData != null && track.ThumbnailData.Length > 0 && ArtworkService.IsValidImageData(track.ThumbnailData))
        {
            _rawBytesCache[key] = track.ThumbnailData;
            _artworkService.CacheArtwork(track.Artist, track.Title, track.ThumbnailData);
        }

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
                    writer.DetachStream();
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

        if (palette != GlowPalette.Default)
        {
            _paletteCache[key] = palette;
            PruneCache(_paletteCache, key);
        }
        return palette;
    }

    private static void PruneCache<T>(ConcurrentDictionary<string, T> dict, string retainKey)
    {
        if (dict.Count <= MaxCacheEntries) return;
        int toRemove = dict.Count - MaxCacheEntries;
        foreach (var k in dict.Keys)
        {
            if (toRemove <= 0) break;
            if (!string.Equals(k, retainKey, StringComparison.OrdinalIgnoreCase))
            {
                if (dict.TryRemove(k, out _)) toRemove--;
            }
        }
    }

    public static int ComputeArtworkSignature(MediaTrackInfo track)
    {
        if (track.IsEmpty) return 0;
        int len = track.ThumbnailData?.Length ?? 0;
        int firstByte = (len > 0) ? track.ThumbnailData![0] : 0;
        int lastByte = (len > 1) ? track.ThumbnailData![len - 1] : 0;
        return HashCode.Combine(len, firstByte, lastByte, track.AlbumArtUri ?? string.Empty);
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
