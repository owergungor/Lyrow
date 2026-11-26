using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// Five-step onboarding sequence for the Lyrow v4.6 Welcome Experience.
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
/// MVVM-friendly, UI-framework-independent coordinator for the 5-step First-Run Welcome Experience.
/// Manages step navigation, live setting staging, safe audio output persistence, and atomic completion
/// without overwriting existing user settings or causing UI-thread deadlocks.
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
    private readonly string? _initialAudioDeviceId;

    private string _selectedLanguage;
    private ThemeMode _selectedTheme;
    private bool _enableEdgeLight;
    private bool _enableDynamicBackground;
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

        // Capture exact pre-Welcome snapshot so Customize Later / Window Close can discard temporary edits cleanly
        var current = _settingsManager.Current;
        _initialLanguage = string.IsNullOrWhiteSpace(current.Language) ? "en" : current.Language;
        _initialTheme = current.Theme;
        _initialEdgeLight = current.GlowEnabled;
        _initialDynamicBackground = current.VisualIntegrationEnabled;
        _initialAudioDeviceId = string.IsNullOrWhiteSpace(current.Glow.PreferredDeviceId)
            ? null
            : current.Glow.PreferredDeviceId;

        // Seed active selections from current user settings
        _selectedLanguage = _initialLanguage;
        _selectedTheme = _initialTheme;
        _enableEdgeLight = _initialEdgeLight;
        _enableDynamicBackground = _initialDynamicBackground;
        _selectedAudioDeviceId = _initialAudioDeviceId;
    }

    public bool IsManualReopen { get; }

    public string InitialLanguage => _initialLanguage;
    public ThemeMode InitialTheme => _initialTheme;
    public bool InitialEdgeLight => _initialEdgeLight;
    public bool InitialDynamicBackground => _initialDynamicBackground;

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
    /// Commits all user selections made on the Welcome screen (Language, Theme, Edge Light,
    /// Dynamic Background, Audio Output) and marks Welcome as completed.
    /// </summary>
    public async Task<bool> CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _finishSignaled, 1, 0) != 0)
        {
            return false;
        }

        var settings = _settingsManager.Current;
        settings.Language = _selectedLanguage;
        settings.Theme = _selectedTheme;
        settings.GlowEnabled = _enableEdgeLight;
        if (_enableEdgeLight && settings.GlowThickness <= 0)
        {
            settings.GlowThickness = 24;
        }

        settings.VisualIntegrationEnabled = _enableDynamicBackground;
        settings.Glow.PreferredDeviceId = _selectedAudioDeviceId ?? string.Empty;
        settings.HasCompletedWelcome = true;
        settings.HasCompletedFirstRun = true;

        await _settingsManager.SaveAsync(settings, cancellationToken).ConfigureAwait(false);

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
        _selectedAudioDeviceId = _initialAudioDeviceId;
        _hasUserChangedAudioDevice = false;

        var settings = _settingsManager.Current;
        settings.Language = _initialLanguage;
        settings.Theme = _initialTheme;
        settings.GlowEnabled = _initialEdgeLight;
        settings.VisualIntegrationEnabled = _initialDynamicBackground;
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
        _selectedAudioDeviceId = _initialAudioDeviceId;
        _hasUserChangedAudioDevice = false;

        var settings = _settingsManager.Current;
        settings.Language = _initialLanguage;
        settings.Theme = _initialTheme;
        settings.GlowEnabled = _initialEdgeLight;
        settings.VisualIntegrationEnabled = _initialDynamicBackground;
        settings.Glow.PreferredDeviceId = _initialAudioDeviceId ?? string.Empty;
        settings.HasCompletedWelcome = true;
        settings.HasCompletedFirstRun = true;

        await _settingsManager.SaveAsync(settings, cancellationToken).ConfigureAwait(false);

        OnboardingFinished?.Invoke(this, reason);
        return true;
    }
}
