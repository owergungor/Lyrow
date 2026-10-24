using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App.Platform;

/// <summary>
/// In-memory mock global hotkey and display notifier for unit tests and non-Windows environments.
/// </summary>
public sealed class SyntheticGlobalHotkeyManager : IGlobalHotkeyManager
{
    private Action? _onGlow;
    private Action? _onLyrics;
    private Action? _onClickThrough;
    private readonly List<HotkeyStatus> _statuses = new();

    public bool IsSupported => true;

    /// <summary>Flag to simulate hotkey registration conflicts in tests.</summary>
    public bool SimulateConflict { get; set; }

    public event EventHandler? DisplayConfigurationChanged;

    public bool RegisterHotkeys(
        HotkeySettings settings,
        Action onGlowToggle,
        Action onLyricsToggle,
        Action onClickThroughToggle)
    {
        _statuses.Clear();
        _onGlow = string.IsNullOrWhiteSpace(settings.ToggleGlow) ? null : onGlowToggle;
        _onLyrics = string.IsNullOrWhiteSpace(settings.ToggleLyrics) ? null : onLyricsToggle;
        _onClickThrough = string.IsNullOrWhiteSpace(settings.ToggleClickThrough) ? null : onClickThroughToggle;

        if (SimulateConflict)
        {
            _statuses.Add(new HotkeyStatus("Glow", settings.ToggleGlow, false, $"{settings.ToggleGlow} kullanılamıyor. Bu kısayol Windows veya başka bir uygulama tarafından kullanılıyor.", HotkeyRegistrationState.Conflict));
            _statuses.Add(new HotkeyStatus("Lyrics", settings.ToggleLyrics, !string.IsNullOrWhiteSpace(settings.ToggleLyrics), null, string.IsNullOrWhiteSpace(settings.ToggleLyrics) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered));
            _statuses.Add(new HotkeyStatus("ClickThrough", settings.ToggleClickThrough, !string.IsNullOrWhiteSpace(settings.ToggleClickThrough), null, string.IsNullOrWhiteSpace(settings.ToggleClickThrough) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered));
            return false;
        }

        // Check internal collisions
        if (!HotkeyValidator.CheckInternalCollisions(settings, out var collisionError))
        {
            bool glowCol = HotkeyValidator.AreColliding(settings.ToggleGlow, settings.ToggleLyrics) || HotkeyValidator.AreColliding(settings.ToggleGlow, settings.ToggleClickThrough);
            bool lyricsCol = HotkeyValidator.AreColliding(settings.ToggleLyrics, settings.ToggleGlow) || HotkeyValidator.AreColliding(settings.ToggleLyrics, settings.ToggleClickThrough);
            bool clickCol = HotkeyValidator.AreColliding(settings.ToggleClickThrough, settings.ToggleGlow) || HotkeyValidator.AreColliding(settings.ToggleClickThrough, settings.ToggleLyrics);

            _statuses.Add(new HotkeyStatus("Glow", settings.ToggleGlow, false, glowCol ? collisionError : null, glowCol ? HotkeyRegistrationState.Conflict : (string.IsNullOrWhiteSpace(settings.ToggleGlow) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered)));
            _statuses.Add(new HotkeyStatus("Lyrics", settings.ToggleLyrics, false, lyricsCol ? collisionError : null, lyricsCol ? HotkeyRegistrationState.Conflict : (string.IsNullOrWhiteSpace(settings.ToggleLyrics) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered)));
            _statuses.Add(new HotkeyStatus("ClickThrough", settings.ToggleClickThrough, false, clickCol ? collisionError : null, clickCol ? HotkeyRegistrationState.Conflict : (string.IsNullOrWhiteSpace(settings.ToggleClickThrough) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered)));
            return false;
        }

        AddSingleStatus("Glow", settings.ToggleGlow);
        AddSingleStatus("Lyrics", settings.ToggleLyrics);
        AddSingleStatus("ClickThrough", settings.ToggleClickThrough);
        return true;
    }

    private void AddSingleStatus(string actionName, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            _statuses.Add(new HotkeyStatus(actionName, string.Empty, false, null, HotkeyRegistrationState.NotRegistered));
        }
        else
        {
            _statuses.Add(new HotkeyStatus(actionName, gesture, true, null, HotkeyRegistrationState.Registered));
        }
    }

    public void UnregisterAll()
    {
        _onGlow = null;
        _onLyrics = null;
        _onClickThrough = null;
        _statuses.Clear();
    }

    public IReadOnlyList<HotkeyStatus> GetStatus() => _statuses.AsReadOnly();

    public void TriggerGlow() => _onGlow?.Invoke();
    public void TriggerLyrics() => _onLyrics?.Invoke();
    public void TriggerClickThrough() => _onClickThrough?.Invoke();
    public void TriggerDisplayChange() => DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        UnregisterAll();
    }
}
