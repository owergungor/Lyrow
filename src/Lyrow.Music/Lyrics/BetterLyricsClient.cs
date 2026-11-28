using System.Collections.Concurrent;
using System.Text.Json;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Music.Metadata;

namespace Lyrow.Music.Lyrics;

/// <summary>
/// Live REST client for Better Lyrics (https://lyrics-api.boidu.dev/getLyrics),
/// returning Apple Music TTML with real syllable, word, and line timestamps when available.
/// </summary>
public sealed class BetterLyricsClient : ILyricsSyncProvider
{
    public const string DefaultApiUrl = "https://lyrics-api.boidu.dev/getLyrics";

    private readonly HttpClient _httpClient;
    private readonly IMediaMetadataNormalizer _normalizer;
    private readonly string _endpointUrl;
    private readonly ConcurrentDictionary<string, LyricsTimingData> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<LyricsTimingData?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public BetterLyricsClient(
        HttpClient? httpClient = null,
        IMediaMetadataNormalizer? normalizer = null,
        string? endpointUrl = null,
        TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = timeout ?? TimeSpan.FromMilliseconds(2800) };
        _normalizer = normalizer ?? new MediaMetadataNormalizer();
        _endpointUrl = string.IsNullOrWhiteSpace(endpointUrl) ? DefaultApiUrl : endpointUrl;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Lyrow/5.0 (https://github.com/owergungor/Lyrow)");
        }
    }

    public LyricsProviderId ProviderId => LyricsProviderId.BetterLyrics;

    public async Task<LyricsTimingData?> FetchTimingDataAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        string rawTrack = (trackName ?? string.Empty).Trim();
        string rawArtist = (artistName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawTrack) || string.IsNullOrWhiteSpace(rawArtist)) return null;

        string key = $"{rawTrack.ToLowerInvariant()}|{rawArtist.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var task = _inFlight.GetOrAdd(key, _ => FetchCoreAsync(key, rawTrack, rawArtist));
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<LyricsTimingData?> FetchCoreAsync(string key, string rawTrack, string rawArtist)
    {
        try
        {
            var norm = _normalizer.Normalize(rawTrack, rawArtist, null);
            string qSong = string.IsNullOrWhiteSpace(norm.CleanSearchTitle) ? rawTrack : norm.CleanSearchTitle;
            string qArtist = string.IsNullOrWhiteSpace(norm.CleanSearchArtist) ? rawArtist : norm.CleanSearchArtist;

            string url = $"{_endpointUrl}?a={Uri.EscapeDataString(qArtist)}&s={Uri.EscapeDataString(qSong)}";
            using var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return null;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("ttml", out var ttmlProp)) return null;

            string? ttml = ttmlProp.GetString();
            if (string.IsNullOrWhiteSpace(ttml)) return null;

            var parsed = TtmlLyricsParser.Parse(ttml, qSong, qArtist, LyricsProviderId.BetterLyrics);
            if (parsed.ActualGranularity == LyricsTimingGranularity.None) return null;

            _cache[key] = parsed;
            return parsed;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }
}

/// <summary>
/// Provider adapter for Better Lyrics Unison (https://unison.betterlyrics.org/lyrics).
/// Handles HTTP 400/404 gracefully when artist+song queries lack a YouTube videoId.
/// </summary>
public sealed class BetterLyricsUnisonClient : ILyricsSyncProvider
{
    public const string DefaultApiUrl = "https://unison.betterlyrics.org/lyrics";

    private readonly HttpClient _httpClient;
    private readonly IMediaMetadataNormalizer _normalizer;
    private readonly string _endpointUrl;
    private readonly ConcurrentDictionary<string, LyricsTimingData> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<LyricsTimingData?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public BetterLyricsUnisonClient(
        HttpClient? httpClient = null,
        IMediaMetadataNormalizer? normalizer = null,
        string? endpointUrl = null,
        TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = timeout ?? TimeSpan.FromMilliseconds(2200) };
        _normalizer = normalizer ?? new MediaMetadataNormalizer();
        _endpointUrl = string.IsNullOrWhiteSpace(endpointUrl) ? DefaultApiUrl : endpointUrl;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Lyrow/5.0 (https://github.com/owergungor/Lyrow)");
        }
    }

    public LyricsProviderId ProviderId => LyricsProviderId.BetterLyricsUnison;

    public async Task<LyricsTimingData?> FetchTimingDataAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        string rawTrack = (trackName ?? string.Empty).Trim();
        string rawArtist = (artistName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawTrack) || string.IsNullOrWhiteSpace(rawArtist)) return null;

        string key = $"{rawTrack.ToLowerInvariant()}|{rawArtist.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var task = _inFlight.GetOrAdd(key, _ => FetchCoreAsync(key, rawTrack, rawArtist));
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<LyricsTimingData?> FetchCoreAsync(string key, string rawTrack, string rawArtist)
    {
        try
        {
            var norm = _normalizer.Normalize(rawTrack, rawArtist, null);
            string qSong = string.IsNullOrWhiteSpace(norm.CleanSearchTitle) ? rawTrack : norm.CleanSearchTitle;
            string qArtist = string.IsNullOrWhiteSpace(norm.CleanSearchArtist) ? rawArtist : norm.CleanSearchArtist;

            string url = $"{_endpointUrl}?a={Uri.EscapeDataString(qArtist)}&s={Uri.EscapeDataString(qSong)}";
            using var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            string payload = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(payload)) return null;

            LyricsTimingData? parsed = null;
            if (payload.TrimStart().StartsWith("{"))
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty("ttml", out var ttmlProp) && !string.IsNullOrWhiteSpace(ttmlProp.GetString()))
                {
                    parsed = TtmlLyricsParser.Parse(ttmlProp.GetString(), qSong, qArtist, LyricsProviderId.BetterLyricsUnison);
                }
                else if (doc.RootElement.TryGetProperty("lyrics", out var plainProp) && !string.IsNullOrWhiteSpace(plainProp.GetString()))
                {
                    parsed = new LyricsTimingData(qSong, qArtist, LyricsProviderId.BetterLyricsUnison, LyricsTimingGranularity.PlainLyrics, Array.Empty<LyricLineTiming>(), plainProp.GetString());
                }
            }
            else if (payload.TrimStart().StartsWith("<"))
            {
                parsed = TtmlLyricsParser.Parse(payload, qSong, qArtist, LyricsProviderId.BetterLyricsUnison);
            }

            if (parsed != null && parsed.ActualGranularity != LyricsTimingGranularity.None)
            {
                _cache[key] = parsed;
                return parsed;
            }

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }
}

/// <summary>
/// Wraps the existing production <see cref="LrclibClient"/> as a capability-aware <see cref="ILyricsSyncProvider"/>.
/// </summary>
public sealed class LrclibSyncProvider : ILyricsSyncProvider
{
    private readonly ILyricsClient _lrclibClient;

    public LrclibSyncProvider(ILyricsClient lrclibClient)
    {
        _lrclibClient = lrclibClient ?? throw new ArgumentNullException(nameof(lrclibClient));
    }

    public LyricsProviderId ProviderId => LyricsProviderId.Lrclib;

    public async Task<LyricsTimingData?> FetchTimingDataAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _lrclibClient.FetchLyricsAsync(trackName, artistName, duration, cancellationToken).ConfigureAwait(false);
        if (!result.Found || result.Lyrics == null) return null;

        return LyricsTimingData.FromSyncedLyrics(result.Lyrics, LyricsProviderId.Lrclib);
    }
}

/// <summary>
/// Honest, non-fabricating adapter for providers without a documented unauthenticated public desktop endpoint
/// (BiniLyrics, Better Lyrics Portato, Better Lyrics Legato). Returns null cleanly so the priority resolver
/// advances to the next available provider without ever inventing timing data.
/// </summary>
public sealed class UnconfiguredSyncProvider : ILyricsSyncProvider
{
    public UnconfiguredSyncProvider(LyricsProviderId providerId)
    {
        ProviderId = providerId;
    }

    public LyricsProviderId ProviderId { get; }

    public Task<LyricsTimingData?> FetchTimingDataAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<LyricsTimingData?>(null);
}
