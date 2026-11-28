namespace Lyrow.Core.Lyrics;

/// <summary>
/// Resolves mode-specific lyrics provider priority chains, enforces truthful capability filtering,
/// and determines fallback / recommended modes without ever fabricating unsupported timing levels.
/// </summary>
public sealed class LyricsProviderPriorityResolver
{
    private static readonly IReadOnlyDictionary<LyricsProviderId, LyricsProviderCapability> DefaultCapabilities =
        new Dictionary<LyricsProviderId, LyricsProviderCapability>
        {
            [LyricsProviderId.BetterLyrics] = new(
                LyricsProviderId.BetterLyrics,
                "Better Lyrics",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedSyllables,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Live public REST API (lyrics-api.boidu.dev/getLyrics) returning Apple Music TTML with line, word, and syllable timestamps."),

            [LyricsProviderId.BetterLyricsUnison] = new(
                LyricsProviderId.BetterLyricsUnison,
                "Better Lyrics Unison",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedSyllables,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Public community endpoint (unison.betterlyrics.org/lyrics); primarily keyed by YouTube videoId (falls back cleanly when artist+song returns HTTP 400/404)."),

            [LyricsProviderId.BiniLyrics] = new(
                LyricsProviderId.BiniLyrics,
                "BiniLyrics",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedSyllables,
                IsPublicEndpointAvailable: false,
                AvailabilityNote: "No unauthenticated standalone public REST API documented; adapter ready for custom endpoint configuration."),

            [LyricsProviderId.BetterLyricsPortato] = new(
                LyricsProviderId.BetterLyricsPortato,
                "Better Lyrics Portato",
                LyricsTimingGranularity.TimedWords,
                IsPublicEndpointAvailable: false,
                AvailabilityNote: "Internal Better Lyrics word-sync relay (QRC/QQ Music); no direct unauthenticated public endpoint."),

            [LyricsProviderId.Lrclib] = new(
                LyricsProviderId.Lrclib,
                "LRCLIB",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Live open public REST API (lrclib.net/api/get) providing standard LRC line-synced and plain lyrics."),

            [LyricsProviderId.BetterLyricsLegato] = new(
                LyricsProviderId.BetterLyricsLegato,
                "Better Lyrics Legato",
                LyricsTimingGranularity.TimedLines,
                IsPublicEndpointAvailable: false,
                AvailabilityNote: "Internal Better Lyrics line-sync relay (Kugou); no direct unauthenticated public endpoint."),

            [LyricsProviderId.AppleMusic] = new(
                LyricsProviderId.AppleMusic,
                "Apple Music",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedSyllables,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Live via LiriQo aggregator (api.liriqo-alfarrizi.my.id/v1/lyrics); returns native Apple Music word/syllable timing."),

            [LyricsProviderId.QQMusic] = new(
                LyricsProviderId.QQMusic,
                "QQ Music",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Accessible via LiriQo aggregator; native word and line timing when available."),

            [LyricsProviderId.NetEase] = new(
                LyricsProviderId.NetEase,
                "NetEase",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Accessible via LiriQo aggregator; word and line timing when available."),

            [LyricsProviderId.KuGou] = new(
                LyricsProviderId.KuGou,
                "KuGou",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Accessible via LiriQo aggregator; word and line timing when available."),

            [LyricsProviderId.Kuwo] = new(
                LyricsProviderId.Kuwo,
                "Kuwo",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Accessible via LiriQo aggregator; character timing is never misclassified as syllable."),

            [LyricsProviderId.Migu] = new(
                LyricsProviderId.Migu,
                "Migu",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Accessible via LiriQo aggregator; character timing is never misclassified as syllable."),

            [LyricsProviderId.PetitLyrics] = new(
                LyricsProviderId.PetitLyrics,
                "PetitLyrics",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords,
                IsPublicEndpointAvailable: true,
                AvailabilityNote: "Accessible via LiriQo aggregator; word and line timing when available."),

            [LyricsProviderId.LiriQoUnknown] = new(
                LyricsProviderId.LiriQoUnknown,
                "Unknown Upstream",
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines,
                IsPublicEndpointAvailable: false,
                AvailabilityNote: "Unrecognized upstream returned by aggregator; never assigned a fabricated provider code.")
        };

    private readonly IReadOnlyDictionary<LyricsProviderId, LyricsProviderCapability> _capabilities;

    public LyricsProviderPriorityResolver(IEnumerable<LyricsProviderCapability>? customCapabilities = null)
    {
        if (customCapabilities == null)
        {
            _capabilities = DefaultCapabilities;
        }
        else
        {
            var map = new Dictionary<LyricsProviderId, LyricsProviderCapability>(DefaultCapabilities);
            foreach (var cap in customCapabilities)
            {
                map[cap.ProviderId] = cap;
            }
            _capabilities = map;
        }
    }

    public IReadOnlyCollection<LyricsProviderCapability> AllCapabilities => _capabilities.Values.ToArray();

    public LyricsProviderCapability GetCapability(LyricsProviderId providerId) => _capabilities[providerId];

    /// <summary>
    /// Returns the exact prioritized provider list for the specified synchronization mode,
    /// filtered to providers whose declared capabilities genuinely support that mode.
    /// </summary>
    public IReadOnlyList<LyricsProviderId> GetPriorityOrder(LyricsSyncMode mode)
    {
        LyricsProviderId[] rawChain = mode switch
        {
            // A. Syllable Sync (H): 1. BLY, 2. APL, 3. BLU, 4. BNL
            LyricsSyncMode.Syllable => new[]
            {
                LyricsProviderId.BetterLyrics,
                LyricsProviderId.AppleMusic,
                LyricsProviderId.BetterLyricsUnison,
                LyricsProviderId.BiniLyrics
            },

            // B. Word Sync (M): 1. APL, 2. BLU, 3. QQ, 4. BLP, 5. NET, 6. KUG, 7. KWO, 8. MIG, 9. PET
            LyricsSyncMode.Word => new[]
            {
                LyricsProviderId.AppleMusic,
                LyricsProviderId.BetterLyricsUnison,
                LyricsProviderId.QQMusic,
                LyricsProviderId.BetterLyricsPortato,
                LyricsProviderId.NetEase,
                LyricsProviderId.KuGou,
                LyricsProviderId.Kuwo,
                LyricsProviderId.Migu,
                LyricsProviderId.PetitLyrics
            },

            // C. Line Sync (L): 1. BLY, 2. APL, 3. BLU, 4. BNL, 5. LRC, 6. BLL, 7. QQ, 8. NET, 9. KUG, 10. KWO, 11. MIG
            LyricsSyncMode.Line => new[]
            {
                LyricsProviderId.BetterLyrics,
                LyricsProviderId.AppleMusic,
                LyricsProviderId.BetterLyricsUnison,
                LyricsProviderId.BiniLyrics,
                LyricsProviderId.Lrclib,
                LyricsProviderId.BetterLyricsLegato,
                LyricsProviderId.QQMusic,
                LyricsProviderId.NetEase,
                LyricsProviderId.KuGou,
                LyricsProviderId.Kuwo,
                LyricsProviderId.Migu
            },

            // D. Async Sync (N): 1. BLU
            LyricsSyncMode.Asynchronous => new[]
            {
                LyricsProviderId.BetterLyricsUnison
            },

            // Automatic: highest precision live providers first, then all verified word/line fallbacks
            LyricsSyncMode.Automatic => new[]
            {
                LyricsProviderId.BetterLyrics,
                LyricsProviderId.AppleMusic,
                LyricsProviderId.BetterLyricsUnison,
                LyricsProviderId.BiniLyrics,
                LyricsProviderId.QQMusic,
                LyricsProviderId.BetterLyricsPortato,
                LyricsProviderId.NetEase,
                LyricsProviderId.KuGou,
                LyricsProviderId.Kuwo,
                LyricsProviderId.Migu,
                LyricsProviderId.PetitLyrics,
                LyricsProviderId.Lrclib,
                LyricsProviderId.BetterLyricsLegato
            },

            _ => Array.Empty<LyricsProviderId>()
        };

        return rawChain
            .Where(id => _capabilities.TryGetValue(id, out var cap) && cap.SupportsMode(mode))
            .ToArray();
    }

    /// <summary>
    /// Strictly verifies whether a provider's actual returned <see cref="LyricsTimingData"/> satisfies
    /// the requested <see cref="LyricsSyncMode"/>. Never accepts lower-granularity data for a stricter mode.
    /// </summary>
    public static bool SatisfiesStrictMode(LyricsSyncMode requestedMode, LyricsTimingData? data)
    {
        if (data == null) return false;
        return requestedMode switch
        {
            LyricsSyncMode.Syllable => data.HasTimedSyllables,
            LyricsSyncMode.Word => data.HasTimedWords,
            LyricsSyncMode.Line => data.HasTimedLines,
            LyricsSyncMode.Asynchronous => data.HasPlainLyrics || data.Lines.Count > 0,
            LyricsSyncMode.Automatic => data.HasTimedSyllables || data.HasTimedWords || data.HasTimedLines || data.HasPlainLyrics,
            _ => false
        };
    }

    /// <summary>
    /// Given candidate payloads collected from providers when a strict mode failed,
    /// selects the highest-precision supported mode to recommend to the user.
    /// </summary>
    public static LyricsSyncMode? RecommendBestAvailableMode(IEnumerable<LyricsTimingData> availablePayloads)
    {
        LyricsSyncMode? best = null;
        int bestRank = -1;

        foreach (var data in availablePayloads)
        {
            if (data == null) continue;
            var candidate = data.BestSupportedMode;
            if (!data.SupportsMode(candidate)) continue;

            int rank = GetPrecisionRank(candidate);
            if (rank > bestRank)
            {
                bestRank = rank;
                best = candidate;
            }
        }

        return best;
    }

    public static int GetPrecisionRank(LyricsSyncMode mode) => mode switch
    {
        LyricsSyncMode.Syllable => 4,
        LyricsSyncMode.Word => 3,
        LyricsSyncMode.Line => 2,
        LyricsSyncMode.Asynchronous => 1,
        _ => 0
    };
}
