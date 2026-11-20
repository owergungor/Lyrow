namespace Lyrow.Core.Models;

/// <summary>
/// Authoritative ordered list of visible settings navigation categories in the Settings Modal (v4.3).
/// Exactly 7 tabs: General, Appearance, Lighting, Lyrics, System, Updates, About (Shortcuts live inside About).
/// </summary>
public static class SettingsNavigationModel
{
    public static readonly string[] VisibleSettingsTabKeys =
    {
        "General",
        "Appearance",
        "Lighting",
        "Lyrics",
        "System",
        "Updates",
        "About"
    };
}
