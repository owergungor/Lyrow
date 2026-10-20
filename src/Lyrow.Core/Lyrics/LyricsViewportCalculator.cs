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
        var (activeSize, nearSize, farSize) = fontScalePreset switch
        {
            1 => (30.0, 22.0, 19.0),
            2 => (34.0, 25.0, 21.0),
            _ => (26.0, 20.0, 17.5)
        };

        if (activeIndex < 0)
        {
            // Intro before first line starts: keep first couple lines gently readable
            double introOpacity = lineIndex == 0 ? 0.65 : (lineIndex == 1 ? 0.42 : 0.24);
            return new LyricLineVisualTarget(
                TargetOpacity: introOpacity,
                TargetScale: 1.0,
                FontSize: farSize,
                IsActive: false,
                Distance: lineIndex + 1);
        }

        int distance = Math.Abs(lineIndex - activeIndex);

        if (distance == 0)
        {
            return new LyricLineVisualTarget(
                TargetOpacity: 1.0,
                TargetScale: 1.04,
                FontSize: activeSize,
                IsActive: true,
                Distance: 0);
        }

        if (distance == 1)
        {
            return new LyricLineVisualTarget(
                TargetOpacity: 0.56,
                TargetScale: 1.0,
                FontSize: nearSize,
                IsActive: false,
                Distance: 1);
        }

        if (distance == 2)
        {
            return new LyricLineVisualTarget(
                TargetOpacity: 0.34,
                TargetScale: 0.98,
                FontSize: farSize,
                IsActive: false,
                Distance: 2);
        }

        return new LyricLineVisualTarget(
            TargetOpacity: 0.18,
            TargetScale: 0.96,
            FontSize: farSize,
            IsActive: false,
            Distance: distance);
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
}

/// <summary>
/// Target visual parameters for a single lyric line in the Apple Music-style hierarchy.
/// </summary>
public readonly record struct LyricLineVisualTarget(
    double TargetOpacity,
    double TargetScale,
    double FontSize,
    bool IsActive,
    int Distance);
