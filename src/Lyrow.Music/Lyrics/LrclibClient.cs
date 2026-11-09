using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Music.Metadata;

namespace Lyrow.Music.Lyrics;

/// <summary>
/// Production REST API client for the open LRCLIB lyrics service (https://lrclib.net).
/// Implements resilient error handling for 404, 429 rate-limiting, 503 ServerOverloaded backoff,
/// in-flight deduplication, and in-memory caching.
/// </summary>
public sealed class LrclibClient : ILyricsClient
{
    private readonly HttpClient _httpClient;
    private readonly IMediaMetadataNormalizer _normalizer;
    private const string BaseApiUrl = "https://lrclib.net/api/get";

    private readonly ConcurrentDictionary<string, Task<LyricsSearchResult>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SyncedLyrics> _successCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DateTime ExpireUtc, LyricsSearchResult Result)> _negativeCache = new(StringComparer.OrdinalIgnoreCase);

    public LrclibClient(HttpClient? httpClient = null, IMediaMetadataNormalizer? normalizer = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _normalizer = normalizer ?? new MediaMetadataNormalizer();

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Lyrow/1.0 (https://github.com/owergungor/Lyrow)");
        }
        if (_httpClient.Timeout == TimeSpan.FromSeconds(100))
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(8);
        }
    }

    public Task<LyricsSearchResult> FetchLyricsAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        string rawTrack = (trackName ?? string.Empty).Trim();
        string rawArtist = (artistName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(rawTrack) || string.IsNullOrWhiteSpace(rawArtist))
        {
            return Task.FromResult(new LyricsSearchResult(false, null, "LRCLIB", "Track name or artist name is empty."));
        }

        // Deduplication & In-Flight Management
        string cacheKey = $"{rawTrack.ToLowerInvariant()}|{rawArtist.ToLowerInvariant()}";

        // 1. In-memory success cache
        if (_successCache.TryGetValue(cacheKey, out var cachedLyrics))
        {
            return Task.FromResult(new LyricsSearchResult(true, cachedLyrics, "LRCLIB"));
        }

        // 2. Short-lived negative cache for transient 503 errors (15s TTL)
        if (_negativeCache.TryGetValue(cacheKey, out var negEntry))
        {
            if (DateTime.UtcNow < negEntry.ExpireUtc)
            {
                return Task.FromResult(negEntry.Result);
            }
            _negativeCache.TryRemove(cacheKey, out _);
        }

        // 3. In-flight task coalescing (do not emit parallel duplicate HTTP requests)
        return _inFlight.GetOrAdd(cacheKey, _ => FetchLyricsInternalAsync(rawTrack, rawArtist, duration, cacheKey, cancellationToken));
    }

    private async Task<LyricsSearchResult> FetchLyricsInternalAsync(
        string rawTrack,
        string rawArtist,
        TimeSpan? duration,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var norm = _normalizer.Normalize(rawTrack, rawArtist, null);
            string searchTitle = norm.CleanSearchTitle;
            string searchArtist = norm.CleanSearchArtist;

            var result = await QueryLrclibWithFallbacksAsync(rawTrack, rawArtist, searchTitle, searchArtist, duration, cancellationToken).ConfigureAwait(false);

            if (result.Found && result.Lyrics != null)
            {
                _successCache[cacheKey] = result.Lyrics;
                _negativeCache.TryRemove(cacheKey, out _);
            }
            else if (result.IsRateLimited || (result.Error != null && result.Error.Contains("503")))
            {
                int ttlSeconds = result.RetryAfterSeconds > 0 ? Math.Clamp(result.RetryAfterSeconds, 5, 60) : 15;
                _negativeCache[cacheKey] = (DateTime.UtcNow.AddSeconds(ttlSeconds), result);
            }

            return result;
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
        }
    }

    private async Task<LyricsSearchResult> QueryLrclibWithFallbacksAsync(
        string rawTrack,
        string rawArtist,
        string cleanTrack,
        string cleanArtist,
        TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        // Attempt 1: Exact query with clean search title & artist and duration
        var query = $"?track_name={Uri.EscapeDataString(cleanTrack)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
        if (duration.HasValue && duration.Value.TotalSeconds > 1.0)
        {
            query += $"&duration={(int)Math.Round(duration.Value.TotalSeconds)}";
        }

        var (resp, status, retrySec) = await SendWithResilienceAsync(new Uri(BaseApiUrl + query), cancellationToken).ConfigureAwait(false);
        using (resp)
        {
            if (resp != null && resp.IsSuccessStatusCode)
            {
                var result = await ParseLrclibResponseAsync(resp, cleanTrack, cleanArtist, cancellationToken).ConfigureAwait(false);
                if (result.Found) return result;
            }

            if (status == HttpStatusCode.TooManyRequests)
            {
                return new LyricsSearchResult(false, null, "LRCLIB", $"Rate limited by LRCLIB API. {retrySec} saniye sonra tekrar denenecek.", IsRateLimited: true, RetryAfterSeconds: retrySec);
            }

            if ((int)status >= 500)
            {
                return new LyricsSearchResult(false, null, "LRCLIB", $"LRCLIB servisi şu anda yoğun (HTTP {(int)status}). Lütfen daha sonra tekrar deneyin.", RetryAfterSeconds: retrySec > 0 ? retrySec : 5);
            }

            // Fallback 1: If duration was included and returned 404, retry without duration filter
            if (status == HttpStatusCode.NotFound && duration.HasValue && duration.Value.TotalSeconds > 1.0)
            {
                var noDurQuery = $"?track_name={Uri.EscapeDataString(cleanTrack)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
                var (noDurResp, noDurStatus, noDurRetry) = await SendWithResilienceAsync(new Uri(BaseApiUrl + noDurQuery), cancellationToken).ConfigureAwait(false);
                using (noDurResp)
                {
                    if (noDurResp != null && noDurResp.IsSuccessStatusCode)
                    {
                        var result = await ParseLrclibResponseAsync(noDurResp, cleanTrack, cleanArtist, cancellationToken).ConfigureAwait(false);
                        if (result.Found) return result;
                    }
                    if ((int)noDurStatus >= 500)
                    {
                        return new LyricsSearchResult(false, null, "LRCLIB", $"LRCLIB servisi şu anda yoğun (HTTP {(int)noDurStatus}).", RetryAfterSeconds: noDurRetry > 0 ? noDurRetry : 5);
                    }
                }
            }

            // Fallback 2: If cleanTrack/cleanArtist differed from raw, retry with raw or broader stripped query
            if (status == HttpStatusCode.NotFound &&
                (!string.Equals(rawTrack, cleanTrack, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(rawArtist, cleanArtist, StringComparison.OrdinalIgnoreCase)))
            {
                string strippedTrack = StripTrackSuffixes(rawTrack);
                string primaryArtist = ExtractPrimaryArtist(rawArtist);

                if (!string.Equals(strippedTrack, cleanTrack, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(primaryArtist, cleanArtist, StringComparison.OrdinalIgnoreCase))
                {
                    var normQuery = $"?track_name={Uri.EscapeDataString(strippedTrack)}&artist_name={Uri.EscapeDataString(primaryArtist)}";
                    var (normResp, normStatus, normRetry) = await SendWithResilienceAsync(new Uri(BaseApiUrl + normQuery), cancellationToken).ConfigureAwait(false);
                    using (normResp)
                    {
                        if (normResp != null && normResp.IsSuccessStatusCode)
                        {
                            var result = await ParseLrclibResponseAsync(normResp, strippedTrack, primaryArtist, cancellationToken).ConfigureAwait(false);
                            if (result.Found) return result;
                        }
                    }
                }
            }

            // Fallback 3: Search using /api/search?track_name={cleanTrack}&artist_name={cleanArtist} or /api/search?q={query}
            var searchCandidates = await SearchLrclibCandidatesAsync(cleanTrack, cleanArtist, cancellationToken).ConfigureAwait(false);
            if (searchCandidates != null && searchCandidates.Count > 0)
            {
                var candidate = SelectBestSearchCandidate(searchCandidates, cleanTrack, cleanArtist, duration);
                if (candidate != null)
                {
                    if (!string.IsNullOrWhiteSpace(candidate.SyncedLyrics))
                    {
                        var parsed = LrcParser.Parse(candidate.SyncedLyrics, candidate.TrackName ?? cleanTrack, candidate.ArtistName ?? cleanArtist);
                        return new LyricsSearchResult(true, parsed, "LRCLIB");
                    }
                    if (!string.IsNullOrWhiteSpace(candidate.PlainLyrics))
                    {
                        var plain = new SyncedLyrics(
                            title: candidate.TrackName ?? cleanTrack,
                            artist: candidate.ArtistName ?? cleanArtist,
                            isSynced: false,
                            lines: Array.Empty<LyricLine>(),
                            plainLyrics: candidate.PlainLyrics);
                        return new LyricsSearchResult(true, plain, "LRCLIB");
                    }
                }
            }

            return new LyricsSearchResult(false, null, "LRCLIB", "Lyrics not found for track.");
        }
    }

    private async Task<List<LrclibResponse>?> SearchLrclibCandidatesAsync(
        string cleanTrack,
        string cleanArtist,
        CancellationToken cancellationToken)
    {
        // 1. Try field-specific search endpoint
        var fieldSearchUrl = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(cleanTrack)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
        var (searchResp, _, _) = await SendWithResilienceAsync(new Uri(fieldSearchUrl), cancellationToken).ConfigureAwait(false);
        using (searchResp)
        {
            if (searchResp != null && searchResp.IsSuccessStatusCode)
            {
                var searchStream = await searchResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var searchList = await JsonSerializer.DeserializeAsync<List<LrclibResponse>>(searchStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (searchList != null && searchList.Count > 0)
                {
                    return searchList;
                }
            }
        }

        // 2. Fallback to broad full-text query
        string broadQ = $"{cleanArtist} {cleanTrack}".Trim();
        if (!string.IsNullOrWhiteSpace(broadQ))
        {
            var broadSearchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(broadQ)}";
            var (broadResp, _, _) = await SendWithResilienceAsync(new Uri(broadSearchUrl), cancellationToken).ConfigureAwait(false);
            using (broadResp)
            {
                if (broadResp != null && broadResp.IsSuccessStatusCode)
                {
                    var broadStream = await broadResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    var broadList = await JsonSerializer.DeserializeAsync<List<LrclibResponse>>(broadStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (broadList != null && broadList.Count > 0)
                    {
                        return broadList;
                    }
                }
            }
        }

        return null;
    }

    private async Task<(HttpResponseMessage? Response, HttpStatusCode StatusCode, int RetryAfterSeconds)> SendWithResilienceAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 2; // Up to 3 total attempts
        HttpResponseMessage? lastResponse = null;
        HttpStatusCode lastStatus = HttpStatusCode.OK;
        int lastRetryAfter = 0;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                lastResponse?.Dispose();
                lastResponse = response;
                lastStatus = response.StatusCode;
                lastRetryAfter = ParseRetryAfter(response);

                // If success or non-retryable 4xx (except 429), return immediately
                if (response.IsSuccessStatusCode ||
                    ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500 && response.StatusCode != HttpStatusCode.TooManyRequests))
                {
                    return (response, lastStatus, lastRetryAfter);
                }

                // If retryable (429, 500, 502, 503, 504) and we still have attempts left:
                if (attempt < maxRetries && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
                {
                    int delayMs;
                    if (lastRetryAfter > 0)
                    {
                        delayMs = Math.Clamp(lastRetryAfter * 1000, 300, 3000);
                    }
                    else
                    {
                        // Exponential backoff with small random jitter
                        delayMs = (250 * (1 << attempt)) + Random.Shared.Next(50, 150);
                    }

                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return (response, lastStatus, lastRetryAfter);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Internal timeout on single request
                if (attempt < maxRetries)
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return (null, HttpStatusCode.RequestTimeout, 0);
            }
            catch (HttpRequestException)
            {
                if (attempt < maxRetries)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return (null, HttpStatusCode.ServiceUnavailable, 2);
            }
        }

        return (lastResponse, lastStatus, lastRetryAfter);
    }

    private static int ParseRetryAfter(HttpResponseMessage response)
    {
        try
        {
            if (response.Headers.RetryAfter != null)
            {
                if (response.Headers.RetryAfter.Delta.HasValue)
                {
                    return (int)Math.Max(1, response.Headers.RetryAfter.Delta.Value.TotalSeconds);
                }
                if (response.Headers.RetryAfter.Date.HasValue)
                {
                    var diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                    return (int)Math.Max(1, diff.TotalSeconds);
                }
            }
        }
        catch { }
        return 0;
    }

    private static async Task<LyricsSearchResult> ParseLrclibResponseAsync(
        HttpResponseMessage response,
        string fallbackTrack,
        string fallbackArtist,
        CancellationToken cancellationToken)
    {
        try
        {
            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var apiResponse = await JsonSerializer.DeserializeAsync<LrclibResponse>(contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (apiResponse == null)
            {
                return new LyricsSearchResult(false, null, "LRCLIB", "Failed to deserialize response.");
            }

            if (!string.IsNullOrWhiteSpace(apiResponse.SyncedLyrics))
            {
                var synced = LrcParser.Parse(apiResponse.SyncedLyrics, apiResponse.TrackName ?? fallbackTrack, apiResponse.ArtistName ?? fallbackArtist);
                return new LyricsSearchResult(true, synced, "LRCLIB");
            }

            if (!string.IsNullOrWhiteSpace(apiResponse.PlainLyrics))
            {
                var plain = new SyncedLyrics(
                    title: apiResponse.TrackName ?? fallbackTrack,
                    artist: apiResponse.ArtistName ?? fallbackArtist,
                    isSynced: false,
                    lines: Array.Empty<LyricLine>(),
                    plainLyrics: apiResponse.PlainLyrics);
                return new LyricsSearchResult(true, plain, "LRCLIB");
            }

            return new LyricsSearchResult(false, null, "LRCLIB", "No lyrics content found.");
        }
        catch (Exception ex)
        {
            return new LyricsSearchResult(false, null, "LRCLIB", $"Response parsing error: {ex.Message}");
        }
    }

    public static string StripTrackSuffixes(string title) => MediaMetadataNormalizer.StripTrackSuffixes(title);

    public static string ExtractPrimaryArtist(string artist) => MediaMetadataNormalizer.ExtractPrimaryArtist(artist);

    private static LrclibResponse? SelectBestSearchCandidate(
        List<LrclibResponse> candidates,
        string cleanTrack,
        string cleanArtist,
        TimeSpan? targetDuration)
    {
        if (candidates.Count == 0) return null;

        double targetSec = (targetDuration.HasValue && targetDuration.Value.TotalSeconds > 1.0)
            ? targetDuration.Value.TotalSeconds
            : 0.0;

        LrclibResponse? bestSynced = null;
        double bestSyncedScore = double.NegativeInfinity;

        LrclibResponse? bestPlain = null;
        double bestPlainScore = double.NegativeInfinity;

        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            bool hasSynced = !string.IsNullOrWhiteSpace(c.SyncedLyrics);
            bool hasPlain = !string.IsNullOrWhiteSpace(c.PlainLyrics);
            if (!hasSynced && !hasPlain) continue;

            double score = 0.0;
            string candTrack = StripTrackSuffixes(c.TrackName ?? string.Empty);
            string candArtist = ExtractPrimaryArtist(c.ArtistName ?? string.Empty);

            if (string.Equals(candTrack, cleanTrack, StringComparison.OrdinalIgnoreCase))
            {
                score += 40.0;
            }
            else if (candTrack.Contains(cleanTrack, StringComparison.OrdinalIgnoreCase) ||
                     cleanTrack.Contains(candTrack, StringComparison.OrdinalIgnoreCase))
            {
                score += 15.0;
            }

            if (string.Equals(candArtist, cleanArtist, StringComparison.OrdinalIgnoreCase))
            {
                score += 35.0;
            }
            else if (candArtist.Contains(cleanArtist, StringComparison.OrdinalIgnoreCase) ||
                     cleanArtist.Contains(candArtist, StringComparison.OrdinalIgnoreCase))
            {
                score += 15.0;
            }

            if (targetSec > 1.0 && c.Duration.HasValue && c.Duration.Value > 1.0)
            {
                double diffSec = Math.Abs(c.Duration.Value - targetSec);
                if (diffSec <= 2.5) score += 50.0;
                else if (diffSec <= 6.0) score += 25.0;
                else score -= Math.Min(60.0, diffSec * 1.5);
            }

            // Preserve original list order as a tiny tie-breaker
            score -= i * 0.001;

            if (hasSynced && score > bestSyncedScore)
            {
                bestSyncedScore = score;
                bestSynced = c;
            }
            else if (!hasSynced && hasPlain && score > bestPlainScore)
            {
                bestPlainScore = score;
                bestPlain = c;
            }
        }

        return bestSynced ?? bestPlain;
    }

    private sealed class LrclibResponse
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }

        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }

        [JsonPropertyName("albumName")]
        public string? AlbumName { get; set; }

        [JsonPropertyName("duration")]
        public double? Duration { get; set; }

        [JsonPropertyName("instrumental")]
        public bool? Instrumental { get; set; }

        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; set; }

        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; set; }
    }
}
