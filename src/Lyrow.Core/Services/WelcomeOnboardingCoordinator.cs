using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// Five-step onboarding sequence for the Lyrow Welcome Experience (V4 tabs:
/// Start, Appearance, Music Reactivity, Keyboard Shortcuts, Finish).
/// </summary>
public enum WelcomeStep
{
    Welcome = 0,
    Appearance = 1,
    Experience = 2,
    Audio = 3,
    Ready = 4
}

/// <summary>
/// Completion reason emitted when the Welcome onboarding session finishes.
/// </summary>
public enum WelcomeCompletionReason
{
    Completed = 0,
    Skipped = 1,
    WindowClosed = 2
}

/// <summary>
/// MVVM-friendly, UI-framework-independent coordinator for the 5-tab Welcome V4 Experience.
/// Manages tab navigation, live setting staging (language, theme, appearance, music reactivity,
/// keyboard shortcuts), validation, rollback on save failure, and atomic completion without
/// overwriting existing user settings or breaking migration behavior.
/// </summary>
public sealed class WelcomeOnboardingCoordinator
{
    public const int TotalSteps = 5;

    private readonly ISettingsManager _settingsManager;
    private readonly IAudioCaptureService? _audioCaptureService;
    private readonly IAudioRoutingService? _audioRoutingService;
    private int _finishSignaled;

    private WelcomeStep _currentStep = WelcomeStep.Welcome;
    private readonly string _initialLanguage;
    private readonly ThemeMode _initialTheme;
    private readonly bool _initialEdgeLight;
    private readonly bool _initialDynamicBackground;
    private readonly int _initialFftSize;
    private readonly string _initialShortcutGlow;
    private readonly string _initialShortcutLyrics;
    private readonly string? _initialAudioDeviceId;

    private string _selectedLanguage;
    private ThemeMode _selectedTheme;
    private bool _enableEdgeLight;
    private bool _enableDynamicBackground;
    private int _selectedFftSize;
    private string _selectedShortcutGlow;
    private string _selectedShortcutLyrics;
    private string? _selectedAudioDeviceId;
    private IReadOnlyList<AudioDeviceDescriptor> _availableDevices = Array.Empty<AudioDeviceDescriptor>();
    private bool _hasUserChangedAudioDevice;

    public WelcomeOnboardingCoordinator(
        ISettingsManager settingsManager,
        IAudioCaptureService? audioCaptureService = null,
        IAudioRoutingService? audioRoutingService = null,
        bool isManualReopen = false)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _audioCaptureService = audioCaptureService;
        _audioRoutingService = audioRoutingService;
        IsManualReopen = isManualReopen;

        // Capture exact pre-Welcome snapshot so Skip / Window Close / ESC / Save Error can roll back cleanly
        var current = _settingsManager.Current;
        _initialLanguage = string.IsNullOrWhiteSpace(current.Language) ? "en" : current.Language;
        _initialTheme = current.Theme;
        _initialEdgeLight = current.GlowEnabled;
        _initialDynamicBackground = current.VisualIntegrationEnabled;
        _initialFftSize = NormalizeFftSize(current.Glow.FftSize);
        _initialShortcutGlow = HotkeyValidator.NormalizeGesture(current.Hotkeys.ToggleGlow);
        _initialShortcutLyrics = HotkeyValidator.NormalizeGesture(current.Hotkeys.ToggleLyrics);
        _initialAudioDeviceId = string.IsNullOrWhiteSpace(current.Glow.PreferredDeviceId)
            ? null
            : current.Glow.PreferredDeviceId;

        // Seed active selections from current user settings
        _selectedLanguage = _initialLanguage;
        _selectedTheme = _initialTheme;
        _enableEdgeLight = _initialEdgeLight;
        _enableDynamicBackground = _initialDynamicBackground;
        _selectedFftSize = _initialFftSize;
        _selectedShortcutGlow = _initialShortcutGlow;
        _selectedShortcutLyrics = _initialShortcutLyrics;
        _selectedAudioDeviceId = _initialAudioDeviceId;
    }

    private static int NormalizeFftSize(int fftSize) => fftSize switch
    {
        1024 => 1024,
        2048 => 2048,
        4096 => 4096,
        _ => 1024
    };

    public bool IsManualReopen { get; }

    public string InitialLanguage => _initialLanguage;
    public ThemeMode InitialTheme => _initialTheme;
    public bool InitialEdgeLight => _initialEdgeLight;
    public bool InitialDynamicBackground => _initialDynamicBackground;
    public int InitialFftSize => _initialFftSize;
    public string InitialShortcutGlow => _initialShortcutGlow;
    public string InitialShortcutLyrics => _initialShortcutLyrics;

    public string? LastError { get; private set; }

    public WelcomeStep CurrentStep => _currentStep;

    public int CurrentStepIndex => (int)_currentStep;

    public bool CanGoBack => _currentStep > WelcomeStep.Welcome;

    public bool CanContinue => _currentStep < WelcomeStep.Ready;

    public bool IsReadyStep => _currentStep == WelcomeStep.Ready;

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "en" : value.Trim().ToLowerInvariant();
            if (string.Equals(_selectedLanguage, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _selectedLanguage = normalized;
            LanguageSelectionChanged?.Invoke(this, normalized);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ThemeMode SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_selectedTheme == value) return;
            _selectedTheme = value;
            ThemeSelectionChanged?.Invoke(this, value);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool EnableEdgeLight
    {
        get => _enableEdgeLight;
        set
        {
            if (_enableEdgeLight == value) return;
            _enableEdgeLight = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool EnableDynamicBackground
    {
        get => _enableDynamicBackground;
        set
        {
            if (_enableDynamicBackground == value) return;
            _enableDynamicBackground = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// User-friendly Music Reactivity Sensitivity mapped accurately to the underlying DSP window:
    /// 4096 = Calm (smoother transitions), 2048 = Balanced (default), 1024 = Expressive (fastest beat response).
    /// </summary>
    public int SelectedFftSize
    {
        get => _selectedFftSize;
        set
        {
            int normalized = NormalizeFftSize(value);
            if (_selectedFftSize == normalized) return;
            _selectedFftSize = normalized;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedShortcutGlow
    {
        get => _selectedShortcutGlow;
        set
        {
            string normalized = HotkeyValidator.NormalizeGesture(value);
            if (string.Equals(_selectedShortcutGlow, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _selectedShortcutGlow = normalized;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedShortcutLyrics
    {
        get => _selectedShortcutLyrics;
        set
        {
            string normalized = HotkeyValidator.NormalizeGesture(value);
            if (string.Equals(_selectedShortcutLyrics, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _selectedShortcutLyrics = normalized;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SelectedAudioDeviceId
    {
        get => _selectedAudioDeviceId;
        set
        {
            string? normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (string.Equals(_selectedAudioDeviceId, normalized, StringComparison.OrdinalIgnoreCase)) return;
            _selectedAudioDeviceId = normalized;
            _hasUserChangedAudioDevice = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<AudioDeviceDescriptor> AvailableAudioDevices => _availableDevices;

    public bool HasAudioDevices => _availableDevices.Count > 0;

    public event EventHandler? StateChanged;
    public event EventHandler<WelcomeStep>? StepChanged;
    public event EventHandler<string>? LanguageSelectionChanged;
    public event EventHandler<ThemeMode>? ThemeSelectionChanged;
    public event EventHandler<WelcomeCompletionReason>? OnboardingFinished;

    /// <summary>
    /// Determines whether the Welcome screen should be presented automatically on application launch.
    /// Never opens during smoke tests or for returning / upgraded users with HasCompletedWelcome == true.
    /// </summary>
    public static bool ShouldShowWelcomeOnStartup(LyrowSettings settings, bool isSmokeTestMode = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (isSmokeTestMode) return false;
        return !settings.HasCompletedWelcome;
    }

    /// <summary>
    /// Resets the staged keyboard shortcuts to the application's default values.
    /// </summary>
    public void ResetShortcutsToDefaults()
    {
        var defaults = LyrowSettings.CreateDefault();
        _selectedShortcutGlow = HotkeyValidator.NormalizeGesture(defaults.Hotkeys.ToggleGlow);
        _selectedShortcutLyrics = HotkeyValidator.NormalizeGesture(defaults.Hotkeys.ToggleLyrics);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Validates staged preferences (including hotkey syntax and internal collisions) before persisting.
    /// </summary>
    public bool ValidatePendingPreferences(out string? error)
    {
        if (!HotkeyValidator.IsValidGesture(_selectedShortcutGlow, out error))
        {
            return false;
        }

        if (!HotkeyValidator.IsValidGesture(_selectedShortcutLyrics, out error))
        {
            return false;
        }

        var candidate = new HotkeySettings
        {
            ToggleGlow = _selectedShortcutGlow,
            ToggleLyrics = _selectedShortcutLyrics,
            ToggleClickThrough = _settingsManager.Current.Hotkeys.ToggleClickThrough
        };

        if (!HotkeyValidator.CheckInternalCollisions(candidate, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Asynchronously loads available audio output endpoints off the UI thread so enumeration never blocks window rendering.
    /// </summary>
    public async Task RefreshAudioDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (_audioCaptureService == null)
        {
            _availableDevices = Array.Empty<AudioDeviceDescriptor>();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            var devices = await Task.Run(() => _audioCaptureService.GetAvailableDevices(), cancellationToken).ConfigureAwait(false);
            _availableDevices = devices ?? Array.Empty<AudioDeviceDescriptor>();
        }
        catch
        {
            _availableDevices = Array.Empty<AudioDeviceDescriptor>();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resolves the friendly display name of the currently selected audio output device (or system default).
    /// </summary>
    public string GetSelectedAudioDeviceDisplayName(string defaultSystemLabel)
    {
        if (!string.IsNullOrWhiteSpace(_selectedAudioDeviceId))
        {
            var match = _availableDevices.FirstOrDefault(d =>
                string.Equals(d.Id, _selectedAudioDeviceId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match.Name;
            }
        }

        var defaultDev = _availableDevices.FirstOrDefault(d => d.IsDefault);
        return defaultDev != null ? $"{defaultDev.Name} ({defaultSystemLabel})" : defaultSystemLabel;
    }

    public void GetStarted() => GoToStep(WelcomeStep.Appearance);

    public bool Continue()
    {
        if (_currentStep >= WelcomeStep.Ready) return false;
        GoToStep((WelcomeStep)((int)_currentStep + 1));
        return true;
    }

    public bool Back()
    {
        if (_currentStep <= WelcomeStep.Welcome) return false;
        GoToStep((WelcomeStep)((int)_currentStep - 1));
        return true;
    }

    public void GoToStep(WelcomeStep step)
    {
        if (!Enum.IsDefined(typeof(WelcomeStep), step)) return;
        if (_currentStep == step) return;
        _currentStep = step;
        StepChanged?.Invoke(this, _currentStep);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Validates and commits all user selections made on the Welcome screen (Language, Theme,
    /// Edge Light, Dynamic Background, Music Reactivity Sensitivity, Keyboard Shortcuts, Audio Output)
    /// and marks Welcome as completed. If validation or persistence fails, rolls back in-memory state,
    /// populates <see cref="LastError"/>, and returns false so Welcome stays open.
    /// </summary>
    public async Task<bool> CompleteAsync(CancellationToken cancellationToken = default)
    {
        LastError = null;
        if (!ValidatePendingPreferences(out string? validationError))
        {
            LastError = validationError;
            return false;
        }

        if (Interlocked.CompareExchange(ref _finishSignaled, 1, 0) != 0)
        {
            return false;
        }

        var settings = _settingsManager.Current;
        bool prevCompletedWelcome = settings.HasCompletedWelcome;
        bool prevCompletedFirstRun = settings.HasCompletedFirstRun;
        int prevGlowThickness = settings.GlowThickness;

        settings.Language = _selectedLanguage;
        settings.Theme = _selectedTheme;
        settings.GlowEnabled = _enableEdgeLight;
        if (_enableEdgeLight && settings.GlowThickness <= 0)
        {
            settings.GlowThickness = 24;
        }

        settings.VisualIntegrationEnabled = _enableDynamicBackground;
        settings.Glow.FftSize = _selectedFftSize;
        settings.Hotkeys.ToggleGlow = _selectedShortcutGlow;
        settings.Hotkeys.ToggleLyrics = _selectedShortcutLyrics;
        settings.Glow.PreferredDeviceId = _selectedAudioDeviceId ?? string.Empty;
        settings.HasCompletedWelcome = true;
        settings.HasCompletedFirstRun = true;

        try
        {
            await _settingsManager.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Roll back in-memory settings and allow retry without closing Welcome
            settings.Language = _initialLanguage;
            settings.Theme = _initialTheme;
            settings.GlowEnabled = _initialEdgeLight;
            settings.GlowThickness = prevGlowThickness;
            settings.VisualIntegrationEnabled = _initialDynamicBackground;
            settings.Glow.FftSize = _initialFftSize;
            settings.Hotkeys.ToggleGlow = _initialShortcutGlow;
            settings.Hotkeys.ToggleLyrics = _initialShortcutLyrics;
            settings.Glow.PreferredDeviceId = _initialAudioDeviceId ?? string.Empty;
            settings.HasCompletedWelcome = prevCompletedWelcome;
            settings.HasCompletedFirstRun = prevCompletedFirstRun;

            LastError = ex.Message;
            Interlocked.Exchange(ref _finishSignaled, 0);
            return false;
        }

        // Apply optional audio endpoint change asynchronously without blocking caller
        if (_hasUserChangedAudioDevice && !string.IsNullOrWhiteSpace(_selectedAudioDeviceId))
        {
            string targetDeviceId = _selectedAudioDeviceId;
            _ = Task.Run(async () =>
            {
                try
                {
                    _audioRoutingService?.SetDefaultPlaybackDevice(targetDeviceId);
                    if (_audioCaptureService != null && _audioCaptureService.IsCapturing)
                    {
                        await _audioCaptureService.StopAsync().ConfigureAwait(false);
                        await _audioCaptureService.StartAsync(targetDeviceId).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Non-fatal: missing/disconnected audio hardware must never fail onboarding
                }
            }, CancellationToken.None);
        }

        OnboardingFinished?.Invoke(this, WelcomeCompletionReason.Completed);
        return true;
    }

    /// <summary>
    /// Discards any temporary selections made on the Welcome screen, preserves pre-Welcome
    /// settings (or application defaults for new users), and marks Welcome as completed.
    /// </summary>
    public Task<bool> SkipAsync(CancellationToken cancellationToken = default)
        => DismissWithoutApplyingChangesAsync(WelcomeCompletionReason.Skipped, cancellationToken);

    /// <summary>
    /// Discards temporary Welcome edits when closed via window chrome (X) or Escape,
    /// restores pre-Welcome settings without saving temporary choices, and does NOT mark
    /// Welcome as completed so new users will see Welcome again on next launch.
    /// </summary>
    public Task<bool> CloseWindowAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _finishSignaled, 1, 0) != 0)
        {
            return Task.FromResult(false);
        }

        // Restore exact in-memory pre-Welcome snapshot without mutating persisted HasCompletedWelcome
        _selectedLanguage = _initialLanguage;
        _selectedTheme = _initialTheme;
        _enableEdgeLight = _initialEdgeLight;
        _enableDynamicBackground = _initialDynamicBackground;
        _selectedFftSize = _initialFftSize;
        _selectedShortcutGlow = _initialShortcutGlow;
        _selectedShortcutLyrics = _initialShortcutLyrics;
        _selectedAudioDeviceId = _initialAudioDeviceId;
        _hasUserChangedAudioDevice = false;

        var settings = _settingsManager.Current;
        settings.Language = _initialLanguage;
        settings.Theme = _initialTheme;
        settings.GlowEnabled = _initialEdgeLight;
        settings.VisualIntegrationEnabled = _initialDynamicBackground;
        settings.Glow.FftSize = _initialFftSize;
        settings.Hotkeys.ToggleGlow = _initialShortcutGlow;
        settings.Hotkeys.ToggleLyrics = _initialShortcutLyrics;
        settings.Glow.PreferredDeviceId = _initialAudioDeviceId ?? string.Empty;

        OnboardingFinished?.Invoke(this, WelcomeCompletionReason.WindowClosed);
        return Task.FromResult(true);
    }

    private async Task<bool> DismissWithoutApplyingChangesAsync(WelcomeCompletionReason reason, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _finishSignaled, 1, 0) != 0)
        {
            return false;
        }

        // Restore exact pre-Welcome snapshot (defaults for brand-new user, existing prefs for returning user)
        _selectedLanguage = _initialLanguage;
        _selectedTheme = _initialTheme;
        _enableEdgeLight = _initialEdgeLight;
        _enableDynamicBackground = _initialDynamicBackground;
        _selectedFftSize = _initialFftSize;
        _selectedShortcutGlow = _initialShortcutGlow;
        _selectedShortcutLyrics = _initialShortcutLyrics;
        _selectedAudioDeviceId = _initialAudioDeviceId;
        _hasUserChangedAudioDevice = false;

        var settings = _settingsManager.Current;
        settings.Language = _initialLanguage;
        settings.Theme = _initialTheme;
        settings.GlowEnabled = _initialEdgeLight;
        settings.VisualIntegrationEnabled = _initialDynamicBackground;
        settings.Glow.FftSize = _initialFftSize;
        settings.Hotkeys.ToggleGlow = _initialShortcutGlow;
        settings.Hotkeys.ToggleLyrics = _initialShortcutLyrics;
        settings.Glow.PreferredDeviceId = _initialAudioDeviceId ?? string.Empty;
        settings.HasCompletedWelcome = true;
        settings.HasCompletedFirstRun = true;

        await _settingsManager.SaveAsync(settings, cancellationToken).ConfigureAwait(false);

        OnboardingFinished?.Invoke(this, reason);
        return true;
    }
}
