namespace Lyrow.Core.Models;

/// <summary>
/// Authoritative list of visible settings navigation categories in the Settings Modal (v4.3).
/// Exactly 3 tabs: General (Genel), Lyrics (Şarkı Sözleri), and System & Notifications (Sistem ve Bildirimler).
/// </summary>
public static class SettingsNavigationModel
{
    public static readonly string[] VisibleSettingsTabKeys =
    {
        "General",
        "Lyrics",
        "System"
    };
}
