using System.Collections.Concurrent;
using System.Text.Json;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Music.Metadata;

namespace Lyrow.Music.Lyrics;

/// <summary>
/// Live REST client for the LiriQo multi-provider lyrics aggregator
/// (https://api.liriqo-alfarrizi.my.id/v1/lyrics).
/// Returns upstream-attributed <see cref="LyricsTimingData"/> entries for Apple Music (APL),
/// QQ Music (QQ), NetEase (NET), KuGou (KUG), Kuwo (KWO), Migu (MIG), PetitLyrics (PET),
/// Unison (BLU), and LRCLIB (LRC) from a single HTTP round-trip.
/// Strictly distinguishes character timing from genuine syllable timing and never misattributes LiriQo itself as the upstream source.
/// </summary>
public sealed class LiriQoClient : IMultiTrackLyricsProvider
{
    public const string DefaultApiUrl = "https://api.liriqo-alfarrizi.my.id/v1/lyrics";

    private readonly HttpClient _httpClient;
    private readonly IMediaMetadataNormalizer _normalizer;
    private readonly string _endpointUrl;
    private readonly ConcurrentDictionary<string, IReadOnlyList<LyricsTimingData>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<LyricsTimingData>>> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public LiriQoClient(
        HttpClient? httpClient = null,
        IMediaMetadataNormalizer? normalizer = null,
        string? endpointUrl = null,
        TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = timeout ?? TimeSpan.FromMilliseconds(4500) };
        _normalizer = normalizer ?? new MediaMetadataNormalizer();
        _endpointUrl = string.IsNullOrWhiteSpace(endpointUrl) ? DefaultApiUrl : endpointUrl;

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Lyrow/5.0 (https://github.com/owergungor/Lyrow)");
        }
    }

    public string AggregatorName => "LiriQo";

    public async Task<IReadOnlyList<LyricsTimingData>> FetchAllUpstreamTracksAsync(
        string trackName,
        string artistName,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        string rawTrack = (trackName ?? string.Empty).Trim();
        string rawArtist = (artistName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawTrack) || string.IsNullOrWhiteSpace(rawArtist))
        {
            return Array.Empty<LyricsTimingData>();
        }

        string key = $"{rawTrack.ToLowerInvariant()}|{rawArtist.ToLowerInvariant()}";
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var task = _inFlight.GetOrAdd(key, _ => FetchCoreAsync(key, rawTrack, rawArtist, duration));
        try
        {
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<LyricsTimingData>();
        }
    }

    private async Task<IReadOnlyList<LyricsTimingData>> FetchCoreAsync(string key, string rawTrack, string rawArtist, TimeSpan? duration)
    {
        try
        {
            var norm = _normalizer.Normalize(rawTrack, rawArtist, null);
            string qSong = string.IsNullOrWhiteSpace(norm.CleanSearchTitle) ? rawTrack : norm.CleanSearchTitle;
            string qArtist = string.IsNullOrWhiteSpace(norm.CleanSearchArtist) ? rawArtist : norm.CleanSearchArtist;

            string url = $"{_endpointUrl}?title={Uri.EscapeDataString(qSong)}&artist={Uri.EscapeDataString(qArtist)}";
            if (duration.HasValue && duration.Value.TotalSeconds >= 10)
            {
                url += $"&duration={(int)Math.Round(duration.Value.TotalSeconds)}";
            }

            using var resp = await _httpClient.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return Array.Empty<LyricsTimingData>();

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<LyricsTimingData>();

            var parsed = ParseResponseJson(json, qSong, qArtist);
            if (parsed.Count > 0)
            {
                _cache[key] = parsed;
            }
            return parsed;
        }
        catch
        {
            return Array.Empty<LyricsTimingData>();
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Parses a LiriQo <c>/v1/lyrics</c> JSON payload into upstream-attributed <see cref="LyricsTimingData"/> objects.
    /// Deduplicates by upstream provider within the response and strictly separates character timing from syllable timing.
    /// </summary>
    public static IReadOnlyList<LyricsTimingData> ParseResponseJson(string json, string fallbackTitle, string fallbackArtist)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<LyricsTimingData>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var results = new List<LyricsTimingData>();
            var seenUpstreams = new HashSet<LyricsProviderId>();

            if (root.TryGetProperty("tracks", out var tracksEl) && tracksEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var trackEl in tracksEl.EnumerateArray())
                {
                    var item = ParseTrackElement(trackEl, fallbackTitle, fallbackArtist);
                    if (item != null && item.ActualGranularity != LyricsTimingGranularity.None)
                    {
                        if (item.SourceProvider != LyricsProviderId.LiriQoUnknown && !seenUpstreams.Add(item.SourceProvider))
                        {
                            continue;
                        }
                        results.Add(item);
                    }
                }
            }
            else if (root.TryGetProperty("primary", out var primaryEl) && primaryEl.ValueKind == JsonValueKind.Object)
            {
                var item = ParseTrackElement(primaryEl, fallbackTitle, fallbackArtist);
                if (item != null && item.ActualGranularity != LyricsTimingGranularity.None)
                {
                    results.Add(item);
                }
            }

            return results;
        }
        catch
        {
            return Array.Empty<LyricsTimingData>();
        }
    }

    private static LyricsTimingData? ParseTrackElement(JsonElement el, string fallbackTitle, string fallbackArtist)
    {
        string rawProvider = GetStringProp(el, "provider") ?? string.Empty;
        string rawUpstream = GetStringProp(el, "upstreamProvider") ?? string.Empty;
        string syncLevel = (GetStringProp(el, "syncLevel") ?? string.Empty).Trim().ToLowerInvariant();
        string title = GetStringProp(el, "title") ?? fallbackTitle;
        string artist = GetStringProp(el, "artist") ?? fallbackArtist;
        string? plain = GetStringProp(el, "plain");
        bool nativeWordTiming = !el.TryGetProperty("nativeWordTiming", out var nwt) || nwt.ValueKind != JsonValueKind.False;

        var providerId = MapUpstreamProvider(rawProvider, rawUpstream);

        // 1. Prefer raw TTML if present (strict intra-word syllable vs word-only parser)
        string? ttml = GetStringProp(el, "ttml");
        if (!string.IsNullOrWhiteSpace(ttml))
        {
            var ttmlParsed = TtmlLyricsParser.Parse(ttml, title, artist, providerId);
            if (ttmlParsed.ActualGranularity != LyricsTimingGranularity.None)
            {
                return ttmlParsed;
            }
        }

        // 2. Parse LiriQo's structured "timed" array
        var lines = new List<LyricLineTiming>();
        bool anyRealWords = false;
        bool anyRealSyllables = false;

        // Character timing (e.g. Kuwo/Migu/PetitLyrics per-character) MUST NOT automatically be classified as Syllable (H)!
        bool isCharacterLevel = syncLevel is "character" or "char";
        bool allowsSyllableParsing = syncLevel == "syllable" && !isCharacterLevel;
        bool allowsWordParsing = nativeWordTiming && (syncLevel is "syllable" or "word" or "character" or "char");

        if (el.TryGetProperty("timed", out var timedEl) && timedEl.ValueKind == JsonValueKind.Array)
        {
            int lineIndex = 0;
            foreach (var lineEl in timedEl.EnumerateArray())
            {
                string lineText = (GetStringProp(lineEl, "text") ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(lineText)) continue;

                TimeSpan lineStart = TimeSpan.FromMilliseconds(Math.Max(0, GetDoubleProp(lineEl, "start")));
                double rawEndMs = GetDoubleProp(lineEl, "end");
                TimeSpan lineEnd = rawEndMs > lineStart.TotalMilliseconds
                    ? TimeSpan.FromMilliseconds(rawEndMs)
                    : lineStart + TimeSpan.FromSeconds(3.0);

                var words = new List<LyricWordSegment>();
                var syllables = new List<LyricSyllableTiming>();

                if (allowsWordParsing && lineEl.TryGetProperty("words", out var wordsEl) && wordsEl.ValueKind == JsonValueKind.Array)
                {
                    int wordIdx = 0;
                    int sylIdx = 0;
                    foreach (var wEl in wordsEl.EnumerateArray())
                    {
                        string wText = (GetStringProp(wEl, "text") ?? string.Empty).Trim();
                        if (string.IsNullOrEmpty(wText)) continue;

                        TimeSpan wStart = TimeSpan.FromMilliseconds(Math.Max(0, GetDoubleProp(wEl, "start")));
                        double wEndMs = GetDoubleProp(wEl, "end");
                        TimeSpan wEnd = wEndMs > wStart.TotalMilliseconds
                            ? TimeSpan.FromMilliseconds(wEndMs)
                            : wStart + TimeSpan.FromMilliseconds(150);

                        var wordSyls = new List<LyricSyllableTiming>();
                        if (allowsSyllableParsing && wEl.TryGetProperty("syllables", out var sylsEl) && sylsEl.ValueKind == JsonValueKind.Array)
                        {
                            int sCount = sylsEl.GetArrayLength();
                            int localIdx = 0;
                            foreach (var sEl in sylsEl.EnumerateArray())
                            {
                                string sText = (GetStringProp(sEl, "text") ?? string.Empty).Trim();
                                if (string.IsNullOrEmpty(sText)) continue;
                                TimeSpan sStart = TimeSpan.FromMilliseconds(Math.Max(0, GetDoubleProp(sEl, "start")));
                                double sEndMs = GetDoubleProp(sEl, "end");
                                TimeSpan sEnd = sEndMs > sStart.TotalMilliseconds
                                    ? TimeSpan.FromMilliseconds(sEndMs)
                                    : sStart + TimeSpan.FromMilliseconds(100);

                                var syl = new LyricSyllableTiming(sText, sStart, sEnd, sylIdx++, wordIdx, IsWordEnd: localIdx == sCount - 1);
                                wordSyls.Add(syl);
                                syllables.Add(syl);
                                localIdx++;
                            }

                            if (wordSyls.Count >= 2 && !IsSingleCharLatinSplit(wordSyls))
                            {
                                anyRealSyllables = true;
                            }
                        }

                        words.Add(new LyricWordSegment(wText, wStart, wEnd, wordIdx++, wordSyls));
                    }

                    // Character-level payloads where every "word" is a 1-character Latin split are NOT real word timings
                    if (words.Count > 0 && !(isCharacterLevel && IsSingleCharLatinWords(words)))
                    {
                        anyRealWords = true;
                    }
                    else if (isCharacterLevel)
                    {
                        words.Clear();
                    }
                }

                if (!anyRealSyllables)
                {
                    syllables.Clear();
                }

                lines.Add(new LyricLineTiming(lineIndex++, lineText, lineStart, lineEnd, words, syllables));
            }
        }

        // If no genuine intra-word syllables were verified across the track, strip syllables so we never mislabel as H
        if (!anyRealSyllables)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Syllables.Count > 0)
                {
                    lines[i] = lines[i] with { Syllables = Array.Empty<LyricSyllableTiming>() };
                }
            }
        }

        var granularity = LyricsTimingGranularity.None;
        if (!string.IsNullOrWhiteSpace(plain) || lines.Count > 0) granularity |= LyricsTimingGranularity.PlainLyrics;
        if (lines.Count > 0) granularity |= LyricsTimingGranularity.TimedLines;
        if (anyRealWords) granularity |= LyricsTimingGranularity.TimedWords;
        if (anyRealSyllables) granularity |= LyricsTimingGranularity.TimedSyllables;

        return new LyricsTimingData(title, artist, providerId, granularity, lines, plain);
    }

    /// <summary>
    /// Detects synthetic per-character splits of Latin words (e.g. "h","e","l","l","o")
    /// so character timing is never falsely classified as Syllable (H) or Word (M).
    /// </summary>
    private static bool IsSingleCharLatinSplit(IReadOnlyList<LyricSyllableTiming> syls)
    {
        if (syls.Count < 3) return false;
        int singleLatin = 0;
        for (int i = 0; i < syls.Count; i++)
        {
            string t = syls[i].Text;
            if (t.Length == 1 && char.IsAsciiLetter(t[0])) singleLatin++;
        }
        return singleLatin == syls.Count;
    }

    private static bool IsSingleCharLatinWords(IReadOnlyList<LyricWordSegment> words)
    {
        if (words.Count < 4) return false;
        int singleLatin = 0;
        for (int i = 0; i < words.Count; i++)
        {
            string t = words[i].Text;
            if (t.Length == 1 && char.IsAsciiLetter(t[0])) singleLatin++;
        }
        return singleLatin >= (int)(words.Count * 0.8);
    }

    public static LyricsProviderId MapUpstreamProvider(string provider, string upstreamProvider)
    {
        string combined = $"{provider} {upstreamProvider}".Trim().ToLowerInvariant();
        if (combined.Contains("apple")) return LyricsProviderId.AppleMusic;
        if (combined.Contains("unison")) return LyricsProviderId.BetterLyricsUnison;
        if (combined.Contains("lrclib")) return LyricsProviderId.Lrclib;
        if (combined.Contains("qq")) return LyricsProviderId.QQMusic;
        if (combined.Contains("netease") || combined.Contains("ncm")) return LyricsProviderId.NetEase;
        if (combined.Contains("kugou")) return LyricsProviderId.KuGou;
        if (combined.Contains("kuwo")) return LyricsProviderId.Kuwo;
        if (combined.Contains("migu")) return LyricsProviderId.Migu;
        if (combined.Contains("petit")) return LyricsProviderId.PetitLyrics;
        if (combined.Contains("bini")) return LyricsProviderId.BiniLyrics;
        return LyricsProviderId.LiriQoUnknown;
    }

    private static string? GetStringProp(JsonElement el, string name)
        => el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static double GetDoubleProp(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var prop)) return 0.0;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out double d)) return d;
        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)) return parsed;
        return 0.0;
    }
}
