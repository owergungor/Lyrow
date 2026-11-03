using Lyrow.Core.Lyrics;
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
    private readonly LyricsModule? _lyricsModule;

    private readonly List<IDisposable> _subscriptions = new();
    private readonly DispatcherTimer _dashboardTimer;
    private ToastNotificationManager? _toastManager;
    private LyricsWindow? _lyricsWindow;
    private string _lastTrackCacheKey = string.Empty;
    private int _lastArtworkSignature = 0;
    private long _mainTrackSequence = 0;
    private string? _latestUpdateReleaseUrl;
    private string? _latestInstallerDownloadUrl;
    private bool _isExplicitExit = false;
    private bool _isUpdatingToggles = false;
    private bool _isUpdatingVolumeUi = false;
    private bool _isUpdatingSettingsUi = false;
    private bool _isDraggingHomeProgress = false;
    private string _currentActivePage = "Home";
    private bool _isCompact = false;
    private int _currentWizardStep = 0;

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
        _lyricsModule = App.Services?.GetService<LyricsModule>();

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
        InitializeSetupWizard();

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
            bool isSmokeTest = App.IsSmokeTestMode
                || Environment.GetCommandLineArgs().Any(a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase))
                || Environment.GetEnvironmentVariable("LYROW_SMOKE_TEST") == "1";
            LocalDiagnosticLogger.LogInfo($"AppWindow.Closing triggered! isSmokeTest={isSmokeTest}, MinimizeToTray={_settingsManager.Current.MinimizeToTray}, isExplicitExit={_isExplicitExit}");
            if (_settingsManager.Current.MinimizeToTray && !_isExplicitExit && !isSmokeTest)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
        };

        try
        {
            var mainHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _subclassProc = MainWindowSubclass;
            SetWindowSubclass(mainHwnd, _subclassProc, UIntPtr.Zero, UIntPtr.Zero);
        }
        catch { }

        WireHomeCardsNavigation();
        WireHomeProgressScrubber();
        RootShellGrid.SizeChanged += OnRootShellSizeChanged;

        _trayIconManager?.Initialize(
            onShowMainWindow: () => DispatcherQueue.TryEnqueue(() => { AppWindow.Show(); NavigateTo("Home"); }),
            onToggleGlow: () => ToggleGlowFromExternal(),
            onToggleLyrics: () => ToggleLyricsFromExternal(),
            onToggleClickThrough: () => { },
            onOpenSettings: () => DispatcherQueue.TryEnqueue(() => { AppWindow.Show(); NavigateTo("Settings"); }),
            onExit: () => DispatcherQueue.TryEnqueue(RequestExit));

        _trayIconManager?.AttachExtendedHandlers(
            onPlayPause: () => DispatcherQueue.TryEnqueue(async () => await ExecutePlayPauseCommandAsync()),
            onPrev: () => DispatcherQueue.TryEnqueue(async () => await ExecutePrevTrackCommandAsync()),
            onNext: () => DispatcherQueue.TryEnqueue(async () => await ExecuteNextTrackCommandAsync()),
            onToggleMute: () => DispatcherQueue.TryEnqueue(() => { _volumeService.TogglePlaybackMute(); SyncVolumeUiFromHardware(); }),
            onOpenPlayback: () => DispatcherQueue.TryEnqueue(() => { AppWindow.Show(); NavigateTo("Playback"); }),
            onOpenGlow: () => DispatcherQueue.TryEnqueue(() => { AppWindow.Show(); NavigateTo("Glow"); }),
            onCheckUpdates: () => DispatcherQueue.TryEnqueue(async () => { AppWindow.Show(); NavigateTo("Settings"); ShowSettingsCategory("Updates"); await CheckForUpdatesInternalAsync(userInitiated: true); }),
            onToggleLyricsFullscreen: () => DispatcherQueue.TryEnqueue(async () => { await SetLyricsStateAsync(true); }));

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

        bool isCtrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        if (isCtrl)
        {
            switch (e.Key)
            {
                case VirtualKey.Number1:
                    NavView.SelectedItem = NavHome;
                    ShowPage(PageHome);
                    e.Handled = true;
                    return;
                case VirtualKey.Number2:
                    if (NavPlayback != null) NavView.SelectedItem = NavPlayback;
                    if (PagePlayback != null) ShowPage(PagePlayback);
                    e.Handled = true;
                    return;
                case VirtualKey.Number3:
                    NavView.SelectedItem = NavGlow;
                    ShowPage(PageGlow);
                    e.Handled = true;
                    return;
                case VirtualKey.Number4:
                    NavView.SelectedItem = NavLyrics;
                    ShowPage(PageLyrics);
                    e.Handled = true;
                    return;
                case VirtualKey.Number5:
                    NavView.SelectedItem = NavView.SettingsItem;
                    ShowPage(PageSettings);
                    e.Handled = true;
                    return;
            }
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

            case VirtualKey.Left:
                if (_mediaMonitor != null)
                {
                    if (!_mediaMonitor.CanSeek)
                    {
                        ShowToast(LocalizationService.Instance["Lyrics_SeekFailed"], InfoBarSeverity.Informational);
                    }
                    else
                    {
                        var cur = _mediaMonitor.CurrentTimeline.Position;
                        int step = _settingsManager?.Current.SeekStepSeconds ?? 5;
                        var target = cur - TimeSpan.FromSeconds(step);
                        _ = _mediaMonitor.TrySeekAsync(target < TimeSpan.Zero ? TimeSpan.Zero : target);
                    }
                }
                e.Handled = true;
                break;

            case VirtualKey.Right:
                if (_mediaMonitor != null)
                {
                    if (!_mediaMonitor.CanSeek)
                    {
                        ShowToast(LocalizationService.Instance["Lyrics_SeekFailed"], InfoBarSeverity.Informational);
                    }
                    else
                    {
                        var cur = _mediaMonitor.CurrentTimeline.Position;
                        int step = _settingsManager?.Current.SeekStepSeconds ?? 5;
                        var target = cur + TimeSpan.FromSeconds(step);
                        _ = _mediaMonitor.TrySeekAsync(target);
                    }
                }
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

    private readonly System.Threading.Lock _saveLock = new();
    private CancellationTokenSource? _saveDebounceCts;

    public void SaveCurrentSettingsDebounced(int delayMs = 300)
    {
        lock (_saveLock)
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts?.Dispose();
            _saveDebounceCts = new CancellationTokenSource();
            var token = _saveDebounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, token);
                    if (!token.IsCancellationRequested && _settingsManager != null)
                    {
                        await _settingsManager.SaveAsync(_settingsManager.Current, CancellationToken.None);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LocalDiagnosticLogger.LogWarning($"Failed to persist settings: {ex.Message}");
                }
            }, token);
        }
    }

    private static bool IsAnimationEnabled()
    {
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            return uiSettings.AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private async Task HandleAutoPauseOnMuteAsync()
    {
        if (_settingsManager?.Current.AutoPauseOnMute == true && (_volumeService.IsPlaybackMuted || _volumeService.IsSystemMuted))
        {
            if (_mediaMonitor != null && _mediaMonitor.HasActiveSession && _mediaMonitor.CurrentTimeline.Status == MediaPlaybackStatus.Playing)
            {
                await _mediaMonitor.TryPauseAsync();
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
            TxtGlowStatus.Text = "Stopped";
            TxtGlowStatus.Foreground = (Brush)Application.Current.Resources["LyrowStoppedBrush"];
            TxtGlowVisualState.Text = "Standby — 4-edge ambient lighting ready";

            SwitchHomeLyrics.IsOn = false;
            SwitchLyricsMaster.IsOn = false;
            TxtLyricsStatus.Text = "Stopped";
            TxtLyricsStatus.Foreground = (Brush)Application.Current.Resources["LyrowStoppedBrush"];

            // Glow controls
            SliderGlowIntensity.Value = settings.GlowIntensity;
            TxtGlowIntensityVal.Text = $"{settings.GlowIntensity}%";

            SliderGlowThickness.Value = settings.GlowThickness;
            TxtGlowThicknessVal.Text = $"{settings.GlowThickness} px";

            int sensPct = (int)Math.Round(settings.Glow.Sensitivity * 100.0f);
            SliderGlowSensitivity.Value = Math.Clamp(sensPct, 40, 250);
            TxtGlowSensitivityVal.Text = $"{Math.Clamp(sensPct, 40, 250)}%";

            SwitchAlbumArtSync.IsOn = settings.SyncWithAlbumArt;
            ChkMultiMonitor.IsChecked = settings.MultiMonitorGlow;

            // Rich Fallback Palette Presets (6 presets with 4-dot colored circle items)
            PopulatePalettePresetCombo(ComboPalettePreset, settings.FallbackPalettePresetIndex);

            // Shutdown Delay ComboBox
            SelectShutdownDelayCombo(ComboGlowShutdownDelay, settings.GlowShutdownDelaySeconds);

            // Lyrics controls
            SliderLyricsOffset.Value = settings.Lyrics.SyncOffsetMs;
            TxtPageLyricsOffset.Text = $"{settings.Lyrics.SyncOffsetMs:+0;-0;0} ms";

            // Lyrics Font Sliders
            SliderFocusedFontSize.Value = settings.FocusedLyricFontSize;
            TxtFocusedFontSize.Text = $"{settings.FocusedLyricFontSize:0} px";

            SliderUnfocusedFontSize.Value = settings.UnfocusedLyricFontSize;
            TxtUnfocusedFontSize.Text = $"{settings.UnfocusedLyricFontSize:0} px";

            // General & Updates
            SwitchStartWithWindows.IsOn = settings.StartWithWindows;
            SwitchSystemTray.IsOn = settings.SystemTrayEnabled;
            if (SwitchEnableAnimations != null) SwitchEnableAnimations.IsOn = settings.EnableAnimations;
            if (SwitchNotifications != null) SwitchNotifications.IsOn = settings.NotificationsEnabled;
            if (SwitchNotifyOnTrackChange != null) SwitchNotifyOnTrackChange.IsOn = settings.NotifyOnTrackChange;
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

            TxtHotkeyGlow.Text = settings.Hotkeys.ToggleGlow;
            TxtHotkeyLyrics.Text = settings.Hotkeys.ToggleLyrics;

            ComboTheme.SelectedIndex = settings.Theme switch
            {
                ThemeMode.Dark => 1,
                ThemeMode.Light => 2,
                _ => 0
            };

            // Categorized Settings Initial State
            if (SliderMasterOpacity != null)
            {
                SliderMasterOpacity.Value = Math.Clamp(settings.MasterOpacity * 100.0, 50.0, 100.0);
                if (TxtMasterOpacityPercent != null) TxtMasterOpacityPercent.Text = $"{(int)SliderMasterOpacity.Value}%";
                if (RootShellGrid != null) RootShellGrid.Opacity = Math.Clamp(settings.MasterOpacity, 0.50, 1.0);
            }
            if (BorderHomeTrackArtwork != null)
            {
                BorderHomeTrackArtwork.CornerRadius = new CornerRadius(settings.ArtworkCornerRadius);
            }
            if (HomeAmbientBackgroundLayer != null)
            {
                HomeAmbientBackgroundLayer.Visibility = settings.VisualIntegrationEnabled ? Visibility.Visible : Visibility.Collapsed;
                double baseOp = settings.Theme == ThemeMode.Light ? 0.12 : 0.55;
                HomeAmbientBackgroundLayer.Opacity = Math.Clamp(baseOp * settings.ArtworkBackgroundSaturation, 0.05, 0.95);
            }
            if (ComboSeekStep != null)
            {
                ComboSeekStep.SelectedIndex = settings.SeekStepSeconds switch { 10 => 1, 15 => 2, _ => 0 };
            }
            if (SwitchAutoPauseOnMute != null) SwitchAutoPauseOnMute.IsOn = settings.AutoPauseOnMute;
            if (SliderSettingsFocusedFontSize != null)
            {
                SliderSettingsFocusedFontSize.Value = settings.FocusedLyricFontSize;
                if (TxtSettingsFocusedFontSize != null) TxtSettingsFocusedFontSize.Text = $"{settings.FocusedLyricFontSize:0} px";
            }
            if (TxtLyricsPreviewFocused != null) TxtLyricsPreviewFocused.FontSize = settings.FocusedLyricFontSize;
            if (SliderSettingsUnfocusedFontSize != null)
            {
                SliderSettingsUnfocusedFontSize.Value = settings.UnfocusedLyricFontSize;
                if (TxtSettingsUnfocusedFontSize != null) TxtSettingsUnfocusedFontSize.Text = $"{settings.UnfocusedLyricFontSize:0} px";
            }
            if (TxtLyricsPreviewPrev != null) TxtLyricsPreviewPrev.FontSize = settings.UnfocusedLyricFontSize;
            if (TxtLyricsPreviewNext != null) TxtLyricsPreviewNext.FontSize = settings.UnfocusedLyricFontSize;
            if (SliderSettingsLyricsLineSpacing != null)
            {
                SliderSettingsLyricsLineSpacing.Value = settings.LyricsLineSpacing;
                if (TxtSettingsLyricsLineSpacing != null) TxtSettingsLyricsLineSpacing.Text = $"{settings.LyricsLineSpacing} px";
            }
            if (ComboSettingsLyricsAlignment != null)
            {
                ComboSettingsLyricsAlignment.SelectedIndex = string.Equals(settings.LyricsAlignment, "Center", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
            if (SliderSettingsLyricsBackdropOpacity != null)
            {
                SliderSettingsLyricsBackdropOpacity.Value = settings.LyricsBackdropOpacity;
                if (TxtSettingsLyricsBackdropOpacity != null) TxtSettingsLyricsBackdropOpacity.Text = $"{settings.LyricsBackdropOpacity}%";
            }
            if (ComboSettingsGlowAcousticProfile != null)
            {
                ComboSettingsGlowAcousticProfile.SelectedIndex = Math.Clamp((int)settings.GlowAcousticProfile, 0, 3);
            }
            UpdateLyricsLivePreview(settings.LyricsLineSpacing, settings.LyricsAlignment);
            if (SliderSettingsLyricsOffset != null)
            {
                SliderSettingsLyricsOffset.Value = settings.Lyrics.SyncOffsetMs;
                if (TxtSettingsLyricsOffset != null) TxtSettingsLyricsOffset.Text = $"{settings.Lyrics.SyncOffsetMs:+0;-0;0} ms";
            }
            if (SliderSettingsGlowThickness != null)
            {
                SliderSettingsGlowThickness.Value = settings.GlowThickness;
                if (TxtSettingsGlowThickness != null) TxtSettingsGlowThickness.Text = $"{settings.GlowThickness} px";
                double pt = Math.Max(2.0, settings.GlowThickness / 4.0);
                if (GlowPreviewTop != null) GlowPreviewTop.Height = pt;
                if (GlowPreviewBottom != null) GlowPreviewBottom.Height = pt;
                if (GlowPreviewLeft != null) GlowPreviewLeft.Width = pt;
                if (GlowPreviewRight != null) GlowPreviewRight.Width = pt;
            }
            if (SliderSettingsGlowIntensity != null)
            {
                SliderSettingsGlowIntensity.Value = settings.GlowIntensity;
                if (TxtSettingsGlowIntensity != null) TxtSettingsGlowIntensity.Text = $"{settings.GlowIntensity}%";
                double pop = Math.Clamp(settings.GlowIntensity / 100.0, 0.1, 1.0);
                if (GlowPreviewTop != null) GlowPreviewTop.Opacity = pop;
                if (GlowPreviewBottom != null) GlowPreviewBottom.Opacity = pop;
                if (GlowPreviewLeft != null) GlowPreviewLeft.Opacity = pop;
                if (GlowPreviewRight != null) GlowPreviewRight.Opacity = pop;
            }
            if (SliderSettingsGlowSensitivity != null)
            {
                SliderSettingsGlowSensitivity.Value = Math.Clamp(settings.Glow.Sensitivity, 0.5f, 5.0f);
                if (TxtSettingsGlowSensitivity != null) TxtSettingsGlowSensitivity.Text = $"{settings.Glow.Sensitivity:0.0}x";
            }
            if (SwitchArtworkDynamicBackground != null) SwitchArtworkDynamicBackground.IsOn = settings.VisualIntegrationEnabled;
            if (SliderArtworkBackgroundSaturation != null)
            {
                SliderArtworkBackgroundSaturation.Value = Math.Clamp(settings.ArtworkBackgroundSaturation * 100.0, 10.0, 150.0);
                if (TxtArtworkSaturationPercent != null) TxtArtworkSaturationPercent.Text = $"{(int)SliderArtworkBackgroundSaturation.Value}%";
            }
            if (SliderArtworkCornerRadius != null)
            {
                SliderArtworkCornerRadius.Value = Math.Clamp(settings.ArtworkCornerRadius, 4.0, 24.0);
                if (TxtArtworkRadiusValue != null) TxtArtworkRadiusValue.Text = $"{(int)SliderArtworkCornerRadius.Value} px";
            }
            if (ComboFftSize != null)
            {
                ComboFftSize.SelectedIndex = settings.Glow.FftSize switch { 1024 => 0, 4096 => 2, _ => 1 };
            }

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
            if (SliderPagePlaybackVolume != null) SliderPagePlaybackVolume.Value = currentPlayback;
            TxtPlaybackVolumePercent.Text = playbackMuted ? $"Muted ({currentPlayback}%)" : $"{currentPlayback}%";
            if (TxtPagePlaybackVolumePercent != null) TxtPagePlaybackVolumePercent.Text = playbackMuted ? $"Muted ({currentPlayback}%)" : $"{currentPlayback}%";
            TxtPlaybackTarget.Text = _volumeService.PlaybackTargetName;
            IconPlaybackMute.Glyph = (playbackMuted || currentPlayback == 0) ? "\uE74F" : "\uE767";

            // 2. System endpoint master volume
            int currentSystem = _volumeService.GetSystemVolumePercent();
            bool systemMuted = _volumeService.IsSystemMuted;
            SliderSystemVolume.Value = currentSystem;
            if (SliderPageSystemVolume != null) SliderPageSystemVolume.Value = currentSystem;
            TxtSystemVolumePercent.Text = systemMuted ? $"Muted ({currentSystem}%)" : $"{currentSystem}%";
            if (TxtPageSystemVolumePercent != null) TxtPageSystemVolumePercent.Text = systemMuted ? $"Muted ({currentSystem}%)" : $"{currentSystem}%";
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
        // Glow Toggles (Home, Glow Page)
        SwitchHomeGlow.Toggled += async (_, _) => await SetGlowStateAsync(SwitchHomeGlow.IsOn);
        SwitchGlowMaster.Toggled += async (_, _) => await SetGlowStateAsync(SwitchGlowMaster.IsOn);

        // Lyrics Toggles (Home, Lyrics Page)
        SwitchHomeLyrics.Toggled += async (_, _) => await SetLyricsStateAsync(SwitchHomeLyrics.IsOn);
        SwitchLyricsMaster.Toggled += async (_, _) => await SetLyricsStateAsync(SwitchLyricsMaster.IsOn);

        // Real Windows Dual Volume Sliders (0% - 100% linear percentage)
        SliderPlaybackVolume.ValueChanged += (_, e) => OnPlaybackVolumeSliderChanged(e.NewValue);
        if (SliderPagePlaybackVolume != null) SliderPagePlaybackVolume.ValueChanged += (_, e) => OnPlaybackVolumeSliderChanged(e.NewValue);

        BtnMutePlayback.Click += (_, _) =>
        {
            _volumeService.TogglePlaybackMute();
            SyncVolumeUiFromHardware();
        };

        SliderSystemVolume.ValueChanged += (_, e) => OnSystemVolumeSliderChanged(e.NewValue);
        if (SliderPageSystemVolume != null) SliderPageSystemVolume.ValueChanged += (_, e) => OnSystemVolumeSliderChanged(e.NewValue);

        BtnMuteSystem.Click += (_, _) =>
        {
            _volumeService.ToggleSystemMute();
            SyncVolumeUiFromHardware();
        };

        // Glow Intensity Sliders
        SliderGlowIntensity.ValueChanged += (_, e) => OnGlowIntensityChanged(e.NewValue);

        SliderGlowThickness.ValueChanged += (_, e) =>
        {
            if (_isUpdatingSettingsUi) return;
            int val = (int)Math.Round(e.NewValue);
            TxtGlowThicknessVal.Text = $"{val} px";
            _settingsManager.Current.GlowThickness = val;
                SaveCurrentSettingsDebounced();
        };

        SliderGlowSensitivity.ValueChanged += (_, e) =>
        {
            if (_isUpdatingSettingsUi) return;
            int pct = (int)Math.Round(e.NewValue);
            TxtGlowSensitivityVal.Text = $"{pct}%";
            float sens = Math.Clamp(pct / 100.0f, 0.0f, 5.0f);
            _settingsManager.Current.Glow.Sensitivity = sens;
                SaveCurrentSettingsDebounced();
            var analyzer = App.Services?.GetService<ISpectrumAnalyzer>();
            if (analyzer != null) analyzer.Sensitivity = sens;
        };

        SwitchAlbumArtSync.Toggled += (_, _) => OnAlbumArtSyncToggled(SwitchAlbumArtSync.IsOn);

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
        SliderUnfocusedFontSize.ValueChanged += (_, e) => OnUnfocusedFontSizeChanged(e.NewValue);

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

        
        if (SwitchEnableAnimations != null)
        {
            SwitchEnableAnimations.Toggled += (_, _) =>
            {
                if (_isUpdatingSettingsUi) return;
                _settingsManager.Current.EnableAnimations = SwitchEnableAnimations.IsOn;
                SaveCurrentSettingsDebounced();
            };
        }

        if (SwitchNotifications != null)
        {
            SwitchNotifications.Toggled += (_, _) =>
            {
                if (_isUpdatingSettingsUi) return;
                _settingsManager.Current.NotificationsEnabled = SwitchNotifications.IsOn;
                SaveCurrentSettingsDebounced();
            };
        }

        if (SwitchNotifyOnTrackChange != null)
        {
            SwitchNotifyOnTrackChange.Toggled += (_, _) =>
            {
                if (_isUpdatingSettingsUi) return;
                _settingsManager.Current.NotifyOnTrackChange = SwitchNotifyOnTrackChange.IsOn;
                SaveCurrentSettingsDebounced();
            };
        }
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
            _settingsManager.Current.Hotkeys.ToggleGlow = TxtHotkeyGlow.Text.Trim();
            _settingsManager.Current.Hotkeys.ToggleLyrics = TxtHotkeyLyrics.Text.Trim();
            await _settingsManager.SaveAsync(_settingsManager.Current);
            _hotkeyManager.RegisterHotkeys(
                _settingsManager.Current.Hotkeys,
                onGlowToggle: ToggleGlowFromExternal,
                onLyricsToggle: ToggleLyricsFromExternal,
                onClickThroughToggle: () => { });
            ShowToast("Shortcuts saved and applied.", InfoBarSeverity.Success);
        };

        BtnClearHotkeys.Click += async (_, _) =>
        {
            TxtHotkeyGlow.Text = string.Empty;
            TxtHotkeyLyrics.Text = string.Empty;
            _settingsManager.Current.Hotkeys.ToggleGlow = string.Empty;
            _settingsManager.Current.Hotkeys.ToggleLyrics = string.Empty;
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

        if (BtnPagePlayPause != null) BtnPagePlayPause.Click += async (_, _) => await ExecutePlayPauseCommandAsync();
        if (BtnPageNext != null) BtnPageNext.Click += async (_, _) => await ExecuteNextTrackCommandAsync();
        if (BtnPagePrev != null) BtnPagePrev.Click += async (_, _) => await ExecutePrevTrackCommandAsync();

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

            string formatted = $"{percent}%";
            TxtPlaybackVolumePercent.Text = formatted;
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

            string formatted = $"{percent}%";
            TxtSystemVolumePercent.Text = formatted;
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
            TxtGlowIntensityVal.Text = $"{val}%";
            _settingsManager.Current.GlowIntensity = val;
                SaveCurrentSettingsDebounced();
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
            if (SliderFocusedFontSize != null) SliderFocusedFontSize.Value = val;
            if (TxtFocusedFontSize != null) TxtFocusedFontSize.Text = $"{val:0} px";
            if (SliderSettingsFocusedFontSize != null) SliderSettingsFocusedFontSize.Value = val;
            if (TxtSettingsFocusedFontSize != null) TxtSettingsFocusedFontSize.Text = $"{val:0} px";
            if (TxtLyricsPreviewFocused != null) TxtLyricsPreviewFocused.FontSize = val;
            _settingsManager.Current.FocusedLyricFontSize = val;

            // Enforce non-conflicting font size hierarchy
            if (val < _settingsManager.Current.UnfocusedLyricFontSize + 2)
            {
                double newUnfocused = Math.Clamp(val - 2, 14.0, 36.0);
                _settingsManager.Current.UnfocusedLyricFontSize = newUnfocused;
                if (SliderUnfocusedFontSize != null) SliderUnfocusedFontSize.Value = newUnfocused;
                if (TxtUnfocusedFontSize != null) TxtUnfocusedFontSize.Text = $"{newUnfocused:0} px";
                if (SliderSettingsUnfocusedFontSize != null) SliderSettingsUnfocusedFontSize.Value = newUnfocused;
                if (TxtSettingsUnfocusedFontSize != null) TxtSettingsUnfocusedFontSize.Text = $"{newUnfocused:0} px";
                if (TxtLyricsPreviewPrev != null) TxtLyricsPreviewPrev.FontSize = newUnfocused;
                if (TxtLyricsPreviewNext != null) TxtLyricsPreviewNext.FontSize = newUnfocused;
            }

            SaveCurrentSettingsDebounced();
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
            if (SliderUnfocusedFontSize != null) SliderUnfocusedFontSize.Value = val;
            if (TxtUnfocusedFontSize != null) TxtUnfocusedFontSize.Text = $"{val:0} px";
            if (SliderSettingsUnfocusedFontSize != null) SliderSettingsUnfocusedFontSize.Value = val;
            if (TxtSettingsUnfocusedFontSize != null) TxtSettingsUnfocusedFontSize.Text = $"{val:0} px";
            if (TxtLyricsPreviewPrev != null) TxtLyricsPreviewPrev.FontSize = val;
            if (TxtLyricsPreviewNext != null) TxtLyricsPreviewNext.FontSize = val;
            _settingsManager.Current.UnfocusedLyricFontSize = val;

            // Enforce non-conflicting font size hierarchy
            if (val > _settingsManager.Current.FocusedLyricFontSize - 2)
            {
                double newFocused = Math.Clamp(val + 2, 20.0, 48.0);
                _settingsManager.Current.FocusedLyricFontSize = newFocused;
                if (SliderFocusedFontSize != null) SliderFocusedFontSize.Value = newFocused;
                if (TxtFocusedFontSize != null) TxtFocusedFontSize.Text = $"{newFocused:0} px";
                if (SliderSettingsFocusedFontSize != null) SliderSettingsFocusedFontSize.Value = newFocused;
                if (TxtSettingsFocusedFontSize != null) TxtSettingsFocusedFontSize.Text = $"{newFocused:0} px";
                if (TxtLyricsPreviewFocused != null) TxtLyricsPreviewFocused.FontSize = newFocused;
            }

            SaveCurrentSettingsDebounced();
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
            if (SliderLyricsOffset != null) SliderLyricsOffset.Value = val;
            if (TxtPageLyricsOffset != null) TxtPageLyricsOffset.Text = $"{val:+0;-0;0} ms";
            if (SliderSettingsLyricsOffset != null) SliderSettingsLyricsOffset.Value = val;
            if (TxtSettingsLyricsOffset != null) TxtSettingsLyricsOffset.Text = $"{val:+0;-0;0} ms";
            _settingsManager.Current.Lyrics.SyncOffsetMs = val;
            _lyricsModule?.SetSyncOffset(val);
            SaveCurrentSettingsDebounced();
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

        if (BtnPagePrev != null) BtnPagePrev.IsEnabled = hasSession;
        if (BtnPagePlayPause != null) BtnPagePlayPause.IsEnabled = hasSession;
        if (BtnPageNext != null) BtnPageNext.IsEnabled = hasSession;

        bool isPlaying = status == MediaPlaybackStatus.Playing;
        IconPlayPause.Glyph = isPlaying ? "\uE769" : "\uE768";
        if (IconPagePlayPause != null) IconPagePlayPause.Glyph = isPlaying ? "\uE769" : "\uE768";

        if (TxtPageMediaStatus != null)
        {
            TxtPageMediaStatus.Text = hasSession
                ? $"Connected ({(isPlaying ? "Playing" : status.ToString())})"
                : "No active media session detected";
        }
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
                    _toastManager?.ShowUpdateAvailableToast(
                        title: LocalizationService.Instance["Updates_Available"],
                        versionMessage: $"Lyrow v{result.LatestVersion} ({result.ReleaseTitle})",
                        actionLabel: LocalizationService.Instance["Updates_DownloadAndInstall"],
                        onStartDownload: async (toastHandle) =>
                        {
                            await StartSelfUpdateDownloadAsync(_latestInstallerDownloadUrl, toastHandle);
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

    private async Task StartSelfUpdateDownloadAsync(string? downloadUrl, ProgressToastHandle? existingHandle = null)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            ShowToast("No direct installer asset available for this release.", InfoBarSeverity.Warning);
            return;
        }

        ProgressToastHandle? toastHandle = existingHandle;

        try
        {
            BtnDownloadAndInstallUpdate.IsEnabled = false;
            ProgressUpdateDownload.Visibility = Visibility.Visible;
            ProgressUpdateDownload.Value = 0;
            TxtUpdateDownloadProgress.Visibility = Visibility.Visible;
            TxtUpdateDownloadProgress.Text = $"{LocalizationService.Instance["Updates_Downloading"]} 0%";

            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Lyrow", "Updates");
            string fileName = $"Lyrow-{GitHubUpdateService.AppVersion}-Setup-x64.exe";
            try
            {
                var uri = new Uri(downloadUrl);
                var seg = System.IO.Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(seg) && seg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = seg;
                }
            }
            catch { }
            string destFile = System.IO.Path.Combine(tempDir, fileName);

            if (toastHandle == null)
            {
                toastHandle = _toastManager?.ShowUpdateDownloadToast(
                    title: LocalizationService.Instance["Updates_Available"],
                    initialMessage: $"{LocalizationService.Instance["Updates_Downloading"]} 0%",
                    onStartDownload: () => { });
            }

            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (p.IsIndeterminate)
                    {
                        ProgressUpdateDownload.IsIndeterminate = true;
                        string progressText = $"{LocalizationService.Instance["Updates_Downloading"]} ({p.MegabytesRead:0.0} MB)";
                        TxtUpdateDownloadProgress.Text = progressText;
                        toastHandle?.ReportProgress(-1.0, p.BytesRead, null, progressText);
                    }
                    else
                    {
                        ProgressUpdateDownload.IsIndeterminate = false;
                        ProgressUpdateDownload.Value = p.Percent;
                        string progressText = $"{LocalizationService.Instance["Updates_Downloading"]} {p.Percent:0}%";
                        TxtUpdateDownloadProgress.Text = progressText;
                        toastHandle?.ReportProgress(p.Percent, p.BytesRead, p.TotalBytes, progressText);
                    }
                });
            });

            await _updateService.DownloadUpdatePackageAsync(downloadUrl, destFile, progress);

            string verifyText = LocalizationService.Instance["Updates_Verifying"];
            TxtUpdateDownloadProgress.Text = verifyText;
            toastHandle?.SetState(UpdateToastState.Verifying, verifyText);
            await Task.Delay(250);

            if (!_updateService.VerifyDownloadedPackage(destFile))
            {
                throw new InvalidOperationException("Downloaded update package failed integrity verification.");
            }

            string installText = LocalizationService.Instance["Updates_Installing"];
            TxtUpdateDownloadProgress.Text = installText;
            toastHandle?.SetState(UpdateToastState.Installing, installText);
            await Task.Delay(150);

            string completedText = LocalizationService.Instance["Updates_Completed"];
            TxtUpdateDownloadProgress.Text = completedText;
            toastHandle?.SetState(UpdateToastState.Completed, completedText);

            // Trigger silent installer execution and graceful restart with single-instance lock release
            bool launched = _updateService.LaunchSilentUpdateAndRestart(
                destFile,
                appExecutablePath: Environment.ProcessPath,
                onBeforeExit: () =>
                {
                    App.ReleaseSingleInstanceLock();
                    App.ShutdownApp();
                });

            if (!launched)
            {
                throw new InvalidOperationException("Could not start update installer process.");
            }
        }
        catch (Exception ex)
        {
            string failText = $"{LocalizationService.Instance["Updates_Failed"]}: {ex.Message}";
            TxtUpdateDownloadProgress.Text = failText;
            toastHandle?.SetState(
                UpdateToastState.DownloadFailed,
                failText,
                retryAction: () =>
                {
                    _ = StartSelfUpdateDownloadAsync(downloadUrl, toastHandle);
                });
        }
        finally
        {
            BtnDownloadAndInstallUpdate.IsEnabled = true;
        }
    }

    private void WireSettingsCategoryFilters()
    {
        BtnFilterAllSettings.Click += (_, _) => ShowSettingsCategory("All");
        if (BtnFilterAppearance != null) BtnFilterAppearance.Click += (_, _) => ShowSettingsCategory("Appearance");
        if (BtnFilterPlayback != null) BtnFilterPlayback.Click += (_, _) => ShowSettingsCategory("Playback");
        if (BtnFilterLyrics != null) BtnFilterLyrics.Click += (_, _) => ShowSettingsCategory("Lyrics");
        if (BtnFilterGlow != null) BtnFilterGlow.Click += (_, _) => ShowSettingsCategory("Glow");
        if (BtnFilterArtwork != null) BtnFilterArtwork.Click += (_, _) => ShowSettingsCategory("Artwork");
        if (BtnFilterAudio != null) BtnFilterAudio.Click += (_, _) => ShowSettingsCategory("Audio");
        if (BtnFilterSystem != null) BtnFilterSystem.Click += (_, _) => ShowSettingsCategory("System");
        if (BtnFilterAbout != null) BtnFilterAbout.Click += (_, _) => ShowSettingsCategory("About");

        // Backwards compatibility aliases
        BtnFilterGeneral.Click += (_, _) => ShowSettingsCategory("Appearance");
        BtnFilterUpdates.Click += (_, _) => ShowSettingsCategory("System");
        BtnFilterShortcuts.Click += (_, _) => ShowSettingsCategory("System");

        WireCategorizedSettingsControls();
    }

    private void WireCategorizedSettingsControls()
    {
        // 1. Appearance / Master Opacity
        if (SliderMasterOpacity != null)
        {
            SliderMasterOpacity.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                int pct = (int)Math.Round(e.NewValue);
                if (TxtMasterOpacityPercent != null) TxtMasterOpacityPercent.Text = $"{pct}%";
                _settingsManager.Current.MasterOpacity = pct / 100.0;
                if (RootShellGrid != null) RootShellGrid.Opacity = Math.Clamp(pct / 100.0, 0.50, 1.0);
                SaveCurrentSettingsDebounced();
            };
        }

        // 2. Playback / Seek step & Auto-pause
        if (ComboSeekStep != null)
        {
            ComboSeekStep.SelectionChanged += (_, _) =>
            {
                if (_isUpdatingSettingsUi || ComboSeekStep.SelectedItem is not ComboBoxItem item) return;
                if (int.TryParse(item.Tag?.ToString(), out int sec))
                {
                    _settingsManager.Current.SeekStepSeconds = sec;
                    SaveCurrentSettingsDebounced();
                }
            };
        }

        if (SwitchAutoPauseOnMute != null)
        {
            SwitchAutoPauseOnMute.Toggled += (_, _) =>
            {
                if (_isUpdatingSettingsUi) return;
                _settingsManager.Current.AutoPauseOnMute = SwitchAutoPauseOnMute.IsOn;
                SaveCurrentSettingsDebounced();
                _ = HandleAutoPauseOnMuteAsync();
            };
        }

        // 3. Lyrics Live Preview & Sliders
        if (SliderSettingsFocusedFontSize != null)
        {
            SliderSettingsFocusedFontSize.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                double val = Math.Clamp(Math.Round(e.NewValue), 20.0, 48.0);
                if (TxtSettingsFocusedFontSize != null) TxtSettingsFocusedFontSize.Text = $"{val:0} px";
                if (TxtLyricsPreviewFocused != null) TxtLyricsPreviewFocused.FontSize = val;
                OnFocusedFontSizeChanged(val);
            };
        }

        if (SliderSettingsUnfocusedFontSize != null)
        {
            SliderSettingsUnfocusedFontSize.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                double val = Math.Clamp(Math.Round(e.NewValue), 14.0, 36.0);
                if (TxtSettingsUnfocusedFontSize != null) TxtSettingsUnfocusedFontSize.Text = $"{val:0} px";
                if (TxtLyricsPreviewPrev != null) TxtLyricsPreviewPrev.FontSize = val;
                if (TxtLyricsPreviewNext != null) TxtLyricsPreviewNext.FontSize = val;
                OnUnfocusedFontSizeChanged(val);
            };
        }

        if (SliderSettingsLyricsOffset != null)
        {
            SliderSettingsLyricsOffset.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                int val = (int)Math.Round(e.NewValue);
                if (TxtSettingsLyricsOffset != null) TxtSettingsLyricsOffset.Text = $"{val:+0;-0;0} ms";
                OnLyricsOffsetChanged(val);
            };
        }

        if (BtnSettingsOffsetMinus500 != null) BtnSettingsOffsetMinus500.Click += (_, _) => OnLyricsOffsetChanged((SliderSettingsLyricsOffset?.Value ?? 0) - 500);
        if (BtnSettingsOffsetReset != null) BtnSettingsOffsetReset.Click += (_, _) => OnLyricsOffsetChanged(0);
        if (BtnSettingsOffsetPlus500 != null) BtnSettingsOffsetPlus500.Click += (_, _) => OnLyricsOffsetChanged((SliderSettingsLyricsOffset?.Value ?? 0) + 500);

        if (SliderSettingsLyricsLineSpacing != null)
        {
            SliderSettingsLyricsLineSpacing.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                int val = (int)Math.Round(e.NewValue);
                if (TxtSettingsLyricsLineSpacing != null) TxtSettingsLyricsLineSpacing.Text = $"{val} px";
                _settingsManager.Current.LyricsLineSpacing = val;
                _settingsManager.Current.Lyrics.LineSpacing = val;
                SaveCurrentSettingsDebounced();
                UpdateLyricsLivePreview(val, _settingsManager.Current.LyricsAlignment);
                _lyricsWindow?.ApplyLyricsPersonalization();
            };
        }

        if (ComboSettingsLyricsAlignment != null)
        {
            ComboSettingsLyricsAlignment.SelectionChanged += (_, _) =>
            {
                if (_isUpdatingSettingsUi || ComboSettingsLyricsAlignment.SelectedItem is not ComboBoxItem item) return;
                string align = item.Tag?.ToString() ?? "Left";
                _settingsManager.Current.LyricsAlignment = align;
                _settingsManager.Current.Lyrics.Alignment = align;
                SaveCurrentSettingsDebounced();
                UpdateLyricsLivePreview(_settingsManager.Current.LyricsLineSpacing, align);
                _lyricsWindow?.ApplyLyricsPersonalization();
            };
        }

        if (SliderSettingsLyricsBackdropOpacity != null)
        {
            SliderSettingsLyricsBackdropOpacity.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                int val = (int)Math.Round(e.NewValue);
                if (TxtSettingsLyricsBackdropOpacity != null) TxtSettingsLyricsBackdropOpacity.Text = $"{val}%";
                _settingsManager.Current.LyricsBackdropOpacity = val;
                _settingsManager.Current.Lyrics.BackdropOpacity = val;
                SaveCurrentSettingsDebounced();
                _lyricsWindow?.UpdateBackdropOpacity(val);
            };
        }

        if (ComboSettingsGlowAcousticProfile != null)
        {
            ComboSettingsGlowAcousticProfile.SelectionChanged += (_, _) =>
            {
                if (_isUpdatingSettingsUi || ComboSettingsGlowAcousticProfile.SelectedItem is not ComboBoxItem item) return;
                if (Enum.TryParse<GlowAcousticProfile>(item.Tag?.ToString(), out var profile))
                {
                    _settingsManager.Current.GlowAcousticProfile = profile;
                    _settingsManager.Current.Glow.AcousticProfile = profile;
                    SaveCurrentSettingsDebounced();
                }
            };
        }

        // 4. Ambient Glow Live Preview & Sliders
        if (SliderSettingsGlowThickness != null)
        {
            SliderSettingsGlowThickness.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                int val = (int)Math.Round(e.NewValue);
                if (TxtSettingsGlowThickness != null) TxtSettingsGlowThickness.Text = $"{val} px";
                _settingsManager.Current.GlowThickness = val;
                SaveCurrentSettingsDebounced();
                if (SliderGlowThickness != null) SliderGlowThickness.Value = val;
                if (TxtGlowThicknessVal != null) TxtGlowThicknessVal.Text = $"{val} px";
                double t = Math.Max(2.0, val / 4.0);
                if (GlowPreviewTop != null) GlowPreviewTop.Height = t;
                if (GlowPreviewBottom != null) GlowPreviewBottom.Height = t;
                if (GlowPreviewLeft != null) GlowPreviewLeft.Width = t;
                if (GlowPreviewRight != null) GlowPreviewRight.Width = t;
            };
        }

        if (SliderSettingsGlowIntensity != null)
        {
            SliderSettingsGlowIntensity.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                int val = (int)Math.Round(e.NewValue);
                if (TxtSettingsGlowIntensity != null) TxtSettingsGlowIntensity.Text = $"{val}%";
                _settingsManager.Current.GlowIntensity = val;
                SaveCurrentSettingsDebounced();
                if (SliderGlowIntensity != null) SliderGlowIntensity.Value = val;
                if (TxtGlowIntensityVal != null) TxtGlowIntensityVal.Text = $"{val}%";
                double op = Math.Clamp(val / 100.0, 0.1, 1.0);
                if (GlowPreviewTop != null) GlowPreviewTop.Opacity = op;
                if (GlowPreviewBottom != null) GlowPreviewBottom.Opacity = op;
                if (GlowPreviewLeft != null) GlowPreviewLeft.Opacity = op;
                if (GlowPreviewRight != null) GlowPreviewRight.Opacity = op;
            };
        }

        if (SliderSettingsGlowSensitivity != null)
        {
            SliderSettingsGlowSensitivity.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                float sens = (float)e.NewValue;
                if (TxtSettingsGlowSensitivity != null) TxtSettingsGlowSensitivity.Text = $"{sens:0.0}x";
                _settingsManager.Current.Glow.Sensitivity = sens;
                SaveCurrentSettingsDebounced();
            };
        }

        if (ComboSettingsGlowPalette != null)
        {
            ComboSettingsGlowPalette.SelectionChanged += (_, _) =>
            {
                if (_isUpdatingSettingsUi || ComboSettingsGlowPalette.SelectedItem is not ComboBoxItem item) return;
                string mode = item.Tag?.ToString() ?? "AlbumArt";
                _settingsManager.Current.GlowPaletteMode = mode;
                SaveCurrentSettingsDebounced();
            };
        }

        // 5. Artwork Dynamic Background & Corner Radius
        if (SwitchArtworkDynamicBackground != null)
        {
            SwitchArtworkDynamicBackground.Toggled += (_, _) =>
            {
                if (_isUpdatingSettingsUi) return;
                bool enabled = SwitchArtworkDynamicBackground.IsOn;
                _settingsManager.Current.VisualIntegrationEnabled = enabled;
                SaveCurrentSettingsDebounced();
                if (HomeAmbientBackgroundLayer != null)
                {
                    HomeAmbientBackgroundLayer.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                }
            };
        }

        if (SliderArtworkBackgroundSaturation != null)
        {
            SliderArtworkBackgroundSaturation.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                int val = (int)Math.Round(e.NewValue);
                if (TxtArtworkSaturationPercent != null) TxtArtworkSaturationPercent.Text = $"{val}%";
                _settingsManager.Current.ArtworkBackgroundSaturation = val / 100.0;
                SaveCurrentSettingsDebounced();
                if (HomeAmbientBackgroundLayer != null)
                {
                    double baseOpacity = _settingsManager.Current.Theme == ThemeMode.Light ? 0.12 : 0.55;
                    HomeAmbientBackgroundLayer.Opacity = Math.Clamp(baseOpacity * (val / 100.0), 0.05, 0.95);
                }
            };
        }

        if (SliderArtworkCornerRadius != null)
        {
            SliderArtworkCornerRadius.ValueChanged += (_, e) =>
            {
                if (_isUpdatingSettingsUi) return;
                int val = (int)Math.Round(e.NewValue);
                if (TxtArtworkRadiusValue != null) TxtArtworkRadiusValue.Text = $"{val} px";
                _settingsManager.Current.ArtworkCornerRadius = val;
                SaveCurrentSettingsDebounced();
                if (BorderHomeTrackArtwork != null)
                {
                    BorderHomeTrackArtwork.CornerRadius = new CornerRadius(val);
                }
            };
        }

        // 6. Audio Engine Settings
        if (ComboFftSize != null)
        {
            ComboFftSize.SelectionChanged += (_, _) =>
            {
                if (_isUpdatingSettingsUi || ComboFftSize.SelectedItem is not ComboBoxItem item) return;
                if (int.TryParse(item.Tag?.ToString(), out int fft))
                {
                    _settingsManager.Current.Glow.FftSize = fft;
                    var analyzer = App.Services?.GetService<ISpectrumAnalyzer>();
                    if (analyzer != null)
                    {
                        analyzer.FftSize = fft;
                    }
                    SaveCurrentSettingsDebounced();
                }
            };
        }
    }

    public void ShowSettingsCategory(string category)
    {
        var accent = (Style)Application.Current.Resources["AccentButtonStyle"];
        var standard = (Style)Application.Current.Resources["DefaultButtonStyle"];

        BtnFilterAllSettings.Style = category == "All" ? accent : standard;
        if (BtnFilterAppearance != null) BtnFilterAppearance.Style = (category == "Appearance" || category == "General") ? accent : standard;
        if (BtnFilterPlayback != null) BtnFilterPlayback.Style = (category == "Playback") ? accent : standard;
        if (BtnFilterLyrics != null) BtnFilterLyrics.Style = (category == "Lyrics") ? accent : standard;
        if (BtnFilterGlow != null) BtnFilterGlow.Style = (category == "Glow") ? accent : standard;
        if (BtnFilterArtwork != null) BtnFilterArtwork.Style = (category == "Artwork") ? accent : standard;
        if (BtnFilterAudio != null) BtnFilterAudio.Style = (category == "Audio") ? accent : standard;
        if (BtnFilterSystem != null) BtnFilterSystem.Style = (category == "System" || category == "Updates" || category == "Shortcuts") ? accent : standard;
        if (BtnFilterAbout != null) BtnFilterAbout.Style = (category == "About") ? accent : standard;

        BtnFilterGeneral.Style = (category == "General" || category == "Appearance") ? accent : standard;
        BtnFilterUpdates.Style = (category == "Updates" || category == "System") ? accent : standard;
        BtnFilterShortcuts.Style = (category == "Shortcuts" || category == "System") ? accent : standard;

        // Visibility of 8 settings categories
        SectionGeneral.Visibility = (category == "All" || category == "Appearance" || category == "General") ? Visibility.Visible : Visibility.Collapsed;
        if (SectionSettingsPlayback != null) SectionSettingsPlayback.Visibility = (category == "All" || category == "Playback") ? Visibility.Visible : Visibility.Collapsed;
        if (SectionSettingsLyrics != null) SectionSettingsLyrics.Visibility = (category == "All" || category == "Lyrics") ? Visibility.Visible : Visibility.Collapsed;
        if (SectionSettingsGlow != null) SectionSettingsGlow.Visibility = (category == "All" || category == "Glow") ? Visibility.Visible : Visibility.Collapsed;
        if (SectionSettingsArtwork != null) SectionSettingsArtwork.Visibility = (category == "All" || category == "Artwork") ? Visibility.Visible : Visibility.Collapsed;
        if (SectionSettingsAudio != null) SectionSettingsAudio.Visibility = (category == "All" || category == "Audio") ? Visibility.Visible : Visibility.Collapsed;
        SectionUpdates.Visibility = (category == "All" || category == "System" || category == "Updates") ? Visibility.Visible : Visibility.Collapsed;
        SectionShortcuts.Visibility = (category == "All" || category == "System" || category == "Shortcuts") ? Visibility.Visible : Visibility.Collapsed;
        if (SectionAbout != null) SectionAbout.Visibility = (category == "All" || category == "About") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLyricsLivePreview(int lineSpacing, string alignment)
    {
        if (StackLyricsPreview != null)
        {
            StackLyricsPreview.Spacing = Math.Clamp(lineSpacing / 2.0, 4.0, 24.0);
        }
        var textAlignment = string.Equals(alignment, "Center", StringComparison.OrdinalIgnoreCase)
            ? TextAlignment.Center
            : TextAlignment.Left;
        var horizAlignment = string.Equals(alignment, "Center", StringComparison.OrdinalIgnoreCase)
            ? HorizontalAlignment.Center
            : HorizontalAlignment.Left;

        if (TxtLyricsPreviewPrev != null)
        {
            TxtLyricsPreviewPrev.TextAlignment = textAlignment;
            TxtLyricsPreviewPrev.HorizontalAlignment = horizAlignment;
        }
        if (TxtLyricsPreviewFocused != null)
        {
            TxtLyricsPreviewFocused.TextAlignment = textAlignment;
            TxtLyricsPreviewFocused.HorizontalAlignment = horizAlignment;
        }
        if (TxtLyricsPreviewNext != null)
        {
            TxtLyricsPreviewNext.TextAlignment = textAlignment;
            TxtLyricsPreviewNext.HorizontalAlignment = horizAlignment;
        }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        // Navigation
        NavHome.Content = loc["Nav_Home"];
        if (NavPlayback != null) NavPlayback.Content = loc["Nav_Playback"];
        NavGlow.Content = loc["Nav_Glow"];
        NavLyrics.Content = loc["Nav_Lyrics"];
        TxtSettingsHeader.Text = loc["Settings_Title"];

        // Settings Category Filters
        BtnFilterAllSettings.Content = loc["Tab_All"];
        if (BtnFilterAppearance != null) BtnFilterAppearance.Content = loc["Tab_Appearance"];
        if (BtnFilterPlayback != null) BtnFilterPlayback.Content = loc["Tab_Playback"];
        if (BtnFilterLyrics != null) BtnFilterLyrics.Content = loc["Tab_Lyrics"];
        if (BtnFilterGlow != null) BtnFilterGlow.Content = loc["Tab_Glow"];
        if (BtnFilterArtwork != null) BtnFilterArtwork.Content = loc["Tab_Artwork"];
        if (BtnFilterAudio != null) BtnFilterAudio.Content = loc["Tab_Audio"];
        if (BtnFilterSystem != null) BtnFilterSystem.Content = loc["Tab_System"];
        if (BtnFilterAbout != null) BtnFilterAbout.Content = loc["Tab_About"];

        BtnFilterGeneral.Content = loc["Tab_General"];
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

        // Lyrics & Glow Personalization
        if (TxtLyricsLineSpacingHeader != null) TxtLyricsLineSpacingHeader.Text = loc["Lyrics_LineSpacing"];
        if (TxtLyricsLineSpacingDesc != null) TxtLyricsLineSpacingDesc.Text = loc["Lyrics_LineSpacing_Desc"];
        if (TxtLyricsAlignmentHeader != null) TxtLyricsAlignmentHeader.Text = loc["Lyrics_Alignment"];
        if (TxtLyricsAlignmentDesc != null) TxtLyricsAlignmentDesc.Text = loc["Lyrics_Alignment_Desc"];
        if (ComboItemAlignLeft != null) ComboItemAlignLeft.Content = loc["Align_Left"];
        if (ComboItemAlignCenter != null) ComboItemAlignCenter.Content = loc["Align_Center"];
        if (TxtLyricsBackdropOpacityHeader != null) TxtLyricsBackdropOpacityHeader.Text = loc["Lyrics_BackdropOpacity"];
        if (TxtLyricsBackdropOpacityDesc != null) TxtLyricsBackdropOpacityDesc.Text = loc["Lyrics_BackdropOpacity_Desc"];
        if (TxtGlowAcousticProfileHeader != null) TxtGlowAcousticProfileHeader.Text = loc["Glow_AcousticProfile"];
        if (TxtGlowAcousticProfileDesc != null) TxtGlowAcousticProfileDesc.Text = loc["Glow_AcousticProfile_Desc"];
        if (ComboItemProfileBalanced != null) ComboItemProfileBalanced.Content = loc["Profile_Balanced"];
        if (ComboItemProfileBassBoost != null) ComboItemProfileBassBoost.Content = loc["Profile_BassBoost"];
        if (ComboItemProfileVocalFocus != null) ComboItemProfileVocalFocus.Content = loc["Profile_VocalFocus"];
        if (ComboItemProfileHighFidelity != null) ComboItemProfileHighFidelity.Content = loc["Profile_HighFidelity"];
    }

    private async Task SetGlowStateAsync(bool active)
    {
        if (_isUpdatingToggles) return;
        _isUpdatingToggles = true;
        try
        {
            SwitchHomeGlow.IsOn = active;
            SwitchGlowMaster.IsOn = active;

            await _moduleManager.SetModuleActiveAsync("glow", active);

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

            await _moduleManager.SetModuleActiveAsync("lyrics", active);

            if (active)
            {
                TxtLyricsStatus.Text = "Running (Active)";
                TxtLyricsStatus.Foreground = (Brush)Application.Current.Resources["LyrowSuccessBrush"];

                if (_lyricsWindow == null && _lyricsModule != null)
                {
                    _lyricsWindow = new LyricsWindow(_lyricsModule, _settingsManager, _artworkBitmapManager, _mediaMonitor, _volumeService, _toastManager);
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

        if (_settingsManager?.Current != null &&
            _settingsManager.Current.NotificationsEnabled &&
            _settingsManager.Current.NotifyOnTrackChange &&
            !track.IsEmpty)
        {
            _toastService?.ShowInfo($"{track.Title} • {track.Artist}");
        }
    }

    private void OnHardwareMediaTimelineChanged(object? sender, MediaTimelineInfo timeline)
    {
        _lyricsModule?.UpdateTimelineExternal(timeline);

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

        // If LyricsModule is not currently Running, still trigger background lyrics lookup & timeline reset
        // so Dashboard lyrics status/current line and future LyricsWindow activation are immediately synced!
        if (_lyricsModule != null && _lyricsModule.State != ModuleState.Running)
        {
            if (_mediaMonitor != null)
            {
                _lyricsModule.UpdateTimelineExternal(_mediaMonitor.CurrentTimeline);
            }
            _ = _lyricsModule.HandleTrackChangedAsync(track);
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            SyncVolumeUiFromHardware();
            UpdateMediaTransportControlsState(_mediaMonitor?.HasActiveSession ?? !track.IsEmpty, _mediaMonitor?.CurrentTimeline.Status ?? MediaPlaybackStatus.Stopped);

            if (track.IsEmpty)
            {
                var loc = LocalizationService.Instance;
                HomeTxtTitle.Text = loc["Lyrics_WaitingPlayback"];
                HomeTxtArtist.Text = loc["Lyrics_PlayToSeeLyrics"];
                HomeTxtAlbum.Text = string.Empty;
                HomeTxtAlbum.Visibility = Visibility.Collapsed;
                HomeProgressTimeline.Value = 0;
                HomeTxtTimestamp.Text = "00:00 / 00:00";
                HomeArtworkPlaceholder.Visibility = Visibility.Visible;
                HomeTrackArtwork.Source = null;
                TxtLyricsAvailability.Text = loc["Lyrics_WaitingPlayback"];
                TxtHomeCurrentLyricLine.Text = $"♪ {loc["Lyrics_PlayToSeeLyrics"]}";
                return;
            }

            // Atomically reset progress and placeholder for seamless track transition
            HomeProgressTimeline.Value = 0;
            HomeTxtTimestamp.Text = track.Duration > TimeSpan.Zero ? $"00:00 / {track.Duration:mm\\:ss}" : "00:00 / --:--";
            HomeTrackArtwork.Source = null;
            HomeArtworkPlaceholder.Visibility = Visibility.Visible;

            HomeTxtTitle.Text = track.Title;
            HomeTxtArtist.Text = track.Artist;
            if (_settingsManager.Current.EnableAnimations)
            {
                var metaOpacityAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0.35,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(metaOpacityAnim, StackHomeTrackMeta);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(metaOpacityAnim, "Opacity");

                var metaTransAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 5.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(220),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(metaTransAnim, TransHomeTrackMeta);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(metaTransAnim, "Y");

                var sbMeta = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                sbMeta.Children.Add(metaOpacityAnim);
                sbMeta.Children.Add(metaTransAnim);
                sbMeta.Begin();
            }
            else
            {
                StackHomeTrackMeta.Opacity = 1.0;
                TransHomeTrackMeta.Y = 0.0;
            }
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

            TxtLyricsAvailability.Text = LocalizationService.Instance["Lyrics_Searching"];
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

        long currentSeq = Interlocked.Increment(ref _mainTrackSequence);
        string newKey = ArtworkService.CreateCacheKey(track.Artist, track.Title);
        int newArtSig = ArtworkBitmapManager.ComputeArtworkSignature(track);

        bool isTrackSwitch = !string.Equals(_lastTrackCacheKey, newKey, StringComparison.OrdinalIgnoreCase);
        bool isArtworkUpdated = newArtSig != _lastArtworkSignature;

        if (isTrackSwitch || isArtworkUpdated)
        {
            _artworkBitmapManager.Invalidate(track.Artist, track.Title);
        }

        if (isTrackSwitch)
        {
            _glowOverlayManager?.ResetBeatEngines();
        }

        _lastTrackCacheKey = newKey;
        _lastArtworkSignature = newArtSig;

        // Decode BitmapImage on UI thread
        DispatcherQueue.TryEnqueue(async () =>
        {
            var bitmap = await _artworkBitmapManager.GetArtworkBitmapAsync(track);
            if (Interlocked.Read(ref _mainTrackSequence) != currentSeq) return;

            if (bitmap != null)
            {
                HomeTrackArtwork.Opacity = 0.0;
                HomeTrackArtwork.Source = bitmap;
                var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { From = 0.0, To = 1.0, Duration = TimeSpan.FromMilliseconds(240) };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, HomeTrackArtwork);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
                var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                sb.Children.Add(anim);
                sb.Begin();
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
            if (Interlocked.Read(ref _mainTrackSequence) != currentSeq) return;

            _glowOverlayManager?.SetCustomPalette(palette);
                        var ambient = ArtworkPaletteExtractor.ExtractAmbientPalette(palette);
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdatePaletteSwatches(palette);
                HomeStopTopLeft.Color = Windows.UI.Color.FromArgb(255, ambient.Dominant.R, ambient.Dominant.G, ambient.Dominant.B);
                HomeStopCenter.Color = Windows.UI.Color.FromArgb(255, ambient.Secondary.R, ambient.Secondary.G, ambient.Secondary.B);
                HomeStopBottomRight.Color = Windows.UI.Color.FromArgb(255, ambient.Tertiary.R, ambient.Tertiary.G, ambient.Tertiary.B);
            });
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

            if (_lyricsModule != null)
            {
                var pos = timeline.GetInterpolatedPosition();
                _lyricsModule.Synchronizer.EvaluatePosition(pos);
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
        if (_isDraggingHomeProgress) return;
        var pos = timeline.GetInterpolatedPosition();
        var dur = track.Duration > TimeSpan.Zero ? track.Duration : timeline.Duration;

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
                int idx = _lyricsModule?.ActiveLineIndex ?? -1;
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
            _lyricsWindow?.SetLyricsNotFound(e.ErrorMessage);
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
                case "Playback":
                    ShowPage(PagePlayback);
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
        if (PagePlayback != null) PagePlayback.Visibility = (activePage == PagePlayback) ? Visibility.Visible : Visibility.Collapsed;
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
        if (HomeAmbientBackgroundLayer != null)
        {
            double baseOp = mode == ThemeMode.Light ? 0.12 : 0.55;
            double sat = _settingsManager?.Current.ArtworkBackgroundSaturation ?? 1.0;
            HomeAmbientBackgroundLayer.Opacity = Math.Clamp(baseOp * sat, 0.05, 0.95);
        }

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = mode switch
            {
                ThemeMode.Dark => ElementTheme.Dark,
                ThemeMode.Light => ElementTheme.Light,
                _ => ElementTheme.Default
            };
        }

        _trayIconManager?.SetTheme(mode.ToString());
        _lyricsWindow?.ApplyTheme(mode);
    }

    public void ShowToast(string message, InfoBarSeverity severity = InfoBarSeverity.Informational, string? title = null, string? actionLabel = null, Action? onAction = null)
    {
        _toastManager?.ShowToast(message, title, severity, actionLabel, onAction);
    }

    public void ShowAndActivate()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                AppWindow.Show();
                Activate();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                }
            }
            catch (Exception ex)
            {
                LocalDiagnosticLogger.LogError("Error in ShowAndActivate", ex);
            }
        });
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
        try
        {
            if (_settingsManager != null)
            {
                WindowStateManager.SaveWindowState(AppWindow, _settingsManager.Current, _currentActivePage, _isCompact);
                _settingsManager.SaveAsync(_settingsManager.Current, CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        catch { }
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


    public void NavigateTo(string pageName)
    {
        _currentActivePage = pageName;
        switch (pageName)
        {
            case "Home":
                NavView.SelectedItem = NavHome;
                ShowPage(PageHome);
                break;
            case "Lyrics":
                NavView.SelectedItem = NavLyrics;
                ShowPage(PageLyrics);
                break;
            case "Glow":
                NavView.SelectedItem = NavGlow;
                ShowPage(PageGlow);
                break;
            case "Playback":
                NavView.SelectedItem = NavPlayback;
                ShowPage(PagePlayback);
                break;
            case "Settings":
                NavView.SelectedItem = NavView.SettingsItem;
                ShowPage(PageSettings);
                break;
            default:
                NavView.SelectedItem = NavHome;
                ShowPage(PageHome);
                break;
        }
    }

    private void WireHomeCardsNavigation()
    {
        AttachCardInteraction(CardCurrentTrack, () => NavigateTo("Playback"));
        AttachCardInteraction(CardVolume, () => NavigateTo("Playback"));
        AttachCardInteraction(CardGlow, () => NavigateTo("Glow"));
        AttachCardInteraction(CardLyrics, () => NavigateTo("Lyrics"));
    }

    private void AttachCardInteraction(Border? card, Action onAction)
    {
        if (card == null) return;

        card.IsTabStop = true;
        var scale = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
        card.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        card.RenderTransform = scale;

        void AnimateScale(double targetScale, int durationMs = 90)
        {
            try
            {
                var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                var animX = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = targetScale,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuadraticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
                };
                var animY = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    To = targetScale,
                    Duration = TimeSpan.FromMilliseconds(durationMs),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.QuadraticEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut }
                };

                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animX, scale);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animX, "ScaleX");
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animY, scale);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animY, "ScaleY");

                sb.Children.Add(animX);
                sb.Children.Add(animY);
                sb.Begin();
            }
            catch
            {
                scale.ScaleX = targetScale;
                scale.ScaleY = targetScale;
            }
        }

        card.PointerPressed += (s, e) =>
        {
            if (e.OriginalSource is DependencyObject dep && IsInteractiveControl(dep)) return;
            AnimateScale(0.97, 70);
        };

        card.PointerReleased += (s, e) =>
        {
            if (e.OriginalSource is DependencyObject dep && IsInteractiveControl(dep))
            {
                AnimateScale(1.0, 110);
                return;
            }
            AnimateScale(1.0, 120);
            onAction();
        };

        card.PointerCanceled += (s, e) => AnimateScale(1.0, 100);
        card.PointerCaptureLost += (s, e) => AnimateScale(1.0, 100);

        card.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.Space)
            {
                AnimateScale(0.97, 60);
                AnimateScale(1.0, 120);
                onAction();
                e.Handled = true;
            }
        };
    }

    private static bool IsInteractiveControl(DependencyObject dep)
    {
        DependencyObject? current = dep;
        while (current != null)
        {
            if (current is Button || current is Slider || current is ToggleSwitch || current is TextBox || current is ComboBox)
                return true;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }

        [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);
    private SubclassProc? _subclassProc;

    private IntPtr MainWindowSubclass(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        const uint WM_CLOSE = 0x0010;
        if (uMsg == WM_CLOSE)
        {
            LocalDiagnosticLogger.LogInfo("WM_CLOSE message received on MainWindow HWND.");
            bool isSmokeTest = App.IsSmokeTestMode
                || Environment.GetCommandLineArgs().Any(a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase))
                || Environment.GetEnvironmentVariable("LYROW_SMOKE_TEST") == "1";

            if (_settingsManager.Current.MinimizeToTray && !_isExplicitExit && !isSmokeTest)
            {
                DispatcherQueue.TryEnqueue(() => AppWindow.Hide());
            }
            else
            {
                DispatcherQueue.TryEnqueue(RequestExit);
            }
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void OnRootShellSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool isCompact = e.NewSize.Width <= 560 || e.NewSize.Height <= 320;
        _isCompact = isCompact;
        if (isCompact)
        {
            NavView.IsPaneVisible = false;
            RootContentGrid.Margin = new Thickness(12, 6, 12, 6);
            CardVolume.Visibility = Visibility.Collapsed;
            CardGlow.Visibility = Visibility.Collapsed;
            CardLyrics.Visibility = Visibility.Collapsed;
            BorderHomeTrackArtwork.Width = 64;
            BorderHomeTrackArtwork.Height = 64;
            BorderHomeTrackArtwork.CornerRadius = new CornerRadius(10);
            HomeTxtTitle.FontSize = 15;
            HomeTxtArtist.FontSize = 12;
            CardCurrentTrack.Padding = new Thickness(14, 10, 14, 10);
        }
        else
        {
            NavView.IsPaneVisible = true;
            RootContentGrid.Margin = new Thickness(24, 14, 24, 28);
            CardVolume.Visibility = Visibility.Visible;
            CardGlow.Visibility = Visibility.Visible;
            CardLyrics.Visibility = Visibility.Visible;
            BorderHomeTrackArtwork.Width = 84;
            BorderHomeTrackArtwork.Height = 84;
            double userR = _settingsManager?.Current.ArtworkCornerRadius ?? 12.0;
            BorderHomeTrackArtwork.CornerRadius = new CornerRadius(userR);
            HomeTxtTitle.FontSize = 19;
            HomeTxtArtist.FontSize = 13.5;
            CardCurrentTrack.Padding = new Thickness(20);
        }
    }

    private void WireHomeProgressScrubber()
    {
        bool isPointerCaptured = false;

        TimeSpan ResolveDuration()
        {
            var track = _mediaMonitor?.CurrentTrack ?? MediaTrackInfo.Empty;
            var timeline = _mediaMonitor?.CurrentTimeline ?? MediaTimelineInfo.Zero;
            return track.Duration > TimeSpan.Zero ? track.Duration : timeline.Duration;
        }

        void UpdatePreview(Windows.Foundation.Point pt)
        {
            double w = GridHomeProgressHitBox.ActualWidth > 0 ? GridHomeProgressHitBox.ActualWidth : HomeProgressTimeline.ActualWidth;
            var dur = ResolveDuration();
            if (w <= 0 || dur <= TimeSpan.Zero) return;

            var targetTime = LyricsFillProgressCalculator.CalculateSeekTimeFromPointer(pt.X, w, dur);
            double pct = Math.Clamp((targetTime.TotalMilliseconds / dur.TotalMilliseconds) * 100.0, 0.0, 100.0);
            HomeProgressTimeline.Value = pct;
            HomeTxtTimestamp.Text = $"{targetTime:mm\\:ss} / {dur:mm\\:ss}";
        }

        void UpdateScrubberToolTip()
        {
            bool canSeek = _mediaMonitor?.CanSeek ?? false;
            string toolTip = canSeek
                ? "Click or drag to seek"
                : LocalizationService.Instance["Lyrics_SeekFailed"];
            ToolTipService.SetToolTip(GridHomeProgressHitBox, toolTip);
        }

        UpdateScrubberToolTip();
        if (_mediaMonitor != null)
        {
            _mediaMonitor.TimelineChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(UpdateScrubberToolTip);
            };
        }

        GridHomeProgressHitBox.PointerPressed += (s, e) =>
        {
            if (_mediaMonitor != null && !_mediaMonitor.CanSeek)
            {
                ShowToast(LocalizationService.Instance["Lyrics_SeekFailed"], InfoBarSeverity.Informational);
                e.Handled = true;
                return;
            }

            var dur = ResolveDuration();
            if (dur <= TimeSpan.Zero) return;

            isPointerCaptured = GridHomeProgressHitBox.CapturePointer(e.Pointer);
            _isDraggingHomeProgress = true;
            UpdatePreview(e.GetCurrentPoint(GridHomeProgressHitBox).Position);
            e.Handled = true;
        };

        GridHomeProgressHitBox.PointerMoved += (s, e) =>
        {
            if (!_isDraggingHomeProgress || !isPointerCaptured) return;
            UpdatePreview(e.GetCurrentPoint(GridHomeProgressHitBox).Position);
            e.Handled = true;
        };

        GridHomeProgressHitBox.PointerReleased += async (s, e) =>
        {
            if (!_isDraggingHomeProgress) return;
            _isDraggingHomeProgress = false;
            if (isPointerCaptured)
            {
                GridHomeProgressHitBox.ReleasePointerCapture(e.Pointer);
                isPointerCaptured = false;
            }

            double w = GridHomeProgressHitBox.ActualWidth > 0 ? GridHomeProgressHitBox.ActualWidth : HomeProgressTimeline.ActualWidth;
            var dur = ResolveDuration();
            if (w > 0 && dur > TimeSpan.Zero && _mediaMonitor != null && _mediaMonitor.CanSeek)
            {
                var pt = e.GetCurrentPoint(GridHomeProgressHitBox).Position;
                var targetTime = LyricsFillProgressCalculator.CalculateSeekTimeFromPointer(pt.X, w, dur);
                await _mediaMonitor.TrySeekAsync(targetTime);
            }
            e.Handled = true;
        };

        GridHomeProgressHitBox.PointerCanceled += (s, e) =>
        {
            _isDraggingHomeProgress = false;
            if (isPointerCaptured)
            {
                GridHomeProgressHitBox.ReleasePointerCapture(e.Pointer);
                isPointerCaptured = false;
            }
        };
    }


        [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void InitializeSetupWizard()
    {
        if (!_settingsManager.Current.HasCompletedFirstRun && !App.IsSmokeTestMode)
        {
            SetupWizardOverlay.Visibility = Visibility.Visible;
            NavView.Visibility = Visibility.Collapsed;
            ShowWizardStep(0);
        }
        else
        {
            SetupWizardOverlay.Visibility = Visibility.Collapsed;
            NavView.Visibility = Visibility.Visible;
            WindowStateManager.RestoreWindowState(AppWindow, _settingsManager.Current, page => NavigateTo(page));
        }

        if (BtnWizardSkip != null)
        {
            BtnWizardSkip.Click += (_, _) => CompleteWizard(skipped: true);
        }

        if (BtnWizardNext != null)
        {
            BtnWizardNext.Click += (_, _) =>
            {
                if (_currentWizardStep >= 5)
                {
                    CompleteWizard(skipped: false);
                }
                else
                {
                    ShowWizardStep(_currentWizardStep + 1);
                }
            };
        }

        if (BtnWizardBack != null)
        {
            BtnWizardBack.Click += (_, _) =>
            {
                if (_currentWizardStep > 0)
                {
                    ShowWizardStep(_currentWizardStep - 1);
                }
            };
        }

        if (BtnWizardPlayerRefresh != null)
        {
            BtnWizardPlayerRefresh.Click += (_, _) => UpdateWizardPlayerStepStatus();
        }

        if (ComboWizardWelcomeLanguage != null)
        {
            string currentLang = _settingsManager.Current.Language ?? "en";
            for (int i = 0; i < ComboWizardWelcomeLanguage.Items.Count; i++)
            {
                if (ComboWizardWelcomeLanguage.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), currentLang, StringComparison.OrdinalIgnoreCase))
                {
                    ComboWizardWelcomeLanguage.SelectedIndex = i;
                    break;
                }
            }

            ComboWizardWelcomeLanguage.SelectionChanged += (_, _) =>
            {
                if (ComboWizardWelcomeLanguage.SelectedItem is ComboBoxItem item && item.Tag != null)
                {
                    string lang = item.Tag.ToString()!;
                    LocalizationService.Instance.SetLanguage(lang);
                    _settingsManager.Current.Language = lang;
                    SaveCurrentSettingsDebounced();
                    ApplyLocalization();
                }
            };
        }

        if (RadioWizardThemeSystem != null)
        {
            RadioWizardThemeSystem.Checked += (_, _) =>
            {
                _settingsManager.Current.Theme = ThemeMode.SystemDefault;
                ApplyTheme(ThemeMode.SystemDefault);
                SaveCurrentSettingsDebounced();
            };
        }
        if (RadioWizardThemeDark != null)
        {
            RadioWizardThemeDark.Checked += (_, _) =>
            {
                _settingsManager.Current.Theme = ThemeMode.Dark;
                ApplyTheme(ThemeMode.Dark);
                SaveCurrentSettingsDebounced();
            };
        }
        if (RadioWizardThemeLight != null)
        {
            RadioWizardThemeLight.Checked += (_, _) =>
            {
                _settingsManager.Current.Theme = ThemeMode.Light;
                ApplyTheme(ThemeMode.Light);
                SaveCurrentSettingsDebounced();
            };
        }

        if (SwitchWizardAnimations != null)
        {
            SwitchWizardAnimations.IsOn = _settingsManager.Current.EnableAnimations;
            SwitchWizardAnimations.Toggled += (_, _) =>
            {
                _settingsManager.Current.EnableAnimations = SwitchWizardAnimations.IsOn;
                SaveCurrentSettingsDebounced();
            };
        }

        if (SwitchWizardGlow != null)
        {
            SwitchWizardGlow.IsOn = _settingsManager.Current.GlowEnabled;
            SwitchWizardGlow.Toggled += (_, _) =>
            {
                _settingsManager.Current.GlowEnabled = SwitchWizardGlow.IsOn;
                SaveCurrentSettingsDebounced();
            };
        }

        if (BtnRestartWizard != null)
        {
            BtnRestartWizard.Click += (_, _) => RestartWizard();
        }
    }

    public void RestartWizard()
    {
        SetupWizardOverlay.Visibility = Visibility.Visible;
        NavView.Visibility = Visibility.Collapsed;
        ShowWizardStep(0);
    }

    public void ShowWizardStep(int step)
    {
        _currentWizardStep = Math.Clamp(step, 0, 5);

        if (PanelWizardStep0 != null) PanelWizardStep0.Visibility = _currentWizardStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PanelWizardStep1 != null) PanelWizardStep1.Visibility = _currentWizardStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (PanelWizardStep2 != null) PanelWizardStep2.Visibility = _currentWizardStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (PanelWizardStep3 != null) PanelWizardStep3.Visibility = _currentWizardStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (PanelWizardStep4 != null) PanelWizardStep4.Visibility = _currentWizardStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        if (PanelWizardStep5 != null) PanelWizardStep5.Visibility = _currentWizardStep == 5 ? Visibility.Visible : Visibility.Collapsed;

        var accentBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var mutedBrush = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];

        if (DotStep0 != null) DotStep0.Background = _currentWizardStep == 0 ? accentBrush : mutedBrush;
        if (DotStep1 != null) DotStep1.Background = _currentWizardStep == 1 ? accentBrush : mutedBrush;
        if (DotStep2 != null) DotStep2.Background = _currentWizardStep == 2 ? accentBrush : mutedBrush;
        if (DotStep3 != null) DotStep3.Background = _currentWizardStep == 3 ? accentBrush : mutedBrush;
        if (DotStep4 != null) DotStep4.Background = _currentWizardStep == 4 ? accentBrush : mutedBrush;
        if (DotStep5 != null) DotStep5.Background = _currentWizardStep == 5 ? accentBrush : mutedBrush;

        if (BtnWizardBack != null)
        {
            BtnWizardBack.Visibility = _currentWizardStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (BtnWizardNext != null)
        {
            BtnWizardNext.Content = _currentWizardStep switch
            {
                0 => "Get Started",
                5 => "Start Using Lyrow",
                _ => "Next"
            };
        }

        if (_currentWizardStep == 2)
        {
            UpdateWizardPlayerStepStatus();
        }
        else if (_currentWizardStep == 3)
        {
            UpdateWizardLyricsStepStatus();
        }
        else if (_currentWizardStep == 5)
        {
            if (TxtSummaryTheme != null)
            {
                TxtSummaryTheme.Text = _settingsManager.Current.Theme switch
                {
                    ThemeMode.Dark => "Dark",
                    ThemeMode.Light => "Light",
                    _ => "System Default"
                };
            }
            if (TxtSummaryLanguage != null)
            {
                TxtSummaryLanguage.Text = (_settingsManager.Current.Language ?? "en") switch
                {
                    "tr" => "Türkçe",
                    "fr" => "Français",
                    "es" => "Español",
                    "de" => "Deutsch",
                    _ => "English"
                };
            }
        }
    }

    private void UpdateWizardPlayerStepStatus()
    {
        bool hasSession = _mediaMonitor?.HasActiveSession ?? false;
        var currentTrack = _mediaMonitor?.CurrentTrack ?? MediaTrackInfo.Empty;

        if (TxtWizardPlayerStatus != null && IconWizardPlayerStatus != null && TxtWizardPlayerHeader != null)
        {
            if (hasSession && !currentTrack.IsEmpty)
            {
                IconWizardPlayerStatus.Glyph = "\uE73E";
                IconWizardPlayerStatus.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                TxtWizardPlayerHeader.Text = "Active Player Detected";
                TxtWizardPlayerStatus.Text = $"{currentTrack.Title} • {currentTrack.Artist}";
            }
            else if (hasSession)
            {
                IconWizardPlayerStatus.Glyph = "\uE73E";
                IconWizardPlayerStatus.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                TxtWizardPlayerHeader.Text = "Media Session Ready";
                TxtWizardPlayerStatus.Text = "Connected to Windows media session. Start playback anytime.";
            }
            else
            {
                IconWizardPlayerStatus.Glyph = "\uE768";
                IconWizardPlayerStatus.Foreground = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                TxtWizardPlayerHeader.Text = "No Media Session Active";
                TxtWizardPlayerStatus.Text = "Open Apple Music or Spotify and start playback to link with Lyrow.";
            }
        }
    }

    private void UpdateWizardLyricsStepStatus()
    {
        if (TxtWizardLyricsStatus != null)
        {
            TxtWizardLyricsStatus.Text = "LRCLIB Service: Connected (Online & Offline ready)";
        }
    }

    public void CompleteWizard(bool skipped)
    {
        _settingsManager.Current.HasCompletedFirstRun = true;
        _ = _settingsManager.SaveAsync(_settingsManager.Current);

        SetupWizardOverlay.Visibility = Visibility.Collapsed;
        NavView.Visibility = Visibility.Visible;

        WindowStateManager.RestoreWindowState(AppWindow, _settingsManager.Current, page => NavigateTo(page));

        ShowToast(
            skipped ? "Welcome to Lyrow! Setup skipped." : "Welcome to Lyrow! You're all set.",
            InfoBarSeverity.Success);
    }

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