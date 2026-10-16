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
        if (string.IsNullOrWhiteSpace(trackName) || string.IsNullOrWhiteSpace(artistName))
        {
            return new LyricsSearchResult(false, null, "LRCLIB", "Track name or artist name is empty.");
        }

        try
        {
            // Build query parameters with safe URL encoding
            var query = $"?track_name={Uri.EscapeDataString(trackName)}&artist_name={Uri.EscapeDataString(artistName)}";
            if (duration.HasValue && duration.Value.TotalSeconds > 1.0)
            {
                query += $"&duration={(int)Math.Round(duration.Value.TotalSeconds)}";
            }

            var requestUri = new Uri(BaseApiUrl + query);
            using var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new LyricsSearchResult(false, null, "LRCLIB", "Lyrics not found for track.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new LyricsSearchResult(false, null, "LRCLIB", "Rate limited by LRCLIB API. Try again later.");
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
                var synced = LrcParser.Parse(apiResponse.SyncedLyrics, apiResponse.TrackName ?? trackName, apiResponse.ArtistName ?? artistName);
                return new LyricsSearchResult(true, synced, "LRCLIB");
            }

            // 2. Fall back to plain unsynced lyrics
            if (!string.IsNullOrWhiteSpace(apiResponse.PlainLyrics))
            {
                var plain = new SyncedLyrics(
                    title: apiResponse.TrackName ?? trackName,
                    artist: apiResponse.ArtistName ?? artistName,
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
