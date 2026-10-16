namespace Lyrow.Core.Models;

/// <summary>
/// Root persistent user configuration model.
/// </summary>
public sealed class LyrowSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.SystemDefault;

    public bool LumnEnabled { get; set; } = true;

    public LumnSettings Lumn { get; set; } = new();

    public bool VerciEnabled { get; set; } = true;

    public bool VisualIntegrationEnabled { get; set; } = true;

    public bool ClickThroughEnabled { get; set; } = false;

    public double MasterOpacity { get; set; } = 0.95;

    public string AccentColor { get; set; } = "#89B4FA";

    public bool StartWithWindows { get; set; } = false;

    public bool MinimizeToTray { get; set; } = true;

    public Dictionary<string, string> ModuleCustomSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a clean default instance.</summary>
    public static LyrowSettings CreateDefault() => new();
}
