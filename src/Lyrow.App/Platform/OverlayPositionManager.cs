using Lyrow.Core.Models;

namespace Lyrow.App.Platform;

/// <summary>
/// Lightweight point coordinates in physical pixels.
/// </summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>
/// Lightweight dimensions in physical pixels.
/// </summary>
public readonly record struct PixelSize(int Width, int Height);

/// <summary>
/// Lightweight rectangle bounds in physical pixels.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public PixelPoint Position => new(X, Y);
}

/// <summary>
/// Screen display metadata.
/// </summary>
public sealed class DisplayScreenInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public PixelRect Bounds { get; init; }
    public PixelRect WorkingArea { get; init; }
    public double Scaling { get; init; } = 1.0;
    public bool IsPrimary { get; init; }
}

/// <summary>
/// Multi-monitor aware window positioning service.
/// Calculates accurate physical pixel coordinates accounting for display scaling, taskbar margins,
/// and safe fallback when secondary monitors are disconnected.
/// </summary>
public static class OverlayPositionManager
{
    public const int DefaultMarginPixels = 24;

    /// <summary>
    /// Pure function calculating the window pixel position within a given display working area.
    /// </summary>
    public static PixelPoint CalculatePosition(
        PixelRect workingArea,
        PixelSize windowPixelSize,
        OverlayPosition position,
        int margin = DefaultMarginPixels)
    {
        int x = position switch
        {
            OverlayPosition.TopLeft or OverlayPosition.BottomLeft => workingArea.X + margin,
            OverlayPosition.TopRight or OverlayPosition.BottomRight => workingArea.X + workingArea.Width - windowPixelSize.Width - margin,
            _ => workingArea.X + (workingArea.Width - windowPixelSize.Width) / 2
        };

        int y = position switch
        {
            OverlayPosition.TopLeft or OverlayPosition.TopCenter or OverlayPosition.TopRight => workingArea.Y + margin,
            OverlayPosition.BottomLeft or OverlayPosition.BottomCenter or OverlayPosition.BottomRight => workingArea.Y + workingArea.Height - windowPixelSize.Height - margin,
            _ => workingArea.Y + (workingArea.Height - windowPixelSize.Height) / 2
        };

        // Guarantee window remains within visible bounds
        x = Math.Clamp(x, workingArea.X, Math.Max(workingArea.X, workingArea.X + workingArea.Width - windowPixelSize.Width));
        y = Math.Clamp(y, workingArea.Y, Math.Max(workingArea.Y, workingArea.Y + workingArea.Height - windowPixelSize.Height));

        return new PixelPoint(x, y);
    }

    /// <summary>
    /// Selects the designated target screen, gracefully falling back to primary screen
    /// if index is invalid or monitor was unplugged.
    /// </summary>
    public static DisplayScreenInfo? ResolveTargetScreen(IReadOnlyList<DisplayScreenInfo>? screens, int targetIndex)
    {
        if (screens == null || screens.Count == 0) return null;

        if (targetIndex >= 0 && targetIndex < screens.Count)
        {
            return screens[targetIndex];
        }

        return screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
    }
}
