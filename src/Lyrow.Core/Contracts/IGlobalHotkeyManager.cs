using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// Detailed state of a global hotkey registration.
/// </summary>
public enum HotkeyRegistrationState
{
    NotRegistered,
    Registered,
    Conflict,
    Failed
}

/// <summary>
/// Status of a registered global keyboard shortcut.
/// </summary>
public sealed record HotkeyStatus(
    string ActionName,
    string KeyGesture,
    bool IsRegistered,
    string? ErrorMessage = null,
    HotkeyRegistrationState State = HotkeyRegistrationState.NotRegistered);

/// <summary>
/// Abstraction for registering system-wide global hotkeys and monitoring display topology changes.
/// </summary>
public interface IGlobalHotkeyManager : IDisposable
{
    /// <summary>Indicates whether global hotkeys are supported on the current platform.</summary>
    bool IsSupported { get; }

    /// <summary>Fired when system displays, monitor connections, or desktop resolutions change.</summary>
    event EventHandler? DisplayConfigurationChanged;

    /// <summary>
    /// Registers global shortcuts for Glow, Lyrics, and Click-through.
    /// Unregisters existing hotkeys before applying new ones.
    /// </summary>
    bool RegisterHotkeys(
        HotkeySettings settings,
        Action onGlowToggle,
        Action onLyricsToggle,
        Action onClickThroughToggle);

    /// <summary>Unregisters all registered global hotkeys.</summary>
    void UnregisterAll();

    /// <summary>Returns current registration status for each configured shortcut.</summary>
    IReadOnlyList<HotkeyStatus> GetStatus();
}
