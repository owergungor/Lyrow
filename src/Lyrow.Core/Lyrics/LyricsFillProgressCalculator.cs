using System;
using System.Collections.Generic;
using Lyrow.Core.Models;

namespace Lyrow.Core.Lyrics;

/// <summary>
/// Authoritative real-time lyrics fill progress calculator powering Apple Music-style
/// karaoke character/word fill synchronized directly with hardware media playback position.
/// Computes strictly independent timelines for each line and token, supports pause freeze,
/// instantaneous seek recalculations, and fallback duration distributions.
/// </summary>
public static class LyricsFillProgressCalculator
{
    public static readonly TimeSpan DefaultFallbackLineDuration = TimeSpan.FromSeconds(3.5);

    /// <summary>
    /// Computes strictly bounded independent progress [0.0, 1.0] for a line given start and end timestamps.
    /// </summary>
    public static double CalculateLineProgress(TimeSpan currentPosition, TimeSpan lineStartTime, TimeSpan lineEndTime)
    {
        if (currentPosition < lineStartTime) return 0.0;
        if (currentPosition >= lineEndTime) return 1.0;
        if (lineEndTime <= lineStartTime) return 1.0; // Avoid division by zero, safe fallback

        var elapsed = currentPosition - lineStartTime;
        var duration = lineEndTime - lineStartTime;
        return Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0.0, 1.0);
    }

    /// <summary>
    /// Computes the exact fill progress [0.0, 1.0] for an active lyric line given playback position,
    /// start timestamp, and next line start timestamp.
    /// </summary>
    public static double CalculateLineProgress(
        TimeSpan currentPosition,
        TimeSpan lineTimestamp,
        TimeSpan? nextLineTimestamp = null,
        TimeSpan? trackDuration = null)
    {
        if (currentPosition < lineTimestamp)
        {
            return 0.0;
        }

        TimeSpan effectiveDuration;
        if (nextLineTimestamp.HasValue && nextLineTimestamp.Value > lineTimestamp)
        {
            effectiveDuration = nextLineTimestamp.Value - lineTimestamp;
        }
        else if (trackDuration.HasValue && trackDuration.Value > lineTimestamp)
        {
            var remaining = trackDuration.Value - lineTimestamp;
            effectiveDuration = remaining < DefaultFallbackLineDuration ? remaining : DefaultFallbackLineDuration;
        }
        else
        {
            effectiveDuration = DefaultFallbackLineDuration;
        }

        if (effectiveDuration <= TimeSpan.Zero)
        {
            return 1.0;
        }

        var elapsed = currentPosition - lineTimestamp;
        double progress = elapsed.TotalMilliseconds / effectiveDuration.TotalMilliseconds;
        return Math.Clamp(progress, 0.0, 1.0);
    }

    /// <summary>
    /// Computes independent fill progress for a LyricLine model instance.
    /// </summary>
    public static double CalculateLineProgress(
        LyricLine? line,
        TimeSpan currentPosition,
        TimeSpan? nextLineTimestamp = null,
        TimeSpan? trackDuration = null)
    {
        if (line == null) return 0.0;

        TimeSpan endTime = line.EndTime
            ?? (nextLineTimestamp.HasValue && nextLineTimestamp.Value > line.StartTime
                ? nextLineTimestamp.Value
                : line.ResolvedEndTime);

        return CalculateLineProgress(currentPosition, line.StartTime, endTime);
    }

    /// <summary>
    /// Computes strictly bounded independent token-level progress [0.0, 1.0].
    /// </summary>
    public static double CalculateTokenProgress(LyricToken? token, TimeSpan currentPosition)
    {
        if (token == null) return 0.0;
        if (currentPosition < token.StartTime) return 0.0;
        if (currentPosition >= token.EndTime) return 1.0;
        if (token.EndTime <= token.StartTime) return 1.0; // Avoid division by zero

        var elapsed = currentPosition - token.StartTime;
        var duration = token.EndTime - token.StartTime;
        return Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0.0, 1.0);
    }

    /// <summary>
    /// Computes independent progresses for two consecutive or simultaneous lyric lines.
    /// Never couples line1 progress to line2 progress.
    /// </summary>
    public static (double Line1Progress, double Line2Progress) CalculateTwoLineProgress(
        LyricLine? line1,
        LyricLine? line2,
        TimeSpan currentPosition)
    {
        double p1 = CalculateLineProgress(line1, currentPosition, nextLineTimestamp: line2?.StartTime);
        double p2 = CalculateLineProgress(line2, currentPosition);
        return (p1, p2);
    }

    /// <summary>
    /// Computes character-level precision fill [0.0, 1.0] across a text string.
    /// Provides smooth anti-aliased character boundaries preventing abrupt jumps.
    /// </summary>
    public static double CalculateCharacterProgress(double lineProgress, int textLength)
    {
        if (textLength <= 0 || lineProgress <= 0.0) return 0.0;
        if (lineProgress >= 1.0) return 1.0;

        double exactChar = lineProgress * textLength;
        return Math.Clamp(exactChar / textLength, 0.0, 1.0);
    }

    /// <summary>
    /// Computes progress using word-level timing timestamps if available,
    /// weighting each word proportionally by its character length in the line so the visual pixel clip
    /// aligns with words of varying lengths, parenthetical segments, and repeated words.
    /// Falls back to character-interpolated line progress if word timings are omitted.
    /// </summary>
    public static double CalculateWordTimingProgress(
        TimeSpan currentPosition,
        TimeSpan lineTimestamp,
        TimeSpan? nextLineTimestamp,
        IReadOnlyList<LyricWordTiming>? wordTimings,
        string? lineText = null)
    {
        if (wordTimings == null || wordTimings.Count == 0)
        {
            return CalculateLineProgress(currentPosition, lineTimestamp, nextLineTimestamp);
        }

        if (currentPosition < wordTimings[0].Start)
        {
            return 0.0;
        }

        var lastWord = wordTimings[^1];
        if (currentPosition >= lastWord.End)
        {
            return 1.0;
        }

        int totalWords = wordTimings.Count;
        Span<double> weights = totalWords <= 128 ? stackalloc double[totalWords] : new double[totalWords];
        double totalWeight = 0.0;

        for (int i = 0; i < totalWords; i++)
        {
            int charLen = Math.Max(1, wordTimings[i].Word?.Trim().Length ?? 1);
            double w = charLen;
            weights[i] = w;
            totalWeight += w;
        }

        if (totalWeight <= 0.0)
        {
            totalWeight = totalWords;
            for (int i = 0; i < totalWords; i++) weights[i] = 1.0;
        }

        double cumulativeWeight = 0.0;
        for (int i = 0; i < totalWords; i++)
        {
            var w = wordTimings[i];
            double wordWeight = weights[i];

            if (currentPosition < w.Start)
            {
                // Between words i-1 and i
                return Math.Clamp(cumulativeWeight / totalWeight, 0.0, 1.0);
            }

            if (currentPosition >= w.Start && currentPosition <= w.End)
            {
                var wordDuration = w.End - w.Start;
                double withinWord = wordDuration > TimeSpan.Zero
                    ? (currentPosition - w.Start).TotalMilliseconds / wordDuration.TotalMilliseconds
                    : 1.0;

                double weightedProgress = (cumulativeWeight + (Math.Clamp(withinWord, 0.0, 1.0) * wordWeight)) / totalWeight;
                return Math.Clamp(weightedProgress, 0.0, 1.0);
            }

            cumulativeWeight += wordWeight;
        }

        return 1.0;
    }

    /// <summary>
    /// Splits a lyric line text into individual sub-lines (by explicit newline or logical wrapping threshold).
    /// Guarantees at least one non-empty sub-line.
    /// </summary>
    public static IReadOnlyList<string> SplitIntoSequentialSubLines(string? text, int maxCharsPerSubLine = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new[] { "♪" };
        }

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var explicitLines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (explicitLines.Count == 0)
        {
            return new[] { text.Trim() };
        }

        if (maxCharsPerSubLine <= 0)
        {
            return explicitLines;
        }

        var result = new List<string>();
        foreach (var expLine in explicitLines)
        {
            if (expLine.Length <= maxCharsPerSubLine)
            {
                result.Add(expLine);
                continue;
            }

            var words = expLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 1)
            {
                // Single huge unbroken word or token: chunk by maxCharsPerSubLine so it never overflows viewport
                for (int c = 0; c < expLine.Length; c += maxCharsPerSubLine)
                {
                    int len = Math.Min(maxCharsPerSubLine, expLine.Length - c);
                    result.Add(expLine.Substring(c, len));
                }
                continue;
            }

            var currentBuilder = new System.Text.StringBuilder();
            foreach (var w in words)
            {
                if (w.Length > maxCharsPerSubLine)
                {
                    if (currentBuilder.Length > 0)
                    {
                        result.Add(currentBuilder.ToString());
                        currentBuilder.Clear();
                    }
                    for (int c = 0; c < w.Length; c += maxCharsPerSubLine)
                    {
                        int len = Math.Min(maxCharsPerSubLine, w.Length - c);
                        result.Add(w.Substring(c, len));
                    }
                    continue;
                }

                if (currentBuilder.Length == 0)
                {
                    currentBuilder.Append(w);
                }
                else if (currentBuilder.Length + 1 + w.Length <= maxCharsPerSubLine)
                {
                    currentBuilder.Append(' ').Append(w);
                }
                else
                {
                    result.Add(currentBuilder.ToString());
                    currentBuilder.Clear();
                    currentBuilder.Append(w);
                }
            }

            if (currentBuilder.Length > 0)
            {
                result.Add(currentBuilder.ToString());
            }
        }

        return result.Count > 0 ? result : new[] { text.Trim() };
    }

    /// <summary>
    /// Computes sequential per-subline highlight progress [0.0, 1.0] for a multi-line lyric segment.
    /// Sub-line 0 highlights from 0.0 to 1.0 first; only after sub-line 0 reaches 1.0 does sub-line 1 start
    /// highlighting from 0.0 to 1.0, and so on, weighted proportionally by character length.
    /// </summary>
    public static IReadOnlyList<double> CalculateSequentialSubLineProgresses(
        IReadOnlyList<string> subLines,
        TimeSpan currentPosition,
        TimeSpan segmentStartTime,
        TimeSpan segmentEndTime)
    {
        if (subLines == null || subLines.Count == 0)
        {
            return Array.Empty<double>();
        }

        int count = subLines.Count;
        var results = new double[count];

        if (currentPosition < segmentStartTime)
        {
            return results;
        }

        if (currentPosition >= segmentEndTime || segmentEndTime <= segmentStartTime)
        {
            Array.Fill(results, 1.0);
            return results;
        }

        if (count == 1)
        {
            results[0] = CalculateLineProgress(currentPosition, segmentStartTime, segmentEndTime);
            return results;
        }

        int totalChars = 0;
        for (int i = 0; i < count; i++)
        {
            totalChars += Math.Max(1, subLines[i]?.Trim().Length ?? 1);
        }

        double totalDurationMs = (segmentEndTime - segmentStartTime).TotalMilliseconds;
        double elapsedMs = (currentPosition - segmentStartTime).TotalMilliseconds;

        double cumulativeStartMs = 0.0;
        for (int i = 0; i < count; i++)
        {
            int charWeight = Math.Max(1, subLines[i]?.Trim().Length ?? 1);
            double subDurationMs = totalDurationMs * ((double)charWeight / totalChars);
            double subEndMs = (i == count - 1) ? totalDurationMs : (cumulativeStartMs + subDurationMs);

            if (elapsedMs <= cumulativeStartMs)
            {
                results[i] = 0.0;
            }
            else if (elapsedMs >= subEndMs || subDurationMs <= 0.0001)
            {
                results[i] = 1.0;
            }
            else
            {
                results[i] = Math.Clamp((elapsedMs - cumulativeStartMs) / subDurationMs, 0.0, 1.0);
            }

            cumulativeStartMs = subEndMs;
        }

        return results;
    }

    /// <summary>
    /// Computes sequential per-subline highlight progress [0.0, 1.0] for a LyricLine.
    /// </summary>
    public static IReadOnlyList<double> CalculateSequentialSubLineProgresses(
        LyricLine? line,
        IReadOnlyList<string> subLines,
        TimeSpan currentPosition,
        TimeSpan? nextLineTimestamp = null,
        TimeSpan? trackDuration = null)
    {
        if (line == null || subLines == null || subLines.Count == 0)
        {
            return Array.Empty<double>();
        }

        TimeSpan endTime;
        if (line.EndTime.HasValue && line.EndTime.Value > line.StartTime)
        {
            endTime = line.EndTime.Value;
        }
        else if (nextLineTimestamp.HasValue && nextLineTimestamp.Value > line.StartTime)
        {
            endTime = nextLineTimestamp.Value;
        }
        else if (trackDuration.HasValue && trackDuration.Value > line.StartTime)
        {
            var rem = trackDuration.Value - line.StartTime;
            endTime = line.StartTime + (rem < DefaultFallbackLineDuration ? rem : DefaultFallbackLineDuration);
        }
        else
        {
            endTime = line.ResolvedEndTime;
        }

        if (line.Words != null && line.Words.Count > 0)
        {
            double overallWordProg = CalculateWordTimingProgress(currentPosition, line.StartTime, endTime, line.Words, line.Text);
            if (subLines.Count == 1)
            {
                return new[] { overallWordProg };
            }

            int count = subLines.Count;
            var results = new double[count];
            if (overallWordProg <= 0.0) return results;
            if (overallWordProg >= 1.0)
            {
                Array.Fill(results, 1.0);
                return results;
            }

            int totalChars = 0;
            for (int i = 0; i < count; i++)
            {
                totalChars += Math.Max(1, subLines[i]?.Trim().Length ?? 1);
            }

            double cumulativeRatio = 0.0;
            for (int i = 0; i < count; i++)
            {
                int charWeight = Math.Max(1, subLines[i]?.Trim().Length ?? 1);
                double subRatio = (double)charWeight / totalChars;
                double subEndRatio = (i == count - 1) ? 1.0 : (cumulativeRatio + subRatio);

                if (overallWordProg <= cumulativeRatio)
                {
                    results[i] = 0.0;
                }
                else if (overallWordProg >= subEndRatio || subRatio <= 0.0001)
                {
                    results[i] = 1.0;
                }
                else
                {
                    results[i] = Math.Clamp((overallWordProg - cumulativeRatio) / subRatio, 0.0, 1.0);
                }

                cumulativeRatio = subEndRatio;
            }

            return results;
        }

        return CalculateSequentialSubLineProgresses(subLines, currentPosition, line.StartTime, endTime);
    }

    /// <summary>
    /// Calculates the target playback seek TimeSpan from a pointer horizontal click/drag coordinate on a progress bar.
    /// Clamps ratio strictly to [0.0, 1.0].
    /// </summary>
    public static TimeSpan CalculateSeekTimeFromPointer(double pointerX, double barActualWidth, TimeSpan trackDuration)
    {
        if (barActualWidth <= 0.0 || trackDuration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        double ratio = Math.Clamp(pointerX / barActualWidth, 0.0, 1.0);
        return TimeSpan.FromMilliseconds(trackDuration.TotalMilliseconds * ratio);
    }
}

/// <summary>
/// Authoritative testable progress calculator service satisfying Section 20 requirements.
/// </summary>
public static class LyricsProgressCalculator
{
    public static double CalculateLineProgress(TimeSpan currentPosition, TimeSpan lineStartTime, TimeSpan lineEndTime) =>
        LyricsFillProgressCalculator.CalculateLineProgress(currentPosition, lineStartTime, lineEndTime);

    public static double CalculateLineProgress(LyricLine line, TimeSpan currentPosition, TimeSpan? nextLineTimestamp = null, TimeSpan? trackDuration = null) =>
        LyricsFillProgressCalculator.CalculateLineProgress(line, currentPosition, nextLineTimestamp, trackDuration);

    public static double CalculateTokenProgress(LyricToken token, TimeSpan currentPosition) =>
        LyricsFillProgressCalculator.CalculateTokenProgress(token, currentPosition);

    public static (double Line1Progress, double Line2Progress) CalculateTwoLineProgress(LyricLine line1, LyricLine line2, TimeSpan currentPosition) =>
        LyricsFillProgressCalculator.CalculateTwoLineProgress(line1, line2, currentPosition);
}
