namespace Lyrow.Core.Lyrics;

/// <summary>
/// Computed visual styling state for a single lyric segment (syllable, word, or line).
/// </summary>
public readonly record struct LyricSegmentVisualState(
    double Opacity,
    double GlowIntensity,
    double Scale,
    double FillProgress,
    bool IsActive,
    bool IsPassed);

/// <summary>
/// Zero-allocation snapshot describing the real-time flow glow and layered
/// Syllable + Word + Line highlight geometry for an active lyric line.
/// </summary>
public readonly record struct LyricFlowHighlightSnapshot(
    LyricsSyncMode EffectiveMode,
    bool HasTimedHighlight,
    double LineFillRatio,
    double ActiveWordStartRatio,
    double ActiveWordEndRatio,
    double WordHighlightIntensity,
    double ActiveSyllableStartRatio,
    double ActiveSyllableEndRatio,
    double SyllableHighlightIntensity,
    double FlowGlowIntensity,
    bool IsFlowFrozen)
{
    public static readonly LyricFlowHighlightSnapshot None = new(
        LyricsSyncMode.Asynchronous,
        HasTimedHighlight: false,
        LineFillRatio: 0.0,
        ActiveWordStartRatio: 0.0,
        ActiveWordEndRatio: 0.0,
        WordHighlightIntensity: 0.0,
        ActiveSyllableStartRatio: 0.0,
        ActiveSyllableEndRatio: 0.0,
        SyllableHighlightIntensity: 0.0,
        FlowGlowIntensity: 0.0,
        IsFlowFrozen: false);
}

/// <summary>
/// Pure, UI-framework-independent visual highlight and flow glow calculator for Lyrow v5.0.
/// Computes smooth musical flow progress, layered syllable+word dominance, controlled glow,
/// and accessible contrast for both Dark and Album-Adaptive Light themes with zero allocations.
/// </summary>
public static class LyricsHighlightRenderer
{
    public static LyricSegmentVisualState ComputeSegmentState(
        int segmentIndex,
        int activeSegmentIndex,
        double activeProgress,
        bool isLightTheme,
        bool glowEnabled,
        bool isPaused = false)
    {
        if (activeSegmentIndex < 0)
        {
            return new LyricSegmentVisualState(
                Opacity: isLightTheme ? 0.72 : 0.62,
                GlowIntensity: 0.0,
                Scale: 1.0,
                FillProgress: 0.0,
                IsActive: false,
                IsPassed: false);
        }

        if (segmentIndex < activeSegmentIndex)
        {
            // Already sung segment: returns cleanly to readable passive state
            return new LyricSegmentVisualState(
                Opacity: isLightTheme ? 0.84 : 0.78,
                GlowIntensity: 0.0,
                Scale: 1.0,
                FillProgress: 1.0,
                IsActive: false,
                IsPassed: true);
        }

        if (segmentIndex == activeSegmentIndex)
        {
            double clamped = Math.Clamp(activeProgress, 0.0, 1.0);
            // Subtle breathing peak mid-segment, frozen cleanly when paused
            double pulse = isPaused ? 0.5 : Math.Sin(clamped * Math.PI);
            double glow = glowEnabled
                ? (isLightTheme ? 0.28 + 0.16 * pulse : 0.45 + 0.25 * pulse)
                : 0.0;
            double scale = 1.0 + (0.035 * pulse);

            return new LyricSegmentVisualState(
                Opacity: 1.0,
                GlowIntensity: glow,
                Scale: scale,
                FillProgress: clamped,
                IsActive: true,
                IsPassed: false);
        }

        // Upcoming segment: clearly legible yet visually secondary
        return new LyricSegmentVisualState(
            Opacity: isLightTheme ? 0.64 : 0.52,
            GlowIntensity: 0.0,
            Scale: 1.0,
            FillProgress: 0.0,
            IsActive: false,
            IsPassed: false);
    }

    /// <summary>
    /// Smoothly advances the displayed flow glow progress with natural easing during playback,
    /// freezes instantaneously when paused, and snaps immediately on seek or line change.
    /// </summary>
    public static double AdvanceSmoothFlowRatio(
        double currentDisplayedRatio,
        double targetRatio,
        bool isPaused,
        bool isSeekOrLineChange,
        double easingFactor = 0.32)
    {
        double clampedTarget = Math.Clamp(targetRatio, 0.0, 1.0);
        if (isSeekOrLineChange)
        {
            return clampedTarget;
        }
        if (isPaused)
        {
            return Math.Clamp(currentDisplayedRatio, 0.0, 1.0);
        }

        double diff = clampedTarget - currentDisplayedRatio;
        if (Math.Abs(diff) < 0.0015)
        {
            return clampedTarget;
        }
        // Large backward jump (e.g. external seek) snaps immediately instead of sliding backwards
        if (diff < -0.18)
        {
            return clampedTarget;
        }

        double alpha = Math.Clamp(easingFactor, 0.05, 1.0);
        return Math.Clamp(currentDisplayedRatio + (diff * alpha), 0.0, 1.0);
    }

    /// <summary>
    /// Computes the complete zero-allocation flow glow and layered highlight state for an active line:
    /// - Syllable mode: active syllable is most dominant, containing word receives a softer secondary highlight,
    ///   and flow glow progresses smoothly across real syllable timestamps.
    /// - Word mode: flow glow progresses across real word timestamps without fabricating syllables.
    /// - Line mode: flow glow sweeps naturally left-to-right across the line duration without fabricating word/syllable timing.
    /// - Asynchronous mode: zero timed highlight and zero fake glow.
    /// </summary>
    public static LyricFlowHighlightSnapshot ComputeFlowHighlightSnapshot(
        LyricLineTiming? line,
        ActiveLyricSegment active,
        bool isLightTheme,
        bool glowEnabled)
    {
        if (line == null || active.EffectiveMode == LyricsSyncMode.Asynchronous || active.LineIndex != line.LineIndex)
        {
            return LyricFlowHighlightSnapshot.None;
        }

        double baseGlow = glowEnabled ? (isLightTheme ? 0.34 : 0.56) : 0.0;
        double totalChars = Math.Max(1.0, line.Text.Length);

        // 1. Syllable mode: Syllable (dominant) + Word (softer secondary) + smooth flow progress
        if (active.EffectiveMode == LyricsSyncMode.Syllable && line.Syllables.Count > 0 && active.SyllableIndex >= 0)
        {
            double sylStart = 0.0;
            double sylEnd = 0.0;
            double fillRatio = 0.0;
            double cursor = 0.0;

            for (int i = 0; i < line.Syllables.Count; i++)
            {
                var syl = line.Syllables[i];
                int charLen = syl.Text.Length;
                int stepLen = charLen + (syl.IsWordEnd ? 1 : 0);

                if (i == active.SyllableIndex)
                {
                    sylStart = Math.Clamp(cursor / totalChars, 0.0, 1.0);
                    sylEnd = Math.Clamp((cursor + charLen) / totalChars, 0.0, 1.0);
                    fillRatio = Math.Clamp((cursor + (stepLen * Math.Clamp(active.SyllableProgress, 0.0, 1.0))) / totalChars, 0.0, 1.0);
                    break;
                }
                cursor += stepLen;
            }

            // Locate containing word span for softer word-level emphasis
            double wordStart = sylStart;
            double wordEnd = sylEnd;
            if (line.Words.Count > 0 && active.WordIndex >= 0 && active.WordIndex < line.Words.Count)
            {
                double wCursor = 0.0;
                for (int w = 0; w < line.Words.Count; w++)
                {
                    int wLen = line.Words[w].Text.Length;
                    if (w == active.WordIndex)
                    {
                        wordStart = Math.Clamp(wCursor / totalChars, 0.0, 1.0);
                        wordEnd = Math.Clamp((wCursor + wLen) / totalChars, 0.0, 1.0);
                        break;
                    }
                    wCursor += wLen + 1;
                }
            }

            double syllableIntensity = 1.0;
            double wordIntensity = isLightTheme ? 0.54 : 0.58; // Softly highlights containing word; strictly subordinate to active syllable
            double pulse = active.IsPaused ? 0.5 : Math.Sin(Math.Clamp(active.SyllableProgress, 0.0, 1.0) * Math.PI);
            double glow = glowEnabled ? baseGlow + (isLightTheme ? 0.14 : 0.22) * pulse : 0.0;

            return new LyricFlowHighlightSnapshot(
                EffectiveMode: LyricsSyncMode.Syllable,
                HasTimedHighlight: true,
                LineFillRatio: fillRatio,
                ActiveWordStartRatio: wordStart,
                ActiveWordEndRatio: wordEnd,
                WordHighlightIntensity: wordIntensity,
                ActiveSyllableStartRatio: sylStart,
                ActiveSyllableEndRatio: sylEnd,
                SyllableHighlightIntensity: syllableIntensity,
                FlowGlowIntensity: glow,
                IsFlowFrozen: active.IsPaused);
        }

        // 2. Word mode: real word highlight + smooth word flow glow (no fake syllables)
        if (active.EffectiveMode == LyricsSyncMode.Word && line.Words.Count > 0 && active.WordIndex >= 0)
        {
            double wordStart = 0.0;
            double wordEnd = 0.0;
            double fillRatio = 0.0;
            double wCursor = 0.0;

            for (int w = 0; w < line.Words.Count; w++)
            {
                int wLen = line.Words[w].Text.Length;
                int stepLen = wLen + (w < line.Words.Count - 1 ? 1 : 0);
                if (w == active.WordIndex)
                {
                    wordStart = Math.Clamp(wCursor / totalChars, 0.0, 1.0);
                    wordEnd = Math.Clamp((wCursor + wLen) / totalChars, 0.0, 1.0);
                    fillRatio = Math.Clamp((wCursor + (stepLen * Math.Clamp(active.WordProgress, 0.0, 1.0))) / totalChars, 0.0, 1.0);
                    break;
                }
                wCursor += stepLen;
            }

            double pulse = active.IsPaused ? 0.5 : Math.Sin(Math.Clamp(active.WordProgress, 0.0, 1.0) * Math.PI);
            double glow = glowEnabled ? baseGlow + (isLightTheme ? 0.10 : 0.18) * pulse : 0.0;

            return new LyricFlowHighlightSnapshot(
                EffectiveMode: LyricsSyncMode.Word,
                HasTimedHighlight: true,
                LineFillRatio: fillRatio,
                ActiveWordStartRatio: wordStart,
                ActiveWordEndRatio: wordEnd,
                WordHighlightIntensity: 0.92,
                ActiveSyllableStartRatio: 0.0,
                ActiveSyllableEndRatio: 0.0,
                SyllableHighlightIntensity: 0.0,
                FlowGlowIntensity: glow,
                IsFlowFrozen: active.IsPaused);
        }

        // 3. Line mode: natural left-to-right flow glow across line duration (no fake word/syllable timing)
        double lineRatio = Math.Clamp(active.LineProgress, 0.0, 1.0);
        double linePulse = active.IsPaused ? 0.5 : Math.Sin(lineRatio * Math.PI);
        double lineGlow = glowEnabled ? baseGlow + (isLightTheme ? 0.08 : 0.14) * linePulse : 0.0;

        return new LyricFlowHighlightSnapshot(
            EffectiveMode: LyricsSyncMode.Line,
            HasTimedHighlight: true,
            LineFillRatio: lineRatio,
            ActiveWordStartRatio: 0.0,
            ActiveWordEndRatio: 0.0,
            WordHighlightIntensity: 0.0,
            ActiveSyllableStartRatio: 0.0,
            ActiveSyllableEndRatio: 0.0,
            SyllableHighlightIntensity: 0.0,
            FlowGlowIntensity: lineGlow,
            IsFlowFrozen: active.IsPaused);
    }

    /// <summary>
    /// Computes accurate line-level highlight fill fraction from real syllable, word, or line progress,
    /// restoring smooth left-to-right flow glow in Line mode while never fabricating granular timings.
    /// </summary>
    public static double ComputeExactLineFillRatio(LyricLineTiming line, ActiveLyricSegment active)
    {
        if (line == null || active.LineIndex != line.LineIndex) return 0.0;

        if (active.EffectiveMode == LyricsSyncMode.Syllable && line.Syllables.Count > 0)
        {
            if (active.SyllableIndex < 0) return 0.0;
            double totalChars = Math.Max(1, line.Text.Length);
            double sungChars = 0;
            for (int i = 0; i < line.Syllables.Count; i++)
            {
                int len = line.Syllables[i].Text.Length + (line.Syllables[i].IsWordEnd ? 1 : 0);
                if (i < active.SyllableIndex)
                {
                    sungChars += len;
                }
                else if (i == active.SyllableIndex)
                {
                    sungChars += len * Math.Clamp(active.SyllableProgress, 0.0, 1.0);
                    break;
                }
            }
            return Math.Clamp(sungChars / totalChars, 0.0, 1.0);
        }

        if (active.EffectiveMode == LyricsSyncMode.Word && line.Words.Count > 0)
        {
            if (active.WordIndex < 0) return 0.0;
            double totalChars = Math.Max(1, line.Text.Length);
            double sungChars = 0;
            for (int i = 0; i < line.Words.Count; i++)
            {
                int len = line.Words[i].Text.Length + (i < line.Words.Count - 1 ? 1 : 0);
                if (i < active.WordIndex)
                {
                    sungChars += len;
                }
                else if (i == active.WordIndex)
                {
                    sungChars += len * Math.Clamp(active.WordProgress, 0.0, 1.0);
                    break;
                }
            }
            return Math.Clamp(sungChars / totalChars, 0.0, 1.0);
        }

        if (active.EffectiveMode == LyricsSyncMode.Line)
        {
            return Math.Clamp(active.LineProgress, 0.0, 1.0);
        }

        return 0.0; // Asynchronous mode: zero artificial highlight
    }
}
