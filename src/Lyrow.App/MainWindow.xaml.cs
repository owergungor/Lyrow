using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Lyrow.App.Platform;
using Lyrow.Audio.Control;
using Lyrow.Core.Contracts;
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
    private LyricsWindow? _lyricsWindow;
    private string _lastTrackCacheKey = string.Empty;
    private string? _latestUpdateReleaseUrl;
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

        // 2. Non-intrusive background update check if AutoUpdateEnabled is ON
        if (_settingsManager.Current.AutoUpdateEnabled)
        {
            _ = CheckForUpdatesInternalAsync(userInitiated: false);
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
            ComboPalettePreset.SelectedIndex = Math.Clamp(settings.GlowPresetIndex, 0, 3);

            // Lyrics controls
            SliderLyricsOffset.Value = settings.Verci.SyncOffsetMs;
            SliderSettingsLyricsOffset.Value = settings.Verci.SyncOffsetMs;
            TxtPageLyricsOffset.Text = $"{settings.Verci.SyncOffsetMs:+0;-0;0} ms";
            TxtSettingsLyricsOffset.Text = $"{settings.Verci.SyncOffsetMs:+0;-0;0} ms";

            SwitchLyricsAutoScroll.IsOn = settings.Verci.AutoScroll;
            SwitchSettingsLyricsAutoScroll.IsOn = settings.Verci.AutoScroll;
            ComboFontScale.SelectedIndex = Math.Clamp(settings.LyricsFontScaleIndex, 0, 2);

            // General & Updates
            SwitchStartWithWindows.IsOn = settings.StartWithWindows;
            SwitchSystemTray.IsOn = settings.SystemTrayEnabled;
            SwitchAutoUpdate.IsOn = settings.AutoUpdateEnabled;
            TxtCurrentVersion.Text = $"Current Version: v{_updateService.CurrentVersion}";

            TxtHotkeyGlow.Text = settings.Hotkeys.ToggleLumn;
            TxtHotkeyLyrics.Text = settings.Hotkeys.ToggleVerci;

            ComboTheme.SelectedIndex = settings.Theme switch
            {
                ThemeMode.Dark => 1,
                ThemeMode.Light => 2,
                _ => 0
            };

            // Initialize Volume UI from real Windows Audio Service (0% - 100%)
            SyncVolumeUiFromHardware();
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
            int currentVol = _volumeService.GetVolumePercent();
            bool muted = _volumeService.IsMuted;

            SliderVolume.Value = currentVol;
            SliderSettingsVolume.Value = currentVol;

            string formatted = $"{currentVol}%";
            TxtVolumePercent.Text = muted ? $"Muted ({formatted})" : formatted;
            TxtSettingsVolumePercent.Text = muted ? $"Muted ({formatted})" : formatted;
            TxtVolumeTarget.Text = _volumeService.ActiveTargetName;
            IconVolumeMute.Glyph = (muted || currentVol == 0) ? "\uE74F" : "\uE767";
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

        // Real Windows Volume Sliders (0% - 100% linear percentage)
        SliderVolume.ValueChanged += (_, e) => OnVolumeSliderChanged(e.NewValue);
        SliderSettingsVolume.ValueChanged += (_, e) => OnVolumeSliderChanged(e.NewValue);

        BtnMuteVolume.Click += (_, _) =>
        {
            _volumeService.ToggleMute();
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
            int idx = Math.Clamp(ComboPalettePreset.SelectedIndex, 0, 3);
            _settingsManager.Current.GlowPresetIndex = idx;
            if (!_settingsManager.Current.SyncWithAlbumArt || (_mediaMonitor?.CurrentTrack.IsEmpty ?? true))
            {
                ApplyFallbackPalettePreset(idx);
            }
        };

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

        SwitchLyricsAutoScroll.Toggled += (_, _) => OnLyricsAutoScrollToggled(SwitchLyricsAutoScroll.IsOn);
        SwitchSettingsLyricsAutoScroll.Toggled += (_, _) => OnLyricsAutoScrollToggled(SwitchSettingsLyricsAutoScroll.IsOn);

        ComboFontScale.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingSettingsUi || ComboFontScale == null) return;
            _settingsManager.Current.LyricsFontScaleIndex = Math.Clamp(ComboFontScale.SelectedIndex, 0, 2);
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

    private void OnVolumeSliderChanged(double newValue)
    {
        if (_isUpdatingVolumeUi || _isUpdatingSettingsUi) return;
        _isUpdatingVolumeUi = true;
        try
        {
            int percent = Math.Clamp((int)Math.Round(newValue), 0, 100);
            _volumeService.SetVolumePercent(percent);

            SliderVolume.Value = percent;
            SliderSettingsVolume.Value = percent;

            string formatted = $"{percent}%";
            TxtVolumePercent.Text = formatted;
            TxtSettingsVolumePercent.Text = formatted;
            TxtVolumeTarget.Text = _volumeService.ActiveTargetName;
            IconVolumeMute.Glyph = percent == 0 ? "\uE74F" : "\uE767";
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
                ApplyFallbackPalettePreset(_settingsManager.Current.GlowPresetIndex);
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

    private void ApplyFallbackPalettePreset(int presetIndex)
    {
        var palette = presetIndex switch
        {
            1 => GlowPalette.Aurora,
            2 => GlowPalette.NeonSunset,
            3 => GlowPalette.Cyberpunk,
            _ => GlowPalette.Default
        };
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

    private void OnLyricsAutoScrollToggled(bool enabled)
    {
        if (_isUpdatingSettingsUi) return;
        _isUpdatingSettingsUi = true;
        try
        {
            SwitchLyricsAutoScroll.IsOn = enabled;
            SwitchSettingsLyricsAutoScroll.IsOn = enabled;
            _settingsManager.Current.Verci.AutoScroll = enabled;
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
                    BtnOpenUpdateRelease.Visibility = Visibility.Visible;
                    TxtUpdateStatusSummary.Text = $"New release available: v{result.LatestVersion} ({result.ReleaseTitle})";
                    UpdateStatusInfoBar.Severity = InfoBarSeverity.Success;
                    UpdateStatusInfoBar.Title = $"Update Available — v{result.LatestVersion}";
                    UpdateStatusInfoBar.Message = $"{result.ReleaseTitle} is available on GitHub Releases. Click 'View Release' when you are ready to update.";
                    UpdateStatusInfoBar.IsOpen = true;
                    ShowToast($"Update available: Lyrow v{result.LatestVersion}", InfoBarSeverity.Informational);
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
                        ShowToast($"Lyrow v{_updateService.CurrentVersion} is up to date.", InfoBarSeverity.Success);
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
        });
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
                    _lyricsWindow = new LyricsWindow(_verciModule, _settingsManager, _artworkBitmapManager, _mediaMonitor);
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

    public void ShowToast(string message, InfoBarSeverity severity)
    {
        TopToastBar.Message = message;
        TopToastBar.Severity = severity;
        TopToastBar.IsOpen = true;
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
