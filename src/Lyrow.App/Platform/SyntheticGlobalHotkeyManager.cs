using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App.Platform;

/// <summary>
/// In-memory mock global hotkey and display notifier for unit tests and non-Windows environments.
/// </summary>
public sealed class SyntheticGlobalHotkeyManager : IGlobalHotkeyManager
{
    private Action? _onLumn;
    private Action? _onVerci;
    private Action? _onClickThrough;
    private readonly List<HotkeyStatus> _statuses = new();

    public bool IsSupported => true;

    /// <summary>Flag to simulate hotkey registration conflicts in tests.</summary>
    public bool SimulateConflict { get; set; }

    public event EventHandler? DisplayConfigurationChanged;

    public bool RegisterHotkeys(
        HotkeySettings settings,
        Action onLumnToggle,
        Action onVerciToggle,
        Action onClickThroughToggle)
    {
        _statuses.Clear();
        _onLumn = string.IsNullOrWhiteSpace(settings.ToggleLumn) ? null : onLumnToggle;
        _onVerci = string.IsNullOrWhiteSpace(settings.ToggleVerci) ? null : onVerciToggle;
        _onClickThrough = string.IsNullOrWhiteSpace(settings.ToggleClickThrough) ? null : onClickThroughToggle;

        if (SimulateConflict)
        {
            _statuses.Add(new HotkeyStatus("Glow", settings.ToggleLumn, false, $"{settings.ToggleLumn} kullanılamıyor. Bu kısayol Windows veya başka bir uygulama tarafından kullanılıyor.", HotkeyRegistrationState.Conflict));
            _statuses.Add(new HotkeyStatus("Lyrics", settings.ToggleVerci, !string.IsNullOrWhiteSpace(settings.ToggleVerci), null, string.IsNullOrWhiteSpace(settings.ToggleVerci) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered));
            _statuses.Add(new HotkeyStatus("ClickThrough", settings.ToggleClickThrough, !string.IsNullOrWhiteSpace(settings.ToggleClickThrough), null, string.IsNullOrWhiteSpace(settings.ToggleClickThrough) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered));
            return false;
        }

        // Check internal collisions
        if (!HotkeyValidator.CheckInternalCollisions(settings, out var collisionError))
        {
            bool glowCol = HotkeyValidator.AreColliding(settings.ToggleLumn, settings.ToggleVerci) || HotkeyValidator.AreColliding(settings.ToggleLumn, settings.ToggleClickThrough);
            bool lyricsCol = HotkeyValidator.AreColliding(settings.ToggleVerci, settings.ToggleLumn) || HotkeyValidator.AreColliding(settings.ToggleVerci, settings.ToggleClickThrough);
            bool clickCol = HotkeyValidator.AreColliding(settings.ToggleClickThrough, settings.ToggleLumn) || HotkeyValidator.AreColliding(settings.ToggleClickThrough, settings.ToggleVerci);

            _statuses.Add(new HotkeyStatus("Glow", settings.ToggleLumn, false, glowCol ? collisionError : null, glowCol ? HotkeyRegistrationState.Conflict : (string.IsNullOrWhiteSpace(settings.ToggleLumn) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered)));
            _statuses.Add(new HotkeyStatus("Lyrics", settings.ToggleVerci, false, lyricsCol ? collisionError : null, lyricsCol ? HotkeyRegistrationState.Conflict : (string.IsNullOrWhiteSpace(settings.ToggleVerci) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered)));
            _statuses.Add(new HotkeyStatus("ClickThrough", settings.ToggleClickThrough, false, clickCol ? collisionError : null, clickCol ? HotkeyRegistrationState.Conflict : (string.IsNullOrWhiteSpace(settings.ToggleClickThrough) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered)));
            return false;
        }

        AddSingleStatus("Glow", settings.ToggleLumn);
        AddSingleStatus("Lyrics", settings.ToggleVerci);
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
        _onLumn = null;
        _onVerci = null;
        _onClickThrough = null;
        _statuses.Clear();
    }

    public IReadOnlyList<HotkeyStatus> GetStatus() => _statuses.AsReadOnly();

    public void TriggerLumn() => _onLumn?.Invoke();
    public void TriggerVerci() => _onVerci?.Invoke();
    public void TriggerClickThrough() => _onClickThrough?.Invoke();
    public void TriggerDisplayChange() => DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        UnregisterAll();
    }
}
