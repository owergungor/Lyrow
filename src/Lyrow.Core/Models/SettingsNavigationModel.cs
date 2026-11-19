namespace Lyrow.Core.Models;

/// <summary>
/// Authoritative list of visible settings category tabs in Lyrow v4.1+.
/// Excludes removed 'All' / 'Tümü' and 'System' tabs.
/// </summary>
public static class SettingsNavigationModel
{
    public static readonly string[] VisibleSettingsTabKeys =
    {
        "Appearance",
        "Artwork",
        "Audio",
        "Updates",
        "Shortcuts",
        "About"
    };
}
