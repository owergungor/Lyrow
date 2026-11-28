namespace Lyrow.Core.Services;

/// <summary>
/// Computed responsive layout metrics for the Welcome V4 window at a given DIP width.
/// Guarantees zero horizontal overflow across 1280x720, 1332x907, 1366x768, 1920x1080 and 100%/125%/150% DPI.
/// </summary>
public sealed record WelcomeResponsiveLayoutMetrics(
    double ViewportDipWidth,
    double HorizontalPadding,
    double ContentMaxWidth,
    double LanguageComboWidth,
    double ShortcutRecorderMinWidth,
    bool UseCompactStepper,
    bool StackThemeCards,
    bool StackReactivityCards,
    bool StackLanguageRow,
    bool StackShortcutRows,
    bool StackSummaryColumns);

/// <summary>
/// Pure, deterministic responsive layout calculator for Welcome V4.
/// Ensures all content, cards, stepper tabs, and dropdowns fit strictly within the available window width.
/// </summary>
public static class WelcomeResponsiveLayoutCalculator
{
    public const double MaxContentWidth = 760.0;
    public const double ScrollbarReserveDip = 18.0;

    public static WelcomeResponsiveLayoutMetrics Compute(double viewportDipWidth)
    {
        double safeWidth = double.IsNaN(viewportDipWidth) || double.IsInfinity(viewportDipWidth) || viewportDipWidth <= 0
            ? 860.0
            : viewportDipWidth;

        double horizontalPadding = safeWidth >= 820
            ? 32.0
            : safeWidth >= 640
                ? 22.0
                : 14.0;

        double usableContentWidth = Math.Max(260.0, safeWidth - (horizontalPadding * 2.0) - ScrollbarReserveDip);
        double contentMaxWidth = Math.Min(MaxContentWidth, usableContentWidth);

        bool useCompactStepper = safeWidth < 780.0;
        bool stackThemeCards = contentMaxWidth < 540.0;
        bool stackReactivityCards = contentMaxWidth < 540.0;
        bool stackLanguageRow = contentMaxWidth < 520.0;
        bool stackShortcutRows = contentMaxWidth < 520.0;
        bool stackSummaryColumns = contentMaxWidth < 520.0;

        double languageComboWidth = stackLanguageRow
            ? contentMaxWidth - 36.0
            : Math.Clamp(contentMaxWidth * 0.30, 150.0, 210.0);

        double shortcutRecorderMinWidth = stackShortcutRows
            ? Math.Min(220.0, contentMaxWidth - 40.0)
            : Math.Clamp(contentMaxWidth * 0.32, 160.0, 210.0);

        return new WelcomeResponsiveLayoutMetrics(
            ViewportDipWidth: safeWidth,
            HorizontalPadding: horizontalPadding,
            ContentMaxWidth: contentMaxWidth,
            LanguageComboWidth: languageComboWidth,
            ShortcutRecorderMinWidth: shortcutRecorderMinWidth,
            UseCompactStepper: useCompactStepper,
            StackThemeCards: stackThemeCards,
            StackReactivityCards: stackReactivityCards,
            StackLanguageRow: stackLanguageRow,
            StackShortcutRows: stackShortcutRows,
            StackSummaryColumns: stackSummaryColumns);
    }

    /// <summary>
    /// Computes the logical DIP width for a physical pixel width and DPI percentage (100, 125, 150, etc.).
    /// </summary>
    public static double ToDipWidth(int physicalPixelWidth, int dpiPercent)
    {
        double scale = Math.Max(1.0, dpiPercent / 100.0);
        return physicalPixelWidth / scale;
    }
}
