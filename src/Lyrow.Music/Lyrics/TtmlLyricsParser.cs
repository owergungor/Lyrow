using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Lyrow.Core.Lyrics;

namespace Lyrow.Music.Lyrics;

/// <summary>
/// Production TTML (Timed Text Markup Language) parser for Better Lyrics / Apple Music payloads.
/// Truthfully classifies each parsed payload as TimedSyllables, TimedWords, TimedLines, or PlainLyrics
/// by inspecting actual &lt;p&gt; and &lt;span&gt; timestamps and intra-word whitespace boundaries.
/// </summary>
public static class TtmlLyricsParser
{
    public static LyricsTimingData Parse(
        string? rawTtml,
        string title = "",
        string artist = "",
        LyricsProviderId providerId = LyricsProviderId.BetterLyrics)
    {
        if (string.IsNullOrWhiteSpace(rawTtml))
        {
            return LyricsTimingData.Empty;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(rawTtml, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return LyricsTimingData.Empty;
        }

        var paragraphs = doc.Descendants().Where(e => string.Equals(e.Name.LocalName, "p", StringComparison.OrdinalIgnoreCase)).ToList();
        if (paragraphs.Count == 0)
        {
            return LyricsTimingData.Empty;
        }

        var parsedLines = new List<LyricLineTiming>(paragraphs.Count);
        var plainLines = new List<string>(paragraphs.Count);
        bool anyTimedLines = false;
        bool anyTimedWords = false;
        bool anyRealIntraWordSyllables = false;
        int lineIndex = 0;

        foreach (var p in paragraphs)
        {
            bool hasLineBegin = TryParseTtmlTimestamp(p.Attribute("begin")?.Value, out var lineStart);
            bool hasLineEnd = TryParseTtmlTimestamp(p.Attribute("end")?.Value, out var lineEnd);

            var childSpans = p.Elements()
                .Where(e => string.Equals(e.Name.LocalName, "span", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Flatten nested spans if present (e.g. background vocals wrappers) while ignoring non-vocal metadata roles
            var timedSpans = new List<(XElement Element, TimeSpan Start, TimeSpan End, string Text)>();
            foreach (var sp in childSpans)
            {
                string? role = sp.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "role", StringComparison.OrdinalIgnoreCase))?.Value;
                if (string.Equals(role, "x-translation", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(role, "x-roman", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryParseTtmlTimestamp(sp.Attribute("begin")?.Value, out var sb) &&
                    TryParseTtmlTimestamp(sp.Attribute("end")?.Value, out var se))
                {
                    string spText = sp.Value;
                    if (!string.IsNullOrEmpty(spText))
                    {
                        timedSpans.Add((sp, sb, se, spText));
                    }
                }
            }

            if (timedSpans.Count > 0)
            {
                anyTimedLines = true;
                anyTimedWords = true;

                if (!hasLineBegin) lineStart = timedSpans[0].Start;
                if (!hasLineEnd || lineEnd < lineStart) lineEnd = timedSpans[^1].End;

                // Group adjacent spans with NO intervening whitespace into words with true intra-word syllables
                var words = new List<LyricWordSegment>();
                var syllables = new List<LyricSyllableTiming>();
                var lineTextBuilder = new StringBuilder();

                var currentWordSpans = new List<(TimeSpan Start, TimeSpan End, string Text)>();
                int wordIdx = 0;
                int sylIdx = 0;

                void FlushWord(bool addTrailingSpace)
                {
                    if (currentWordSpans.Count == 0) return;

                    var wordTextBuilder = new StringBuilder();
                    var wordSyllables = new List<LyricSyllableTiming>(currentWordSpans.Count);
                    TimeSpan wStart = currentWordSpans[0].Start;
                    TimeSpan wEnd = currentWordSpans[^1].End;

                    if (currentWordSpans.Count > 1)
                    {
                        anyRealIntraWordSyllables = true;
                    }

                    for (int k = 0; k < currentWordSpans.Count; k++)
                    {
                        var seg = currentWordSpans[k];
                        string cleaned = seg.Text.Trim();
                        if (string.IsNullOrEmpty(cleaned)) continue;

                        wordTextBuilder.Append(cleaned);
                        bool isEnd = k == currentWordSpans.Count - 1;
                        var syl = new LyricSyllableTiming(cleaned, seg.Start, seg.End, sylIdx++, wordIdx, IsWordEnd: isEnd);
                        wordSyllables.Add(syl);
                        syllables.Add(syl);
                    }

                    string fullWord = wordTextBuilder.ToString();
                    if (!string.IsNullOrEmpty(fullWord))
                    {
                        words.Add(new LyricWordSegment(fullWord, wStart, wEnd, wordIdx++, wordSyllables));
                        if (lineTextBuilder.Length > 0) lineTextBuilder.Append(' ');
                        lineTextBuilder.Append(fullWord);
                    }

                    currentWordSpans.Clear();
                }

                for (int i = 0; i < timedSpans.Count; i++)
                {
                    var cur = timedSpans[i];
                    bool hasLeadingWhitespace = cur.Text.Length > 0 && char.IsWhiteSpace(cur.Text[0]);
                    bool hasTrailingWhitespace = cur.Text.Length > 0 && char.IsWhiteSpace(cur.Text[^1]);

                    if (i > 0)
                    {
                        bool separatedBySpaceNode = HasWhitespaceBetween(timedSpans[i - 1].Element, cur.Element);
                        if (separatedBySpaceNode || hasLeadingWhitespace)
                        {
                            FlushWord(addTrailingSpace: true);
                        }
                    }

                    currentWordSpans.Add((cur.Start, cur.End, cur.Text));

                    if (hasTrailingWhitespace)
                    {
                        FlushWord(addTrailingSpace: true);
                    }
                }

                FlushWord(addTrailingSpace: false);

                string lineText = lineTextBuilder.ToString().Trim();
                if (!string.IsNullOrEmpty(lineText))
                {
                    plainLines.Add(lineText);
                    // Only attach line-level syllables if true intra-word syllables exist in this line or track;
                    // we keep them on the line and decide track-level granularity truthfully below.
                    parsedLines.Add(new LyricLineTiming(lineIndex++, lineText, lineStart, lineEnd, words, syllables));
                }
            }
            else
            {
                // Paragraph with line-level timing only (no timed <span> children)
                string text = NormalizeWhitespace(p.Value);
                if (string.IsNullOrEmpty(text)) continue;

                plainLines.Add(text);
                if (hasLineBegin)
                {
                    anyTimedLines = true;
                    TimeSpan resolvedEnd = hasLineEnd && lineEnd > lineStart
                        ? lineEnd
                        : lineStart + TimeSpan.FromSeconds(3.0);
                    parsedLines.Add(new LyricLineTiming(
                        lineIndex++,
                        text,
                        lineStart,
                        resolvedEnd,
                        Array.Empty<LyricWordSegment>(),
                        Array.Empty<LyricSyllableTiming>()));
                }
            }
        }

        // Truthful granularity classification:
        // If NO line had real intra-word syllable splits, strip syllable lists so Word-only TTML is NEVER mislabeled as Syllable!
        if (!anyRealIntraWordSyllables && parsedLines.Count > 0)
        {
            for (int i = 0; i < parsedLines.Count; i++)
            {
                var l = parsedLines[i];
                if (l.Syllables.Count > 0)
                {
                    var strippedWords = l.Words
                        .Select(w => w with { Syllables = Array.Empty<LyricSyllableTiming>() })
                        .ToArray();
                    parsedLines[i] = l with { Words = strippedWords, Syllables = Array.Empty<LyricSyllableTiming>() };
                }
            }
        }

        string plain = string.Join(Environment.NewLine, plainLines);
        var granularity = LyricsTimingGranularity.None;
        if (!string.IsNullOrWhiteSpace(plain)) granularity |= LyricsTimingGranularity.PlainLyrics;
        if (anyTimedLines) granularity |= LyricsTimingGranularity.TimedLines;
        if (anyTimedWords) granularity |= LyricsTimingGranularity.TimedWords;
        if (anyRealIntraWordSyllables) granularity |= LyricsTimingGranularity.TimedSyllables;

        return new LyricsTimingData(title, artist, providerId, granularity, parsedLines, plain);
    }

    /// <summary>
    /// Parses TTML timestamp formats: "ss.fff", "mm:ss.fff", "hh:mm:ss.fff", or "ss".
    /// </summary>
    public static bool TryParseTtmlTimestamp(string? raw, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string s = raw.Trim();
        if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^1];
        }

        var parts = s.Split(':');
        if (parts.Length == 1)
        {
            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double sec) && sec >= 0)
            {
                result = TimeSpan.FromSeconds(sec);
                return true;
            }
            return false;
        }

        if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double sec) &&
                min >= 0 && sec >= 0)
            {
                result = TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec);
                return true;
            }
            return false;
        }

        if (parts.Length == 3)
        {
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hrs) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int min) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double sec) &&
                hrs >= 0 && min >= 0 && sec >= 0)
            {
                result = TimeSpan.FromHours(hrs) + TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec);
                return true;
            }
        }

        return false;
    }

    private static bool HasWhitespaceBetween(XElement prev, XElement next)
    {
        XNode? node = prev.NextNode;
        while (node != null && node != next)
        {
            if (node is XText txt && txt.Value.Any(char.IsWhiteSpace))
            {
                return true;
            }
            node = node.NextNode;
        }
        return false;
    }

    private static string NormalizeWhitespace(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
