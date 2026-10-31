using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Music.Lyrics;

/// <summary>
/// Production REST API client for the open LRCLIB lyrics service (https://lrclib.net).
/// Implements resilient error handling for 404, 429 rate-limiting, and network timeouts.
/// </summary>
public sealed class LrclibClient : ILyricsClient
{
    private readonly HttpClient _httpClient;
    private const string BaseApiUrl = "https://lrclib.net/api/get";

    public LrclibClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Lyrow/1.0 (https://github.com/owergungor/Lyrow)");
        }
        if (_httpClient.Timeout == TimeSpan.FromSeconds(100)) // Default timeout
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(8); // Snappy timeout for desktop UI responsiveness
        }
    }

    public async Task<LyricsSearchResult> FetchLyricsAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        string cleanTrack = (trackName ?? string.Empty).Trim();
        string cleanArtist = (artistName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(cleanTrack) || string.IsNullOrWhiteSpace(cleanArtist))
        {
            return new LyricsSearchResult(false, null, "LRCLIB", "Track name or artist name is empty.");
        }

        try
        {
            // Build query parameters with safe URL encoding
            var query = $"?track_name={Uri.EscapeDataString(cleanTrack)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
            if (duration.HasValue && duration.Value.TotalSeconds > 1.0)
            {
                query += $"&duration={(int)Math.Round(duration.Value.TotalSeconds)}";
            }

            var requestUri = new Uri(BaseApiUrl + query);
            using var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Fallback 1: If duration was included in query and failed with 404, retry without duration filter
                // in case streaming service/GSMTC duration differs slightly from LRCLIB database entry.
                if (duration.HasValue && duration.Value.TotalSeconds > 1.0)
                {
                    var fallbackQuery = $"?track_name={Uri.EscapeDataString(cleanTrack)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
                    using var fallbackResp = await _httpClient.GetAsync(new Uri(BaseApiUrl + fallbackQuery), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (fallbackResp.IsSuccessStatusCode)
                    {
                        var fallbackStream = await fallbackResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        var fallbackModel = await JsonSerializer.DeserializeAsync<LrclibResponse>(fallbackStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (fallbackModel != null && !string.IsNullOrWhiteSpace(fallbackModel.SyncedLyrics))
                        {
                            var parsed = LrcParser.Parse(fallbackModel.SyncedLyrics, fallbackModel.TrackName ?? cleanTrack, fallbackModel.ArtistName ?? cleanArtist);
                            return new LyricsSearchResult(true, parsed, "LRCLIB");
                        }
                        if (fallbackModel != null && !string.IsNullOrWhiteSpace(fallbackModel.PlainLyrics))
                        {
                            var plain = new SyncedLyrics(
                                title: fallbackModel.TrackName ?? cleanTrack,
                                artist: fallbackModel.ArtistName ?? cleanArtist,
                                isSynced: false,
                                lines: Array.Empty<LyricLine>(),
                                plainLyrics: fallbackModel.PlainLyrics);
                            return new LyricsSearchResult(true, plain, "LRCLIB");
                        }
                    }
                }

                // Fallback 2: Strip common streaming suffixes (e.g. " - Remastered", " (feat. ...)") if present
                string strippedTrack = StripTrackSuffixes(cleanTrack);
                string primaryArtist = ExtractPrimaryArtist(cleanArtist);
                if (!string.Equals(strippedTrack, cleanTrack, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(primaryArtist, cleanArtist, StringComparison.OrdinalIgnoreCase))
                {
                    var normQuery = $"?track_name={Uri.EscapeDataString(strippedTrack)}&artist_name={Uri.EscapeDataString(primaryArtist)}";
                    using var normResp = await _httpClient.GetAsync(new Uri(BaseApiUrl + normQuery), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (normResp.IsSuccessStatusCode)
                    {
                        var normStream = await normResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        var normModel = await JsonSerializer.DeserializeAsync<LrclibResponse>(normStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (normModel != null && !string.IsNullOrWhiteSpace(normModel.SyncedLyrics))
                        {
                            var parsed = LrcParser.Parse(normModel.SyncedLyrics, normModel.TrackName ?? cleanTrack, normModel.ArtistName ?? cleanArtist);
                            return new LyricsSearchResult(true, parsed, "LRCLIB");
                        }
                        if (normModel != null && !string.IsNullOrWhiteSpace(normModel.PlainLyrics))
                        {
                            var plain = new SyncedLyrics(
                                title: normModel.TrackName ?? cleanTrack,
                                artist: normModel.ArtistName ?? cleanArtist,
                                isSynced: false,
                                lines: Array.Empty<LyricLine>(),
                                plainLyrics: normModel.PlainLyrics);
                            return new LyricsSearchResult(true, plain, "LRCLIB");
                        }
                    }
                }

                // Fallback 3: Broad search using /api/search?q={query} if exact get failed
                string broadQ = $"{primaryArtist} {strippedTrack}".Trim();
                if (!string.IsNullOrWhiteSpace(broadQ))
                {
                    var searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(broadQ)}";
                    using var searchResp = await _httpClient.GetAsync(new Uri(searchUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (searchResp.IsSuccessStatusCode)
                    {
                        var searchStream = await searchResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        var searchList = await JsonSerializer.DeserializeAsync<List<LrclibResponse>>(searchStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                        if (searchList != null && searchList.Count > 0)
                        {
                            // Prefer synced lyrics first, then plain
                            var candidate = searchList.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.SyncedLyrics))
                                         ?? searchList.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.PlainLyrics));
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
                    }
                }

                return new LyricsSearchResult(false, null, "LRCLIB", "Lyrics not found for track.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                int retrySeconds = 15;
                if (response.Headers.RetryAfter != null)
                {
                    if (response.Headers.RetryAfter.Delta.HasValue)
                    {
                        retrySeconds = (int)Math.Max(1, response.Headers.RetryAfter.Delta.Value.TotalSeconds);
                    }
                    else if (response.Headers.RetryAfter.Date.HasValue)
                    {
                        var diff = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                        retrySeconds = (int)Math.Max(1, diff.TotalSeconds);
                    }
                }
                return new LyricsSearchResult(
                    Found: false,
                    Lyrics: null,
                    Source: "LRCLIB",
                    Error: $"Rate limited by LRCLIB API. {retrySeconds} saniye sonra tekrar denenecek.",
                    IsRateLimited: true,
                    RetryAfterSeconds: retrySeconds);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new LyricsSearchResult(false, null, "LRCLIB", $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var apiResponse = await JsonSerializer.DeserializeAsync<LrclibResponse>(contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (apiResponse == null)
            {
                return new LyricsSearchResult(false, null, "LRCLIB", "Failed to deserialize response.");
            }

            // 1. Prefer synced lyrics if available
            if (!string.IsNullOrWhiteSpace(apiResponse.SyncedLyrics))
            {
                var synced = LrcParser.Parse(apiResponse.SyncedLyrics, apiResponse.TrackName ?? cleanTrack, apiResponse.ArtistName ?? cleanArtist);
                return new LyricsSearchResult(true, synced, "LRCLIB");
            }

            // 2. Fall back to plain unsynced lyrics
            if (!string.IsNullOrWhiteSpace(apiResponse.PlainLyrics))
            {
                var plain = new SyncedLyrics(
                    title: apiResponse.TrackName ?? cleanTrack,
                    artist: apiResponse.ArtistName ?? cleanArtist,
                    isSynced: false,
                    lines: Array.Empty<LyricLine>(),
                    plainLyrics: apiResponse.PlainLyrics);

                return new LyricsSearchResult(true, plain, "LRCLIB");
            }

            return new LyricsSearchResult(false, null, "LRCLIB", "No synced or plain lyrics content found.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LyricsSearchResult(false, null, "LRCLIB", "Lyrics request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new LyricsSearchResult(false, null, "LRCLIB", $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new LyricsSearchResult(false, null, "LRCLIB", $"Unexpected error: {ex.Message}");
        }
    }

    public static string StripTrackSuffixes(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        string s = title.Trim();
        int dashIdx = s.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx > 1)
        {
            s = s[..dashIdx].Trim();
        }
        int parenIdx = s.IndexOf(" (", StringComparison.Ordinal);
        if (parenIdx > 1)
        {
            s = s[..parenIdx].Trim();
        }
        int bracketIdx = s.IndexOf(" [", StringComparison.Ordinal);
        if (bracketIdx > 1)
        {
            s = s[..bracketIdx].Trim();
        }
        return s;
    }

    public static string ExtractPrimaryArtist(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        string s = artist.Trim();
        int commaIdx = s.IndexOf(',');
        if (commaIdx > 1) s = s[..commaIdx].Trim();
        int semiIdx = s.IndexOf(';');
        if (semiIdx > 1) s = s[..semiIdx].Trim();
        int featIdx = s.IndexOf(" feat.", StringComparison.OrdinalIgnoreCase);
        if (featIdx > 1) s = s[..featIdx].Trim();
        int ftIdx = s.IndexOf(" ft.", StringComparison.OrdinalIgnoreCase);
        if (ftIdx > 1) s = s[..ftIdx].Trim();
        return s;
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
