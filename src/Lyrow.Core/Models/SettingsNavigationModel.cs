namespace Lyrow.Core.Models;

/// <summary>
/// Authoritative ordered list of visible settings navigation categories in the Settings Modal (v5.0).
/// Exactly 7 tabs: Overview, General, Appearance, Lighting, Lyrics, System, About (Updates live inside About).
/// </summary>
public static class SettingsNavigationModel
{
    public static readonly string[] VisibleSettingsTabKeys =
    {
        "Overview",
        "General",
        "Appearance",
        "Lighting",
        "Lyrics",
        "System",
        "About"
    };
}
