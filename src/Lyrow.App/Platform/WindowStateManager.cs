using System.Diagnostics;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App.Platform;

/// <summary>
/// Service managing window geometry persistence, multi-monitor bounds validation,
/// safe fallback when external monitors are disconnected, and restore of normal/compact state.
/// </summary>
public static class WindowStateManager
{
    public static readonly PixelSize MinimumSize = new(500, 232);
    public static readonly PixelSize DefaultNormalSize = new(960, 720);
    public static readonly PixelSize DefaultCompactSize = new(500, 232);

    /// <summary>
    /// Pure validation algorithm that checks whether candidate bounds fall within at least one visible screen.
    /// If off-screen or monitor disconnected, falls back to centering on primary working area.
    /// Clamps size to minimum dimensions.
    /// </summary>
    public static PixelRect ValidateOrClampBounds(
        PixelRect candidate,
        IReadOnlyList<PixelRect>? screens,
        PixelRect primaryWorkingArea,
        PixelSize minSize,
        PixelSize defaultSize)
    {
        // Enforce min size
        int width = Math.Max(candidate.Width, minSize.Width);
        int height = Math.Max(candidate.Height, minSize.Height);

        // If no screens supplied, clamp within primary working area
        if (screens == null || screens.Count == 0)
        {
            return CenterInWorkingArea(primaryWorkingArea, new PixelSize(width, height));
        }

        // Check if at least 120x60 pixels of the candidate window intersect with any screen's working area
        bool hasVisibleIntersection = false;
        foreach (var screen in screens)
        {
            int intersectLeft = Math.Max(candidate.X, screen.X);
            int intersectTop = Math.Max(candidate.Y, screen.Y);
            int intersectRight = Math.Min(candidate.X + width, screen.X + screen.Width);
            int intersectBottom = Math.Min(candidate.Y + height, screen.Y + screen.Height);

            int intersectW = Math.Max(0, intersectRight - intersectLeft);
            int intersectH = Math.Max(0, intersectBottom - intersectTop);

            // Require minimal visible portion for user to be able to drag/interact
            if (intersectW >= 120 && intersectH >= 60)
            {
                hasVisibleIntersection = true;
                break;
            }
        }

        if (!hasVisibleIntersection)
        {
            // Off-screen: Fallback to primary screen center with default size
            return CenterInWorkingArea(primaryWorkingArea, defaultSize);
        }

        return new PixelRect(candidate.X, candidate.Y, width, height);
    }

    /// <summary>
    /// Calculates a centered rectangle inside the given working area.
    /// </summary>
    public static PixelRect CenterInWorkingArea(PixelRect workingArea, PixelSize size)
    {
        int x = workingArea.X + Math.Max(0, (workingArea.Width - size.Width) / 2);
        int y = workingArea.Y + Math.Max(0, (workingArea.Height - size.Height) / 2);
        return new PixelRect(x, y, size.Width, size.Height);
    }

    /// <summary>
    /// Captures current window bounds and presentation state into settings.
    /// </summary>
    public static void SaveWindowState(
        AppWindow? appWindow,
        LyrowSettings settings,
        string activePage,
        bool isCompact)
    {
        if (appWindow == null || settings == null) return;

        try
        {
            var presenter = appWindow.Presenter as OverlappedPresenter;
            bool isMaximized = presenter?.State == OverlappedPresenterState.Maximized;

            settings.IsMaximized = isMaximized;
            settings.IsCompactMode = isCompact;
            settings.LastActivePage = string.IsNullOrWhiteSpace(activePage) ? "Home" : activePage;

            // Only update X, Y, Width, Height if not minimized or maximized
            if (!isMaximized && presenter?.State != OverlappedPresenterState.Minimized)
            {
                settings.WindowX = appWindow.Position.X;
                settings.WindowY = appWindow.Position.Y;
                settings.WindowWidth = appWindow.Size.Width;
                settings.WindowHeight = appWindow.Size.Height;
            }
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Error saving window state", ex);
        }
    }

    /// <summary>
    /// Restores window geometry, position, and active page from saved settings with multi-monitor protection.
    /// </summary>
    public static void RestoreWindowState(
        AppWindow? appWindow,
        LyrowSettings settings,
        Action<string>? navigateCallback)
    {
        if (appWindow == null || settings == null) return;

        try
        {
            // Collect all display areas
            var allDisplayAreas = DisplayArea.FindAll();
            var primaryArea = DisplayArea.Primary;

            PixelRect primaryWorkingArea = primaryArea != null
                ? new PixelRect(primaryArea.WorkArea.X, primaryArea.WorkArea.Y, primaryArea.WorkArea.Width, primaryArea.WorkArea.Height)
                : new PixelRect(0, 0, 1920, 1080);

            var screenRects = new List<PixelRect>();
            if (allDisplayAreas != null)
            {
                foreach (var area in allDisplayAreas)
                {
                    screenRects.Add(new PixelRect(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height));
                }
            }

            PixelSize targetDefault = settings.IsCompactMode ? DefaultCompactSize : DefaultNormalSize;

            if (settings.WindowX.HasValue && settings.WindowY.HasValue &&
                settings.WindowWidth.HasValue && settings.WindowHeight.HasValue)
            {
                var candidate = new PixelRect(
                    settings.WindowX.Value,
                    settings.WindowY.Value,
                    settings.WindowWidth.Value,
                    settings.WindowHeight.Value);

                var validated = ValidateOrClampBounds(candidate, screenRects, primaryWorkingArea, MinimumSize, targetDefault);

                appWindow.MoveAndResize(new RectInt32(validated.X, validated.Y, validated.Width, validated.Height));
            }
            else
            {
                var centered = CenterInWorkingArea(primaryWorkingArea, targetDefault);
                appWindow.MoveAndResize(new RectInt32(centered.X, centered.Y, centered.Width, centered.Height));
            }

            if (settings.IsMaximized && appWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.Maximize();
            }

            if (!string.IsNullOrWhiteSpace(settings.LastActivePage))
            {
                navigateCallback?.Invoke(settings.LastActivePage);
            }
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Error restoring window state", ex);
        }
    }
}
