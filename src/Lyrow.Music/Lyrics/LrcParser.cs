using System.Text.RegularExpressions;
using Lyrow.Core.Models;

namespace Lyrow.Music.Lyrics;

/// <summary>
/// High-performance RFC-compliant LRC parser.
/// Supports standard [mm:ss.xx], [mm:ss.xxx], multi-timestamp lines, metadata tags, and plain text fallbacks.
/// </summary>
public static partial class LrcParser
{
    // Regex matching timestamp tags like [01:23.45] or [01:23.456]
    [GeneratedRegex(@"\[(\d{1,3}):(\d{2})(?:\.(\d{2,3}))?\]", RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();

    // Regex matching metadata headers like [ti:Title], [ar:Artist], [al:Album]
    [GeneratedRegex(@"^\[(ar|ti|al|by|offset|length):(.*)\]$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MetadataHeaderRegex();

    /// <summary>
    /// Parses raw LRC string content into a SyncedLyrics model.
    /// Handles disordered lines by sorting chronologically and sanitizes text.
    /// </summary>
    public static SyncedLyrics Parse(string? rawLrc, string defaultTitle = "", string defaultArtist = "")
    {
        if (string.IsNullOrWhiteSpace(rawLrc))
        {
            return new SyncedLyrics(defaultTitle, defaultArtist, isSynced: false, Array.Empty<LyricLine>(), plainLyrics: null);
        }

        var lines = rawLrc.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var parsedLines = new List<LyricLine>(lines.Length);

        string title = defaultTitle;
        string artist = defaultArtist;
        var plainTextLines = new List<string>(lines.Length);
        bool hasAnyTimestamp = false;
        int lineCounter = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Check if metadata header
            var metaMatch = MetadataHeaderRegex().Match(line);
            if (metaMatch.Success)
            {
                var tag = metaMatch.Groups[1].Value.ToLowerInvariant();
                var val = metaMatch.Groups[2].Value.Trim();
                if (tag == "ti" && string.IsNullOrEmpty(title)) title = val;
                if (tag == "ar" && string.IsNullOrEmpty(artist)) artist = val;
                continue;
            }

            // Extract all timestamps in this line (e.g. [00:12.00][00:25.00]Lyric text)
            var matches = TimestampRegex().Matches(line);
            if (matches.Count > 0)
            {
                hasAnyTimestamp = true;

                // Find the end of the last timestamp to extract lyrics text
                var lastMatch = matches[^1];
                int textStartIndex = lastMatch.Index + lastMatch.Length;
                string lyricText = textStartIndex < line.Length ? line[textStartIndex..].Trim() : string.Empty;

                plainTextLines.Add(lyricText);

                foreach (Match match in matches)
                {
                    if (TryParseTimestamp(match, out var timestamp))
                    {
                        parsedLines.Add(new LyricLine(timestamp, lyricText, lineCounter++));
                    }
                }
            }
            else
            {
                // Line without timestamps (plain text line)
                plainTextLines.Add(line);
            }
        }

        if (!hasAnyTimestamp || parsedLines.Count == 0)
        {
            // Pure plain lyrics fallback
            string combinedPlain = string.Join(Environment.NewLine, plainTextLines);
            return new SyncedLyrics(
                title: title,
                artist: artist,
                isSynced: false,
                lines: Array.Empty<LyricLine>(),
                plainLyrics: combinedPlain);
        }

        // Sort parsed lines chronologically by timestamp
        parsedLines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // Reassign clean 0-based sequential LineIndex after sorting
        var finalizedLines = new List<LyricLine>(parsedLines.Count);
        for (int i = 0; i < parsedLines.Count; i++)
        {
            finalizedLines.Add(new LyricLine(parsedLines[i].Timestamp, parsedLines[i].Text, i));
        }

        string plainLyrics = string.Join(Environment.NewLine, finalizedLines.Select(l => l.Text));

        return new SyncedLyrics(
            title: title,
            artist: artist,
            isSynced: true,
            lines: finalizedLines,
            plainLyrics: plainLyrics);
    }

    private static bool TryParseTimestamp(Match match, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups[1].Value, out int minutes)) return false;
        if (!int.TryParse(match.Groups[2].Value, out int seconds)) return false;

        int milliseconds = 0;
        if (match.Groups[3].Success)
        {
            string fracStr = match.Groups[3].Value;
            if (fracStr.Length == 2 && int.TryParse(fracStr, out int centiseconds))
            {
                milliseconds = centiseconds * 10;
            }
            else if (fracStr.Length == 3 && int.TryParse(fracStr, out int millis))
            {
                milliseconds = millis;
            }
        }

        timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
        return true;
    }
}
