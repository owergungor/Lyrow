using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

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
        _onLumn = onLumnToggle;
        _onVerci = onVerciToggle;
        _onClickThrough = onClickThroughToggle;

        if (SimulateConflict)
        {
            _statuses.Add(new HotkeyStatus("Glow", settings.ToggleLumn, false, $"{settings.ToggleLumn} kullanılamıyor. Bu kısayol başka bir uygulama tarafından kullanılıyor olabilir.", HotkeyRegistrationState.Conflict));
            _statuses.Add(new HotkeyStatus("Lyrics", settings.ToggleVerci, true, null, HotkeyRegistrationState.Registered));
            _statuses.Add(new HotkeyStatus("ClickThrough", settings.ToggleClickThrough, true, null, HotkeyRegistrationState.Registered));
            return false;
        }

        _statuses.Add(new HotkeyStatus("Glow", settings.ToggleLumn, true, null, HotkeyRegistrationState.Registered));
        _statuses.Add(new HotkeyStatus("Lyrics", settings.ToggleVerci, true, null, HotkeyRegistrationState.Registered));
        _statuses.Add(new HotkeyStatus("ClickThrough", settings.ToggleClickThrough, true, null, HotkeyRegistrationState.Registered));
        return true;
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
