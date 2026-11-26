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

        // Seed directly from current user settings so existing preferences are never overwritten
        var current = _settingsManager.Current;
        _selectedTheme = current.Theme;
        _enableEdgeLight = current.GlowEnabled;
        _enableDynamicBackground = current.VisualIntegrationEnabled;
        _selectedAudioDeviceId = string.IsNullOrWhiteSpace(current.Glow.PreferredDeviceId)
            ? null
            : current.Glow.PreferredDeviceId;
    }

    public bool IsManualReopen { get; }

    public WelcomeStep CurrentStep => _currentStep;

    public int CurrentStepIndex => (int)_currentStep;

    public bool CanGoBack => _currentStep > WelcomeStep.Welcome;

    public bool CanContinue => _currentStep < WelcomeStep.Ready;

    public bool IsReadyStep => _currentStep == WelcomeStep.Ready;

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

    public Task<bool> CompleteAsync(CancellationToken cancellationToken = default)
        => FinalizeAsync(WelcomeCompletionReason.Completed, cancellationToken);

    public Task<bool> SkipAsync(CancellationToken cancellationToken = default)
        => FinalizeAsync(WelcomeCompletionReason.Skipped, cancellationToken);

    public Task<bool> CloseWindowAsync(CancellationToken cancellationToken = default)
        => FinalizeAsync(WelcomeCompletionReason.WindowClosed, cancellationToken);

    private async Task<bool> FinalizeAsync(WelcomeCompletionReason reason, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _finishSignaled, 1, 0) != 0)
        {
            return false;
        }

        var settings = _settingsManager.Current;
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

        OnboardingFinished?.Invoke(this, reason);
        return true;
    }
}
