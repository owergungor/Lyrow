namespace Lyrow.Core.Models;

/// <summary>
/// User-configurable keyboard shortcuts for global actions.
/// </summary>
public sealed class HotkeySettings
{
    /// <summary>Global shortcut to toggle Lumn audio visualizer window.</summary>
    public string ToggleLumn { get; set; } = "Ctrl+Shift+L";

    /// <summary>Global shortcut to toggle Verci lyrics overlay window.</summary>
    public string ToggleVerci { get; set; } = "Ctrl+Shift+V";

    /// <summary>Global shortcut to toggle mouse click-through transparency.</summary>
    public string ToggleClickThrough { get; set; } = "Ctrl+Shift+C";
}
