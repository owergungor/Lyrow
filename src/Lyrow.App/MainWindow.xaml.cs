using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Lyrow.App.Controls;
using Lyrow.App.Platform;
using Lyrow.Audio.Control;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;

namespace Lyrow.App;

/// <summary>
/// Main application shell implementing Windows 11 native WinUI 3 design language,
/// Mica backdrop, compact NavigationView, real Windows audio volume control,
/// GSMTC media transport controls, Auto Update, and complete synchronization with Glow and Lyrics.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly IModuleManager _moduleManager;
    private readonly ISettingsManager _settingsManager;
    private readonly IWindowPlatformAdapter _platformAdapter;
    private readonly IWindowsStartupManager _startupManager;
    private readonly IGlobalHotkeyManager _hotkeyManager;
    private readonly LyrowTrayIconManager? _trayIconManager;
    private readonly GlowOverlayManager? _glowOverlayManager;
    private readonly ArtworkBitmapManager? _artworkBitmapManager;
    private readonly IToastNotificationService _toastService;
    private readonly IAudioVolumeService _volumeService;
    private readonly IUpdateService _updateService;
    private readonly IEventBus? _eventBus;
    private readonly IMediaSessionMonitor? _mediaMonitor;
    private readonly VerciModule? _verciModule;

    private readonly List<IDisposable> _subscriptions = new();
    private readonly DispatcherTimer _dashboardTimer;
    private ToastNotificationManager? _toastManager;
    private LyricsWindow? _lyricsWindow;
    private string _lastTrackCacheKey = string.Empty;
    private string? _latestUpdateReleaseUrl;
    private string? _latestInstallerDownloadUrl;
    private bool _isExplicitExit = false;
    private bool _isUpdatingToggles = false;
    private bool _isUpdatingVolumeUi = false;
    private bool _isUpdatingSettingsUi = false;

    public MainWindow(
        IModuleManager moduleManager,
        ISettingsManager settingsManager,
        IWindowPlatformAdapter? platformAdapter = null,
        IWindowsStartupManager? startupManager = null,
        IGlobalHotkeyManager? hotkeyManager = null,
        LyrowTrayIconManager? trayIconManager = null,
        GlowOverlayManager? glowOverlayManager = null,
        ArtworkBitmapManager? artworkBitmapManager = null,
        IToastNotificationService? toastService = null,
        IAudioVolumeService? volumeService = null,
        IUpdateService? updateService = null)
    {
        _moduleManager = moduleManager;
        _settingsManager = settingsManager;
        _platformAdapter = platformAdapter ?? new SyntheticWindowPlatformAdapter();
        _startupManager = startupManager ?? new SyntheticStartupManager();
        _hotkeyManager = hotkeyManager ?? new SyntheticGlobalHotkeyManager();
        _trayIconManager = trayIconManager;
        _glowOverlayManager = glowOverlayManager;
        _artworkBitmapManager = artworkBitmapManager;
        _toastService = toastService ?? new ToastNotificationService();
        _volumeService = volumeService
            ?? App.Services?.GetService<IAudioVolumeService>()
            ?? (OperatingSystem.IsWindows() ? new WindowsAudioVolumeService() : new SyntheticAudioVolumeService());
        _updateService = updateService
            ?? App.Services?.GetService<IUpdateService>()
            ?? new GitHubUpdateService();
        _eventBus = App.Services?.GetService<IEventBus>();
        _mediaMonitor = App.Services?.GetService<IMediaSessionMonitor>();
        _verciModule = App.Services?.GetService<VerciModule>();

        try
        {
            InitializeComponent();
            _toastManager = new ToastNotificationManager(ToastContainer, DispatcherQueue);
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError($"[DIAGNOSTIC] MainWindow.InitializeComponent FAILED! HResult=0x{ex.HResult:X8}, Message={ex.Message}\nDetails: {ex}", ex);
            throw;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ResizeForCurrentDpi(960, 720);

        NavView.SelectedItem = NavHome;

        InitializeSettingsUi();
        BindModuleToggles();
        ConfigureScrollingAndKeyboardSupport();
        HookEvents();

        _dashboardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100) // Smooth 10Hz dashboard timeline, lyrics preview & swatch sync
        };
        _dashboardTimer.Tick += OnDashboardTimerTick;
        _dashboardTimer.Start();

        _ = InitializeMediaAndUpdatesAsync();

        AppWindow.Closing += (sender, args) =>
        {
            if (_settingsManager.Current.MinimizeToTray && !_isExplicitExit)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
        };

        _trayIconManager?.Initialize(
            onShowMainWindow: () => DispatcherQueue.TryEnqueue(() => { AppWindow.Show(); }),
            onToggleLumn: () => ToggleGlowFromExternal(),
            onToggleVerci: () => ToggleLyricsFromExternal(),
            onToggleClickThrough: () => { },
            onOpenSettings: () => DispatcherQueue.TryEnqueue(() => { AppWindow.Show(); NavView.SelectedItem = NavView.SettingsItem; ShowPage(PageSettings); }),
            onExit: () => DispatcherQueue.TryEnqueue(RequestExit));

        Closed += OnWindowClosed;
    }

    private void ResizeForCurrentDpi(int logicalWidth, int logicalHeight)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            double scale = dpi > 0 ? (dpi / 96.0) : 1.0;
            int physW = (int)Math.Round(logicalWidth * scale);
            int physH = (int)Math.Round(logicalHeight * scale);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(physW, physH));
        }
        catch
        {
            try { AppWindow.Resize(new Windows.Graphics.SizeInt32(logicalWidth, logicalHeight)); } catch { }
        }
    }

    private void ConfigureScrollingAndKeyboardSupport()
    {
        // Ensure mouse wheel & touchpad two-finger scroll work anywhere on the page
        // even when hovering over Sliders, ComboBoxes, or cards
        MainScrollViewer.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnMainScrollViewerPointerWheelChanged),
            handledEventsToo: true);

        RootShellGrid.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnShellKeyDown),
            handledEventsToo: false);
    }

    private void OnMainScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(MainScrollViewer);
        var props = point.Properties;
        if (props.IsHorizontalMouseWheel || props.MouseWheelDelta == 0) return;

        double scrollable = MainScrollViewer.ScrollableHeight;
        if (scrollable <= 0) return;

        double delta = props.MouseWheelDelta * 0.85;
        double targetOffset = Math.Clamp(MainScrollViewer.VerticalOffset - delta, 0.0, scrollable);
        MainScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
        e.Handled = true;
    }

    private void OnShellKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox)
        {
            return;
        }

        double scrollable = MainScrollViewer.ScrollableHeight;
        if (scrollable <= 0) return;

        double current = MainScrollViewer.VerticalOffset;
        double viewport = Math.Max(200.0, MainScrollViewer.ViewportHeight);

        switch (e.Key)
        {
            case VirtualKey.PageDown:
                MainScrollViewer.ChangeView(null, Math.Clamp(current + viewport * 0.85, 0.0, scrollable), null, disableAnimation: false);
                e.Handled = true;
                break;

            case VirtualKey.PageUp:
                MainScrollViewer.ChangeView(null, Math.Clamp(current - viewport * 0.85, 0.0, scrollable), null, disableAnimation: false);
                e.Handled = true;
                break;

            case VirtualKey.Home:
                MainScrollViewer.ChangeView(null, 0.0, null, disableAnimation: false);
                e.Handled = true;
                break;

            case VirtualKey.End:
                MainScrollViewer.ChangeView(null, scrollable, null, disableAnimation: false);
                e.Handled = true;
                break;
        }
    }

    private async Task InitializeMediaAndUpdatesAsync()
    {
        // 1. Start GSMTC media session monitor immediately so metadata, artwork, playback controls,
        // and Glow artwork colors work regardless of whether Lyrics overlay is toggled ON or OFF.
        if (_mediaMonitor != null)
        {
            try
            {
                if (!_mediaMonitor.IsMonitoring)
                {
                    await _mediaMonitor.StartAsync();
                }

                _volumeService.SetPreferredAppId(_mediaMonitor.SourceAppId);
                DispatcherQueue.TryEnqueue(() =>
                {
                    SyncVolumeUiFromHardware();
                    UpdateMediaTransportControlsState(_mediaMonitor.HasActiveSession, _mediaMonitor.CurrentTimeline.Status);
                });

                if (!_mediaMonitor.CurrentTrack.IsEmpty)
                {
                    await HandleTrackChangedUnifiedAsync(_mediaMonitor.CurrentTrack);
                }
            }
            catch (Exception ex)
            {
                LocalDiagnosticLogger.LogWarning($"GSMTC startup notice: {ex.Message}");
            }
        }

        // 2. Non-intrusive background update check if AutoUpdateEnabled is ON and due
        if (_settingsManager.Current.AutoUpdateEnabled)
        {
            var freq = _settingsManager.Current.UpdateFrequency;
            var lastCheck = _settingsManager.Current.LastUpdateCheckUtc;
            if (GitHubUpdateService.IsUpdateCheckDue(freq, lastCheck, DateTime.UtcNow))
            {
                _ = CheckForUpdatesInternalAsync(userInitiated: false);
            }
        }
    }

    private void InitializeSettingsUi()
    {
        _isUpdatingSettingsUi = true;
        try
        {
            var settings = _settingsManager.Current;

            // Startup State: Always ensure Glow and Lyrics start in OFF state
            SwitchHomeGlow.IsOn = false;
            SwitchGlowMaster.IsOn = false;
            SwitchSettingsGlowEnabled.IsOn = false;
            TxtGlowStatus.Text = "Stopped";
            TxtGlowStatus.Foreground = (Brush)Application.Current.Resources["LyrowStoppedBrush"];
            TxtGlowVisualState.Text = "Standby — 4-edge ambient lighting ready";

            SwitchHomeLyrics.IsOn = false;
            SwitchLyricsMaster.IsOn = false;
            TxtLyricsStatus.Text = "Stopped";
            TxtLyricsStatus.Foreground = (Brush)Application.Current.Resources["LyrowStoppedBrush"];

            // Glow controls
            SliderGlowIntensity.Value = settings.GlowIntensity;
            SliderSettingsGlowIntensity.Value = settings.GlowIntensity;
            TxtGlowIntensityVal.Text = $"{settings.GlowIntensity}%";
            TxtSettingsGlowIntensity.Text = $"{settings.GlowIntensity}%";

            SliderGlowThickness.Value = settings.GlowThickness;
            TxtGlowThicknessVal.Text = $"{settings.GlowThickness} px";

            int sensPct = (int)Math.Round(settings.Lumn.Sensitivity * 100.0f);
            SliderGlowSensitivity.Value = Math.Clamp(sensPct, 40, 250);
            TxtGlowSensitivityVal.Text = $"{Math.Clamp(sensPct, 40, 250)}%";

            SwitchAlbumArtSync.IsOn = settings.SyncWithAlbumArt;
            SwitchSettingsAlbumArtSync.IsOn = settings.SyncWithAlbumArt;
            ChkMultiMonitor.IsChecked = settings.MultiMonitorGlow;

            // Rich Fallback Palette Presets (6 presets with 4-dot colored circle items)
            PopulatePalettePresetCombo(ComboPalettePreset, settings.FallbackPalettePresetIndex);

            // Shutdown Delay ComboBoxes
            SelectShutdownDelayCombo(ComboGlowShutdownDelay, settings.GlowShutdownDelaySeconds);
            SelectShutdownDelayCombo(ComboSettingsGlowShutdownDelay, settings.GlowShutdownDelaySeconds);

            // Lyrics controls
            SliderLyricsOffset.Value = settings.Verci.SyncOffsetMs;
            SliderSettingsLyricsOffset.Value = settings.Verci.SyncOffsetMs;
            TxtPageLyricsOffset.Text = $"{settings.Verci.SyncOffsetMs:+0;-0;0} ms";
            TxtSettingsLyricsOffset.Text = $"{settings.Verci.SyncOffsetMs:+0;-0;0} ms";

            // Lyrics Font Sliders
            SliderFocusedFontSize.Value = settings.FocusedLyricFontSize;
            SliderSettingsFocusedFontSize.Value = settings.FocusedLyricFontSize;
            TxtFocusedFontSize.Text = $"{settings.FocusedLyricFontSize:0} px";
            TxtSettingsFocusedFontSize.Text = $"{settings.FocusedLyricFontSize:0} px";

            SliderUnfocusedFontSize.Value = settings.UnfocusedLyricFontSize;
            SliderSettingsUnfocusedFontSize.Value = settings.UnfocusedLyricFontSize;
            TxtUnfocusedFontSize.Text = $"{settings.UnfocusedLyricFontSize:0} px";
            TxtSettingsUnfocusedFontSize.Text = $"{settings.UnfocusedLyricFontSize:0} px";

            // General & Updates
            SwitchStartWithWindows.IsOn = settings.StartWithWindows;
            SwitchSystemTray.IsOn = settings.SystemTrayEnabled;
            SwitchAutoUpdate.IsOn = settings.AutoUpdateEnabled;
            ComboUpdateFrequency.SelectedIndex = Math.Clamp((int)settings.UpdateFrequency, 0, 3);
            TxtCurrentVersion.Text = $"Current Version: v{_updateService.CurrentVersion}";

            string langCode = settings.Language ?? "en";
            ComboLanguage.SelectedIndex = langCode switch
            {
                "fr" => 1,
                "es" => 2,
                "de" => 3,
                "tr" => 4,
                _ => 0
            };
            LocalizationService.Instance.SetLanguage(langCode);
            ApplyLocalization();
            ShowSettingsCategory("All");

            TxtHotkeyGlow.Text = settings.Hotkeys.ToggleLumn;
            TxtHotkeyLyrics.Text = settings.Hotkeys.ToggleVerci;

            ComboTheme.SelectedIndex = settings.Theme switch
            {
                ThemeMode.Dark => 1,
                ThemeMode.Light => 2,
                _ => 0
            };

            // Initialize Dual Volume UI from real Windows Audio Service (0% - 100%)
            SyncVolumeUiFromHardware();
            _volumeService.VolumeChanged += (_, _) =>
            {
                DispatcherQueue?.TryEnqueue(SyncVolumeUiFromHardware);
            };
            UpdatePaletteSwatches(GlowPalette.Default);
        }
        finally
        {
            _isUpdatingSettingsUi = false;
        }
    }

    private void SyncVolumeUiFromHardware()
    {
        if (_isUpdatingVolumeUi) return;
        _isUpdatingVolumeUi = true;
        try
        {
            // 1. Playback session volume
            int currentPlayback = _volumeService.GetPlaybackVolumePercent();
            bool playbackMuted = _volumeService.IsPlaybackMuted;
            SliderPlaybackVolume.Value = currentPlayback;
            SliderSettingsPlaybackVolume.Value = currentPlayback;
            TxtPlaybackVolumePercent.Text = playbackMuted ? $"Muted ({currentPlayback}%)" : $"{currentPlayback}%";
            TxtSettingsPlaybackVolumePercent.Text = playbackMuted ? $"Muted ({currentPlayback}%)" : $"{currentPlayback}%";
            TxtPlaybackTarget.Text = _volumeService.PlaybackTargetName;
            IconPlaybackMute.Glyph = (playbackMuted || currentPlayback == 0) ? "\uE74F" : "\uE767";

            // 2. System endpoint master volume
            int currentSystem = _volumeService.GetSystemVolumePercent();
            bool systemMuted = _volumeService.IsSystemMuted;
            SliderSystemVolume.Value = currentSystem;
            SliderSettingsSystemVolume.Value = currentSystem;
            TxtSystemVolumePercent.Text = systemMuted ? $"Muted ({currentSystem}%)" : $"{currentSystem}%";
            TxtSettingsSystemVolumePercent.Text = systemMuted ? $"Muted ({currentSystem}%)" : $"{currentSystem}%";
            TxtSystemTarget.Text = _volumeService.SystemTargetName;
            IconSystemMute.Glyph = (systemMuted || currentSystem == 0) ? "\uE74F" : "\uE767";
        }
        finally
        {
            _isUpdatingVolumeUi = false;
        }
    }

    private void BindModuleToggles()
    {
        // Glow Toggles (Home, Glow Page, Settings Page)
        SwitchHomeGlow.Toggled += async (_, _) => await SetGlowStateAsync(SwitchHomeGlow.IsOn);
        SwitchGlowMaster.Toggled += async (_, _) => await SetGlowStateAsync(SwitchGlowMaster.IsOn);
        SwitchSettingsGlowEnabled.Toggled += async (_, _) => await SetGlowStateAsync(SwitchSettingsGlowEnabled.IsOn);

        // Lyrics Toggles (Home, Lyrics Page)
        SwitchHomeLyrics.Toggled += async (_, _) => await SetLyricsStateAsync(SwitchHomeLyrics.IsOn);
        SwitchLyricsMaster.Toggled += async (_, _) => await SetLyricsStateAsync(SwitchLyricsMaster.IsOn);

        // Real Windows Dual Volume Sliders (0% - 100% linear percentage)
        SliderPlaybackVolume.ValueChanged += (_, e) => OnPlaybackVolumeSliderChanged(e.NewValue);
        SliderSettingsPlaybackVolume.ValueChanged += (_, e) => OnPlaybackVolumeSliderChanged(e.NewValue);

        BtnMutePlayback.Click += (_, _) =>
        {
            _volumeService.TogglePlaybackMute();
            SyncVolumeUiFromHardware();
        };

        SliderSystemVolume.ValueChanged += (_, e) => OnSystemVolumeSliderChanged(e.NewValue);
        SliderSettingsSystemVolume.ValueChanged += (_, e) => OnSystemVolumeSliderChanged(e.NewValue);

        BtnMuteSystem.Click += (_, _) =>
        {
            _volumeService.ToggleSystemMute();
            SyncVolumeUiFromHardware();
        };

        // Glow Intensity Sliders
        SliderGlowIntensity.ValueChanged += (_, e) => OnGlowIntensityChanged(e.NewValue);
        SliderSettingsGlowIntensity.ValueChanged += (_, e) => OnGlowIntensityChanged(e.NewValue);

        SliderGlowThickness.ValueChanged += (_, e) =>
        {
            if (_isUpdatingSettingsUi) return;
            int val = (int)Math.Round(e.NewValue);
            TxtGlowThicknessVal.Text = $"{val} px";
            _settingsManager.Current.GlowThickness = val;
        };

        SliderGlowSensitivity.ValueChanged += (_, e) =>
        {
            if (_isUpdatingSettingsUi) return;
            int pct = (int)Math.Round(e.NewValue);
            TxtGlowSensitivityVal.Text = $"{pct}%";
            float sens = Math.Clamp(pct / 100.0f, 0.4f, 2.5f);
            _settingsManager.Current.Lumn.Sensitivity = sens;
            var analyzer = App.Services?.GetService<ISpectrumAnalyzer>();
            if (analyzer != null) analyzer.Sensitivity = sens;
        };

        SwitchAlbumArtSync.Toggled += (_, _) => OnAlbumArtSyncToggled(SwitchAlbumArtSync.IsOn);
        SwitchSettingsAlbumArtSync.Toggled += (_, _) => OnAlbumArtSyncToggled(SwitchSettingsAlbumArtSync.IsOn);

        ComboPalettePreset.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingSettingsUi || ComboPalettePreset == null) return;
            int idx = Math.Clamp(ComboPalettePreset.SelectedIndex, 0, PalettePresets.All.Count - 1);
            _settingsManager.Current.FallbackPalettePresetIndex = idx;
            if (!_settingsManager.Current.SyncWithAlbumArt || (_mediaMonitor?.CurrentTrack.IsEmpty ?? true))
            {
                ApplyFallbackPalettePreset(idx);
            }
        };

        ComboGlowShutdownDelay.SelectionChanged += (_, _) => OnGlowShutdownDelayChanged(ComboGlowShutdownDelay);
        ComboSettingsGlowShutdownDelay.SelectionChanged += (_, _) => OnGlowShutdownDelayChanged(ComboSettingsGlowShutdownDelay);

        ChkMultiMonitor.Checked += (_, _) =>
        {
            if (_isUpdatingSettingsUi) return;
            _settingsManager.Current.MultiMonitorGlow = true;
            _glowOverlayManager?.HandleDisplayConfigurationChanged();
        };
        ChkMultiMonitor.Unchecked += (_, _) =>
        {
            if (_isUpdatingSettingsUi) return;
            _settingsManager.Current.MultiMonitorGlow = false;
            _glowOverlayManager?.HandleDisplayConfigurationChanged();
        };

        // Lyrics offset sliders
        SliderLyricsOffset.ValueChanged += (_, e) => OnLyricsOffsetChanged(e.NewValue);
        SliderSettingsLyricsOffset.ValueChanged += (_, e) => OnLyricsOffsetChanged(e.NewValue);

        BtnOffsetMinus500.Click += (_, _) => SliderLyricsOffset.Value -= 500;
        BtnOffsetPlus500.Click += (_, _) => SliderLyricsOffset.Value += 500;
        BtnOffsetReset.Click += (_, _) => SliderLyricsOffset.Value = 0;

        // Language Selection
        ComboLanguage.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingSettingsUi || ComboLanguage == null) return;
            string code = ComboLanguage.SelectedIndex switch
            {
                1 => "fr",
                2 => "es",
                3 => "de",
                4 => "tr",
                _ => "en"
            };
            _settingsManager.Current.Language = code;
            LocalizationService.Instance.SetLanguage(code);
            ApplyLocalization();
            _ = _settingsManager.SaveAsync(_settingsManager.Current);
        };

        WireSettingsCategoryFilters();

        BtnDownloadAndInstallUpdate.Click += async (_, _) =>
        {
            await StartSelfUpdateDownloadAsync(_latestInstallerDownloadUrl);
        };

        // Lyrics Font Sliders
        SliderFocusedFontSize.ValueChanged += (_, e) => OnFocusedFontSizeChanged(e.NewValue);
        SliderSettingsFocusedFontSize.ValueChanged += (_, e) => OnFocusedFontSizeChanged(e.NewValue);

        SliderUnfocusedFontSize.ValueChanged += (_, e) => OnUnfocusedFontSizeChanged(e.NewValue);
        SliderSettingsUnfocusedFontSize.ValueChanged += (_, e) => OnUnfocusedFontSizeChanged(e.NewValue);

        ComboUpdateFrequency.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingSettingsUi || ComboUpdateFrequency == null) return;
            _settingsManager.Current.UpdateFrequency = (UpdateCheckFrequency)Math.Clamp(ComboUpdateFrequency.SelectedIndex, 0, 3);
        };

        // General & Startup Settings
        SwitchStartWithWindows.Toggled += (_, _) =>
        {
            if (_isUpdatingSettingsUi) return;
            bool enable = SwitchStartWithWindows.IsOn;
            _settingsManager.Current.StartWithWindows = enable;
            _startupManager.SetStartupEnabled(enable);
            ShowToast(enable ? "Lyrow will launch automatically on Windows startup." : "Windows startup launch disabled.", InfoBarSeverity.Success);
        };

        SwitchSystemTray.Toggled += (_, _) =>
        {
            if (_isUpdatingSettingsUi) return;
            _settingsManager.Current.SystemTrayEnabled = SwitchSystemTray.IsOn;
        };

        // Auto Update Settings
        SwitchAutoUpdate.Toggled += async (_, _) =>
        {
            if (_isUpdatingSettingsUi) return;
            _settingsManager.Current.AutoUpdateEnabled = SwitchAutoUpdate.IsOn;
            await _settingsManager.SaveAsync(_settingsManager.Current);
        };

        BtnCheckUpdates.Click += async (_, _) =>
        {
            await CheckForUpdatesInternalAsync(userInitiated: true);
        };

        BtnOpenUpdateRelease.Click += (_, _) =>
        {
            string url = !string.IsNullOrWhiteSpace(_latestUpdateReleaseUrl)
                ? _latestUpdateReleaseUrl!
                : GitHubUpdateService.DefaultReleasesPageUrl;

            if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch { }
            }
        };

        // Shortcuts
        BtnSaveHotkeys.Click += async (_, _) =>
        {
            _settingsManager.Current.Hotkeys.ToggleLumn = TxtHotkeyGlow.Text.Trim();
            _settingsManager.Current.Hotkeys.ToggleVerci = TxtHotkeyLyrics.Text.Trim();
            await _settingsManager.SaveAsync(_settingsManager.Current);
            _hotkeyManager.RegisterHotkeys(
                _settingsManager.Current.Hotkeys,
                onLumnToggle: ToggleGlowFromExternal,
                onVerciToggle: ToggleLyricsFromExternal,
                onClickThroughToggle: () => { });
            ShowToast("Shortcuts saved and applied.", InfoBarSeverity.Success);
        };

        BtnClearHotkeys.Click += async (_, _) =>
        {
            TxtHotkeyGlow.Text = string.Empty;
            TxtHotkeyLyrics.Text = string.Empty;
            _settingsManager.Current.Hotkeys.ToggleLumn = string.Empty;
            _settingsManager.Current.Hotkeys.ToggleVerci = string.Empty;
            _hotkeyManager.UnregisterAll();
            await _settingsManager.SaveAsync(_settingsManager.Current);
            ShowToast("All global shortcuts cleared.", InfoBarSeverity.Informational);
        };

        BtnResetSettings.Click += async (_, _) =>
        {
            var def = LyrowSettings.CreateDefault();
            await _settingsManager.SaveAsync(def);
            InitializeSettingsUi();
            ShowToast("Settings restored to defaults.", InfoBarSeverity.Informational);
        };

        BtnOpenLogs.Click += (_, _) =>
        {
            try
            {
                if (File.Exists(LocalDiagnosticLogger.LogFilePath))
                {
                    Process.Start(new ProcessStartInfo(LocalDiagnosticLogger.LogFilePath) { UseShellExecute = true });
                }
            }
            catch { }
        };

        // Playback Controls (Dashboard & Settings)
        BtnPlayPause.Click += async (_, _) => await ExecutePlayPauseCommandAsync();
        BtnNextTrack.Click += async (_, _) => await ExecuteNextTrackCommandAsync();
        BtnPrevTrack.Click += async (_, _) => await ExecutePrevTrackCommandAsync();

        BtnSettingsPlayPause.Click += async (_, _) => await ExecutePlayPauseCommandAsync();
        BtnSettingsNext.Click += async (_, _) => await ExecuteNextTrackCommandAsync();
        BtnSettingsPrev.Click += async (_, _) => await ExecutePrevTrackCommandAsync();

        // Navigation and Theme events
        NavView.SelectionChanged += NavView_SelectionChanged;
        ComboTheme.SelectionChanged += ComboTheme_SelectionChanged;
    }

    private void OnPlaybackVolumeSliderChanged(double newValue)
    {
        if (_isUpdatingVolumeUi || _isUpdatingSettingsUi) return;
        _isUpdatingVolumeUi = true;
        try
        {
            int percent = Math.Clamp((int)Math.Round(newValue), 0, 100);
            _volumeService.SetPlaybackVolumePercent(percent);

            SliderPlaybackVolume.Value = percent;
            SliderSettingsPlaybackVolume.Value = percent;

            string formatted = $"{percent}%";
            TxtPlaybackVolumePercent.Text = formatted;
            TxtSettingsPlaybackVolumePercent.Text = formatted;
            TxtPlaybackTarget.Text = _volumeService.PlaybackTargetName;
            IconPlaybackMute.Glyph = percent == 0 ? "\uE74F" : "\uE767";
        }
        finally
        {
            _isUpdatingVolumeUi = false;
        }
    }

    private void OnSystemVolumeSliderChanged(double newValue)
    {
        if (_isUpdatingVolumeUi || _isUpdatingSettingsUi) return;
        _isUpdatingVolumeUi = true;
        try
        {
            int percent = Math.Clamp((int)Math.Round(newValue), 0, 100);
            _volumeService.SetSystemVolumePercent(percent);

            SliderSystemVolume.Value = percent;
            SliderSettingsSystemVolume.Value = percent;

            string formatted = $"{percent}%";
            TxtSystemVolumePercent.Text = formatted;
            TxtSettingsSystemVolumePercent.Text = formatted;
            TxtSystemTarget.Text = _volumeService.SystemTargetName;
            IconSystemMute.Glyph = percent == 0 ? "\uE74F" : "\uE767";
        }
        finally
        {
            _isUpdatingVolumeUi = false;
        }
    }

    private void OnGlowIntensityChanged(double newValue)
    {
        if (_isUpdatingSettingsUi) return;
        _isUpdatingSettingsUi = true;
        try
        {
            int val = Math.Clamp((int)Math.Round(newValue), 10, 100);
            SliderGlowIntensity.Value = val;
            SliderSettingsGlowIntensity.Value = val;
            TxtGlowIntensityVal.Text = $"{val}%";
            TxtSettingsGlowIntensity.Text = $"{val}%";
            _settingsManager.Current.GlowIntensity = val;
        }
        finally
        {
            _isUpdatingSettingsUi = false;
        }
    }

    private void OnAlbumArtSyncToggled(bool enabled)
    {
        if (_isUpdatingSettingsUi) return;
        _isUpdatingSettingsUi = true;
        try
        {
            SwitchAlbumArtSync.IsOn = enabled;
            SwitchSettingsAlbumArtSync.IsOn = enabled;
            _settingsManager.Current.SyncWithAlbumArt = enabled;

            if (!enabled)
            {
                ApplyFallbackPalettePreset(_settingsManager.Current.FallbackPalettePresetIndex);
            }
            else if (_mediaMonitor != null && !_mediaMonitor.CurrentTrack.IsEmpty)
            {
                _ = UpdateTrackArtworkAndPaletteAsync(_mediaMonitor.CurrentTrack);
            }
        }
        finally
        {
            _isUpdatingSettingsUi = false;
        }
    }

    private void OnFocusedFontSizeChanged(double newValue)
    {
        if (_isUpdatingSettingsUi) return;
        _isUpdatingSettingsUi = true;
        try
        {
            double val = Math.Clamp(Math.Round(newValue), 20.0, 48.0);
            SliderFocusedFontSize.Value = val;
            SliderSettingsFocusedFontSize.Value = val;
            TxtFocusedFontSize.Text = $"{val:0} px";
            TxtSettingsFocusedFontSize.Text = $"{val:0} px";
            _settingsManager.Current.FocusedLyricFontSize = val;
        }
        finally
        {
            _isUpdatingSettingsUi = false;
        }
    }

    private void OnUnfocusedFontSizeChanged(double newValue)
    {
        if (_isUpdatingSettingsUi) return;
        _isUpdatingSettingsUi = true;
        try
        {
            double val = Math.Clamp(Math.Round(newValue), 14.0, 36.0);
            SliderUnfocusedFontSize.Value = val;
            SliderSettingsUnfocusedFontSize.Value = val;
            TxtUnfocusedFontSize.Text = $"{val:0} px";
            TxtSettingsUnfocusedFontSize.Text = $"{val:0} px";
            _settingsManager.Current.UnfocusedLyricFontSize = val;
        }
        finally
        {
            _isUpdatingSettingsUi = false;
        }
    }

    private void OnGlowShutdownDelayChanged(ComboBox source)
    {
        if (_isUpdatingSettingsUi || source?.SelectedItem is not ComboBoxItem item) return;
        if (int.TryParse(item.Tag?.ToString(), out int delaySec))
        {
            _settingsManager.Current.GlowShutdownDelaySeconds = delaySec;
            if (source == ComboGlowShutdownDelay && ComboSettingsGlowShutdownDelay != null)
            {
                SelectShutdownDelayCombo(ComboSettingsGlowShutdownDelay, delaySec);
            }
            else if (source == ComboSettingsGlowShutdownDelay && ComboGlowShutdownDelay != null)
            {
                SelectShutdownDelayCombo(ComboGlowShutdownDelay, delaySec);
            }
        }
    }

    private static void SelectShutdownDelayCombo(ComboBox combo, int delaySec)
    {
        if (combo == null) return;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem cbi && int.TryParse(cbi.Tag?.ToString(), out int tagVal) && tagVal == delaySec)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 2; // Default 3s
    }

    private static void PopulatePalettePresetCombo(ComboBox combo, int selectedIndex)
    {
        if (combo == null) return;
        combo.Items.Clear();

        foreach (var preset in PalettePresets.All)
        {
            var dots = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            dots.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = HexToBrush(preset.TopColorHex)
            });
            dots.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = HexToBrush(preset.RightColorHex)
            });
            dots.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = HexToBrush(preset.BottomColorHex)
            });
            dots.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = HexToBrush(preset.LeftColorHex)
            });

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(dots);
            row.Children.Add(new TextBlock
            {
                Text = preset.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            });

            var item = new ComboBoxItem
            {
                Content = row,
                Tag = preset.Id
            };
            combo.Items.Add(item);
        }

        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, PalettePresets.All.Count - 1);
    }

    private static SolidColorBrush HexToBrush(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }
        catch
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 128, 128, 128));
        }
    }

    private void ApplyFallbackPalettePreset(int presetIndex)
    {
        int safeIndex = Math.Clamp(presetIndex, 0, PalettePresets.All.Count - 1);
        var palette = PalettePresets.All[safeIndex].Palette;
        _glowOverlayManager?.SetCustomPalette(palette);
        UpdatePaletteSwatches(palette);
    }

    private void OnLyricsOffsetChanged(double newValue)
    {
        if (_isUpdatingSettingsUi) return;
        _isUpdatingSettingsUi = true;
        try
        {
            int val = (int)Math.Round(newValue);
            SliderLyricsOffset.Value = val;
            SliderSettingsLyricsOffset.Value = val;
            TxtPageLyricsOffset.Text = $"{val:+0;-0;0} ms";
            TxtSettingsLyricsOffset.Text = $"{val:+0;-0;0} ms";
            _settingsManager.Current.Verci.SyncOffsetMs = val;
            _verciModule?.SetSyncOffset(val);
        }
        finally
        {
            _isUpdatingSettingsUi = false;
        }
    }


    private async Task ExecutePlayPauseCommandAsync()
    {
        if (_mediaMonitor == null || !_mediaMonitor.HasActiveSession)
        {
            UpdateMediaTransportControlsState(hasSession: false, MediaPlaybackStatus.Stopped);
            ShowToast("No active media session found. Open Spotify, Apple Music, or your browser.", InfoBarSeverity.Warning);
            return;
        }

        bool ok = await _mediaMonitor.TryTogglePlayPauseAsync();
        if (!ok)
        {
            ShowToast("Media player did not respond to Play/Pause command.", InfoBarSeverity.Informational);
        }
        UpdateMediaTransportControlsState(_mediaMonitor.HasActiveSession, _mediaMonitor.CurrentTimeline.Status);
    }

    private async Task ExecuteNextTrackCommandAsync()
    {
        if (_mediaMonitor == null || !_mediaMonitor.HasActiveSession)
        {
            UpdateMediaTransportControlsState(hasSession: false, MediaPlaybackStatus.Stopped);
            ShowToast("No active media session found.", InfoBarSeverity.Warning);
            return;
        }

        await _mediaMonitor.TrySkipNextAsync();
    }

    private async Task ExecutePrevTrackCommandAsync()
    {
        if (_mediaMonitor == null || !_mediaMonitor.HasActiveSession)
        {
            UpdateMediaTransportControlsState(hasSession: false, MediaPlaybackStatus.Stopped);
            ShowToast("No active media session found.", InfoBarSeverity.Warning);
            return;
        }

        await _mediaMonitor.TrySkipPreviousAsync();
    }

    private void UpdateMediaTransportControlsState(bool hasSession, MediaPlaybackStatus status)
    {
        BtnPrevTrack.IsEnabled = hasSession;
        BtnPlayPause.IsEnabled = hasSession;
        BtnNextTrack.IsEnabled = hasSession;

        BtnSettingsPrev.IsEnabled = hasSession;
        BtnSettingsPlayPause.IsEnabled = hasSession;
        BtnSettingsNext.IsEnabled = hasSession;

        bool isPlaying = status == MediaPlaybackStatus.Playing;
        IconPlayPause.Glyph = isPlaying ? "\uE769" : "\uE768"; // Pause vs Play glyph
        TxtSettingsMediaStatus.Text = hasSession
            ? $"Connected ({(isPlaying ? "Playing" : status.ToString())})"
            : "No active media session detected";
    }

    private async Task CheckForUpdatesInternalAsync(bool userInitiated)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            BtnCheckUpdates.IsEnabled = false;
            BtnCheckUpdates.Content = "Checking...";
        });

        var result = await _updateService.CheckForUpdatesAsync();

        DispatcherQueue.TryEnqueue(() =>
        {
            BtnCheckUpdates.IsEnabled = true;
            BtnCheckUpdates.Content = "Check for Updates";

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _latestUpdateReleaseUrl = result.ReleaseUrl;
                    _latestInstallerDownloadUrl = result.InstallerDownloadUrl;
                    BtnDownloadAndInstallUpdate.Visibility = !string.IsNullOrWhiteSpace(_latestInstallerDownloadUrl)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    BtnOpenUpdateRelease.Visibility = Visibility.Collapsed;
                    TxtUpdateStatusSummary.Text = $"{LocalizationService.Instance["Updates_Available"]}: v{result.LatestVersion} ({result.ReleaseTitle})";
                    UpdateStatusInfoBar.Severity = InfoBarSeverity.Success;
                    UpdateStatusInfoBar.Title = $"{LocalizationService.Instance["Updates_Available"]} — v{result.LatestVersion}";
                    UpdateStatusInfoBar.Message = $"{result.ReleaseTitle} is available on GitHub Releases.";
                    UpdateStatusInfoBar.IsOpen = true;
                    ShowToast(
                        $"Lyrow v{result.LatestVersion} is available ({result.ReleaseTitle}).",
                        InfoBarSeverity.Success,
                        title: LocalizationService.Instance["Updates_Available"],
                        actionLabel: LocalizationService.Instance["Updates_DownloadAndInstall"],
                        onAction: () =>
                        {
                            _ = StartSelfUpdateDownloadAsync(_latestInstallerDownloadUrl);
                        });
                    break;

                case UpdateCheckStatus.UpToDate:
                    BtnOpenUpdateRelease.Visibility = Visibility.Collapsed;
                    TxtUpdateStatusSummary.Text = $"Up to date (v{_updateService.CurrentVersion}) — Checked {DateTime.Now:HH:mm}";
                    if (userInitiated)
                    {
                        UpdateStatusInfoBar.Severity = InfoBarSeverity.Success;
                        UpdateStatusInfoBar.Title = "Up to Date";
                        UpdateStatusInfoBar.Message = $"You are running the latest version of Lyrow (v{_updateService.CurrentVersion}).";
                        UpdateStatusInfoBar.IsOpen = true;
                        ShowToast($"Lyrow v{_updateService.CurrentVersion} is up to date.", InfoBarSeverity.Success, title: "Up to Date");
                    }
                    break;

                case UpdateCheckStatus.Offline:
                    BtnOpenUpdateRelease.Visibility = Visibility.Collapsed;
                    TxtUpdateStatusSummary.Text = "Offline — Could not reach GitHub Releases";
                    if (userInitiated)
                    {
                        UpdateStatusInfoBar.Severity = InfoBarSeverity.Warning;
                        UpdateStatusInfoBar.Title = "Offline";
                        UpdateStatusInfoBar.Message = result.ErrorMessage ?? "Could not check for updates while offline.";
                        UpdateStatusInfoBar.IsOpen = true;
                        ShowToast("Offline — Could not reach GitHub Releases", InfoBarSeverity.Warning, title: "Offline");
                    }
                    break;

                default:
                    BtnOpenUpdateRelease.Visibility = Visibility.Collapsed;
                    TxtUpdateStatusSummary.Text = "Update check unavailable right now";
                    if (userInitiated)
                    {
                        UpdateStatusInfoBar.Severity = InfoBarSeverity.Warning;
                        UpdateStatusInfoBar.Title = "Update Check Notice";
                        UpdateStatusInfoBar.Message = result.ErrorMessage ?? "Unable to verify release status.";
                        UpdateStatusInfoBar.IsOpen = true;
                    }
                    break;
            }

            _settingsManager.Current.LastUpdateCheckUtc = DateTime.UtcNow;
            _ = _settingsManager.SaveAsync(_settingsManager.Current);
        });
    }

    private async Task StartSelfUpdateDownloadAsync(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            ShowToast("No direct installer asset available for this release.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            BtnDownloadAndInstallUpdate.IsEnabled = false;
            ProgressUpdateDownload.Visibility = Visibility.Visible;
            ProgressUpdateDownload.Value = 0;
            TxtUpdateDownloadProgress.Visibility = Visibility.Visible;
            TxtUpdateDownloadProgress.Text = $"{LocalizationService.Instance["Updates_Downloading"]} 0%";

            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lyrow", "Updates");
            string destFile = System.IO.Path.Combine(tempDir, "Lyrow-2.3-Setup-x64.exe");

            var progress = new Progress<double>(pct =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ProgressUpdateDownload.Value = pct;
                    TxtUpdateDownloadProgress.Text = $"{LocalizationService.Instance["Updates_Downloading"]} {pct:0}%";
                });
            });

            await _updateService.DownloadUpdatePackageAsync(downloadUrl, destFile, progress);

            TxtUpdateDownloadProgress.Text = "Update ready. Applying update...";
            ShowToast(
                LocalizationService.Instance["Updates_InstallPrompt"],
                severity: InfoBarSeverity.Success,
                title: LocalizationService.Instance["Updates_Available"],
                actionLabel: LocalizationService.Instance["Updates_InstallBtn"],
                onAction: () =>
                {
                    _updateService.LaunchInstallerAndExit(destFile);
                });
        }
        catch (Exception ex)
        {
            TxtUpdateDownloadProgress.Text = $"Download failed: {ex.Message}";
            ShowToast($"Update download failed: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            BtnDownloadAndInstallUpdate.IsEnabled = true;
        }
    }

    private void WireSettingsCategoryFilters()
    {
        BtnFilterAllSettings.Click += (_, _) => ShowSettingsCategory("All");
        BtnFilterGeneral.Click += (_, _) => ShowSettingsCategory("General");
        BtnFilterPlayback.Click += (_, _) => ShowSettingsCategory("Playback");
        BtnFilterLyrics.Click += (_, _) => ShowSettingsCategory("Lyrics");
        BtnFilterGlow.Click += (_, _) => ShowSettingsCategory("Glow");
        BtnFilterUpdates.Click += (_, _) => ShowSettingsCategory("Updates");
        BtnFilterShortcuts.Click += (_, _) => ShowSettingsCategory("Shortcuts");
    }

    private void ShowSettingsCategory(string category)
    {
        var accent = (Style)Application.Current.Resources["AccentButtonStyle"];
        var standard = (Style)Application.Current.Resources["DefaultButtonStyle"];

        BtnFilterAllSettings.Style = category == "All" ? accent : standard;
        BtnFilterGeneral.Style = category == "General" ? accent : standard;
        BtnFilterPlayback.Style = category == "Playback" ? accent : standard;
        BtnFilterLyrics.Style = category == "Lyrics" ? accent : standard;
        BtnFilterGlow.Style = category == "Glow" ? accent : standard;
        BtnFilterUpdates.Style = category == "Updates" ? accent : standard;
        BtnFilterShortcuts.Style = category == "Shortcuts" ? accent : standard;

        SectionGeneral.Visibility = (category == "All" || category == "General") ? Visibility.Visible : Visibility.Collapsed;
        SectionPlayback.Visibility = (category == "All" || category == "Playback") ? Visibility.Visible : Visibility.Collapsed;
        SectionLyrics.Visibility = (category == "All" || category == "Lyrics") ? Visibility.Visible : Visibility.Collapsed;
        SectionGlow.Visibility = (category == "All" || category == "Glow") ? Visibility.Visible : Visibility.Collapsed;
        SectionUpdates.Visibility = (category == "All" || category == "Updates") ? Visibility.Visible : Visibility.Collapsed;
        SectionShortcuts.Visibility = (category == "All" || category == "Shortcuts") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        // Navigation
        NavHome.Content = loc["Nav_Dashboard"];
        NavGlow.Content = loc["Nav_Glow"];
        NavLyrics.Content = loc["Nav_Lyrics"];
        TxtSettingsHeader.Text = loc["Settings_Title"];

        // Settings Category Filters
        BtnFilterAllSettings.Content = "All";
        BtnFilterGeneral.Content = loc["Tab_General"];
        BtnFilterPlayback.Content = loc["Tab_Playback"];
        BtnFilterLyrics.Content = loc["Tab_Lyrics"];
        BtnFilterGlow.Content = loc["Tab_Glow"];
        BtnFilterUpdates.Content = loc["Tab_Updates"];
        BtnFilterShortcuts.Content = loc["Tab_Shortcuts"];

        // General
        TxtSectionGeneral.Text = loc["Tab_General"];
        TxtAppTheme.Text = loc["General_AppTheme"];
        TxtAppThemeDesc.Text = loc["General_AppTheme_Desc"];
        TxtLanguageLabel.Text = loc["General_Language"];
        TxtLanguageDesc.Text = loc["General_Language_Desc"];
        TxtStartupLabel.Text = loc["General_Startup"];
        TxtStartupDesc.Text = loc["General_Startup_Desc"];
        TxtSystemTrayLabel.Text = loc["General_SystemTray"];
        TxtSystemTrayDesc.Text = loc["General_SystemTray_Desc"];

        // Playback
        TxtSectionPlayback.Text = loc["Playback_Title"];
        TxtPlaybackActiveLabel.Text = loc["Playback_ActiveSession"];
        TxtPlaybackSystemLabel.Text = loc["Playback_SystemEndpoint"];
        TxtSettingsMediaStatus.Text = loc["Playback_Transport"];

        // Lyrics
        TxtSectionLyrics.Text = loc["Lyrics_Title"];
        TxtSyncOffsetLabel.Text = loc["Lyrics_SyncOffset"];
        TxtSyncOffsetDesc.Text = loc["Lyrics_SyncOffset_Desc"];
        TxtLyricsAutoCenterNotice.Text = loc["Lyrics_AutoCenter_Notice"];

        // Glow
        TxtSectionGlow.Text = loc["Glow_Title"];
        TxtAmbientGlowLabel.Text = loc["Glow_AmbientGlow"];
        TxtAmbientGlowDesc.Text = loc["Glow_AmbientGlow_Desc"];
        TxtGlowIntensityLabel.Text = loc["Glow_Intensity"];
        TxtArtworkSyncLabel.Text = loc["Glow_ArtworkSync"];
        TxtArtworkSyncDesc.Text = loc["Glow_ArtworkSync_Desc"];
        TxtShutdownDelayLabel.Text = loc["Glow_ShutdownDelay"];
        TxtShutdownDelayDesc.Text = loc["Glow_ShutdownDelay_Desc"];

        // Updates
        TxtSectionUpdates.Text = loc["Updates_Title"];
        TxtAutoUpdatesLabel.Text = loc["Updates_AutoChecks"];
        TxtAutoUpdatesDesc.Text = loc["Updates_AutoChecks_Desc"];
        TxtUpdateFrequencyLabel.Text = loc["Updates_Frequency"];
        BtnCheckUpdates.Content = loc["Updates_CheckNow"];
        BtnDownloadAndInstallUpdate.Content = loc["Updates_DownloadAndInstall"];

        // Shortcuts
        TxtSectionShortcuts.Text = loc["Shortcuts_Title"];
        TxtShortcutsDesc.Text = loc["Shortcuts_Desc"];
        TxtShortcutGlowLabel.Text = loc["Shortcuts_ToggleGlow"];
        TxtShortcutLyricsLabel.Text = loc["Shortcuts_ToggleLyrics"];
        BtnClearHotkeys.Content = loc["Shortcuts_Clear"];
        BtnSaveHotkeys.Content = loc["Shortcuts_Apply"];

        // Diagnostics
        BtnResetSettings.Content = loc["Diag_ResetSettings"];
        BtnOpenLogs.Content = loc["Diag_OpenLogs"];
    }

    private async Task SetGlowStateAsync(bool active)
    {
        if (_isUpdatingToggles) return;
        _isUpdatingToggles = true;
        try
        {
            SwitchHomeGlow.IsOn = active;
            SwitchGlowMaster.IsOn = active;
            SwitchSettingsGlowEnabled.IsOn = active;

            await _moduleManager.SetModuleActiveAsync("lumn", active);

            if (active)
            {
                TxtGlowStatus.Text = "Running (Active)";
                TxtGlowStatus.Foreground = (Brush)Application.Current.Resources["LyrowSuccessBrush"];
                TxtGlowVisualState.Text = "Active — 4-edge music reactive bloom (60 FPS)";
                _glowOverlayManager?.StartOverlays();

                // Immediately sync current track's artwork palette into Glow
                if (_mediaMonitor != null && !_mediaMonitor.CurrentTrack.IsEmpty && _settingsManager.Current.SyncWithAlbumArt)
                {
                    _ = UpdateTrackArtworkAndPaletteAsync(_mediaMonitor.CurrentTrack);
                }

                ShowToast("Ambient Glow activated — 4-edge ambient lighting active.", InfoBarSeverity.Success);
            }
            else
            {
                TxtGlowStatus.Text = "Stopped";
                TxtGlowStatus.Foreground = (Brush)Application.Current.Resources["LyrowStoppedBrush"];
                TxtGlowVisualState.Text = "Standby — 4-edge ambient lighting ready";
                _glowOverlayManager?.StopOverlays();
                ShowToast("Ambient Glow overlay closed.", InfoBarSeverity.Informational);
            }
        }
        finally
        {
            _isUpdatingToggles = false;
        }
    }

    private async Task SetLyricsStateAsync(bool active)
    {
        if (_isUpdatingToggles) return;
        _isUpdatingToggles = true;
        try
        {
            SwitchHomeLyrics.IsOn = active;
            SwitchLyricsMaster.IsOn = active;

            await _moduleManager.SetModuleActiveAsync("verci", active);

            if (active)
            {
                TxtLyricsStatus.Text = "Running (Active)";
                TxtLyricsStatus.Foreground = (Brush)Application.Current.Resources["LyrowSuccessBrush"];

                if (_lyricsWindow == null && _verciModule != null)
                {
                    _lyricsWindow = new LyricsWindow(_verciModule, _settingsManager, _artworkBitmapManager, _mediaMonitor, _volumeService, _toastManager);
                    _lyricsWindow.Closed += (_, _) =>
                    {
                        _lyricsWindow = null;
                        _ = SetLyricsStateAsync(false);
                    };
                }
                _lyricsWindow?.Activate();
                ShowToast("Synchronized Lyrics window opened.", InfoBarSeverity.Success);
            }
            else
            {
                TxtLyricsStatus.Text = "Stopped";
                TxtLyricsStatus.Foreground = (Brush)Application.Current.Resources["LyrowStoppedBrush"];
                _lyricsWindow?.Shutdown();
                _lyricsWindow = null;
                ShowToast("Lyrics window closed.", InfoBarSeverity.Informational);
            }
        }
        finally
        {
            _isUpdatingToggles = false;
        }
    }

    private void HookEvents()
    {
        if (_mediaMonitor != null)
        {
            _mediaMonitor.TrackChanged += OnHardwareMediaTrackChanged;
            _mediaMonitor.TimelineChanged += OnHardwareMediaTimelineChanged;
        }

        if (_eventBus != null)
        {
            _subscriptions.Add(_eventBus.Subscribe<LyricsLoadedEvent>(OnLyricsLoaded));
            _subscriptions.Add(_eventBus.Subscribe<LyricsFetchFailedEvent>(OnLyricsFetchFailed));
            _subscriptions.Add(_eventBus.Subscribe<ActiveLyricLineChangedEvent>(OnActiveLyricLineChanged));
        }
    }

    private void OnHardwareMediaTrackChanged(object? sender, MediaTrackInfo track)
    {
        _ = HandleTrackChangedUnifiedAsync(track);
    }

    private void OnHardwareMediaTimelineChanged(object? sender, MediaTimelineInfo timeline)
    {
        _verciModule?.UpdateTimelineExternal(timeline);

        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateMediaTransportControlsState(_mediaMonitor?.HasActiveSession ?? false, timeline.Status);
            UpdateDashboardTimelineDisplay(timeline, _mediaMonitor?.CurrentTrack ?? MediaTrackInfo.Empty);
        });
    }

    private async Task HandleTrackChangedUnifiedAsync(MediaTrackInfo track)
    {
        // Re-bind volume service to active media session if player changed
        _volumeService.SetPreferredAppId(_mediaMonitor?.SourceAppId);

        // If VerciModule is not currently Running, still trigger background lyrics lookup & timeline reset
        // so Dashboard lyrics status/current line and future LyricsWindow activation are immediately synced!
        if (_verciModule != null && _verciModule.State != ModuleState.Running)
        {
            if (_mediaMonitor != null)
            {
                _verciModule.UpdateTimelineExternal(_mediaMonitor.CurrentTimeline);
            }
            _ = _verciModule.HandleTrackChangedAsync(track);
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            SyncVolumeUiFromHardware();
            UpdateMediaTransportControlsState(_mediaMonitor?.HasActiveSession ?? !track.IsEmpty, _mediaMonitor?.CurrentTimeline.Status ?? MediaPlaybackStatus.Stopped);

            if (track.IsEmpty)
            {
                HomeTxtTitle.Text = "Waiting for playback...";
                HomeTxtArtist.Text = "Play music in Spotify, Apple Music, or your browser";
                HomeTxtAlbum.Text = string.Empty;
                HomeTxtAlbum.Visibility = Visibility.Collapsed;
                HomeProgressTimeline.Value = 0;
                HomeTxtTimestamp.Text = "00:00 / 00:00";
                HomeArtworkPlaceholder.Visibility = Visibility.Visible;
                HomeTrackArtwork.Source = null;
                TxtLyricsAvailability.Text = "Waiting for active track...";
                TxtHomeCurrentLyricLine.Text = "♪ Play a song to see live synchronized lyrics";
                return;
            }

            HomeTxtTitle.Text = track.Title;
            HomeTxtArtist.Text = track.Artist;
            if (!string.IsNullOrWhiteSpace(track.Album))
            {
                HomeTxtAlbum.Text = track.Album;
                HomeTxtAlbum.Visibility = Visibility.Visible;
            }
            else
            {
                HomeTxtAlbum.Text = string.Empty;
                HomeTxtAlbum.Visibility = Visibility.Collapsed;
            }

            TxtLyricsAvailability.Text = "Loading synchronized lyrics...";
            TxtHomeCurrentLyricLine.Text = $"♪ {track.Title} — {track.Artist}";
        });

        if (!track.IsEmpty)
        {
            await UpdateTrackArtworkAndPaletteAsync(track);
        }
    }

    private async Task UpdateTrackArtworkAndPaletteAsync(MediaTrackInfo track)
    {
        if (_artworkBitmapManager == null) return;

        string newKey = ArtworkService.CreateCacheKey(track.Artist, track.Title);
        if (!string.IsNullOrEmpty(_lastTrackCacheKey) && !string.Equals(_lastTrackCacheKey, newKey, StringComparison.OrdinalIgnoreCase))
        {
            _artworkBitmapManager.Invalidate(track.Artist, track.Title);
        }
        _lastTrackCacheKey = newKey;

        // Decode BitmapImage on UI thread
        DispatcherQueue.TryEnqueue(async () =>
        {
            var bitmap = await _artworkBitmapManager.GetArtworkBitmapAsync(track);
            if (bitmap != null)
            {
                HomeTrackArtwork.Source = bitmap;
                HomeArtworkPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                HomeTrackArtwork.Source = null;
                HomeArtworkPlaceholder.Visibility = Visibility.Visible;
            }
        });

        // Extract vibrant 4-edge Glow palette on background thread & trigger smooth crossfade
        if (_settingsManager.Current.SyncWithAlbumArt)
        {
            var palette = await _artworkBitmapManager.ExtractArtworkPaletteAsync(track);
            _glowOverlayManager?.SetCustomPalette(palette);
            DispatcherQueue.TryEnqueue(() => UpdatePaletteSwatches(palette));
        }
    }

    private void UpdatePaletteSwatches(GlowPalette palette)
    {
        SwatchTop.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, palette.TopColor.R, palette.TopColor.G, palette.TopColor.B));
        SwatchBottom.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, palette.BottomColor.R, palette.BottomColor.G, palette.BottomColor.B));
        SwatchLeft.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, palette.LeftColor.R, palette.LeftColor.G, palette.LeftColor.B));
        SwatchRight.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, palette.RightColor.R, palette.RightColor.G, palette.RightColor.B));
    }

    private void OnDashboardTimerTick(object? sender, object e)
    {
        bool hasSession = _mediaMonitor?.HasActiveSession ?? false;
        var status = _mediaMonitor?.CurrentTimeline.Status ?? MediaPlaybackStatus.Stopped;
        _glowOverlayManager?.UpdatePlaybackState(status, hasSession);

        if (_mediaMonitor != null && !_mediaMonitor.CurrentTrack.IsEmpty)
        {
            var timeline = _mediaMonitor.CurrentTimeline;
            UpdateDashboardTimelineDisplay(timeline, _mediaMonitor.CurrentTrack);

            if (_verciModule != null)
            {
                var pos = timeline.GetInterpolatedPosition();
                _verciModule.Synchronizer.EvaluatePosition(pos);
            }
        }

        if (_glowOverlayManager != null && _glowOverlayManager.IsRunning)
        {
            var livePalette = _glowOverlayManager.CurrentPalette;
            UpdatePaletteSwatches(livePalette);
        }
    }

    private void UpdateDashboardTimelineDisplay(MediaTimelineInfo timeline, MediaTrackInfo track)
    {
        var pos = timeline.GetInterpolatedPosition();
        var dur = timeline.Duration > TimeSpan.Zero ? timeline.Duration : track.Duration;

        HomeTxtTimestamp.Text = $"{pos:mm\\:ss} / {dur:mm\\:ss}";
        if (dur.TotalSeconds > 0)
        {
            HomeProgressTimeline.Value = Math.Clamp((pos.TotalSeconds / dur.TotalSeconds) * 100.0, 0, 100);
        }
        else
        {
            HomeProgressTimeline.Value = 0;
        }
    }

    private void OnLyricsLoaded(LyricsLoadedEvent e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            int count = e.Lyrics.Lines.Count;
            if (count > 0)
            {
                TxtLyricsAvailability.Text = $"Synchronized ({count} timed lines • {e.Source})";
                int idx = _verciModule?.ActiveLineIndex ?? -1;
                if (idx >= 0 && idx < count)
                {
                    TxtHomeCurrentLyricLine.Text = e.Lyrics.Lines[idx].Text;
                }
                else
                {
                    TxtHomeCurrentLyricLine.Text = e.Lyrics.Lines[0].Text;
                }
            }
            else if (!string.IsNullOrWhiteSpace(e.Lyrics.PlainLyrics))
            {
                TxtLyricsAvailability.Text = $"Plain lyrics available ({e.Source})";
                TxtHomeCurrentLyricLine.Text = "Plain text lyrics loaded";
            }
        });
    }

    private void OnLyricsFetchFailed(LyricsFetchFailedEvent e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            TxtLyricsAvailability.Text = "No synchronized lyrics found for this track";
            TxtHomeCurrentLyricLine.Text = "♪ Instrumental or unsynchronized track";
        });
    }

    private void OnActiveLyricLineChanged(ActiveLyricLineChangedEvent e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Line != null && !string.IsNullOrWhiteSpace(e.Line.Text))
            {
                TxtHomeCurrentLyricLine.Text = e.Line.Text;
            }
        });
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ShowPage(PageSettings);
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            string tag = item.Tag?.ToString() ?? "Home";
            switch (tag)
            {
                case "Home":
                    ShowPage(PageHome);
                    break;
                case "Glow":
                    ShowPage(PageGlow);
                    break;
                case "Lyrics":
                    ShowPage(PageLyrics);
                    break;
                default:
                    ShowPage(PageHome);
                    break;
            }
        }
    }

    private void ShowPage(UIElement activePage)
    {
        if (PageHome == null || PageGlow == null || PageLyrics == null || PageSettings == null) return;
        PageHome.Visibility = (activePage == PageHome) ? Visibility.Visible : Visibility.Collapsed;
        PageGlow.Visibility = (activePage == PageGlow) ? Visibility.Visible : Visibility.Collapsed;
        PageLyrics.Visibility = (activePage == PageLyrics) ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = (activePage == PageSettings) ? Visibility.Visible : Visibility.Collapsed;

        MainScrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void ComboTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSettingsUi || _settingsManager == null || ComboTheme == null) return;
        if (ComboTheme.SelectedItem is ComboBoxItem item)
        {
            string tag = item.Tag?.ToString() ?? "SystemDefault";
            var mode = tag switch
            {
                "Dark" => ThemeMode.Dark,
                "Light" => ThemeMode.Light,
                _ => ThemeMode.SystemDefault
            };

            _settingsManager.Current.Theme = mode;
            ApplyTheme(mode);
        }
    }

    public void ApplyTheme(ThemeMode mode)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = mode switch
            {
                ThemeMode.Dark => ElementTheme.Dark,
                ThemeMode.Light => ElementTheme.Light,
                _ => ElementTheme.Default
            };
        }
    }

    public void ShowToast(string message, InfoBarSeverity severity = InfoBarSeverity.Informational, string? title = null, string? actionLabel = null, Action? onAction = null)
    {
        _toastManager?.ShowToast(message, title, severity, actionLabel, onAction);
    }

    public void ShowAndActivate()
    {
        Activate();
    }

    public void ToggleGlowFromExternal()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await SetGlowStateAsync(!SwitchHomeGlow.IsOn);
        });
    }

    public void ToggleLyricsFromExternal()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await SetLyricsStateAsync(!SwitchHomeLyrics.IsOn);
        });
    }

    public void RequestExit()
    {
        _isExplicitExit = true;
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        _dashboardTimer.Stop();

        if (_mediaMonitor != null)
        {
            _mediaMonitor.TrackChanged -= OnHardwareMediaTrackChanged;
            _mediaMonitor.TimelineChanged -= OnHardwareMediaTimelineChanged;
        }

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();

        _lyricsWindow?.Shutdown();
        _lyricsWindow = null;

        _glowOverlayManager?.Dispose();

        App.ShutdownApp();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

/// <summary>
/// Display view model for FallbackPalettePreset with colored circle brushes for ComboBox preview.
/// </summary>
public sealed class PalettePresetViewModel
{
    public FallbackPalettePreset Preset { get; }
    public string Name => Preset.Name;
    public Brush TopBrush { get; }
    public Brush RightBrush { get; }
    public Brush BottomBrush { get; }
    public Brush LeftBrush { get; }

    public PalettePresetViewModel(FallbackPalettePreset preset)
    {
        Preset = preset;
        TopBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, preset.Palette.TopColor.R, preset.Palette.TopColor.G, preset.Palette.TopColor.B));
        RightBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, preset.Palette.RightColor.R, preset.Palette.RightColor.G, preset.Palette.RightColor.B));
        BottomBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, preset.Palette.BottomColor.R, preset.Palette.BottomColor.G, preset.Palette.BottomColor.B));
        LeftBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, preset.Palette.LeftColor.R, preset.Palette.LeftColor.G, preset.Palette.LeftColor.B));
    }

    public override string ToString() => Name;
}
