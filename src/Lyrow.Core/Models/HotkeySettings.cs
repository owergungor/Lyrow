namespace Lyrow.Core.Models;

/// <summary>
/// User-configurable keyboard shortcuts for global actions.
/// </summary>
public sealed class HotkeySettings
{
    /// <summary>Global shortcut to toggle Glow audio visualizer / ambient edge lighting.</summary>
    public string ToggleLumn { get; set; } = string.Empty;

    /// <summary>Global shortcut to toggle Lyrics overlay window.</summary>
    public string ToggleVerci { get; set; } = string.Empty;

    /// <summary>Global shortcut to toggle mouse click-through transparency.</summary>
    public string ToggleClickThrough { get; set; } = string.Empty;
}
