namespace Lyrow.Core.Lyrics;

/// <summary>
/// Pure mathematical engine for Apple Music-style lyrics vertical centering,
/// smooth 60 FPS scroll offset interpolation, and distance-based typography/opacity hierarchy.
/// </summary>
public static class LyricsViewportCalculator
{
    /// <summary>
    /// Computes the exact ScrollViewer VerticalOffset required to place the active line's vertical midpoint
    /// at the exact vertical midpoint of the viewport (viewportHeight / 2.0).
    /// </summary>
    public static double CalculateTargetCenterOffset(
        double viewportHeight,
        double lineTopInContent,
        double lineHeight,
        double maxScrollableHeight = double.MaxValue)
    {
        if (viewportHeight <= 0) return 0.0;
        double lineCenter = lineTopInContent + (Math.Max(0.0, lineHeight) / 2.0);
        double desiredOffset = lineCenter - (viewportHeight / 2.0);
        double clampedMax = Math.Max(0.0, maxScrollableHeight);
        return Math.Clamp(desiredOffset, 0.0, clampedMax);
    }

    /// <summary>
    /// Computes the target VerticalOffset given the active line's current Y coordinate inside the ScrollViewer viewport
    /// and the current VerticalOffset. This is invariant to container margins and paddings.
    /// </summary>
    public static double CalculateTargetCenterOffsetFromViewportPoint(
        double currentVerticalOffset,
        double viewportHeight,
        double lineTopInViewport,
        double lineHeight,
        double maxScrollableHeight = double.MaxValue)
    {
        if (viewportHeight <= 0) return Math.Max(0.0, currentVerticalOffset);
        double lineCenterInViewport = lineTopInViewport + (Math.Max(0.0, lineHeight) / 2.0);
        double viewportCenter = viewportHeight / 2.0;
        double deltaY = lineCenterInViewport - viewportCenter;
        double targetOffset = currentVerticalOffset + deltaY;
        double clampedMax = Math.Max(0.0, maxScrollableHeight);
        return Math.Clamp(targetOffset, 0.0, clampedMax);
    }

    /// <summary>
    /// Smoothly interpolates the current scroll offset toward the target center offset for 60 FPS fluid motion
    /// without sudden jumps or oscillation.
    /// </summary>
    public static double InterpolateScrollOffset(
        double currentOffset,
        double targetOffset,
        double smoothingFactor = 0.16,
        double snapEpsilon = 0.35)
    {
        double diff = targetOffset - currentOffset;
        if (Math.Abs(diff) <= snapEpsilon)
        {
            return targetOffset;
        }

        double factor = Math.Clamp(smoothingFactor, 0.02, 1.0);
        return currentOffset + (diff * factor);
    }

    /// <summary>
    /// Computes target opacity and scale for a lyric line based on its distance from the active line,
    /// following Apple Music's visual hierarchy (Active -> Near -> Far).
    /// </summary>
    public static LyricLineVisualTarget GetLineVisualTarget(int lineIndex, int activeIndex, int fontScalePreset = 0)
    {
        var (activeSize, nearSize) = fontScalePreset switch
        {
            1 => (30.0, 22.0),
            2 => (34.0, 25.0),
            _ => (26.0, 20.0)
        };

        return GetLineVisualTarget(lineIndex, activeIndex, activeSize, nearSize);
    }

    /// <summary>
    /// Computes target opacity, scale, and font size for a lyric line with user-defined focused and unfocused font sizes.
    /// Invariant: active line has full opacity and primary scale; distance fades gracefully.
    /// </summary>
    public static LyricLineVisualTarget GetLineVisualTarget(
        int lineIndex,
        int activeIndex,
        double focusedFontSize,
        double unfocusedFontSize)
    {
        double activeSize = Math.Clamp(focusedFontSize, 18.0, 56.0);
        double nearSize = Math.Clamp(unfocusedFontSize, 12.0, 40.0);
        double farSize = Math.Max(11.0, nearSize * 0.88);

        if (activeIndex < 0)
        {
            // Intro before first line starts: keep first couple lines gently readable
            double introOpacity = lineIndex == 0 ? 0.65 : (lineIndex == 1 ? 0.42 : 0.24);
            return new LyricLineVisualTarget(
                TargetOpacity: introOpacity,
                TargetScale: 1.0,
                FontSize: farSize,
                IsActive: false,
                Distance: lineIndex + 1,
                State: LyricLineMotionState.Idle);
        }

        int distance = Math.Abs(lineIndex - activeIndex);
        var motionState = ResolveLineMotionState(lineIndex, activeIndex);

        if (distance == 0)
        {
            return new LyricLineVisualTarget(
                TargetOpacity: 1.0,
                TargetScale: 1.04,
                FontSize: activeSize,
                IsActive: true,
                Distance: 0,
                State: motionState);
        }

        if (distance == 1)
        {
            return new LyricLineVisualTarget(
                TargetOpacity: 0.56,
                TargetScale: 1.0,
                FontSize: nearSize,
                IsActive: false,
                Distance: 1,
                State: motionState);
        }

        if (distance == 2)
        {
            return new LyricLineVisualTarget(
                TargetOpacity: 0.34,
                TargetScale: 0.98,
                FontSize: farSize,
                IsActive: false,
                Distance: 2,
                State: motionState);
        }

        return new LyricLineVisualTarget(
            TargetOpacity: 0.18,
            TargetScale: 0.96,
            FontSize: farSize,
            IsActive: false,
            Distance: distance,
            State: motionState);
    }

    /// <summary>
    /// Resolves the animation state of a lyric line relative to the current active line.
    /// </summary>
    public static LyricLineMotionState ResolveLineMotionState(int lineIndex, int activeIndex)
    {
        if (activeIndex < 0) return LyricLineMotionState.Idle;
        if (lineIndex == activeIndex) return LyricLineMotionState.Active;
        if (lineIndex == activeIndex + 1) return LyricLineMotionState.ApproachingActive;
        if (lineIndex < activeIndex) return LyricLineMotionState.Passed;
        return LyricLineMotionState.Idle;
    }

    /// <summary>
    /// Smoothly interpolates a scalar property (e.g., Opacity or Scale) toward its target at 60 FPS.
    /// </summary>
    public static double InterpolateValue(double current, double target, double factor = 0.20, double epsilon = 0.004)
    {
        double diff = target - current;
        if (Math.Abs(diff) <= epsilon) return target;
        return current + diff * Math.Clamp(factor, 0.02, 1.0);
    }

    /// <summary>
    /// Computes the exact target media playback position when the user clicks a synced lyric line.
    /// Since LyricsSynchronizer evaluates effectiveTime = mediaPosition + syncOffsetMs,
    /// seeking to (lyricTimestamp - syncOffsetMs) ensures effectiveTime lands on lyricTimestamp.
    /// Applies SyncOffsetMs once and clamps within [0, trackDuration].
    /// </summary>
    public static TimeSpan CalculateTargetMediaSeekPosition(
        TimeSpan lyricTimestamp,
        int syncOffsetMs,
        TimeSpan trackDuration = default)
    {
        var rawTarget = lyricTimestamp - TimeSpan.FromMilliseconds(syncOffsetMs);
        if (rawTarget < TimeSpan.Zero)
        {
            rawTarget = TimeSpan.Zero;
        }

        if (trackDuration > TimeSpan.Zero && rawTarget > trackDuration)
        {
            rawTarget = trackDuration;
        }

        return rawTarget;
    }

    /// <summary>
    /// Width threshold in logical pixels at or above which resizing a Compact (Dar Mod) window
    /// automatically transitions back to Horizontal 50/50 normal mode.
    /// </summary>
    public const double CompactToNormalWidthThreshold = 640.0;

    /// <summary>
    /// Width threshold in logical pixels below which (along with height below NormalToCompactHeightThreshold)
    /// resizing a Horizontal window transitions into Compact mode.
    /// </summary>
    public const double NormalToCompactWidthThreshold = 520.0;
    public const double NormalToCompactHeightThreshold = 260.0;

    /// <summary>
    /// Default logical dimensions (in DIPs) for Compact Mode (Dar Mod) mini-player.
    /// At 150% DPI scaling (scale = 1.5), 352×148 DIPs = 528×222 physical pixels (within 528 × 214–230 px target)
    /// and 60×60 DIPs artwork = 90×90 physical pixels.
    /// </summary>
    public const int CompactDefaultLogicalWidth = 352;
    public const int CompactDefaultLogicalHeight = 148;
    public const int CompactExpandedLogicalHeight = 212;
    public const int CompactMinLogicalWidth = 320;
    public const int CompactMinLogicalHeight = 136;
    public const int CompactArtworkLogicalSize = 60;

    /// <summary>
    /// Computes the exact required window size (logical and physical DPI-scaled pixels) for Compact Mode
    /// with or without the 3-line expanded lyrics view, guaranteeing zero vertical or horizontal clipping.
    /// </summary>
    public static (int LogicalWidth, int LogicalHeight, int PhysicalWidth, int PhysicalHeight) CalculateCompactWindowSize(
        bool isLyricsExpanded,
        double dpiScale = 1.0)
    {
        double safeScale = Math.Clamp(dpiScale > 0 ? dpiScale : 1.0, 1.0, 4.0);
        int logicalW = CompactDefaultLogicalWidth;
        int logicalH = isLyricsExpanded ? CompactExpandedLogicalHeight : CompactDefaultLogicalHeight;
        int physW = (int)Math.Ceiling(logicalW * safeScale);
        int physH = (int)Math.Ceiling(logicalH * safeScale);
        return (logicalW, logicalH, physW, physH);
    }

    /// <summary>
    /// Computes the minimum allowed window size (logical and physical DPI-scaled pixels) for Compact Mode
    /// so minimum window constraints never prevent shrinking to the compact mini-player target size.
    /// </summary>
    public static (int LogicalWidth, int LogicalHeight, int PhysicalWidth, int PhysicalHeight) CalculateCompactMinimumWindowSize(
        double dpiScale = 1.0)
    {
        double safeScale = Math.Clamp(dpiScale > 0 ? dpiScale : 1.0, 1.0, 4.0);
        int logicalW = CompactMinLogicalWidth;
        int logicalH = CompactMinLogicalHeight;
        int physW = (int)Math.Ceiling(logicalW * safeScale);
        int physH = (int)Math.Ceiling(logicalH * safeScale);
        return (logicalW, logicalH, physW, physH);
    }

    /// <summary>
    /// Resolves the strictly 3-line compact lyrics viewport window (Previous, Active, Next) for a given active line index.
    /// Returns empty strings for Previous at the start of the song or Next at the end of the song so the 3-slot layout never overflows.
    /// </summary>
    public static (string PreviousLine, string ActiveLine, string NextLine, int? PreviousIndex, int? ActiveIndex, int? NextIndex) GetCompactThreeLineWindow(
        IReadOnlyList<Lyrow.Core.Models.LyricLine>? lines,
        int activeIndex,
        string? plainLyricsFallback = null)
    {
        if (lines == null || lines.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(plainLyricsFallback))
            {
                var split = plainLyricsFallback
                    .Replace("\r\n", "\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                string l0 = split.Length > 0 ? split[0] : string.Empty;
                string l1 = split.Length > 1 ? split[1] : string.Empty;
                string l2 = split.Length > 2 ? split[2] : string.Empty;
                return (string.Empty, l0, !string.IsNullOrEmpty(l1) ? l1 : l2, null, null, null);
            }
            return (string.Empty, "♪", string.Empty, null, null, null);
        }

        if (activeIndex < 0)
        {
            // Before first timestamp begins: Active slot shows line 0, Next slot shows line 1, Previous is empty
            string first = string.IsNullOrWhiteSpace(lines[0].Text) ? "♪" : lines[0].Text;
            string second = lines.Count > 1 ? (string.IsNullOrWhiteSpace(lines[1].Text) ? "♪" : lines[1].Text) : string.Empty;
            return (string.Empty, first, second, null, 0, lines.Count > 1 ? 1 : null);
        }

        int clampedActive = Math.Clamp(activeIndex, 0, lines.Count - 1);
        int? prevIdx = clampedActive > 0 ? clampedActive - 1 : null;
        int? nextIdx = clampedActive + 1 < lines.Count ? clampedActive + 1 : null;

        string prevText = prevIdx.HasValue
            ? (string.IsNullOrWhiteSpace(lines[prevIdx.Value].Text) ? "♪" : lines[prevIdx.Value].Text)
            : string.Empty;
        string activeText = string.IsNullOrWhiteSpace(lines[clampedActive].Text) ? "♪" : lines[clampedActive].Text;
        string nextText = nextIdx.HasValue
            ? (string.IsNullOrWhiteSpace(lines[nextIdx.Value].Text) ? "♪" : lines[nextIdx.Value].Text)
            : string.Empty;

        return (prevText, activeText, nextText, prevIdx, clampedActive, nextIdx);
    }

    /// <summary>
    /// Computes responsive player and lyrics metrics from the current window dimensions.
    /// Maintains 1:1 square artwork, aligns progress and volume bar widths with artwork width,
    /// and scales typography and control dimensions proportionally.
    /// </summary>
    public static ResponsiveLyricsLayoutMetrics CalculateResponsiveMetrics(
        double windowWidth,
        double windowHeight,
        bool isLyricsPanelVisible = true,
        double baseFocusedFontSize = 28.0,
        double baseUnfocusedFontSize = 20.0)
    {
        double safeW = Math.Max(480.0, windowWidth);
        double safeH = Math.Max(220.0, windowHeight);

        double playerAvailableW = isLyricsPanelVisible ? (safeW * 0.50) - 56.0 : safeW - 80.0;
        double playerAvailableH = safeH - 44.0; // Exclude title bar

        // Artwork takes proportional height and width while leaving room for metadata + controls below it (~235-260px)
        double maxArtByWidth = Math.Clamp(playerAvailableW * 0.78, 160.0, 560.0);
        double maxArtByHeight = Math.Clamp(playerAvailableH - 250.0, 160.0, 560.0);
        double artworkSize = Math.Round(Math.Clamp(Math.Min(maxArtByWidth, maxArtByHeight), 160.0, 560.0));

        double scaleRatio = Math.Clamp(artworkSize / 300.0, 0.72, 1.75);

        double songTitleFontSize = Math.Round(Math.Clamp(22.0 * scaleRatio, 16.0, 34.0), 1);
        double artistAlbumFontSize = Math.Round(Math.Clamp(14.0 * scaleRatio, 12.0, 21.0), 1);
        double timestampFontSize = Math.Round(Math.Clamp(11.0 * scaleRatio, 10.0, 15.0), 1);
        double deviceFontSize = Math.Round(Math.Clamp(12.0 * scaleRatio, 10.5, 16.0), 1);

        double playButtonSize = Math.Round(Math.Clamp(46.0 * scaleRatio, 38.0, 66.0));
        double skipButtonSize = Math.Round(Math.Clamp(36.0 * scaleRatio, 30.0, 52.0));
        double utilityButtonSize = Math.Round(Math.Clamp(34.0 * scaleRatio, 28.0, 46.0));

        double lyricScale = Math.Clamp((safeW / 960.0) * 0.55 + (safeH / 640.0) * 0.45, 0.80, 1.65);
        double scaledFocusedFontSize = Math.Round(Math.Clamp(baseFocusedFontSize * lyricScale, 20.0, 54.0), 1);
        double scaledUnfocusedFontSize = Math.Round(Math.Clamp(baseUnfocusedFontSize * lyricScale, 14.0, 38.0), 1);
        double lyricLineSpacing = Math.Round(Math.Clamp(20.0 * lyricScale, 14.0, 34.0), 1);

        return new ResponsiveLyricsLayoutMetrics(
            ArtworkSize: artworkSize,
            PlayerColumnWidth: artworkSize,
            ProgressBarWidth: artworkSize,
            VolumeBarWidth: artworkSize,
            ScaleRatio: scaleRatio,
            SongTitleFontSize: songTitleFontSize,
            ArtistAlbumFontSize: artistAlbumFontSize,
            TimestampFontSize: timestampFontSize,
            DeviceFontSize: deviceFontSize,
            PlayPauseButtonSize: playButtonSize,
            SkipButtonSize: skipButtonSize,
            UtilityButtonSize: utilityButtonSize,
            FocusedLyricFontSize: scaledFocusedFontSize,
            UnfocusedLyricFontSize: scaledUnfocusedFontSize,
            LyricLineSpacing: lyricLineSpacing);
    }
}

/// <summary>
/// Responsive layout metrics calculated for LyricsWindow based on current viewport dimensions.
/// </summary>
public readonly record struct ResponsiveLyricsLayoutMetrics(
    double ArtworkSize,
    double PlayerColumnWidth,
    double ProgressBarWidth,
    double VolumeBarWidth,
    double ScaleRatio,
    double SongTitleFontSize,
    double ArtistAlbumFontSize,
    double TimestampFontSize,
    double DeviceFontSize,
    double PlayPauseButtonSize,
    double SkipButtonSize,
    double UtilityButtonSize,
    double FocusedLyricFontSize,
    double UnfocusedLyricFontSize,
    double LyricLineSpacing);

/// <summary>
/// Motion states in the Apple Music / VoxelVerse lyric line animation state machine.
/// </summary>
public enum LyricLineMotionState
{
    Idle,
    ApproachingActive,
    Active,
    Passed
}

/// <summary>
/// Target visual parameters for a single lyric line in the Apple Music-style hierarchy.
/// </summary>
public readonly record struct LyricLineVisualTarget(
    double TargetOpacity,
    double TargetScale,
    double FontSize,
    bool IsActive,
    int Distance,
    LyricLineMotionState State = LyricLineMotionState.Idle);
