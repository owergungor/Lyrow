namespace Lyrow.Core.Layout;

/// <summary>
/// Pure, DPI-independent window rect representation for width toggle calculations.
/// </summary>
public readonly record struct WindowRectBounds(int X, int Y, int Width, int Height);

/// <summary>
/// Result of computing the target window geometry when showing or hiding the lyrics panel.
/// </summary>
public readonly record struct LyricsWidthToggleResult(
    WindowRectBounds TargetBounds,
    int RecordedSinglePanelWidthPx);

/// <summary>
/// Deterministic calculator for Normal Mode lyrics Show/Hide 2x / 0.5x window width transitions.
/// Preserves Y position and window height strictly, clamps to monitor work area, and prevents
/// width drift or screen overflow across DPI scales and rapid double-toggles.
/// </summary>
public static class LyricsWindowWidthAnimatorCalculator
{
    public const int DefaultSinglePanelLogicalWidth = 480;
    public const int DefaultMinSinglePanelLogicalWidth = 380;

    /// <summary>
    /// Computes the target window bounds when toggling lyrics visibility in Normal Mode.
    /// </summary>
    /// <param name="currentBounds">Current physical window bounds (X, Y, Width, Height).</param>
    /// <param name="workArea">Monitor work area bounds in physical pixels.</param>
    /// <param name="isExpandingToShowLyrics">True when showing lyrics (2x width); false when hiding lyrics (0.5x width).</param>
    /// <param name="savedSinglePanelWidthPx">Previously recorded single-panel width in physical pixels (0 if none yet).</param>
    /// <param name="dpiScale">Current monitor DPI scale factor (e.g. 1.0, 1.25, 1.5, 2.0).</param>
    public static LyricsWidthToggleResult ComputeTargetBounds(
        WindowRectBounds currentBounds,
        WindowRectBounds workArea,
        bool isExpandingToShowLyrics,
        int savedSinglePanelWidthPx,
        double dpiScale)
    {
        double safeScale = dpiScale > 0.25 ? dpiScale : 1.0;
        int minSinglePx = Math.Max(200, (int)Math.Round(DefaultMinSinglePanelLogicalWidth * safeScale));
        int maxWorkWidth = workArea.Width > 0 ? workArea.Width : Math.Max(currentBounds.Width * 2, minSinglePx * 2);

        int targetWidth;
        int recordedSingleWidth;

        if (isExpandingToShowLyrics)
        {
            // Base single-panel width: prefer previously recorded width if valid, else current width
            int baseSingle = savedSinglePanelWidthPx >= minSinglePx
                ? savedSinglePanelWidthPx
                : Math.Max(minSinglePx, currentBounds.Width);

            recordedSingleWidth = baseSingle;
            int desiredDualWidth = baseSingle * 2;
            targetWidth = Math.Min(desiredDualWidth, maxWorkWidth);
        }
        else
        {
            // Collapsing from dual-panel to single-panel: exact half of current width (or saved single width if already recorded from same session)
            int halfWidth = (int)Math.Round(currentBounds.Width / 2.0);
            targetWidth = Math.Clamp(halfWidth, minSinglePx, maxWorkWidth);
            recordedSingleWidth = targetWidth;
        }

        // Preserve Y and Height strictly unchanged; keep X anchored on left unless right edge overflows work area
        int targetX = currentBounds.X;
        if (workArea.Width > 0)
        {
            int maxAllowedX = workArea.X + workArea.Width - targetWidth;
            if (targetX > maxAllowedX)
            {
                targetX = maxAllowedX;
            }
            if (targetX < workArea.X)
            {
                targetX = workArea.X;
            }
        }

        var targetBounds = new WindowRectBounds(
            X: targetX,
            Y: currentBounds.Y,
            Width: targetWidth,
            Height: currentBounds.Height);

        return new LyricsWidthToggleResult(targetBounds, recordedSingleWidth);
    }

    /// <summary>
    /// Smooth ease-out cubic interpolation for fluid width transitions.
    /// </summary>
    public static double EaseOutCubic(double t)
    {
        double clamped = Math.Clamp(t, 0.0, 1.0);
        double inv = 1.0 - clamped;
        return 1.0 - (inv * inv * inv);
    }

    /// <summary>
    /// Interpolates between start and target bounds at normalized progress t in [0, 1],
    /// preserving Y and Height strictly.
    /// </summary>
    public static WindowRectBounds InterpolateBounds(WindowRectBounds start, WindowRectBounds target, double t)
    {
        double eased = EaseOutCubic(t);
        int x = (int)Math.Round(start.X + (target.X - start.X) * eased);
        int w = (int)Math.Round(start.Width + (target.Width - start.Width) * eased);
        return new WindowRectBounds(x, target.Y, Math.Max(1, w), target.Height);
    }
}
