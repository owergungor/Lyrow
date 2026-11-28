namespace Lyrow.Core.Lyrics;

/// <summary>
/// Formats the short, human-readable upstream lyrics provider badge code
/// strictly from the actual resolved provider (never guessing, never mixing
/// sync granularity codes with provider identity).
/// </summary>
public static class LyricsSourceBadgeFormatter
{
    /// <summary>
    /// Returns the short uppercase provider code for a verified upstream <see cref="LyricsProviderId"/>.
    /// </summary>
    public static string? GetSourceCode(LyricsProviderId? providerId) => providerId switch
    {
        LyricsProviderId.BetterLyrics => "WEB",
        LyricsProviderId.AppleMusic => "APL",
        LyricsProviderId.Spotify => "SPT",
        LyricsProviderId.BetterLyricsUnison => "BLU",
        LyricsProviderId.Lrclib => "LRC",
        LyricsProviderId.QQMusic => "QQ",
        LyricsProviderId.NetEase => "NTE",
        LyricsProviderId.KuGou => "KG",
        LyricsProviderId.Kuwo => "KW",
        LyricsProviderId.Migu => "MG",
        LyricsProviderId.PetitLyrics => "PL",
        LyricsProviderId.BiniLyrics => "BNL",
        LyricsProviderId.BetterLyricsPortato => "BLP",
        LyricsProviderId.BetterLyricsLegato => "BLL",
        LyricsProviderId.LiriQoUnknown => "LRQ",
        _ => null
    };

    /// <summary>
    /// Returns the clean provider display name for tooltips and accessibility labels.
    /// </summary>
    public static string? GetSourceDisplayName(LyricsProviderId? providerId) => providerId switch
    {
        LyricsProviderId.BetterLyrics => "Better Lyrics",
        LyricsProviderId.AppleMusic => "Apple Music",
        LyricsProviderId.Spotify => "Spotify",
        LyricsProviderId.BetterLyricsUnison => "Better Lyrics Unison",
        LyricsProviderId.Lrclib => "LRCLIB",
        LyricsProviderId.QQMusic => "QQ Music",
        LyricsProviderId.NetEase => "NetEase",
        LyricsProviderId.KuGou => "KuGou",
        LyricsProviderId.Kuwo => "Kuwo",
        LyricsProviderId.Migu => "Migu",
        LyricsProviderId.PetitLyrics => "PetitLyrics",
        LyricsProviderId.BiniLyrics => "BiniLyrics",
        LyricsProviderId.BetterLyricsPortato => "Better Lyrics Portato",
        LyricsProviderId.BetterLyricsLegato => "Better Lyrics Legato",
        LyricsProviderId.LiriQoUnknown => "LiriQo",
        _ => null
    };

    /// <summary>
    /// Maps a non-orchestrator source identifier (e.g., local cache, fallback client, or user-provided lyrics)
    /// to an honest short code without fabricating an upstream provider name.
    /// </summary>
    public static (string? Code, string? DisplayName) FormatFallbackOrCustomSource(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return (null, null);
        }

        string trimmed = sourceName.Trim();
        if (trimmed.Contains("lrclib", StringComparison.OrdinalIgnoreCase))
        {
            return ("LRC", "LRCLIB");
        }
        if (trimmed.Contains("cache", StringComparison.OrdinalIgnoreCase))
        {
            return ("CACHE", "Local Cache");
        }
        if (trimmed.Contains("user", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("custom", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("local", StringComparison.OrdinalIgnoreCase))
        {
            return ("USR", "User Lyrics");
        }
        if (trimmed.Contains("spotify", StringComparison.OrdinalIgnoreCase))
        {
            return ("SPT", "Spotify");
        }
        if (trimmed.Contains("apple", StringComparison.OrdinalIgnoreCase))
        {
            return ("APL", "Apple Music");
        }
        if (trimmed.Contains("better", StringComparison.OrdinalIgnoreCase))
        {
            return ("WEB", "Better Lyrics");
        }

        return ("SRC", trimmed);
    }
}
