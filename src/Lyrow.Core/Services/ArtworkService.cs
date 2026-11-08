using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// High-performance thread-safe in-memory album cover and artwork caching service.
/// Automatically upgrades low-resolution browser GSMTC thumbnails (e.g. YouTube Music Web / Apple Music Web)
/// to high-resolution 600x600+ album covers while preserving native high-res desktop artwork (e.g. Spotify Desktop).
/// </summary>
public sealed class ArtworkService : IArtworkService
{
    public const int MaxCachedItems = 50;
    public const int LowResThumbnailByteThreshold = 18_000;
    public const int MinHighResDimension = 260;

    private static readonly HttpClient SharedHttpClient = CreateDefaultHttpClient();
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ArtworkService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        try
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Lyrow/3.7 (https://github.com/owergungor/Lyrow)");
        }
        catch { }
        return client;
    }

    public int CachedCount => _cache.Count;

    public static string CreateCacheKey(string artist, string title)
    {
        return $"{artist.Trim()}_{title.Trim()}";
    }

    public async Task<byte[]?> GetArtworkBytesAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        if (track.IsEmpty)
        {
            return null;
        }

        string key = CreateCacheKey(track.Artist, track.Title);
        if (_cache.TryGetValue(key, out var cachedBytes) && cachedBytes != null && !IsLowResolutionThumbnail(cachedBytes))
        {
            return cachedBytes;
        }

        byte[]? gsmtcBytes = null;
        if (track.ThumbnailData != null && track.ThumbnailData.Length > 0)
        {
            if (!IsValidImageData(track.ThumbnailData))
            {
                LocalDiagnosticLogger.LogWarning($"[ArtworkService] Rejected invalid or corrupt GSMTC thumbnail data ({track.ThumbnailData.Length} bytes) for '{track.Artist} - {track.Title}'.");
                return cachedBytes;
            }

            gsmtcBytes = track.ThumbnailData;
            if (!IsLowResolutionThumbnail(gsmtcBytes))
            {
                _cache[key] = gsmtcBytes;
                PruneCacheIfExceeded(key);
                return gsmtcBytes;
            }
        }

        // If track provides a direct AlbumArtUri (e.g. from web session metadata), try downloading its high-res variant first.
        if (gsmtcBytes == null && !string.IsNullOrWhiteSpace(track.AlbumArtUri))
        {
            var uriBytes = await TryDownloadDirectArtworkUriAsync(track.AlbumArtUri, ct).ConfigureAwait(false);
            if (uriBytes != null && IsValidImageData(uriBytes))
            {
                if (!IsLowResolutionThumbnail(uriBytes))
                {
                    _cache[key] = uriBytes;
                    PruneCacheIfExceeded(key);
                    return uriBytes;
                }
                gsmtcBytes = uriBytes;
            }
        }

        // When GSMTC thumbnail is missing (common on YouTube Music Web / Apple Music Web) OR is a low-resolution
        // browser thumbnail (96x96 / 120x120), fetch verified 600x600+ artwork from iTunes Search API.
        var highResBytes = await TryFetchHighResOnlineArtworkAsync(track.Artist, track.Title, track.Album, ct).ConfigureAwait(false);
        if (highResBytes != null && IsValidImageData(highResBytes) && (gsmtcBytes == null || highResBytes.Length >= gsmtcBytes.Length))
        {
            _cache[key] = highResBytes;
            PruneCacheIfExceeded(key);
            return highResBytes;
        }

        if (gsmtcBytes != null)
        {
            _cache[key] = gsmtcBytes;
            PruneCacheIfExceeded(key);
            return gsmtcBytes;
        }

        return cachedBytes;
    }

    /// <summary>
    /// Determines whether an image byte buffer is a low-resolution browser thumbnail (e.g. &lt; 260x260 px or tiny payload)
    /// that should be upgraded to high-resolution album artwork.
    /// </summary>
    public static bool IsLowResolutionThumbnail(byte[]? data)
    {
        if (data == null || data.Length == 0) return true;
        if (TryGetImageDimensions(data, out int width, out int height))
        {
            if (width > 0 && height > 0)
            {
                return width < MinHighResDimension || height < MinHighResDimension;
            }
        }

        // Only treat unrecognized headers as low-res if they are real image streams (JPEG/PNG/GIF/WebP) smaller than threshold
        bool hasImageMagic = data.Length >= 12 &&
            ((data[0] == 0xFF && data[1] == 0xD8) ||
             (data[0] == 0x89 && data[1] == 0x50) ||
             (data[0] == 0x47 && data[1] == 0x49) ||
             (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'));

        return hasImageMagic && data.Length < LowResThumbnailByteThreshold;
    }

    /// <summary>
    /// Upgrades a known media provider artwork URL (Apple Music / iTunes, YouTube / GoogleUserContent) to its high-resolution 600x600+ variant.
    /// </summary>
    public static string UpgradeArtworkUrlToHighRes(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        string upgraded = url.Trim();

        // Apple Music / mzstatic URLs: replace 100x100bb, 60x60bb, 120x120bb, etc. with 600x600bb
        upgraded = System.Text.RegularExpressions.Regex.Replace(
            upgraded,
            @"/\d{2,3}x\d{2,3}(bb|cc|sr|bf)?\.(jpg|jpeg|png|webp)",
            "/600x600bb.$2",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Google / YouTube Music lh3.googleusercontent.com URLs: replace =w60-h60... or =w120-h120... with =w600-h600-l90-rj
        upgraded = System.Text.RegularExpressions.Regex.Replace(
            upgraded,
            @"=w\d+-h\d+[^&\s]*$",
            "=w600-h600-l90-rj",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return upgraded;
    }

    /// <summary>
    /// Inspects PNG, JPEG, GIF, or WebP headers to determine whether the image is an animated multi-frame stream.
    /// Static images return false so Lyrow never fabricates fake animations for static covers.
    /// </summary>
    public static bool IsAnimatedImageData(byte[]? data)
    {
        if (data == null || data.Length < 16) return false;

        // 1. Animated GIF (GIF89a / GIF87a with multiple Graphic Control Extensions or multiple Image Descriptors)
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
        {
            int gceCount = 0;
            int imgDescCount = 0;
            for (int i = 6; i < data.Length - 9; i++)
            {
                if (data[i] == 0x21 && data[i + 1] == 0xF9 && data[i + 2] == 0x04)
                {
                    gceCount++;
                    if (gceCount >= 2) return true;
                }
                else if (data[i] == 0x2C && (data[i + 5] != 0 || data[i + 6] != 0) && (data[i + 7] != 0 || data[i + 8] != 0))
                {
                    imgDescCount++;
                    if (imgDescCount >= 2) return true;
                }
            }
            return false;
        }

        // 2. Animated WebP (RIFF .... WEBP VP8X with Animation flag 0x02 at byte 20 or ANIM chunk)
        if (data.Length >= 24 &&
            data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
            data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            if (data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == 'X')
            {
                bool hasAnimBit = (data[20] & 0x02) != 0;
                if (hasAnimBit) return true;
            }
        }

        // 3. Animated PNG (APNG with acTL chunk before IDAT)
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            int limit = Math.Min(data.Length - 4, 256);
            for (int i = 8; i < limit; i++)
            {
                if (data[i] == 'a' && data[i + 1] == 'c' && data[i + 2] == 'T' && data[i + 3] == 'L')
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts pixel width and height directly from PNG, GIF, BMP, or JPEG headers without GDI/WIC overhead.
    /// </summary>
    public static bool TryGetImageDimensions(byte[]? data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data == null || data.Length < 24) return false;

        // PNG: IHDR at offset 16 (4-byte big-endian width, 4-byte big-endian height)
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return width > 0 && height > 0;
        }

        // GIF: offset 6 (2-byte little-endian width, 2-byte little-endian height)
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
        {
            width = data[6] | (data[7] << 8);
            height = data[8] | (data[9] << 8);
            return width > 0 && height > 0;
        }

        // JPEG: scan SOF0/SOF2 markers (FF C0 / FF C2)
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            int idx = 2;
            while (idx + 9 < data.Length)
            {
                if (data[idx] != 0xFF)
                {
                    idx++;
                    continue;
                }

                byte marker = data[idx + 1];
                if (marker == 0xFF)
                {
                    idx++;
                    continue;
                }

                if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
                {
                    idx += 2;
                    continue;
                }

                int segLen = (data[idx + 2] << 8) | data[idx + 3];
                if (segLen < 2) break;

                if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2 || marker == 0xC3)
                {
                    height = (data[idx + 5] << 8) | data[idx + 6];
                    width = (data[idx + 7] << 8) | data[idx + 8];
                    return width > 0 && height > 0;
                }

                idx += 2 + segLen;
            }
        }

        // WebP: RIFF .... WEBP (VP8X / VP8L / VP8 )
        if (data.Length >= 25 &&
            data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
            data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            if (data.Length >= 30 && data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == 'X')
            {
                width = 1 + (data[24] | (data[25] << 8) | (data[26] << 16));
                height = 1 + (data[27] | (data[28] << 8) | (data[29] << 16));
                return width > 0 && height > 0;
            }
            if (data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == 'L' && data[20] == 0x2F)
            {
                uint bits = (uint)(data[21] | (data[22] << 8) | (data[23] << 16) | (data[24] << 24));
                width = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
                return width > 0 && height > 0;
            }
            if (data.Length >= 30 && data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == ' ' &&
                data[23] == 0x9D && data[24] == 0x01 && data[25] == 0x2A)
            {
                width = (data[26] | (data[27] << 8)) & 0x3FFF;
                height = (data[28] | (data[29] << 8)) & 0x3FFF;
                return width > 0 && height > 0;
            }
        }

        return false;
    }

    private async Task<byte[]?> TryDownloadDirectArtworkUriAsync(string albumArtUri, CancellationToken ct)
    {
        try
        {
            string upgradedUrl = UpgradeArtworkUrlToHighRes(albumArtUri);
            if (!Uri.TryCreate(upgradedUrl, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            return await _httpClient.GetByteArrayAsync(parsed, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogWarning($"[ArtworkService] Direct AlbumArtUri download failed for '{albumArtUri}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Cleans browser/YouTube/Apple Music Web noise (e.g. "- Topic", "(Official Video)", "[Official Audio]")
    /// before querying online artwork providers.
    /// </summary>
    public static (string CleanArtist, string CleanTitle) CleanSearchMetadataForArtwork(string? artist, string? title)
    {
        string a = (artist ?? string.Empty).Trim();
        string t = (title ?? string.Empty).Trim();

        if (a.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
        {
            a = a[..^8].Trim();
        }
        else if (a.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase) && a.Length > 4)
        {
            a = a[..^4].Trim();
        }

        int bulletIdx = a.IndexOf('•');
        if (bulletIdx > 0)
        {
            a = a[..bulletIdx].Trim();
        }

        // Remove common YouTube Music / video suffixes from title
        t = Regex.Replace(
            t,
            @"\s*[\(\[]\s*(official\s+(music\s+)?video|official\s+audio|official\s+lyric\s+video|lyric\s+video|lyrics|visualizer|audio|hd|4k|remastered(\s+\d{4})?)\s*[\)\]]",
            string.Empty,
            RegexOptions.IgnoreCase).Trim();

        // If artist is embedded in title ("Artist - Title") and artist is empty or repeated
        int dashIdx = t.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx > 0)
        {
            string prefix = t[..dashIdx].Trim();
            string suffix = t[(dashIdx + 3)..].Trim();
            if (string.IsNullOrWhiteSpace(a) || string.Equals(a, prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(a)) a = prefix;
                t = suffix;
            }
        }

        return (a, t);
    }

    public static bool IsPlaceholderOrSyntheticTrack(string artist, string title)
    {
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) return true;

        if (artist.StartsWith("Artist", StringComparison.OrdinalIgnoreCase) &&
            title.StartsWith("Track", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(artist, "Unknown Artist", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(title, "Unknown Track", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(title, "Unknown Title", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(artist, "Faulty Artist", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(title, "Broken Cover", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(artist, "Test Artist", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsMatchingArtworkCandidate(string queryArtist, string queryTitle, string? candidateArtist, string? candidateTitle, string? candidateCollection)
    {
        string normQueryTitle = NormalizeToken(queryTitle);
        string normCandTitle = NormalizeToken(candidateTitle);
        string normCandCollection = NormalizeToken(candidateCollection);

        bool titleMatches = !string.IsNullOrEmpty(normQueryTitle) &&
            ((!string.IsNullOrEmpty(normCandTitle) &&
              (normCandTitle.Contains(normQueryTitle, StringComparison.OrdinalIgnoreCase) ||
               normQueryTitle.Contains(normCandTitle, StringComparison.OrdinalIgnoreCase))) ||
             (!string.IsNullOrEmpty(normCandCollection) &&
              (normCandCollection.Contains(normQueryTitle, StringComparison.OrdinalIgnoreCase) ||
               normQueryTitle.Contains(normCandCollection, StringComparison.OrdinalIgnoreCase))));

        if (!titleMatches) return false;

        string normQueryArtist = NormalizeToken(queryArtist);
        if (string.IsNullOrEmpty(normQueryArtist)) return true;

        string normCandArtist = NormalizeToken(candidateArtist);
        if (string.IsNullOrEmpty(normCandArtist)) return false;

        return normCandArtist.Contains(normQueryArtist, StringComparison.OrdinalIgnoreCase) ||
               normQueryArtist.Contains(normCandArtist, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var chars = input.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }

    private async Task<byte[]?> TryFetchHighResOnlineArtworkAsync(string artist, string title, string? album, CancellationToken ct)
    {
        var (cleanArtist, cleanTitle) = CleanSearchMetadataForArtwork(artist, title);
        if (IsPlaceholderOrSyntheticTrack(cleanArtist, cleanTitle))
        {
            return null;
        }

        try
        {
            string queryTerm = $"{cleanArtist} {cleanTitle}".Trim();
            if (string.IsNullOrWhiteSpace(queryTerm)) return null;

            string url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(queryTerm)}&media=music&entity=song&limit=5";

            using var resp = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                LocalDiagnosticLogger.LogWarning($"[ArtworkService] Online artwork lookup returned HTTP {(int)resp.StatusCode} for '{queryTerm}'.");
                return null;
            }

            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                return null;
            }

            string? bestArtUrl = null;
            foreach (var item in results.EnumerateArray())
            {
                string? candArtist = item.TryGetProperty("artistName", out var aProp) ? aProp.GetString() : null;
                string? candTrack = item.TryGetProperty("trackName", out var tProp) ? tProp.GetString() : null;
                string? candCollection = item.TryGetProperty("collectionName", out var cProp) ? cProp.GetString() : null;

                if (!IsMatchingArtworkCandidate(cleanArtist, cleanTitle, candArtist, candTrack, candCollection))
                {
                    continue;
                }

                if (item.TryGetProperty("artworkUrl100", out var artProp))
                {
                    string? rawArt = artProp.GetString();
                    if (!string.IsNullOrWhiteSpace(rawArt))
                    {
                        bestArtUrl = UpgradeArtworkUrlToHighRes(rawArt);
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(bestArtUrl)) return null;

            return await _httpClient.GetByteArrayAsync(bestArtUrl, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogWarning($"[ArtworkService] Online artwork fetch error for '{cleanArtist} - {cleanTitle}': {ex.Message}");
            return null;
        }
    }

    public void CacheArtwork(string artist, string title, byte[] artworkBytes)
    {
        if (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) return;
        if (artworkBytes == null || artworkBytes.Length == 0 || !IsValidImageData(artworkBytes)) return;

        string key = CreateCacheKey(artist, title);
        _cache[key] = artworkBytes;
        PruneCacheIfExceeded(key);
    }

    private void PruneCacheIfExceeded(string retainKey)
    {
        if (_cache.Count <= MaxCachedItems) return;
        int toRemove = _cache.Count - MaxCachedItems;
        foreach (var oldKey in _cache.Keys)
        {
            if (toRemove <= 0) break;
            if (!string.Equals(oldKey, retainKey, StringComparison.OrdinalIgnoreCase))
            {
                if (_cache.TryRemove(oldKey, out _))
                {
                    toRemove--;
                }
            }
        }
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
