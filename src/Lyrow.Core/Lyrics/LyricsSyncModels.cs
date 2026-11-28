using Lyrow.Core.Models;

namespace Lyrow.Core.Lyrics;

/// <summary>
/// User-selectable lyrics synchronization modes (Lyrow v5.0).
/// </summary>
public enum LyricsSyncMode
{
    Automatic = 0,
    Syllable = 1,
    Word = 2,
    Line = 3,
    Asynchronous = 4
}

/// <summary>
/// Explicit timing granularity levels supported by a lyrics provider or present in a parsed payload.
/// A provider offering plain text does NOT imply timed line, word, or syllable support.
/// </summary>
[Flags]
public enum LyricsTimingGranularity
{
    None = 0,
    PlainLyrics = 1 << 0,
    TimedLines = 1 << 1,
    TimedWords = 1 << 2,
    TimedSyllables = 1 << 3
}

/// <summary>
/// Canonical identifiers for lyrics providers participating in the v5.0 priority resolver.
/// </summary>
public enum LyricsProviderId
{
    BetterLyrics = 0,
    BetterLyricsUnison = 1,
    BiniLyrics = 2,
    BetterLyricsPortato = 3,
    Lrclib = 4,
    BetterLyricsLegato = 5,
    AppleMusic = 6,
    QQMusic = 7,
    NetEase = 8,
    KuGou = 9,
    Kuwo = 10,
    Migu = 11,
    PetitLyrics = 12,
    LiriQoUnknown = 13,
    Spotify = 14
}

/// <summary>
/// Declares the true, verified capabilities and availability of a lyrics provider.
/// </summary>
public sealed record LyricsProviderCapability(
    LyricsProviderId ProviderId,
    string DisplayName,
    LyricsTimingGranularity SupportedGranularities,
    bool IsPublicEndpointAvailable,
    string AvailabilityNote)
{
    public bool Supports(LyricsTimingGranularity required) =>
        required != LyricsTimingGranularity.None && (SupportedGranularities & required) == required;

    public bool SupportsMode(LyricsSyncMode mode) => mode switch
    {
        LyricsSyncMode.Syllable => Supports(LyricsTimingGranularity.TimedSyllables),
        LyricsSyncMode.Word => Supports(LyricsTimingGranularity.TimedWords),
        LyricsSyncMode.Line => Supports(LyricsTimingGranularity.TimedLines),
        LyricsSyncMode.Asynchronous => Supports(LyricsTimingGranularity.PlainLyrics),
        LyricsSyncMode.Automatic => SupportedGranularities != LyricsTimingGranularity.None,
        _ => false
    };
}

/// <summary>
/// A single syllable timing segment within a word/line.
/// </summary>
public sealed record LyricSyllableTiming(
    string Text,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int SyllableIndex,
    int WordIndex,
    bool IsWordEnd = false)
{
    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;
}

/// <summary>
/// A single word timing segment within a line, optionally decomposed into timed syllables.
/// </summary>
public sealed record LyricWordSegment(
    string Text,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int WordIndex,
    IReadOnlyList<LyricSyllableTiming> Syllables)
{
    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;
    public bool HasSyllableTimings => Syllables.Count > 0;
}

/// <summary>
/// Normalized lyric line containing line bounds plus optional true word and syllable segments.
/// </summary>
public sealed record LyricLineTiming(
    int LineIndex,
    string Text,
    TimeSpan StartTime,
    TimeSpan EndTime,
    IReadOnlyList<LyricWordSegment> Words,
    IReadOnlyList<LyricSyllableTiming> Syllables)
{
    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;
    public bool HasWordTimings => Words.Count > 0;
    public bool HasSyllableTimings => Syllables.Count > 0;
}

/// <summary>
/// Zero-allocation snapshot describing the active lyric line, word, and syllable at a playback timestamp.
/// </summary>
public readonly record struct ActiveLyricSegment(
    LyricsSyncMode EffectiveMode,
    int LineIndex,
    int WordIndex,
    int SyllableIndex,
    double LineProgress,
    double WordProgress,
    double SyllableProgress,
    bool IsPaused)
{
    public static readonly ActiveLyricSegment None = new(
        LyricsSyncMode.Asynchronous,
        LineIndex: -1,
        WordIndex: -1,
        SyllableIndex: -1,
        LineProgress: 0.0,
        WordProgress: 0.0,
        SyllableProgress: 0.0,
        IsPaused: false);

    public bool HasActiveLine => LineIndex >= 0;
    public bool HasActiveWord => WordIndex >= 0;
    public bool HasActiveSyllable => SyllableIndex >= 0;
}

/// <summary>
/// Unified, normalized timing payload produced by any lyrics provider and consumed by <see cref="LyricsSyncEngine"/>.
/// Guarantees chronological ordering, non-negative durations, and truthful granularity classification.
/// </summary>
public sealed class LyricsTimingData
{
    public static readonly LyricsTimingData Empty = new(
        title: string.Empty,
        artist: string.Empty,
        sourceProvider: LyricsProviderId.Lrclib,
        actualGranularity: LyricsTimingGranularity.None,
        lines: Array.Empty<LyricLineTiming>(),
        plainLyrics: null);

    public LyricsTimingData(
        string title,
        string artist,
        LyricsProviderId sourceProvider,
        LyricsTimingGranularity actualGranularity,
        IReadOnlyList<LyricLineTiming> lines,
        string? plainLyrics)
    {
        Title = title ?? string.Empty;
        Artist = artist ?? string.Empty;
        SourceProvider = sourceProvider;
        PlainLyrics = string.IsNullOrWhiteSpace(plainLyrics) ? null : plainLyrics;
        Lines = NormalizeLines(lines);
        ActualGranularity = ComputeVerifiedGranularity(actualGranularity, Lines, PlainLyrics);
    }

    public string Title { get; }
    public string Artist { get; }
    public LyricsProviderId SourceProvider { get; }
    public LyricsTimingGranularity ActualGranularity { get; }
    public IReadOnlyList<LyricLineTiming> Lines { get; }
    public string? PlainLyrics { get; }

    public bool HasTimedSyllables => (ActualGranularity & LyricsTimingGranularity.TimedSyllables) != 0;
    public bool HasTimedWords => (ActualGranularity & LyricsTimingGranularity.TimedWords) != 0;
    public bool HasTimedLines => (ActualGranularity & LyricsTimingGranularity.TimedLines) != 0;
    public bool HasPlainLyrics => (ActualGranularity & LyricsTimingGranularity.PlainLyrics) != 0;

    /// <summary>
    /// Returns the most precise synchronization mode genuinely supported by this timing payload.
    /// Never promotes line-only data to Word or Syllable.
    /// </summary>
    public LyricsSyncMode BestSupportedMode
    {
        get
        {
            if (HasTimedSyllables) return LyricsSyncMode.Syllable;
            if (HasTimedWords) return LyricsSyncMode.Word;
            if (HasTimedLines) return LyricsSyncMode.Line;
            return LyricsSyncMode.Asynchronous;
        }
    }

    public bool SupportsMode(LyricsSyncMode mode) => mode switch
    {
        LyricsSyncMode.Syllable => HasTimedSyllables,
        LyricsSyncMode.Word => HasTimedWords,
        LyricsSyncMode.Line => HasTimedLines,
        LyricsSyncMode.Asynchronous => HasPlainLyrics || Lines.Count > 0,
        LyricsSyncMode.Automatic => HasPlainLyrics || Lines.Count > 0,
        _ => false
    };

    /// <summary>
    /// Converts a legacy <see cref="SyncedLyrics"/> instance into a normalized <see cref="LyricsTimingData"/>
    /// without inventing word or syllable timings that do not exist in the source.
    /// </summary>
    public static LyricsTimingData FromSyncedLyrics(SyncedLyrics? lyrics, LyricsProviderId providerId = LyricsProviderId.Lrclib)
    {
        if (lyrics == null) return Empty;

        if (!lyrics.IsSynced || lyrics.Lines.Count == 0)
        {
            var plainGran = string.IsNullOrWhiteSpace(lyrics.PlainLyrics)
                ? LyricsTimingGranularity.None
                : LyricsTimingGranularity.PlainLyrics;
            return new LyricsTimingData(lyrics.Title, lyrics.Artist, providerId, plainGran, Array.Empty<LyricLineTiming>(), lyrics.PlainLyrics);
        }

        var lineTimings = new List<LyricLineTiming>(lyrics.Lines.Count);
        bool anyWords = false;

        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            TimeSpan start = line.StartTime < TimeSpan.Zero ? TimeSpan.Zero : line.StartTime;
            TimeSpan end = line.EndTime ?? (i + 1 < lyrics.Lines.Count
                ? lyrics.Lines[i + 1].StartTime
                : start + TimeSpan.FromSeconds(3.5));
            if (end < start) end = start + TimeSpan.FromMilliseconds(500);

            var words = new List<LyricWordSegment>();
            var tokens = line.ResolvedTokens;
            if (tokens.Count > 0)
            {
                anyWords = true;
                for (int w = 0; w < tokens.Count; w++)
                {
                    var tk = tokens[w];
                    TimeSpan ws = tk.StartTime < TimeSpan.Zero ? start : tk.StartTime;
                    TimeSpan we = tk.EndTime > ws ? tk.EndTime : ws + TimeSpan.FromMilliseconds(180);
                    words.Add(new LyricWordSegment(tk.Text, ws, we, w, Array.Empty<LyricSyllableTiming>()));
                }
            }

            lineTimings.Add(new LyricLineTiming(i, line.Text, start, end, words, Array.Empty<LyricSyllableTiming>()));
        }

        var granularity = LyricsTimingGranularity.TimedLines;
        if (anyWords) granularity |= LyricsTimingGranularity.TimedWords;
        if (!string.IsNullOrWhiteSpace(lyrics.PlainLyrics)) granularity |= LyricsTimingGranularity.PlainLyrics;

        return new LyricsTimingData(lyrics.Title, lyrics.Artist, providerId, granularity, lineTimings, lyrics.PlainLyrics);
    }

    /// <summary>
    /// Converts normalized <see cref="LyricsTimingData"/> back into a <see cref="SyncedLyrics"/> view model
    /// so existing Lyrow UI, caching, and seek layers work seamlessly.
    /// </summary>
    public SyncedLyrics ToSyncedLyrics()
    {
        if (Lines.Count == 0)
        {
            return new SyncedLyrics(Title, Artist, isSynced: false, Array.Empty<LyricLine>(), PlainLyrics);
        }

        var syncedLines = new List<LyricLine>(Lines.Count);
        for (int i = 0; i < Lines.Count; i++)
        {
            var l = Lines[i];
            List<LyricWordTiming>? wordTimings = null;
            List<LyricToken>? tokens = null;

            if (l.Syllables.Count > 0)
            {
                tokens = new List<LyricToken>(l.Syllables.Count);
                for (int s = 0; s < l.Syllables.Count; s++)
                {
                    var syl = l.Syllables[s];
                    tokens.Add(new LyricToken(syl.Text, syl.StartTime, syl.EndTime));
                }
            }

            if (l.Words.Count > 0)
            {
                wordTimings = new List<LyricWordTiming>(l.Words.Count);
                for (int w = 0; w < l.Words.Count; w++)
                {
                    var wd = l.Words[w];
                    wordTimings.Add(new LyricWordTiming(wd.Text, wd.StartTime, wd.EndTime));
                }
            }

            syncedLines.Add(new LyricLine(l.StartTime, l.Text, i, Words: wordTimings, EndTime: l.EndTime, Tokens: tokens));
        }

        string plain = PlainLyrics ?? string.Join(Environment.NewLine, Lines.Select(l => l.Text));
        return new SyncedLyrics(Title, Artist, isSynced: HasTimedLines, syncedLines, plain);
    }

    private static IReadOnlyList<LyricLineTiming> NormalizeLines(IReadOnlyList<LyricLineTiming>? rawLines)
    {
        if (rawLines == null || rawLines.Count == 0) return Array.Empty<LyricLineTiming>();

        // 1. Filter null/completely blank lines and clamp negative timestamps
        var cleaned = new List<LyricLineTiming>(rawLines.Count);
        for (int i = 0; i < rawLines.Count; i++)
        {
            var line = rawLines[i];
            if (line == null) continue;
            TimeSpan start = line.StartTime < TimeSpan.Zero ? TimeSpan.Zero : line.StartTime;
            TimeSpan end = line.EndTime < start ? start : line.EndTime;
            cleaned.Add(line with { StartTime = start, EndTime = end });
        }

        if (cleaned.Count == 0) return Array.Empty<LyricLineTiming>();

        // 2. Sort chronologically with stable tie-breaking by original LineIndex
        cleaned.Sort((a, b) =>
        {
            int cmp = a.StartTime.CompareTo(b.StartTime);
            return cmp != 0 ? cmp : a.LineIndex.CompareTo(b.LineIndex);
        });

        // 3. Normalize sequential bounds, overlapping end times, and child word/syllable ordering
        var result = new List<LyricLineTiming>(cleaned.Count);
        for (int i = 0; i < cleaned.Count; i++)
        {
            var cur = cleaned[i];
            TimeSpan start = cur.StartTime;
            TimeSpan end = cur.EndTime;

            if (end <= start)
            {
                end = (i + 1 < cleaned.Count && cleaned[i + 1].StartTime > start)
                    ? cleaned[i + 1].StartTime
                    : start + TimeSpan.FromSeconds(3.0);
            }

            // Clamp line end so it does not overrun the next line's start when they overlap
            if (i + 1 < cleaned.Count && cleaned[i + 1].StartTime > start && end > cleaned[i + 1].StartTime)
            {
                end = cleaned[i + 1].StartTime;
            }

            var normWords = NormalizeWords(cur.Words, start, end);
            var normSyllables = NormalizeSyllables(cur.Syllables, start, end);

            result.Add(new LyricLineTiming(i, cur.Text ?? string.Empty, start, end, normWords, normSyllables));
        }

        return result;
    }

    private static IReadOnlyList<LyricWordSegment> NormalizeWords(
        IReadOnlyList<LyricWordSegment>? rawWords,
        TimeSpan lineStart,
        TimeSpan lineEnd)
    {
        if (rawWords == null || rawWords.Count == 0) return Array.Empty<LyricWordSegment>();

        var sorted = rawWords
            .Where(w => w != null && !string.IsNullOrEmpty(w.Text))
            .OrderBy(w => w.StartTime)
            .ThenBy(w => w.WordIndex)
            .ToList();

        if (sorted.Count == 0) return Array.Empty<LyricWordSegment>();

        var normalized = new List<LyricWordSegment>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var w = sorted[i];
            TimeSpan ws = w.StartTime < TimeSpan.Zero ? lineStart : w.StartTime;
            TimeSpan we = w.EndTime > ws ? w.EndTime : (i + 1 < sorted.Count && sorted[i + 1].StartTime > ws ? sorted[i + 1].StartTime : lineEnd);
            if (we < ws) we = ws;
            if (i + 1 < sorted.Count && sorted[i + 1].StartTime > ws && we > sorted[i + 1].StartTime)
            {
                we = sorted[i + 1].StartTime;
            }

            var childSyllables = NormalizeSyllables(w.Syllables, ws, we);
            normalized.Add(new LyricWordSegment(w.Text, ws, we, i, childSyllables));
        }

        return normalized;
    }

    private static IReadOnlyList<LyricSyllableTiming> NormalizeSyllables(
        IReadOnlyList<LyricSyllableTiming>? rawSyllables,
        TimeSpan parentStart,
        TimeSpan parentEnd)
    {
        if (rawSyllables == null || rawSyllables.Count == 0) return Array.Empty<LyricSyllableTiming>();

        var sorted = rawSyllables
            .Where(s => s != null && !string.IsNullOrEmpty(s.Text))
            .OrderBy(s => s.StartTime)
            .ThenBy(s => s.SyllableIndex)
            .ToList();

        if (sorted.Count == 0) return Array.Empty<LyricSyllableTiming>();

        var normalized = new List<LyricSyllableTiming>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var s = sorted[i];
            TimeSpan ss = s.StartTime < TimeSpan.Zero ? parentStart : s.StartTime;
            TimeSpan se = s.EndTime > ss ? s.EndTime : (i + 1 < sorted.Count && sorted[i + 1].StartTime > ss ? sorted[i + 1].StartTime : parentEnd);
            if (se < ss) se = ss;
            if (i + 1 < sorted.Count && sorted[i + 1].StartTime > ss && se > sorted[i + 1].StartTime)
            {
                se = sorted[i + 1].StartTime;
            }

            normalized.Add(new LyricSyllableTiming(s.Text, ss, se, i, s.WordIndex, s.IsWordEnd));
        }

        return normalized;
    }

    private static LyricsTimingGranularity ComputeVerifiedGranularity(
        LyricsTimingGranularity declared,
        IReadOnlyList<LyricLineTiming> lines,
        string? plainLyrics)
    {
        var verified = LyricsTimingGranularity.None;
        if (!string.IsNullOrWhiteSpace(plainLyrics) || lines.Count > 0)
        {
            verified |= LyricsTimingGranularity.PlainLyrics;
        }

        if (lines.Count > 0 && (declared & LyricsTimingGranularity.TimedLines) != 0)
        {
            verified |= LyricsTimingGranularity.TimedLines;
        }

        bool hasRealWords = lines.Any(l => l.Words.Count > 0);
        if (hasRealWords && (declared & LyricsTimingGranularity.TimedWords) != 0)
        {
            verified |= LyricsTimingGranularity.TimedWords;
        }

        bool hasRealSyllables = lines.Any(l => l.Syllables.Count > 0);
        if (hasRealSyllables && (declared & LyricsTimingGranularity.TimedSyllables) != 0)
        {
            verified |= LyricsTimingGranularity.TimedSyllables;
        }

        return verified;
    }
}

/// <summary>
/// A single verified lyrics source available for user switching (&lt; / &gt;) on the current track.
/// </summary>
public sealed record LyricsTrackSourceEntry(
    LyricsProviderId ProviderId,
    LyricsSyncMode ResolvedMode,
    string BadgeCode,
    LyricsTimingData TimingData,
    SyncedLyrics SyncedLyrics,
    string DisplayName);

/// <summary>
/// Outcome of resolving lyrics across prioritized providers for a requested <see cref="LyricsSyncMode"/>.
/// </summary>
public sealed record LyricsResolutionResult(
    bool Success,
    LyricsSyncMode RequestedMode,
    LyricsSyncMode ResolvedMode,
    LyricsSyncMode? RecommendedMode,
    LyricsProviderId? ProviderId,
    LyricsTimingData TimingData,
    SyncedLyrics? SyncedLyrics,
    string StatusMessage,
    IReadOnlyList<LyricsProviderId> AttemptedProviders,
    IReadOnlyList<LyricsTrackSourceEntry>? AvailableSources = null);
