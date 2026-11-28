namespace Lyrow.Core.Lyrics;

/// <summary>
/// Language-invariant formatter for Lyrow v5.0 synchronization quality and provider short codes.
/// Format: <c>SYNC-PROVIDER</c> (e.g. <c>H-BLY</c>, <c>M-BLP</c>, <c>L-LRC</c>, <c>N-BLU</c>).
/// Never localizes codes, always uppercase, and strictly verifies real timing granularity before emitting a code.
/// </summary>
public static class LyricsSyncCodeFormatter
{
    public static string? GetProviderCode(LyricsProviderId? providerId) => providerId switch
    {
        LyricsProviderId.BetterLyrics => "BLY",
        LyricsProviderId.BetterLyricsUnison => "BLU",
        LyricsProviderId.BiniLyrics => "BNL",
        LyricsProviderId.BetterLyricsPortato => "BLP",
        LyricsProviderId.Lrclib => "LRC",
        LyricsProviderId.BetterLyricsLegato => "BLL",
        LyricsProviderId.AppleMusic => "APL",
        LyricsProviderId.QQMusic => "QQ",
        LyricsProviderId.NetEase => "NET",
        LyricsProviderId.KuGou => "KUG",
        LyricsProviderId.Kuwo => "KWO",
        LyricsProviderId.Migu => "MIG",
        LyricsProviderId.PetitLyrics => "PET",
        _ => null
    };

    /// <summary>
    /// Returns the single-letter quality code strictly verified against the actual timing payload granularity:
    /// H (Syllable), M (Word), L (Line), N (Asynchronous).
    /// Returns null if the actual payload does not genuinely support the resolved mode.
    /// </summary>
    public static string? GetVerifiedQualityCode(LyricsSyncMode effectiveMode, LyricsTimingData? data)
    {
        if (data == null || data.ActualGranularity == LyricsTimingGranularity.None)
        {
            return null;
        }

        return effectiveMode switch
        {
            LyricsSyncMode.Syllable => data.HasTimedSyllables ? "H" : null,
            LyricsSyncMode.Word => data.HasTimedWords ? "M" : null,
            LyricsSyncMode.Line => data.HasTimedLines ? "L" : null,
            LyricsSyncMode.Asynchronous => (data.HasPlainLyrics || data.Lines.Count > 0) ? "N" : null,
            LyricsSyncMode.Automatic => GetVerifiedQualityCode(data.BestSupportedMode, data),
            _ => null
        };
    }

    /// <summary>
    /// Formats the invariant <c>SYNC-PROVIDER</c> short code for a verified active resolution.
    /// Returns null when no verified provider is active (e.g. fallback or no lyrics) — never fabricates a code.
    /// </summary>
    public static string? Format(LyricsSyncMode effectiveMode, LyricsProviderId? providerId, LyricsTimingData? data)
    {
        string? providerCode = GetProviderCode(providerId);
        if (string.IsNullOrEmpty(providerCode)) return null;

        string? qualityCode = GetVerifiedQualityCode(effectiveMode, data);
        if (string.IsNullOrEmpty(qualityCode)) return null;

        return $"{qualityCode}-{providerCode}";
    }

    /// <summary>
    /// Formats the invariant <c>SYNC-PROVIDER</c> short code directly from a <see cref="LyricsResolutionResult"/>.
    /// </summary>
    public static string? Format(LyricsResolutionResult? resolution)
    {
        if (resolution == null || !resolution.Success || !resolution.ProviderId.HasValue)
        {
            return null;
        }

        return Format(resolution.ResolvedMode, resolution.ProviderId, resolution.TimingData);
    }
}
