namespace Lyrow.Core.Lyrics;

/// <summary>
/// High-precision, zero-allocation-per-frame lyrics synchronization engine (Lyrow v5.0).
/// Evaluates active syllable, word, or line segments directly from a single playback position reference,
/// supporting instant seek, pause freeze, clean resume, and atomic track reset.
/// </summary>
public sealed class LyricsSyncEngine
{
    private LyricsTimingData _timingData = LyricsTimingData.Empty;
    private LyricsSyncMode _requestedMode = LyricsSyncMode.Automatic;
    private LyricsSyncMode _effectiveMode = LyricsSyncMode.Asynchronous;
    private int _syncOffsetMs;
    private bool _isPaused;
    private TimeSpan _frozenPosition = TimeSpan.Zero;
    private ActiveLyricSegment _currentSegment = ActiveLyricSegment.None;

    public LyricsTimingData TimingData => _timingData;
    public LyricsSyncMode RequestedMode => _requestedMode;
    public LyricsSyncMode EffectiveMode => _effectiveMode;
    public bool IsPaused => _isPaused;
    public bool HasLyrics => _timingData.Lines.Count > 0 || _timingData.HasPlainLyrics;
    public ActiveLyricSegment CurrentSegment => _currentSegment;

    public int SyncOffsetMs
    {
        get => _syncOffsetMs;
        set => _syncOffsetMs = Math.Clamp(value, -10000, 10000);
    }

    public event EventHandler<ActiveLyricSegment>? ActiveSegmentChanged;

    /// <summary>
    /// Atomically replaces the active track's timing data and resolves the effective synchronization mode.
    /// Clears all previous track timing state immediately.
    /// </summary>
    public void Load(LyricsTimingData? timingData, LyricsSyncMode requestedMode = LyricsSyncMode.Automatic)
    {
        _timingData = timingData ?? LyricsTimingData.Empty;
        _requestedMode = requestedMode;
        _effectiveMode = ResolveEffectiveMode(_timingData, requestedMode);
        _isPaused = false;
        _frozenPosition = TimeSpan.Zero;
        _currentSegment = new ActiveLyricSegment(_effectiveMode, -1, -1, -1, 0.0, 0.0, 0.0, false);
    }

    /// <summary>
    /// Updates the requested synchronization mode for the currently loaded track without refetching.
    /// </summary>
    public void SetMode(LyricsSyncMode requestedMode)
    {
        _requestedMode = requestedMode;
        _effectiveMode = ResolveEffectiveMode(_timingData, requestedMode);
        _currentSegment = Evaluate(_frozenPosition, _isPaused);
    }

    /// <summary>
    /// Clears all loaded timing data and resets active segment indices (called on track switch or stop).
    /// </summary>
    public void Reset()
    {
        _timingData = LyricsTimingData.Empty;
        _effectiveMode = LyricsSyncMode.Asynchronous;
        _isPaused = false;
        _frozenPosition = TimeSpan.Zero;
        _currentSegment = ActiveLyricSegment.None;
    }

    /// <summary>
    /// Freezes segment progression at the current playback position.
    /// </summary>
    public ActiveLyricSegment Pause(TimeSpan currentPosition)
    {
        _isPaused = true;
        _frozenPosition = currentPosition < TimeSpan.Zero ? TimeSpan.Zero : currentPosition;
        var pausedSeg = EvaluateInternal(_frozenPosition, isPaused: true);
        PublishIfChanged(pausedSeg);
        return pausedSeg;
    }

    /// <summary>
    /// Resumes segment progression from the specified playback position.
    /// </summary>
    public ActiveLyricSegment Resume(TimeSpan currentPosition)
    {
        _isPaused = false;
        return Evaluate(currentPosition, isPaused: false);
    }

    /// <summary>
    /// Jumps immediately to the exact segment matching the seeked playback position.
    /// </summary>
    public ActiveLyricSegment Seek(TimeSpan targetPosition)
    {
        _frozenPosition = targetPosition < TimeSpan.Zero ? TimeSpan.Zero : targetPosition;
        var seg = EvaluateInternal(_frozenPosition, _isPaused);
        PublishIfChanged(seg);
        return seg;
    }

    /// <summary>
    /// Evaluates the active line, word, and syllable segment at the given playback position.
    /// Runs in O(log N) time with zero heap allocations.
    /// </summary>
    public ActiveLyricSegment Evaluate(TimeSpan playbackPosition, bool isPaused = false)
    {
        _isPaused = isPaused;
        if (!isPaused)
        {
            _frozenPosition = playbackPosition < TimeSpan.Zero ? TimeSpan.Zero : playbackPosition;
        }

        var evaluated = EvaluateInternal(_frozenPosition, isPaused);
        PublishIfChanged(evaluated);
        return evaluated;
    }

    private ActiveLyricSegment EvaluateInternal(TimeSpan rawPosition, bool isPaused)
    {
        if (_effectiveMode == LyricsSyncMode.Asynchronous || _timingData.Lines.Count == 0)
        {
            return new ActiveLyricSegment(LyricsSyncMode.Asynchronous, -1, -1, -1, 0.0, 0.0, 0.0, isPaused);
        }

        TimeSpan effectivePos = rawPosition + TimeSpan.FromMilliseconds(_syncOffsetMs);
        if (effectivePos < TimeSpan.Zero) effectivePos = TimeSpan.Zero;

        int lineIdx = FindActiveLineIndex(_timingData.Lines, effectivePos);
        if (lineIdx < 0)
        {
            return new ActiveLyricSegment(_effectiveMode, -1, -1, -1, 0.0, 0.0, 0.0, isPaused);
        }

        var line = _timingData.Lines[lineIdx];
        double lineProgress = ComputeProgress(effectivePos, line.StartTime, line.EndTime);

        int wordIdx = -1;
        double wordProgress = 0.0;
        int syllableIdx = -1;
        double syllableProgress = 0.0;

        if (_effectiveMode == LyricsSyncMode.Word && line.Words.Count > 0)
        {
            wordIdx = FindActiveWordIndex(line.Words, effectivePos);
            if (wordIdx >= 0)
            {
                var w = line.Words[wordIdx];
                wordProgress = ComputeProgress(effectivePos, w.StartTime, w.EndTime);
            }
        }
        else if (_effectiveMode == LyricsSyncMode.Syllable && line.Syllables.Count > 0)
        {
            syllableIdx = FindActiveSyllableIndex(line.Syllables, effectivePos);
            if (syllableIdx >= 0)
            {
                var syl = line.Syllables[syllableIdx];
                syllableProgress = ComputeProgress(effectivePos, syl.StartTime, syl.EndTime);
                wordIdx = syl.WordIndex;
                if (wordIdx >= 0 && wordIdx < line.Words.Count)
                {
                    var w = line.Words[wordIdx];
                    wordProgress = ComputeProgress(effectivePos, w.StartTime, w.EndTime);
                }
            }
        }

        return new ActiveLyricSegment(
            _effectiveMode,
            lineIdx,
            wordIdx,
            syllableIdx,
            lineProgress,
            wordProgress,
            syllableProgress,
            isPaused);
    }

    private void PublishIfChanged(ActiveLyricSegment next)
    {
        bool indexChanged =
            next.LineIndex != _currentSegment.LineIndex ||
            next.WordIndex != _currentSegment.WordIndex ||
            next.SyllableIndex != _currentSegment.SyllableIndex ||
            next.EffectiveMode != _currentSegment.EffectiveMode ||
            next.IsPaused != _currentSegment.IsPaused;

        _currentSegment = next;
        if (indexChanged)
        {
            ActiveSegmentChanged?.Invoke(this, next);
        }
    }

    private static LyricsSyncMode ResolveEffectiveMode(LyricsTimingData data, LyricsSyncMode requested)
    {
        if (data.Lines.Count == 0) return LyricsSyncMode.Asynchronous;
        if (requested == LyricsSyncMode.Automatic) return data.BestSupportedMode;
        if (requested == LyricsSyncMode.Asynchronous) return LyricsSyncMode.Asynchronous;

        // Strictly honor requested mode only if real timing data exists; otherwise fall back safely without fabricating data
        if (data.SupportsMode(requested)) return requested;
        return data.BestSupportedMode;
    }

    private static int FindActiveLineIndex(IReadOnlyList<LyricLineTiming> lines, TimeSpan pos)
    {
        if (lines.Count == 0 || pos < lines[0].StartTime) return -1;

        int low = 0;
        int high = lines.Count - 1;
        int candidate = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            if (lines[mid].StartTime <= pos)
            {
                candidate = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return candidate;
    }

    private static int FindActiveWordIndex(IReadOnlyList<LyricWordSegment> words, TimeSpan pos)
    {
        if (words.Count == 0 || pos < words[0].StartTime) return -1;

        int low = 0;
        int high = words.Count - 1;
        int candidate = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            if (words[mid].StartTime <= pos)
            {
                candidate = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        // Do not keep highlighting a word past its explicit end if there is a gap before the next word
        if (candidate >= 0 && pos > words[candidate].EndTime && candidate + 1 < words.Count && pos < words[candidate + 1].StartTime)
        {
            return -1;
        }

        return candidate;
    }

    private static int FindActiveSyllableIndex(IReadOnlyList<LyricSyllableTiming> syllables, TimeSpan pos)
    {
        if (syllables.Count == 0 || pos < syllables[0].StartTime) return -1;

        int low = 0;
        int high = syllables.Count - 1;
        int candidate = -1;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            if (syllables[mid].StartTime <= pos)
            {
                candidate = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (candidate >= 0 && pos > syllables[candidate].EndTime && candidate + 1 < syllables.Count && pos < syllables[candidate + 1].StartTime)
        {
            return -1;
        }

        return candidate;
    }

    private static double ComputeProgress(TimeSpan pos, TimeSpan start, TimeSpan end)
    {
        if (pos <= start) return 0.0;
        if (pos >= end) return 1.0;
        double totalMs = (end - start).TotalMilliseconds;
        if (totalMs <= 1.0) return 1.0;
        return Math.Clamp((pos - start).TotalMilliseconds / totalMs, 0.0, 1.0);
    }
}
