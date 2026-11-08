using System.Text.RegularExpressions;
using Lyrow.Core.Contracts;

namespace Lyrow.Music.Metadata;

/// <summary>
/// Default production media metadata normalizer.
/// Handles media player idiosyncrasies (e.g. Apple Music concatenating artist and album into the Artist field),
/// preserves hyphens within artist and album names, retains release identifiers (Single, EP, Deluxe),
/// and produces clean search tokens for lyrics resolution.
/// </summary>
public sealed class MediaMetadataNormalizer : IMediaMetadataNormalizer
{
    private static readonly string[] SuffixDelimiters = { " - ", " — ", " – " };

    public NormalizedMediaMetadata Normalize(string? rawTitle, string? rawArtist, string? rawAlbum)
    {
        string safeRawTitle = (rawTitle ?? string.Empty).Trim();
        string safeRawArtist = (rawArtist ?? string.Empty).Trim();
        string safeRawAlbum = (rawAlbum ?? string.Empty).Trim();

        string normalizedTitle = safeRawTitle;
        string normalizedArtist = safeRawArtist;
        string normalizedAlbum = safeRawAlbum;

        // 1. Resolve Artist & Album separation (Crucial for Apple Music Windows which leaves AlbumTitle empty)
        if (string.IsNullOrWhiteSpace(normalizedAlbum) && !string.IsNullOrWhiteSpace(safeRawArtist))
        {
            string matchedDelimiter = FindFirstDelimiter(safeRawArtist);
            if (!string.IsNullOrEmpty(matchedDelimiter))
            {
                int firstIdx = safeRawArtist.IndexOf(matchedDelimiter, StringComparison.Ordinal);
                if (firstIdx > 0 && firstIdx < safeRawArtist.Length - matchedDelimiter.Length)
                {
                    string candidateArtist = safeRawArtist[..firstIdx].Trim();
                    string candidateAlbum = safeRawArtist[(firstIdx + matchedDelimiter.Length)..].Trim();

                    if (!string.IsNullOrWhiteSpace(candidateArtist) && !string.IsNullOrWhiteSpace(candidateAlbum))
                    {
                        normalizedArtist = candidateArtist;
                        normalizedAlbum = string.Equals(candidateAlbum, "Topic", StringComparison.OrdinalIgnoreCase)
                            ? string.Empty
                            : candidateAlbum;
                    }
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(normalizedAlbum) && !string.IsNullOrWhiteSpace(safeRawArtist))
        {
            // If rawAlbum is present, check if rawArtist redundantly contains " - {Album}" and clean it
            string matchedDelimiter = FindFirstDelimiter(safeRawArtist);
            if (!string.IsNullOrEmpty(matchedDelimiter))
            {
                int firstIdx = safeRawArtist.IndexOf(matchedDelimiter, StringComparison.Ordinal);
                if (firstIdx > 0)
                {
                    string candidateArtist = safeRawArtist[..firstIdx].Trim();
                    string candidateAlbum = safeRawArtist[(firstIdx + matchedDelimiter.Length)..].Trim();
                    if (string.Equals(candidateAlbum, normalizedAlbum, StringComparison.OrdinalIgnoreCase) ||
                        candidateAlbum.StartsWith(normalizedAlbum, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedArtist = candidateArtist;
                    }
                }
            }
        }

        // 2. Derive Clean Search Tokens for LRCLIB
        string cleanSearchTitle = StripTrackSuffixes(normalizedTitle);
        string cleanSearchArtist = ExtractPrimaryArtist(normalizedArtist);

        // Fallbacks if stripping accidentally emptied tokens
        if (string.IsNullOrWhiteSpace(cleanSearchTitle)) cleanSearchTitle = normalizedTitle;
        if (string.IsNullOrWhiteSpace(cleanSearchArtist)) cleanSearchArtist = normalizedArtist;

        return new NormalizedMediaMetadata(
            Title: normalizedTitle,
            Artist: normalizedArtist,
            Album: normalizedAlbum,
            RawTitle: safeRawTitle,
            RawArtist: safeRawArtist,
            RawAlbum: safeRawAlbum,
            CleanSearchTitle: cleanSearchTitle,
            CleanSearchArtist: cleanSearchArtist);
    }

    private static string FindFirstDelimiter(string text)
    {
        int minIdx = int.MaxValue;
        string best = string.Empty;

        foreach (var delim in SuffixDelimiters)
        {
            int idx = text.IndexOf(delim, StringComparison.Ordinal);
            if (idx >= 0 && idx < minIdx)
            {
                minIdx = idx;
                best = delim;
            }
        }

        return best;
    }

    public static string StripTrackSuffixes(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        string s = title.Trim();

        // 1. Strip common edition suffixes after " - "
        foreach (var delim in SuffixDelimiters)
        {
            int dashIdx = s.IndexOf(delim, StringComparison.Ordinal);
            if (dashIdx > 1)
            {
                string suffix = s[(dashIdx + delim.Length)..].Trim();
                if (IsKnownTrackSuffix(suffix))
                {
                    s = s[..dashIdx].Trim();
                    break;
                }
            }
        }

        // 2. Strip parentheses suffixes: e.g. " (Remastered 2011)", " (feat. Drake)", " (Radio Mix)"
        int parenIdx = s.IndexOf(" (", StringComparison.Ordinal);
        if (parenIdx > 1 && s.EndsWith(')'))
        {
            s = s[..parenIdx].Trim();
        }

        // 3. Strip bracket suffixes: e.g. " [Live]", " [feat. ...]"
        int bracketIdx = s.IndexOf(" [", StringComparison.Ordinal);
        if (bracketIdx > 1 && s.EndsWith(']'))
        {
            s = s[..bracketIdx].Trim();
        }

        return s;
    }

    public static string ExtractPrimaryArtist(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        string s = artist.Trim();

        // Do not split on hyphens inside single words (e.g. blink-182, Jay-Z, A-Ha)
        // Only strip featured suffixes or commas/semicolons
        int commaIdx = s.IndexOf(',');
        if (commaIdx > 1) s = s[..commaIdx].Trim();

        int semiIdx = s.IndexOf(';');
        if (semiIdx > 1) s = s[..semiIdx].Trim();

        int featIdx = s.IndexOf(" feat.", StringComparison.OrdinalIgnoreCase);
        if (featIdx > 1) s = s[..featIdx].Trim();

        int ftIdx = s.IndexOf(" ft.", StringComparison.OrdinalIgnoreCase);
        if (ftIdx > 1) s = s[..ftIdx].Trim();

        int withIdx = s.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
        if (withIdx > 1) s = s[..withIdx].Trim();

        return s;
    }

    private static bool IsKnownTrackSuffix(string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix)) return false;
        string lower = suffix.ToLowerInvariant();
        return lower.Contains("remaster") ||
               lower.Contains("live") ||
               lower.Contains("single") ||
               lower.Contains("ep") ||
               lower.Contains("radio") ||
               lower.Contains("mix") ||
               lower.Contains("edit") ||
               lower.Contains("deluxe") ||
               lower.Contains("version") ||
               lower.Contains("anniversary") ||
               lower.Contains("bonus") ||
               lower.Contains("instrumental") ||
               lower.Contains("acoustic") ||
               lower.Contains("feat") ||
               lower.Contains("soundtrack");
    }
}
