using Lyrow.Core.Models;

namespace Lyrow.Core.Lyrics;

/// <summary>
/// Deterministic coordinator for manual lyric line click-to-seek selection.
/// Guarantees 1:1 visual-index to lyric-line mapping, prevents rapid overlapping clicks
/// from overwriting newer selections, prevents sub-millisecond/seek-rounding fallback to
/// the previous line, and avoids auto-sync fighting during the brief seek settling window.
/// </summary>
public sealed class LyricLineClickCoordinator
{
    private readonly object _lock = new();
    private long _sequence;
    private int _pendingLineIndex = -1;
    private DateTime _pendingUntilUtc = DateTime.MinValue;

    /// <summary>
    /// Default tiny interior nudge applied when seeking to a lyric line's start timestamp
    /// so player/GSMTC rounding or sub-millisecond truncation never lands 1 tick before
    /// the clicked line's StartTime (which would otherwise highlight lineIndex - 1).
    /// </summary>
    public static readonly TimeSpan DefaultInteriorNudge = TimeSpan.FromMilliseconds(15);

    /// <summary>
    /// Maximum settling window during which stale pre-seek timer ticks cannot yank
    /// the highlighted active line away from the user's just-clicked line.
    /// </summary>
    public static readonly TimeSpan DefaultSettlingWindow = TimeSpan.FromMilliseconds(450);

    public long CurrentSequence
    {
        get { lock (_lock) return _sequence; }
    }

    public int PendingLineIndex
    {
        get { lock (_lock) return _pendingLineIndex; }
    }

    /// <summary>
    /// Resolves the exact target seek timestamp for a clicked lyric line index.
    /// Uses a tiny safe interior nudge (strictly bounded below the next line's timestamp
    /// and clamped to trackDuration) so seeking always lands inside the clicked line.
    /// </summary>
    public static (bool IsValid, int LineIndex, TimeSpan SeekTimestamp) ResolveSeekTarget(
        IReadOnlyList<LyricLine>? lines,
        int clickedLineIndex,
        TimeSpan trackDuration = default,
        TimeSpan? interiorNudge = null)
    {
        if (lines == null || clickedLineIndex < 0 || clickedLineIndex >= lines.Count)
        {
            return (false, -1, TimeSpan.Zero);
        }

        TimeSpan start = lines[clickedLineIndex].Timestamp;
        if (start < TimeSpan.Zero) start = TimeSpan.Zero;

        TimeSpan? nextStart = (clickedLineIndex + 1 < lines.Count)
            ? lines[clickedLineIndex + 1].Timestamp
            : null;

        TimeSpan target = ComputeSafeInteriorTimestamp(start, nextStart, trackDuration, interiorNudge ?? DefaultInteriorNudge);
        return (true, clickedLineIndex, target);
    }

    /// <summary>
    /// Overload accepting <see cref="LyricLineTiming"/> directly from <see cref="LyricsTimingData"/>
    /// so syllable/word/line sync engines and UI line clicks always share identical timestamps.
    /// </summary>
    public static (bool IsValid, int LineIndex, TimeSpan SeekTimestamp) ResolveSeekTarget(
        IReadOnlyList<LyricLineTiming>? timingLines,
        int clickedLineIndex,
        TimeSpan trackDuration = default,
        TimeSpan? interiorNudge = null)
    {
        if (timingLines == null || clickedLineIndex < 0 || clickedLineIndex >= timingLines.Count)
        {
            return (false, -1, TimeSpan.Zero);
        }

        TimeSpan start = timingLines[clickedLineIndex].StartTime;
        if (start < TimeSpan.Zero) start = TimeSpan.Zero;

        TimeSpan? nextStart = (clickedLineIndex + 1 < timingLines.Count)
            ? timingLines[clickedLineIndex + 1].StartTime
            : null;

        TimeSpan target = ComputeSafeInteriorTimestamp(start, nextStart, trackDuration, interiorNudge ?? DefaultInteriorNudge);
        return (true, clickedLineIndex, target);
    }

    private static TimeSpan ComputeSafeInteriorTimestamp(
        TimeSpan start,
        TimeSpan? nextStart,
        TimeSpan trackDuration,
        TimeSpan maxNudge)
    {
        if (start < TimeSpan.Zero) start = TimeSpan.Zero;
        if (maxNudge < TimeSpan.Zero) maxNudge = TimeSpan.Zero;

        TimeSpan effectiveNudge = maxNudge;
        if (nextStart.HasValue)
        {
            var gap = nextStart.Value - start;
            if (gap <= TimeSpan.Zero)
            {
                effectiveNudge = TimeSpan.Zero;
            }
            else
            {
                // Strictly stay within [start, nextStart) — never cross into the next line
                var halfGap = TimeSpan.FromTicks(gap.Ticks / 2);
                if (effectiveNudge > halfGap)
                {
                    effectiveNudge = halfGap;
                }
            }
        }

        var target = start + effectiveNudge;
        if (trackDuration > TimeSpan.Zero && target > trackDuration)
        {
            target = start <= trackDuration ? start : trackDuration;
        }

        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        return target;
    }

    /// <summary>
    /// Starts a new manual line click selection and returns its monotonic sequence ID.
    /// Any previously in-flight click automatically becomes stale.
    /// </summary>
    public long BeginSelection(int lineIndex, DateTime utcNow, TimeSpan? settlingWindow = null)
    {
        lock (_lock)
        {
            _sequence++;
            _pendingLineIndex = lineIndex;
            _pendingUntilUtc = utcNow + (settlingWindow ?? DefaultSettlingWindow);
            return _sequence;
        }
    }

    /// <summary>
    /// Completes an in-flight seek operation.
    /// Returns true only if this completion still corresponds to the latest click and succeeded.
    /// Immediately releases the temporary manual-selection lock so automatic lyric progression resumes right away.
    /// </summary>
    public bool TryCompleteSelection(long sequenceId, bool seekSucceeded)
    {
        lock (_lock)
        {
            if (sequenceId != _sequence)
            {
                return false;
            }

            _pendingLineIndex = -1;
            _pendingUntilUtc = DateTime.MinValue;
            return seekSucceeded;
        }
    }

    /// <summary>
    /// Invalidates any in-flight or pending manual line selection (e.g., on track switch or progress bar seek).
    /// </summary>
    public void InvalidatePending()
    {
        lock (_lock)
        {
            _sequence++;
            _pendingLineIndex = -1;
            _pendingUntilUtc = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Reconciles the auto-evaluated active line index with any active manual click settling window
    /// so stale pre-seek ticks never fight or overwrite the user's clicked line.
    /// </summary>
    public int ResolveActiveLineIndex(int evaluatedActiveIndex, int lineCount, DateTime utcNow)
    {
        lock (_lock)
        {
            if (_pendingLineIndex >= 0 && _pendingLineIndex < lineCount && utcNow <= _pendingUntilUtc)
            {
                // Release immediately once playback reaches or advances past the clicked line
                if (evaluatedActiveIndex >= _pendingLineIndex)
                {
                    _pendingLineIndex = -1;
                    _pendingUntilUtc = DateTime.MinValue;
                    return evaluatedActiveIndex;
                }

                return _pendingLineIndex;
            }

            if (_pendingLineIndex >= 0)
            {
                _pendingLineIndex = -1;
                _pendingUntilUtc = DateTime.MinValue;
            }

            return evaluatedActiveIndex;
        }
    }
}
