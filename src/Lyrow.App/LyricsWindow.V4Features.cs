using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Layout;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App;

public sealed partial class LyricsWindow
{
    private Storyboard? _activeModalStoryboard;
    private bool _isTogglingEdgeGlow = false;
    private GlowPalette _lastArtworkGlowPalette = GlowPalette.Default;
    private DispatcherTimer? _edgeThicknessSaveDebounceTimer;
    private bool _isUpdatingEdgeThicknessSlider = false;
    private int _recordedSinglePanelWidthPx = 0;
    private long _widthAnimationGeneration = 0;
    private UpdateCoordinator? _updateCoordinator;
    private Controls.ProgressToastHandle? _activeUpdateToast;
    private WelcomeWindow? _activeWelcomeWindow;
    private DispatcherTimer? _windowWidthAnimTimer;

    private void InitializeV4LyricsGlowAndSettingsModal()
    {
        // 1. Initialize Lyrics Glow state from persisted settings (defaults to false on fresh install)
        _isLyricsGlowEnabled = _settingsManager.Current.Lyrics.LyricsTextGlowEnabled;
        _currentLyricsGlowOpacity = _isLyricsGlowEnabled ? 0.34 : 0.0;
        UpdateLyricsGlowVisualState();
        UpdateLyricsToggleVisualState();

        if (BtnToggleLyricsGlow != null)
        {
            BtnToggleLyricsGlow.Click += (_, _) => ToggleLyricsGlow();
        }

        // 2. Wire Edge Light (Kenar Işığı) buttons (both Normal and Compact modes open shared 0-64 px thickness slider on click)
        InitializeEdgeGlowThicknessClickFlyout();
        UpdateEdgeGlowVisualState();

        // 3. Wire Ghost Settings Button & Modal Close Controls
        if (BtnOpenSettingsModal != null)
        {
            BtnOpenSettingsModal.Click += (_, _) => OpenSettingsModal();
        }
        if (BtnCloseSettingsModal != null)
        {
            BtnCloseSettingsModal.Click += (_, _) => CloseSettingsModal();
        }
        if (SettingsModalBackdrop != null)
        {
            // Swallow pointer events on backdrop so background window cannot be interacted with
            // and accidental clicks outside the modal do not unexpectedly close Settings.
            SettingsModalBackdrop.PointerPressed += (_, e) =>
            {
                e.Handled = true;
            };
        }

        // Prevent scroll wheel inside settings modal from affecting lyrics behind it
        if (SettingsModalScrollViewer != null)
        {
            SettingsModalScrollViewer.AddHandler(
                UIElement.PointerWheelChangedEvent,
                new PointerEventHandler(OnSettingsModalPointerWheelChanged),
                handledEventsToo: true);
        }

        // 7 Canonical Fluent Settings Tabs: Overview, General, Appearance, Lighting, Lyrics, System, About (Updates merged into About)
        WireModalTabButton(BtnModalTabOverview, "Overview");
        WireModalTabButton(BtnModalTabGeneral, "General");
        WireModalTabButton(BtnModalTabAppearance, "Appearance");
        WireModalTabButton(BtnModalTabLighting, "Lighting");
        WireModalTabButton(BtnModalTabLyrics, "Lyrics");
        WireModalTabButton(BtnModalTabSystem, "System");
        WireModalTabButton(BtnModalTabAbout, "About");

        WireModalSettingsControls();
        InitializeUpdateWorkflow();
        _ = InitializePrimaryAppServicesAsync();
    }

    private async Task InitializePrimaryAppServicesAsync()
    {
        try
        {
            if (_mediaMonitor != null)
            {
                _mediaMonitor.TimelineChanged += (_, timeline) =>
                {
                    SyncEdgeGlowPlaybackState(timeline.Status, _mediaMonitor.HasActiveSession);
                };

                if (!_mediaMonitor.IsMonitoring)
                {
                    await _mediaMonitor.StartAsync();
                }
            }

            if (_mediaMonitor != null && _volumeService != null)
            {
                _volumeService.SetPreferredAppId(_mediaMonitor.SourceAppId);
            }

            ApplyHotkeysFromSettings();
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Non-fatal error initializing primary services in LyricsWindow", ex);
        }
    }

    public void ShowAndActivate()
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            try
            {
                AppWindow?.Show();
                Activate();
            }
            catch { }
        });
    }

    public void RequestExit()
    {
        _isExplicitExit = true;
        _isShuttingDown = true;
        _uiTimer?.Stop();
        Close();
    }

    private readonly KeystrokeRecorderSession _glowHotkeyRecorder = new();
    private readonly KeystrokeRecorderSession _lyricsHotkeyRecorder = new();
    private KeystrokeRecorderSession? _activeHotkeyRecorder;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _hotkeyRecordTimeoutTimer;

    private bool IsHotkeyRecordingActive => _activeHotkeyRecorder?.IsRecording == true;

    public void ToggleEdgeGlowFromExternal()
    {
        if (IsHotkeyRecordingActive) return;
        DispatcherQueue?.TryEnqueue(async () => await ToggleEdgeGlowAsync());
    }

    public void ToggleLyricsPanelFromExternal()
    {
        if (IsHotkeyRecordingActive) return;
        DispatcherQueue?.TryEnqueue(() =>
        {
            ShowAndActivate();
            if (_currentMode == LyricsWindowMode.Horizontal)
            {
                ToggleLyricsPanel();
            }
            else
            {
                ToggleCompactLyrics();
            }
        });
    }

    /// <summary>
    /// Toggles the 4-edge screen lighting (Kenar Işığı) using the existing GlowModule and GlowOverlayManager.
    /// </summary>
    public async Task ToggleEdgeGlowAsync()
    {
        await SetEdgeGlowStateAsync(!_isEdgeGlowActive);
    }

    public async Task SetEdgeGlowStateAsync(bool active)
    {
        if (_isTogglingEdgeGlow) return;
        _isTogglingEdgeGlow = true;
        try
        {
            var moduleManager = App.Services?.GetService(typeof(IModuleManager)) as IModuleManager;
            var glowOverlayManager = App.Services?.GetService(typeof(GlowOverlayManager)) as GlowOverlayManager;

            if (moduleManager != null)
            {
                await moduleManager.SetModuleActiveAsync("glow", active);
            }

            _isEdgeGlowActive = active;
            _settingsManager.Current.GlowEnabled = active;
            _ = _settingsManager.SaveAsync(_settingsManager.Current);
            if (active)
            {
                glowOverlayManager?.StartOverlays();
                SyncEdgeGlowPalette(_lastArtworkGlowPalette);
                if (_mediaMonitor != null)
                {
                    glowOverlayManager?.UpdatePlaybackState(_mediaMonitor.CurrentTimeline.Status, _mediaMonitor.HasActiveSession);
                }
            }
            else
            {
                glowOverlayManager?.StopOverlays();
            }

            UpdateEdgeGlowVisualState();

            if (!_isUpdatingModalSettingsUi && SwitchModalAmbientEdgeGlow != null)
            {
                _isUpdatingModalSettingsUi = true;
                try { SwitchModalAmbientEdgeGlow.IsOn = active; }
                finally { _isUpdatingModalSettingsUi = false; }
            }
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Error toggling Edge Light (Kenar Işığı)", ex);
        }
        finally
        {
            _isTogglingEdgeGlow = false;
        }
    }

    private void UpdateEdgeGlowVisualState()
    {
        var loc = LocalizationService.Instance;
        string tip = _isEdgeGlowActive ? loc["EdgeLight_On"] : loc["EdgeLight_Off"];

        var accentStyle = Application.Current.Resources.TryGetValue("AccentButtonStyle", out var acc) ? acc as Style : null;
        var ghostStyle = Application.Current.Resources.TryGetValue("LyrowGhostButtonStyle", out var gh) ? gh as Style : null;

        if (BtnToggleEdgeGlow != null)
        {
            ToolTipService.SetToolTip(BtnToggleEdgeGlow, tip);
            AutomationProperties.SetName(BtnToggleEdgeGlow, tip);
            BtnToggleEdgeGlow.Style = _isEdgeGlowActive ? accentStyle : ghostStyle;
        }
        if (IconToggleEdgeGlow != null)
        {
            IconToggleEdgeGlow.Glyph = "\uE793";
            IconToggleEdgeGlow.Opacity = _isEdgeGlowActive ? 1.0 : 0.70;
        }
        if (BtnToggleEdgeGlowCompact != null)
        {
            ToolTipService.SetToolTip(BtnToggleEdgeGlowCompact, tip);
            AutomationProperties.SetName(BtnToggleEdgeGlowCompact, tip);
            BtnToggleEdgeGlowCompact.Style = _isEdgeGlowActive ? accentStyle : ghostStyle;
        }
        if (IconToggleEdgeGlowCompact != null)
        {
            IconToggleEdgeGlowCompact.Glyph = "\uE793";
            IconToggleEdgeGlowCompact.Opacity = _isEdgeGlowActive ? 1.0 : 0.70;
        }
    }

    /// <summary>
    /// Updates the Edge Light (Kenar Işığı) overlay palette from the active album artwork (or fallback preset).
    /// Smoothly cross-fades inside GlowOverlayWindow.
    /// </summary>
    internal void SyncEdgeGlowPalette(GlowPalette? artworkPalette)
    {
        var effectiveArtwork = artworkPalette ?? GlowPalette.Default;
        _lastArtworkGlowPalette = effectiveArtwork;

        var glowOverlayManager = App.Services?.GetService(typeof(GlowOverlayManager)) as GlowOverlayManager;
        if (glowOverlayManager == null) return;

        GlowPalette target = _settingsManager.Current.SyncWithAlbumArt
            ? effectiveArtwork
            : ResolveFallbackGlowPalette(_settingsManager.Current.FallbackPalettePresetIndex);

        glowOverlayManager.SetCustomPalette(target);
    }

    internal void SyncEdgeGlowPlaybackState(MediaPlaybackStatus status, bool hasSession)
    {
        var glowOverlayManager = App.Services?.GetService(typeof(GlowOverlayManager)) as GlowOverlayManager;
        glowOverlayManager?.UpdatePlaybackState(status, hasSession);
    }

    private static GlowPalette ResolveFallbackGlowPalette(int presetIndex) => presetIndex switch
    {
        1 => GlowPalette.Aurora,
        2 => GlowPalette.NeonSunset,
        3 => GlowPalette.Cyberpunk,
        _ => GlowPalette.Default
    };

    private void InitializeEdgeGlowThicknessClickFlyout()
    {
        SyncEdgeThicknessSliderFromSettings();

        _edgeThicknessSaveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
        _edgeThicknessSaveDebounceTimer.Tick += async (_, _) =>
        {
            _edgeThicknessSaveDebounceTimer?.Stop();
            await _settingsManager.SaveAsync(_settingsManager.Current);
        };

        if (BtnToggleEdgeGlow != null)
        {
            // v4.3: Click-only toggle (NO hover open); clicking again or outside closes the flyout
            BtnToggleEdgeGlow.Click += (_, _) => ToggleEdgeThicknessFlyout();

            // Keyboard accessibility: Up/Down opens thickness slider and focuses it
            BtnToggleEdgeGlow.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.Down)
                {
                    ShowEdgeThicknessFlyout();
                    SliderEdgeGlowThickness?.Focus(FocusState.Keyboard);
                    e.Handled = true;
                }
            };
        }

        if (BtnToggleEdgeGlowCompact != null)
        {
            // v4.3: Compact Mode opens the exact same Brightness + Thickness Edge Light panel as Normal Mode
            BtnToggleEdgeGlowCompact.Click += (_, _) => ToggleEdgeThicknessCompactFlyout();

            BtnToggleEdgeGlowCompact.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.Down)
                {
                    ShowEdgeThicknessCompactFlyout();
                    SliderEdgeGlowThickness?.Focus(FocusState.Keyboard);
                    e.Handled = true;
                }
            };
        }

        if (SliderEdgeGlowBrightness != null)
        {
            SliderEdgeGlowBrightness.ValueChanged += (_, e) =>
            {
                if (_isUpdatingEdgeThicknessSlider) return;
                int brightness = Math.Clamp((int)Math.Round(e.NewValue), 10, 100);
                ApplyEdgeGlowBrightnessChange(brightness, source: "normal");
            };
        }

        if (SliderEdgeGlowThickness != null)
        {
            SliderEdgeGlowThickness.ValueChanged += (_, e) =>
            {
                if (_isUpdatingEdgeThicknessSlider) return;
                int thickness = Math.Clamp((int)Math.Round(e.NewValue), 0, 64);
                ApplyEdgeGlowThicknessChange(thickness, source: "normal");
            };
        }
    }

    private void ApplyEdgeGlowThicknessChange(int thickness, string source)
    {
        thickness = Math.Clamp(thickness, 0, 64);
        _settingsManager.Current.GlowThickness = thickness;

        // Shared sync across Normal/Fullscreen/Compact Edge Light flyout and Settings Modal slider
        _isUpdatingEdgeThicknessSlider = true;
        try
        {
            if (source != "normal" && SliderEdgeGlowThickness != null) SliderEdgeGlowThickness.Value = thickness;
            if (TxtEdgeGlowThicknessHoverValue != null) TxtEdgeGlowThicknessHoverValue.Text = $"{thickness} px";
        }
        finally
        {
            _isUpdatingEdgeThicknessSlider = false;
        }

        if (source != "modal" && !_isUpdatingModalSettingsUi && SliderModalGlowThickness != null)
        {
            _isUpdatingModalSettingsUi = true;
            try
            {
                SliderModalGlowThickness.Value = thickness;
                if (TxtModalGlowThicknessValue != null) TxtModalGlowThicknessValue.Text = $"{thickness} px";
            }
            finally
            {
                _isUpdatingModalSettingsUi = false;
            }
        }

        // 0 px completely turns Edge Light OFF; 1-64 px ensures Edge Light is active with live thickness
        if (thickness == 0 && _isEdgeGlowActive)
        {
            _ = SetEdgeGlowStateAsync(false);
        }
        else if (thickness > 0 && !_isEdgeGlowActive)
        {
            _ = SetEdgeGlowStateAsync(true);
        }

        // Debounced persistence
        _edgeThicknessSaveDebounceTimer?.Stop();
        _edgeThicknessSaveDebounceTimer?.Start();
    }

    private void ApplyEdgeGlowBrightnessChange(int brightnessPct, string source)
    {
        brightnessPct = Math.Clamp(brightnessPct, 10, 100);
        _settingsManager.Current.GlowIntensity = brightnessPct;

        _isUpdatingEdgeThicknessSlider = true;
        try
        {
            if (source != "normal" && SliderEdgeGlowBrightness != null) SliderEdgeGlowBrightness.Value = brightnessPct;
            if (TxtEdgeGlowBrightnessPopupValue != null) TxtEdgeGlowBrightnessPopupValue.Text = $"{brightnessPct}%";
        }
        finally
        {
            _isUpdatingEdgeThicknessSlider = false;
        }

        if (source != "modal" && !_isUpdatingModalSettingsUi && SliderModalGlowIntensity != null)
        {
            _isUpdatingModalSettingsUi = true;
            try
            {
                SliderModalGlowIntensity.Value = brightnessPct;
                if (TxtModalGlowIntensityPercent != null) TxtModalGlowIntensityPercent.Text = $"{brightnessPct}%";
            }
            finally
            {
                _isUpdatingModalSettingsUi = false;
            }
        }

        _edgeThicknessSaveDebounceTimer?.Stop();
        _edgeThicknessSaveDebounceTimer?.Start();
    }

    private void SyncEdgeThicknessSliderFromSettings()
    {
        int thickness = Math.Clamp(_settingsManager.Current.GlowThickness, 0, 64);
        int brightness = Math.Clamp(_settingsManager.Current.GlowIntensity, 10, 100);
        _isUpdatingEdgeThicknessSlider = true;
        try
        {
            if (SliderEdgeGlowBrightness != null) SliderEdgeGlowBrightness.Value = brightness;
            if (TxtEdgeGlowBrightnessPopupValue != null) TxtEdgeGlowBrightnessPopupValue.Text = $"{brightness}%";
            if (SliderEdgeGlowThickness != null) SliderEdgeGlowThickness.Value = thickness;
            if (TxtEdgeGlowThicknessHoverValue != null) TxtEdgeGlowThicknessHoverValue.Text = $"{thickness} px";
        }
        finally
        {
            _isUpdatingEdgeThicknessSlider = false;
        }
    }

    private void ToggleEdgeThicknessFlyout()
    {
        if (BtnToggleEdgeGlow == null || FlyoutEdgeGlowThickness == null || _currentMode != LyricsWindowMode.Horizontal) return;
        try
        {
            if (FlyoutEdgeGlowThickness.IsOpen)
            {
                FlyoutEdgeGlowThickness.Hide();
            }
            else
            {
                ShowEdgeThicknessFlyout();
            }
        }
        catch { }
    }

    private void ShowEdgeThicknessFlyout()
    {
        if (BtnToggleEdgeGlow == null || FlyoutEdgeGlowThickness == null || _currentMode != LyricsWindowMode.Horizontal) return;
        SyncEdgeThicknessSliderFromSettings();
        try
        {
            if (!FlyoutEdgeGlowThickness.IsOpen)
            {
                FlyoutEdgeGlowThickness.ShowAt(BtnToggleEdgeGlow, new FlyoutShowOptions
                {
                    Placement = FlyoutPlacementMode.Top
                });
            }
        }
        catch { }
    }

    private void ToggleEdgeThicknessCompactFlyout()
    {
        if (BtnToggleEdgeGlowCompact == null || FlyoutEdgeGlowThickness == null || _currentMode != LyricsWindowMode.Compact) return;
        try
        {
            if (FlyoutEdgeGlowThickness.IsOpen)
            {
                FlyoutEdgeGlowThickness.Hide();
            }
            else
            {
                ShowEdgeThicknessCompactFlyout();
            }
        }
        catch { }
    }

    private void ShowEdgeThicknessCompactFlyout()
    {
        if (BtnToggleEdgeGlowCompact == null || FlyoutEdgeGlowThickness == null || _currentMode != LyricsWindowMode.Compact) return;
        SyncEdgeThicknessSliderFromSettings();
        try
        {
            if (!FlyoutEdgeGlowThickness.IsOpen)
            {
                FlyoutEdgeGlowThickness.ShowAt(BtnToggleEdgeGlowCompact, new FlyoutShowOptions
                {
                    Placement = FlyoutPlacementMode.TopEdgeAlignedRight
                });
            }
        }
        catch { }
    }

    /// <summary>
    /// Smoothly animates the Normal Mode window X-width (2x when showing lyrics, 0.5x when hiding lyrics)
    /// while strictly preserving Y position and Height and clamping within the current monitor work area.
    /// </summary>
    internal void AnimateLyricsToggleWindowWidth(bool isExpandingToShowLyrics)
    {
        if (_currentMode != LyricsWindowMode.Horizontal) return;
        if (AppWindow == null) return;

        // Do not alter window dimensions while maximized or fullscreen
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
        {
            if (overlapped.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized) return;
        }
        else if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
        {
            return;
        }

        long generation = Interlocked.Increment(ref _widthAnimationGeneration);
        _windowWidthAnimTimer?.Stop();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi > 0 ? (dpi / 96.0) : 1.0;

        // Allow single-panel minimum width when lyrics are hidden; restore dual-panel minimum when shown
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter ov)
        {
            int minLogicalW = isExpandingToShowLyrics ? 640 : LyricsWindowWidthAnimatorCalculator.DefaultMinSinglePanelLogicalWidth;
            ov.PreferredMinimumWidth = (int)Math.Round(minLogicalW * scale);
        }

        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        var currentBounds = new WindowRectBounds(pos.X, pos.Y, size.Width, size.Height);

        WindowRectBounds workArea;
        try
        {
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                AppWindow.Id,
                Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            var wa = displayArea.WorkArea;
            workArea = new WindowRectBounds(wa.X, wa.Y, wa.Width, wa.Height);
        }
        catch
        {
            workArea = new WindowRectBounds(0, 0, Math.Max(size.Width * 2, 1920), Math.Max(size.Height, 1080));
        }

        var calc = LyricsWindowWidthAnimatorCalculator.ComputeTargetBounds(
            currentBounds,
            workArea,
            isExpandingToShowLyrics,
            _recordedSinglePanelWidthPx,
            scale);

        _recordedSinglePanelWidthPx = calc.RecordedSinglePanelWidthPx;
        var targetBounds = calc.TargetBounds;

        if (!_settingsManager.Current.EnableAnimations || currentBounds.Width == targetBounds.Width)
        {
            try
            {
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                    targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height));
            }
            catch { }
            return;
        }

        var startTime = DateTime.UtcNow;
        const double durationMs = 210.0;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _windowWidthAnimTimer = timer;
        timer.Tick += (_, _) =>
        {
            if (Interlocked.Read(ref _widthAnimationGeneration) != generation)
            {
                timer.Stop();
                return;
            }

            double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            double t = Math.Clamp(elapsed / durationMs, 0.0, 1.0);
            var step = LyricsWindowWidthAnimatorCalculator.InterpolateBounds(currentBounds, targetBounds, t);

            try
            {
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(step.X, step.Y, step.Width, step.Height));
            }
            catch
            {
                timer.Stop();
                return;
            }

            if (t >= 1.0)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    private void WireModalTabButton(Button? btn, string category)
    {
        if (btn == null) return;
        btn.Click += (_, _) => ShowModalSettingsCategory(category);
    }

    private void OnSettingsModalPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (SettingsModalScrollViewer == null) return;
        var point = e.GetCurrentPoint(SettingsModalScrollViewer);
        var props = point.Properties;
        if (props.IsHorizontalMouseWheel || props.MouseWheelDelta == 0) return;

        double scrollable = SettingsModalScrollViewer.ScrollableHeight;
        if (scrollable > 0)
        {
            double delta = props.MouseWheelDelta * 0.85;
            double targetOffset = Math.Clamp(SettingsModalScrollViewer.VerticalOffset - delta, 0.0, scrollable);
            SettingsModalScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
        }
        e.Handled = true;
    }

    /// <summary>
    /// Toggles the glow/light effect on synchronized lyrics without altering Show/Hide Lyrics behavior.
    /// </summary>
    public void ToggleLyricsGlow()
    {
        SetLyricsGlowEnabled(!_isLyricsGlowEnabled, persist: true);
    }

    /// <summary>
    /// Sets the lyrics glow/light effect state, updates UI controls, and persists the preference.
    /// </summary>
    public void SetLyricsGlowEnabled(bool enabled, bool persist = true)
    {
        _isLyricsGlowEnabled = enabled;
        _settingsManager.Current.Lyrics.LyricsTextGlowEnabled = enabled;

        UpdateLyricsGlowVisualState();

        if (!_isUpdatingModalSettingsUi && SwitchModalLyricsGlow != null)
        {
            _isUpdatingModalSettingsUi = true;
            try { SwitchModalLyricsGlow.IsOn = enabled; }
            finally { _isUpdatingModalSettingsUi = false; }
        }

        if (persist)
        {
            _ = _settingsManager.SaveAsync(_settingsManager.Current);
        }
    }

    private void UpdateLyricsToggleVisualState()
    {
        var loc = LocalizationService.Instance;
        string tip = _isLyricsEnabled ? loc["Lyrics_HideLyrics"] : loc["Lyrics_ShowLyrics"];
        string glyph = _isLyricsEnabled ? "\uEC4F" : "\uE8D6";

        // Matches BtnToggleLyricsGlow exact visual logic: AccentButtonStyle when active, LyrowGhostButtonStyle when inactive
        var accentStyle = Application.Current.Resources.TryGetValue("AccentButtonStyle", out var acc) ? acc as Style : null;
        var ghostStyle = Application.Current.Resources.TryGetValue("LyrowGhostButtonStyle", out var gh) ? gh as Style : null;

        if (BtnToggleLyrics != null)
        {
            ToolTipService.SetToolTip(BtnToggleLyrics, tip);
            AutomationProperties.SetName(BtnToggleLyrics, tip);
            BtnToggleLyrics.Style = _isLyricsEnabled ? accentStyle : ghostStyle;
        }
        if (IconToggleLyrics != null)
        {
            IconToggleLyrics.Glyph = glyph;
            IconToggleLyrics.Opacity = _isLyricsEnabled ? 1.0 : 0.72;
        }
    }

    private void UpdateLyricsGlowVisualState()
    {
        var loc = LocalizationService.Instance;
        string tip = _isLyricsGlowEnabled ? loc["Lyrics_GlowOn"] : loc["Lyrics_GlowOff"];
        string glyph = _isLyricsGlowEnabled ? "\uE706" : "\uE708";

        // Active Glow button uses exact same AccentButtonStyle colors as Play/Pause; inactive uses Ghost/Neumorphism style
        var accentStyle = Application.Current.Resources.TryGetValue("AccentButtonStyle", out var acc) ? acc as Style : null;
        var ghostStyle = Application.Current.Resources.TryGetValue("LyrowGhostButtonStyle", out var gh) ? gh as Style : null;

        bool isCompact = _currentMode == LyricsWindowMode.Compact;
        bool shouldShowButton = _isLyricsEnabled && !isCompact;
        bool shouldRenderGlow = _isLyricsGlowEnabled && _isLyricsEnabled && !isCompact;

        if (BtnToggleLyricsGlow != null)
        {
            BtnToggleLyricsGlow.Visibility = shouldShowButton ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(BtnToggleLyricsGlow, tip);
            AutomationProperties.SetName(BtnToggleLyricsGlow, tip);
            BtnToggleLyricsGlow.Style = _isLyricsGlowEnabled ? accentStyle : ghostStyle;
        }
        if (IconToggleLyricsGlow != null)
        {
            IconToggleLyricsGlow.Glyph = glyph;
            IconToggleLyricsGlow.Opacity = _isLyricsGlowEnabled ? 1.0 : 0.72;
        }

        // Update active lyric line container highlight (only in Normal mode when glow is ON)
        for (int i = 0; i < _lineContainers.Count; i++)
        {
            _lineContainers[i].Background = (i == _currentActiveIndex && shouldRenderGlow)
                ? ActiveLyricGlowContainerBrush
                : TransparentBrush;
        }

        // Compact Mode strictly has NO Glow effect
        if (GridCompactLyrics != null)
        {
            GridCompactLyrics.Background = TransparentBrush;
        }

        if (LyricsGlowAuraLayer != null)
        {
            if (shouldRenderGlow)
            {
                LyricsGlowAuraLayer.Visibility = Visibility.Visible;
            }
            else
            {
                _currentLyricsGlowOpacity = 0.0;
                LyricsGlowAuraLayer.Opacity = 0.0;
                LyricsGlowAuraLayer.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void StepLyricsGlowAuraAnimation()
    {
        if (LyricsGlowAuraLayer == null) return;

        bool shouldShow = _isLyricsGlowEnabled && _isLyricsEnabled && _currentMode == LyricsWindowMode.Horizontal;
        if (!shouldShow)
        {
            if (LyricsGlowAuraLayer.Visibility != Visibility.Collapsed)
            {
                _currentLyricsGlowOpacity = 0.0;
                LyricsGlowAuraLayer.Opacity = 0.0;
                LyricsGlowAuraLayer.Visibility = Visibility.Collapsed;
            }
            return;
        }

        if (LyricsGlowAuraLayer.Visibility != Visibility.Visible)
        {
            LyricsGlowAuraLayer.Visibility = Visibility.Visible;
        }

        // Brighten dominant artwork tone gently for a clean, zero-allocation luminous aura
        byte r = (byte)Math.Min(255, _currentAmbientPalette.Dominant.R + 95);
        byte g = (byte)Math.Min(255, _currentAmbientPalette.Dominant.G + 95);
        byte b = (byte)Math.Min(255, _currentAmbientPalette.Dominant.B + 115);

        if (LyricsGlowStopCenter != null)
        {
            LyricsGlowStopCenter.Color = Windows.UI.Color.FromArgb(128, r, g, b);
        }
        if (LyricsGlowStopMid != null)
        {
            LyricsGlowStopMid.Color = Windows.UI.Color.FromArgb(46, r, g, b);
        }

        double targetOp = _currentActiveIndex >= 0 ? 0.36 : 0.20;
        if (Math.Abs(targetOp - _currentLyricsGlowOpacity) > 0.005)
        {
            _currentLyricsGlowOpacity += (targetOp - _currentLyricsGlowOpacity) * 0.12;
            LyricsGlowAuraLayer.Opacity = _currentLyricsGlowOpacity;
        }
    }

    /// <summary>
    /// Opens the centered modal settings dialog over the current window without altering window size or position.
    /// </summary>
    public void OpenSettingsModal()
    {
        if (SettingsModalOverlay == null || _isSettingsModalOpen) return;

        SyncModalSettingsFromModel();
        ApplyV4Localization();
        ShowModalSettingsCategory(_activeModalSettingsCategory);

        _isSettingsModalOpen = true;
        if (MainViewportArea != null) MainViewportArea.IsHitTestVisible = false;
        if (BtnOpenSettingsModal != null) BtnOpenSettingsModal.IsEnabled = false;
        SettingsModalOverlay.Visibility = Visibility.Visible;
        SettingsModalScrollViewer?.ChangeView(null, 0, null, disableAnimation: true);

        _activeModalStoryboard?.Stop();

        if (_settingsManager.Current.EnableAnimations)
        {
            var sb = new Storyboard();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = new Duration(TimeSpan.FromMilliseconds(180));

            var backdropFade = new DoubleAnimation { From = 0.0, To = 1.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(backdropFade, SettingsModalBackdrop);
            Storyboard.SetTargetProperty(backdropFade, "Opacity");

            var cardFade = new DoubleAnimation { From = 0.0, To = 1.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(cardFade, SettingsModalCard);
            Storyboard.SetTargetProperty(cardFade, "Opacity");

            var scaleX = new DoubleAnimation { From = 0.95, To = 1.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(scaleX, SettingsModalCardScale);
            Storyboard.SetTargetProperty(scaleX, "ScaleX");

            var scaleY = new DoubleAnimation { From = 0.95, To = 1.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(scaleY, SettingsModalCardScale);
            Storyboard.SetTargetProperty(scaleY, "ScaleY");

            sb.Children.Add(backdropFade);
            sb.Children.Add(cardFade);
            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            _activeModalStoryboard = sb;
            sb.Begin();
        }
        else
        {
            SettingsModalBackdrop.Opacity = 1.0;
            SettingsModalCard.Opacity = 1.0;
            SettingsModalCardScale.ScaleX = 1.0;
            SettingsModalCardScale.ScaleY = 1.0;
        }

        BtnCloseSettingsModal?.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Closes the centered modal settings dialog with a smooth exit animation and returns focus to the window.
    /// </summary>
    public void CloseSettingsModal()
    {
        if (SettingsModalOverlay == null || !_isSettingsModalOpen) return;

        if (IsHotkeyRecordingActive)
        {
            CancelActiveHotkeyRecording(restoreHotkeys: true, feedbackKey: null);
        }

        _isSettingsModalOpen = false;
        if (MainViewportArea != null) MainViewportArea.IsHitTestVisible = true;
        if (BtnOpenSettingsModal != null) BtnOpenSettingsModal.IsEnabled = true;
        _activeModalStoryboard?.Stop();

        if (_settingsManager.Current.EnableAnimations)
        {
            var sb = new Storyboard();
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = new Duration(TimeSpan.FromMilliseconds(140));

            var backdropFade = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(backdropFade, SettingsModalBackdrop);
            Storyboard.SetTargetProperty(backdropFade, "Opacity");

            var cardFade = new DoubleAnimation { To = 0.0, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(cardFade, SettingsModalCard);
            Storyboard.SetTargetProperty(cardFade, "Opacity");

            var scaleX = new DoubleAnimation { To = 0.96, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(scaleX, SettingsModalCardScale);
            Storyboard.SetTargetProperty(scaleX, "ScaleX");

            var scaleY = new DoubleAnimation { To = 0.96, Duration = dur, EasingFunction = ease };
            Storyboard.SetTarget(scaleY, SettingsModalCardScale);
            Storyboard.SetTargetProperty(scaleY, "ScaleY");

            sb.Children.Add(backdropFade);
            sb.Children.Add(cardFade);
            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            sb.Completed += (_, _) =>
            {
                if (!_isSettingsModalOpen && SettingsModalOverlay != null)
                {
                    SettingsModalOverlay.Visibility = Visibility.Collapsed;
                }
            };
            _activeModalStoryboard = sb;
            sb.Begin();
        }
        else
        {
            SettingsModalBackdrop.Opacity = 0.0;
            SettingsModalCard.Opacity = 0.0;
            SettingsModalOverlay.Visibility = Visibility.Collapsed;
        }

        BtnOpenSettingsModal?.Focus(FocusState.Programmatic);
    }

    private void ShowModalSettingsCategory(string category)
    {
        if (string.Equals(category, "Overview", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "Shortcuts", StringComparison.OrdinalIgnoreCase))
        {
            category = "Overview";
        }
        else if (string.Equals(category, "Appearance", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "Artwork", StringComparison.OrdinalIgnoreCase))
        {
            category = "Appearance";
        }
        else if (string.Equals(category, "Lighting", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(category, "Audio", StringComparison.OrdinalIgnoreCase))
        {
            category = "Lighting";
        }
        else if (string.Equals(category, "Lyrics", StringComparison.OrdinalIgnoreCase))
        {
            category = "Lyrics";
        }
        else if (string.Equals(category, "System", StringComparison.OrdinalIgnoreCase))
        {
            category = "System";
        }
        else if (string.Equals(category, "About", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(category, "Updates", StringComparison.OrdinalIgnoreCase))
        {
            category = "About";
        }
        else if (string.Equals(category, "General", StringComparison.OrdinalIgnoreCase))
        {
            category = "General";
        }
        else
        {
            category = "Overview";
        }
        _activeModalSettingsCategory = category;

        if (ModalSectionOverview != null) ModalSectionOverview.Visibility = category == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionGeneral != null) ModalSectionGeneral.Visibility = category == "General" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionAppearance != null) ModalSectionAppearance.Visibility = category == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionLighting != null) ModalSectionLighting.Visibility = category == "Lighting" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionLyrics != null) ModalSectionLyrics.Visibility = category == "Lyrics" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionSystem != null) ModalSectionSystem.Visibility = category == "System" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionAbout != null) ModalSectionAbout.Visibility = category == "About" ? Visibility.Visible : Visibility.Collapsed;

        bool isLight = IsEffectiveLightTheme();
        Brush selectedBg = new SolidColorBrush(isLight
            ? Windows.UI.Color.FromArgb(22, 15, 23, 42)     // Soft Fluent Light nav active pill
            : Windows.UI.Color.FromArgb(24, 255, 255, 255)); // Soft Fluent Dark nav active pill
        Brush primaryFg = new SolidColorBrush(isLight
            ? Windows.UI.Color.FromArgb(255, 15, 23, 42)    // Crisp dark ink (#0F172A) on Light
            : Windows.UI.Color.FromArgb(255, 248, 250, 252)); // Crisp white (#F8FAFC) on Dark
        Brush secondaryFg = new SolidColorBrush(isLight
            ? Windows.UI.Color.FromArgb(255, 71, 85, 105)   // Readable slate (#475569) on Light
            : Windows.UI.Color.FromArgb(255, 166, 173, 200)); // Readable silver (#A6ADC8) on Dark

        void SetNavState(Button? btn, Border? indicator, string tag)
        {
            bool isActive = string.Equals(category, tag, StringComparison.OrdinalIgnoreCase);
            if (indicator != null)
            {
                indicator.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            }
            if (btn != null)
            {
                btn.Background = isActive ? (selectedBg ?? TransparentBrush) : TransparentBrush;
                btn.Foreground = isActive ? primaryFg : secondaryFg;
                btn.FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            }
        }

        SetNavState(BtnModalTabOverview, NavIndicatorOverview, "Overview");
        SetNavState(BtnModalTabGeneral, NavIndicatorGeneral, "General");
        SetNavState(BtnModalTabAppearance, NavIndicatorAppearance, "Appearance");
        SetNavState(BtnModalTabLighting, NavIndicatorLighting, "Lighting");
        SetNavState(BtnModalTabLyrics, NavIndicatorLyrics, "Lyrics");
        SetNavState(BtnModalTabSystem, NavIndicatorSystem, "System");
        SetNavState(BtnModalTabAbout, NavIndicatorAbout, "About");

        UpdateSettingsModalResponsiveLayout(
            RootLayout?.ActualWidth ?? 960,
            RootLayout?.ActualHeight ?? 720);
        SettingsModalScrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
    }

    internal void UpdateSettingsModalResponsiveLayout(double windowWidth, double windowHeight = 720)
    {
        if (SettingsModalCard == null || ColSettingsSidebar == null) return;
        double availableW = Math.Max(320, windowWidth - 48);
        double availableH = Math.Max(340, windowHeight - 76);

        // True centered modal dialog physically separated from Windows title bar & caption buttons
        SettingsModalCard.Width = Math.Min(820, availableW);
        SettingsModalCard.Height = Math.Min(560, availableH);

        bool compactSidebar = availableW < 620;
        ColSettingsSidebar.Width = new GridLength(compactSidebar ? 54 : 204);
        var labelVis = compactSidebar ? Visibility.Collapsed : Visibility.Visible;

        if (TxtModalNavOverview != null) TxtModalNavOverview.Visibility = labelVis;
        if (TxtModalNavGeneral != null) TxtModalNavGeneral.Visibility = labelVis;
        if (TxtModalNavAppearance != null) TxtModalNavAppearance.Visibility = labelVis;
        if (TxtModalNavLighting != null) TxtModalNavLighting.Visibility = labelVis;
        if (TxtModalNavLyrics != null) TxtModalNavLyrics.Visibility = labelVis;
        if (TxtModalNavSystem != null) TxtModalNavSystem.Visibility = labelVis;
        if (TxtModalNavAbout != null) TxtModalNavAbout.Visibility = labelVis;
    }

    private void SyncModalSettingsFromModel()
    {
        _isUpdatingModalSettingsUi = true;
        try
        {
            var s = _settingsManager.Current;

            SelectComboByTag(ComboModalLanguage, s.Language ?? "en");
            SelectComboByTag(ComboModalTheme, s.Theme.ToString());
            SelectComboByTag(ComboModalFftSize, s.Glow.FftSize.ToString());
            SelectComboByTag(ComboOverviewMusicSensitivity, s.Glow.FftSize.ToString());
            SelectComboByTag(ComboModalUpdateFrequency, s.UpdateFrequency.ToString());
            SelectComboByTag(ComboModalPreferredLyricsSource, string.IsNullOrWhiteSpace(s.Lyrics.PreferredProvider) ? "Auto" : s.Lyrics.PreferredProvider);

            if (SwitchModalLyricsGlow != null) SwitchModalLyricsGlow.IsOn = s.Lyrics.LyricsTextGlowEnabled;
            if (SwitchModalEnableAnimations != null) SwitchModalEnableAnimations.IsOn = s.EnableAnimations;

            int opacityPct = Math.Clamp((int)Math.Round(s.MasterOpacity * 100.0), 50, 100);
            if (SliderModalMasterOpacity != null) SliderModalMasterOpacity.Value = opacityPct;
            if (TxtModalMasterOpacityPercent != null) TxtModalMasterOpacityPercent.Text = $"{opacityPct}%";

            int backdropPct = Math.Clamp(s.LyricsBackdropOpacity, 40, 100);
            if (SliderModalBackdropOpacity != null) SliderModalBackdropOpacity.Value = backdropPct;
            if (TxtModalBackdropOpacityPercent != null) TxtModalBackdropOpacityPercent.Text = $"{backdropPct}%";

            double focusedFont = Math.Clamp(s.FocusedLyricFontSize > 0 ? s.FocusedLyricFontSize : 28.0, 18.0, 42.0);
            if (SliderModalFocusedFontSize != null) SliderModalFocusedFontSize.Value = focusedFont;
            if (TxtModalFocusedFontValue != null) TxtModalFocusedFontValue.Text = $"{(int)Math.Round(focusedFont)} px";
            if (SliderOverviewFocusedFontSize != null) SliderOverviewFocusedFontSize.Value = focusedFont;
            if (TxtOverviewFocusedFontValue != null) TxtOverviewFocusedFontValue.Text = $"{(int)Math.Round(focusedFont)} px";

            double unfocusedFont = Math.Clamp(s.UnfocusedLyricFontSize > 0 ? s.UnfocusedLyricFontSize : 20.0, 14.0, 32.0);
            if (SliderModalUnfocusedFontSize != null) SliderModalUnfocusedFontSize.Value = unfocusedFont;
            if (TxtModalUnfocusedFontValue != null) TxtModalUnfocusedFontValue.Text = $"{(int)Math.Round(unfocusedFont)} px";
            if (SliderOverviewUnfocusedFontSize != null) SliderOverviewUnfocusedFontSize.Value = unfocusedFont;
            if (TxtOverviewUnfocusedFontValue != null) TxtOverviewUnfocusedFontValue.Text = $"{(int)Math.Round(unfocusedFont)} px";

            if (SwitchModalStartWithWindows != null) SwitchModalStartWithWindows.IsOn = s.StartWithWindows;
            if (SwitchModalSystemTray != null) SwitchModalSystemTray.IsOn = s.SystemTrayEnabled;
            if (SwitchModalMinimizeToTray != null) SwitchModalMinimizeToTray.IsOn = s.MinimizeToTray;
            if (SwitchModalNotifications != null) SwitchModalNotifications.IsOn = s.NotificationsEnabled;
            if (SwitchModalNotifyOnTrackChange != null) SwitchModalNotifyOnTrackChange.IsOn = s.NotifyOnTrackChange;

            if (SwitchModalArtworkDynamicBg != null) SwitchModalArtworkDynamicBg.IsOn = s.VisualIntegrationEnabled;
            int satPct = Math.Clamp((int)Math.Round(s.ArtworkBackgroundSaturation * 100.0), 10, 150);
            if (SliderModalArtworkSaturation != null) SliderModalArtworkSaturation.Value = satPct;
            if (TxtModalArtworkSaturationPercent != null) TxtModalArtworkSaturationPercent.Text = $"{satPct}%";

            int radius = Math.Clamp((int)Math.Round(s.ArtworkCornerRadius), 0, 64);
            if (SliderModalArtworkCornerRadius != null) SliderModalArtworkCornerRadius.Value = radius;
            if (TxtModalArtworkRadiusValue != null) TxtModalArtworkRadiusValue.Text = $"{radius} px";
            if (BorderTrackArtwork != null) BorderTrackArtwork.CornerRadius = new CornerRadius(radius);

            if (SwitchModalAmbientEdgeGlow != null) SwitchModalAmbientEdgeGlow.IsOn = _isEdgeGlowActive;
            if (SliderModalGlowIntensity != null) SliderModalGlowIntensity.Value = Math.Clamp(s.GlowIntensity, 10, 100);
            if (TxtModalGlowIntensityPercent != null) TxtModalGlowIntensityPercent.Text = $"{Math.Clamp(s.GlowIntensity, 10, 100)}%";
            if (SliderModalGlowThickness != null) SliderModalGlowThickness.Value = Math.Clamp(s.GlowThickness, 0, 64);
            if (TxtModalGlowThicknessValue != null) TxtModalGlowThicknessValue.Text = $"{Math.Clamp(s.GlowThickness, 0, 64)} px";
            if (SwitchModalSyncWithAlbumArt != null) SwitchModalSyncWithAlbumArt.IsOn = s.SyncWithAlbumArt;

            if (SwitchModalAutoUpdate != null) SwitchModalAutoUpdate.IsOn = s.AutoUpdateEnabled;

            _glowHotkeyRecorder.SetCommittedGesture(s.Hotkeys.ToggleGlow);
            _lyricsHotkeyRecorder.SetCommittedGesture(s.Hotkeys.ToggleLyrics);
            RenderKeystrokeRecorderVisual(TxtModalHotkeyGlow, PanelHotkeyGlowTokens, _glowHotkeyRecorder);
            RenderKeystrokeRecorderVisual(TxtModalHotkeyLyrics, PanelHotkeyLyricsTokens, _lyricsHotkeyRecorder);
        }
        finally
        {
            _isUpdatingModalSettingsUi = false;
        }
    }

    private static void SelectComboByTag(ComboBox? combo, string tag)
    {
        if (combo == null) return;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private void ApplyHotkeysFromSettings()
    {
        var hotkeyManager = App.Services?.GetService(typeof(IGlobalHotkeyManager)) as IGlobalHotkeyManager;
        if (hotkeyManager == null) return;

        var hk = _settingsManager.Current.Hotkeys;
        if (string.IsNullOrWhiteSpace(hk.ToggleGlow) && string.IsNullOrWhiteSpace(hk.ToggleLyrics))
        {
            hotkeyManager.UnregisterAll();
        }
        else
        {
            hotkeyManager.RegisterHotkeys(
                hk,
                onGlowToggle: ToggleEdgeGlowFromExternal,
                onLyricsToggle: ToggleLyricsPanelFromExternal,
                onClickThroughToggle: () => { });
        }
    }

    private void WireModalSettingsControls()
    {
        if (ComboModalLanguage != null)
        {
            ComboModalLanguage.SelectionChanged += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                if (ComboModalLanguage.SelectedItem is ComboBoxItem item && item.Tag is string lang)
                {
                    _settingsManager.Current.Language = lang;
                    LocalizationService.Instance.SetLanguage(lang);
                    await _settingsManager.SaveAsync(_settingsManager.Current);
                }
            };
        }

        if (ComboModalTheme != null)
        {
            ComboModalTheme.SelectionChanged += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                if (ComboModalTheme.SelectedItem is ComboBoxItem item &&
                    Enum.TryParse<ThemeMode>(item.Tag?.ToString(), out var mode))
                {
                    _settingsManager.Current.Theme = mode;
                    ApplyTheme(mode);
                    await _settingsManager.SaveAsync(_settingsManager.Current);
                }
            };
        }

        if (SwitchModalLyricsGlow != null)
        {
            SwitchModalLyricsGlow.Toggled += (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                SetLyricsGlowEnabled(SwitchModalLyricsGlow.IsOn, persist: true);
            };
        }

        if (ComboOverviewMusicSensitivity != null)
        {
            ComboOverviewMusicSensitivity.SelectionChanged += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                if (ComboOverviewMusicSensitivity.SelectedItem is ComboBoxItem item &&
                    int.TryParse(item.Tag?.ToString(), out int fft))
                {
                    _settingsManager.Current.Glow.FftSize = fft;
                    _isUpdatingModalSettingsUi = true;
                    try { SelectComboByTag(ComboModalFftSize, fft.ToString()); }
                    finally { _isUpdatingModalSettingsUi = false; }
                    await _settingsManager.SaveAsync(_settingsManager.Current);
                }
            };
        }

        if (SliderOverviewFocusedFontSize != null)
        {
            SliderOverviewFocusedFontSize.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                double val = Math.Clamp(Math.Round(e.NewValue), 18.0, 42.0);
                if (TxtOverviewFocusedFontValue != null) TxtOverviewFocusedFontValue.Text = $"{(int)val} px";
                _isUpdatingModalSettingsUi = true;
                try
                {
                    if (SliderModalFocusedFontSize != null) SliderModalFocusedFontSize.Value = val;
                    if (TxtModalFocusedFontValue != null) TxtModalFocusedFontValue.Text = $"{(int)val} px";
                }
                finally { _isUpdatingModalSettingsUi = false; }
                _settingsManager.Current.FocusedLyricFontSize = val;
                ApplyLyricsPersonalization();
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SliderOverviewUnfocusedFontSize != null)
        {
            SliderOverviewUnfocusedFontSize.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                double val = Math.Clamp(Math.Round(e.NewValue), 14.0, 32.0);
                if (TxtOverviewUnfocusedFontValue != null) TxtOverviewUnfocusedFontValue.Text = $"{(int)val} px";
                _isUpdatingModalSettingsUi = true;
                try
                {
                    if (SliderModalUnfocusedFontSize != null) SliderModalUnfocusedFontSize.Value = val;
                    if (TxtModalUnfocusedFontValue != null) TxtModalUnfocusedFontValue.Text = $"{(int)val} px";
                }
                finally { _isUpdatingModalSettingsUi = false; }
                _settingsManager.Current.UnfocusedLyricFontSize = val;
                ApplyLyricsPersonalization();
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (BtnOverviewCheckUpdates != null)
        {
            BtnOverviewCheckUpdates.Click += async (_, _) =>
            {
                if (_updateCoordinator != null)
                {
                    await _updateCoordinator.CheckForUpdatesAsync(isManual: true);
                }
            };
        }

        if (SwitchModalEnableAnimations != null)
        {
            SwitchModalEnableAnimations.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.EnableAnimations = SwitchModalEnableAnimations.IsOn;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SliderModalMasterOpacity != null)
        {
            SliderModalMasterOpacity.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                int pct = Math.Clamp((int)Math.Round(e.NewValue), 50, 100);
                if (TxtModalMasterOpacityPercent != null) TxtModalMasterOpacityPercent.Text = $"{pct}%";
                _settingsManager.Current.MasterOpacity = pct / 100.0;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SliderModalBackdropOpacity != null)
        {
            SliderModalBackdropOpacity.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                int val = Math.Clamp((int)Math.Round(e.NewValue), 40, 100);
                if (TxtModalBackdropOpacityPercent != null) TxtModalBackdropOpacityPercent.Text = $"{val}%";
                _settingsManager.Current.LyricsBackdropOpacity = val;
                _settingsManager.Current.Lyrics.BackdropOpacity = val;
                UpdateBackdropOpacity(val);
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SliderModalFocusedFontSize != null)
        {
            SliderModalFocusedFontSize.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                double val = Math.Clamp(Math.Round(e.NewValue), 18.0, 42.0);
                if (TxtModalFocusedFontValue != null) TxtModalFocusedFontValue.Text = $"{(int)val} px";
                _isUpdatingModalSettingsUi = true;
                try
                {
                    if (SliderOverviewFocusedFontSize != null) SliderOverviewFocusedFontSize.Value = val;
                    if (TxtOverviewFocusedFontValue != null) TxtOverviewFocusedFontValue.Text = $"{(int)val} px";
                }
                finally { _isUpdatingModalSettingsUi = false; }
                _settingsManager.Current.FocusedLyricFontSize = val;
                ApplyLyricsPersonalization();
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SliderModalUnfocusedFontSize != null)
        {
            SliderModalUnfocusedFontSize.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                double val = Math.Clamp(Math.Round(e.NewValue), 14.0, 32.0);
                if (TxtModalUnfocusedFontValue != null) TxtModalUnfocusedFontValue.Text = $"{(int)val} px";
                _isUpdatingModalSettingsUi = true;
                try
                {
                    if (SliderOverviewUnfocusedFontSize != null) SliderOverviewUnfocusedFontSize.Value = val;
                    if (TxtOverviewUnfocusedFontValue != null) TxtOverviewUnfocusedFontValue.Text = $"{(int)val} px";
                }
                finally { _isUpdatingModalSettingsUi = false; }
                _settingsManager.Current.UnfocusedLyricFontSize = val;
                ApplyLyricsPersonalization();
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SwitchModalStartWithWindows != null)
        {
            SwitchModalStartWithWindows.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.StartWithWindows = SwitchModalStartWithWindows.IsOn;
                var startup = App.Services?.GetService(typeof(IWindowsStartupManager)) as IWindowsStartupManager;
                startup?.SetStartupEnabled(SwitchModalStartWithWindows.IsOn);
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SwitchModalSystemTray != null)
        {
            SwitchModalSystemTray.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.SystemTrayEnabled = SwitchModalSystemTray.IsOn;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SwitchModalMinimizeToTray != null)
        {
            SwitchModalMinimizeToTray.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.MinimizeToTray = SwitchModalMinimizeToTray.IsOn;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SwitchModalNotifications != null)
        {
            SwitchModalNotifications.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.NotificationsEnabled = SwitchModalNotifications.IsOn;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SwitchModalNotifyOnTrackChange != null)
        {
            SwitchModalNotifyOnTrackChange.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.NotifyOnTrackChange = SwitchModalNotifyOnTrackChange.IsOn;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SwitchModalArtworkDynamicBg != null)
        {
            SwitchModalArtworkDynamicBg.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.VisualIntegrationEnabled = SwitchModalArtworkDynamicBg.IsOn;
                if (AmbientBackgroundLayer != null)
                {
                    AmbientBackgroundLayer.Visibility = SwitchModalArtworkDynamicBg.IsOn ? Visibility.Visible : Visibility.Collapsed;
                }
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SliderModalArtworkSaturation != null)
        {
            SliderModalArtworkSaturation.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                int pct = Math.Clamp((int)Math.Round(e.NewValue), 10, 150);
                if (TxtModalArtworkSaturationPercent != null) TxtModalArtworkSaturationPercent.Text = $"{pct}%";
                _settingsManager.Current.ArtworkBackgroundSaturation = pct / 100.0;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SliderModalArtworkCornerRadius != null)
        {
            SliderModalArtworkCornerRadius.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                int r = Math.Clamp((int)Math.Round(e.NewValue), 0, 64);
                if (TxtModalArtworkRadiusValue != null) TxtModalArtworkRadiusValue.Text = $"{r} px";
                _settingsManager.Current.ArtworkCornerRadius = r;
                if (BorderTrackArtwork != null) BorderTrackArtwork.CornerRadius = new CornerRadius(r);
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SwitchModalAmbientEdgeGlow != null)
        {
            SwitchModalAmbientEdgeGlow.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                await SetEdgeGlowStateAsync(SwitchModalAmbientEdgeGlow.IsOn);
            };
        }

        if (SliderModalGlowIntensity != null)
        {
            SliderModalGlowIntensity.ValueChanged += (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                int val = Math.Clamp((int)Math.Round(e.NewValue), 10, 100);
                if (TxtModalGlowIntensityPercent != null) TxtModalGlowIntensityPercent.Text = $"{val}%";
                ApplyEdgeGlowBrightnessChange(val, source: "modal");
            };
        }

        if (SliderModalGlowThickness != null)
        {
            SliderModalGlowThickness.ValueChanged += (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                int val = Math.Clamp((int)Math.Round(e.NewValue), 0, 64);
                if (TxtModalGlowThicknessValue != null) TxtModalGlowThicknessValue.Text = $"{val} px";
                ApplyEdgeGlowThicknessChange(val, source: "modal");
            };
        }

        if (SwitchModalSyncWithAlbumArt != null)
        {
            SwitchModalSyncWithAlbumArt.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.SyncWithAlbumArt = SwitchModalSyncWithAlbumArt.IsOn;
                SyncEdgeGlowPalette(_lastArtworkGlowPalette);
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (ComboModalFftSize != null)
        {
            ComboModalFftSize.SelectionChanged += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                if (ComboModalFftSize.SelectedItem is ComboBoxItem item &&
                    int.TryParse(item.Tag?.ToString(), out int fft))
                {
                    _settingsManager.Current.Glow.FftSize = fft;
                    _isUpdatingModalSettingsUi = true;
                    try { SelectComboByTag(ComboOverviewMusicSensitivity, fft.ToString()); }
                    finally { _isUpdatingModalSettingsUi = false; }
                    await _settingsManager.SaveAsync(_settingsManager.Current);
                }
            };
        }

        if (SwitchModalAutoUpdate != null)
        {
            SwitchModalAutoUpdate.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.AutoUpdateEnabled = SwitchModalAutoUpdate.IsOn;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (ComboModalUpdateFrequency != null)
        {
            ComboModalUpdateFrequency.SelectionChanged += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                if (ComboModalUpdateFrequency.SelectedItem is ComboBoxItem item &&
                    Enum.TryParse<UpdateCheckFrequency>(item.Tag?.ToString(), out var freq))
                {
                    _settingsManager.Current.UpdateFrequency = freq;
                    await _settingsManager.SaveAsync(_settingsManager.Current);
                }
            };
        }

        if (BtnModalCheckUpdates != null)
        {
            BtnModalCheckUpdates.Click += async (_, _) =>
            {
                if (_updateCoordinator != null)
                {
                    await _updateCoordinator.CheckForUpdatesAsync(isManual: true);
                }
            };
        }

        if (BtnModalInstallUpdate != null)
        {
            BtnModalInstallUpdate.Click += async (_, _) =>
            {
                await StartSharedUpdateInstallAsync();
            };
        }

        if (BtnModalRetryUpdate != null)
        {
            BtnModalRetryUpdate.Click += async (_, _) =>
            {
                if (_updateCoordinator == null) return;
                if (_updateCoordinator.CurrentState.HasAvailableUpdate)
                {
                    await StartSharedUpdateInstallAsync();
                }
                else
                {
                    await _updateCoordinator.CheckForUpdatesAsync(isManual: true);
                }
            };
        }

        if (BtnModalViewRelease != null)
        {
            BtnModalViewRelease.Click += (_, _) =>
            {
                string url = _updateCoordinator?.CurrentState.LastResult?.ReleaseUrl
                    ?? GitHubUpdateService.DefaultReleasesPageUrl;
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch { }
            };
        }

        if (ComboModalPreferredLyricsSource != null)
        {
            ComboModalPreferredLyricsSource.SelectionChanged += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                if (ComboModalPreferredLyricsSource.SelectedItem is ComboBoxItem item && item.Tag is string providerTag)
                {
                    _settingsManager.Current.Lyrics.PreferredProvider = string.IsNullOrWhiteSpace(providerTag) ? "Auto" : providerTag;
                    await _settingsManager.SaveAsync(_settingsManager.Current);
                }
            };
        }

        PopulateStaticLyricsSourcesList();

        InitializeKeystrokeRecorderControl(TxtModalHotkeyGlow, PanelHotkeyGlowTokens, _glowHotkeyRecorder);
        InitializeKeystrokeRecorderControl(TxtModalHotkeyLyrics, PanelHotkeyLyricsTokens, _lyricsHotkeyRecorder);
        this.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && IsHotkeyRecordingActive)
            {
                CancelActiveHotkeyRecording(restoreHotkeys: true, feedbackKey: "Hotkey_Cancelled");
            }
        };

        if (BtnModalClearHotkeys != null)
        {
            BtnModalClearHotkeys.Click += async (_, _) =>
            {
                if (IsHotkeyRecordingActive)
                {
                    CancelActiveHotkeyRecording(restoreHotkeys: false, feedbackKey: null);
                }
                _glowHotkeyRecorder.SetCommittedGesture(string.Empty);
                _lyricsHotkeyRecorder.SetCommittedGesture(string.Empty);
                RenderKeystrokeRecorderVisual(TxtModalHotkeyGlow, PanelHotkeyGlowTokens, _glowHotkeyRecorder);
                RenderKeystrokeRecorderVisual(TxtModalHotkeyLyrics, PanelHotkeyLyricsTokens, _lyricsHotkeyRecorder);
                _settingsManager.Current.Hotkeys.ToggleGlow = string.Empty;
                _settingsManager.Current.Hotkeys.ToggleLyrics = string.Empty;
                await _settingsManager.SaveAsync(_settingsManager.Current);
                ApplyHotkeysFromSettings();
                ShowHotkeyFeedback(LocalizationService.Instance["Hotkey_Cleared"], isError: false);
            };
        }

        if (BtnModalSaveHotkeys != null)
        {
            BtnModalSaveHotkeys.Click += async (_, _) =>
            {
                await SaveAndApplyHotkeysAsync();
            };
        }

        if (BtnModalShowWelcomeAbout != null)
        {
            BtnModalShowWelcomeAbout.Click += (_, _) => OpenWelcomeScreenFromSettings();
        }

        if (BtnModalResetSettings != null)
        {
            BtnModalResetSettings.Click += async (_, _) =>
            {
                var defaults = LyrowSettings.CreateDefault();
                defaults.HasCompletedWelcome = true;
                defaults.HasCompletedFirstRun = true;
                await _settingsManager.SaveAsync(defaults);
                await _settingsManager.LoadAsync();
                _isLyricsGlowEnabled = _settingsManager.Current.Lyrics.LyricsTextGlowEnabled;
                ApplyTheme(_settingsManager.Current.Theme);
                LocalizationService.Instance.SetLanguage(_settingsManager.Current.Language);
                UpdateLyricsGlowVisualState();
                UpdateEdgeGlowVisualState();
                SyncModalSettingsFromModel();
                _toastManager?.ShowToast(
                    LocalizationService.Instance["Settings_ResetDefaults"],
                    title: LocalizationService.Instance["Settings_Title"],
                    severity: InfoBarSeverity.Informational);
            };
        }

        if (BtnModalOpenLogs != null)
        {
            BtnModalOpenLogs.Click += (_, _) =>
            {
                try
                {
                    string logPath = LocalDiagnosticLogger.LogFilePath;
                    if (File.Exists(logPath))
                    {
                        Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
                    }
                }
                catch { }
            };
        }
    }

    private void ApplyV4Localization()
    {
        var loc = LocalizationService.Instance;

        if (BtnOpenSettingsModal != null)
        {
            ToolTipService.SetToolTip(BtnOpenSettingsModal, loc["Lyrics_OpenSettings"]);
            AutomationProperties.SetName(BtnOpenSettingsModal, loc["Lyrics_OpenSettings"]);
        }

        if (BtnCloseSettingsModal != null)
        {
            ToolTipService.SetToolTip(BtnCloseSettingsModal, loc["Modal_Close"]);
            AutomationProperties.SetName(BtnCloseSettingsModal, loc["Modal_Close"]);
        }

        if (TxtSettingsModalTitle != null) TxtSettingsModalTitle.Text = loc["Settings_Title"];

        bool isTr = string.Equals(loc.CurrentLanguage, "tr", StringComparison.OrdinalIgnoreCase);
        string tabOverview = loc["Tab_Overview"];
        string tabGeneral = isTr ? "Genel" : "General";
        string tabAppearance = isTr ? "Görünüm" : "Appearance";
        string tabLighting = isTr ? "Işık" : "Lighting";
        string tabLyrics = isTr ? "Şarkı Sözleri" : "Lyrics";
        string tabSystem = isTr ? "Sistem" : "System";
        string tabUpdates = isTr ? "Güncellemeler" : "Updates";
        string tabAbout = isTr ? "Hakkında" : "About";

        if (TxtModalNavOverview != null) TxtModalNavOverview.Text = tabOverview;
        if (TxtModalNavGeneral != null) TxtModalNavGeneral.Text = tabGeneral;
        if (TxtModalNavAppearance != null) TxtModalNavAppearance.Text = tabAppearance;
        if (TxtModalNavLighting != null) TxtModalNavLighting.Text = tabLighting;
        if (TxtModalNavLyrics != null) TxtModalNavLyrics.Text = tabLyrics;
        if (TxtModalNavSystem != null) TxtModalNavSystem.Text = tabSystem;
        if (TxtModalNavAbout != null) TxtModalNavAbout.Text = tabAbout;

        if (TxtModalSectionOverview != null) TxtModalSectionOverview.Text = tabOverview;
        if (TxtModalSectionGeneral != null) TxtModalSectionGeneral.Text = tabGeneral;
        if (TxtModalSectionAppearance != null) TxtModalSectionAppearance.Text = tabAppearance;
        if (TxtModalSectionLighting != null) TxtModalSectionLighting.Text = tabLighting;
        if (TxtModalSectionLyrics != null) TxtModalSectionLyrics.Text = tabLyrics;
        if (TxtModalSectionSystem != null) TxtModalSectionSystem.Text = tabSystem;
        if (TxtModalSectionUpdates != null) TxtModalSectionUpdates.Text = tabUpdates;
        if (TxtModalSectionAbout != null) TxtModalSectionAbout.Text = tabAbout;
        if (TxtModalAboutLicense != null) TxtModalAboutLicense.Text = isTr ? "MIT Lisansı — Açık Kaynak" : "MIT License — Open Source";

        if (BtnModalTabOverview != null) { ToolTipService.SetToolTip(BtnModalTabOverview, tabOverview); AutomationProperties.SetName(BtnModalTabOverview, tabOverview); }
        if (BtnModalTabGeneral != null) { ToolTipService.SetToolTip(BtnModalTabGeneral, tabGeneral); AutomationProperties.SetName(BtnModalTabGeneral, tabGeneral); }
        if (BtnModalTabAppearance != null) { ToolTipService.SetToolTip(BtnModalTabAppearance, tabAppearance); AutomationProperties.SetName(BtnModalTabAppearance, tabAppearance); }
        if (BtnModalTabLighting != null) { ToolTipService.SetToolTip(BtnModalTabLighting, tabLighting); AutomationProperties.SetName(BtnModalTabLighting, tabLighting); }
        if (BtnModalTabLyrics != null) { ToolTipService.SetToolTip(BtnModalTabLyrics, tabLyrics); AutomationProperties.SetName(BtnModalTabLyrics, tabLyrics); }
        if (BtnModalTabSystem != null) { ToolTipService.SetToolTip(BtnModalTabSystem, tabSystem); AutomationProperties.SetName(BtnModalTabSystem, tabSystem); }
        if (BtnModalTabAbout != null) { ToolTipService.SetToolTip(BtnModalTabAbout, tabAbout); AutomationProperties.SetName(BtnModalTabAbout, tabAbout); }
        if (TxtModalLanguageLabel != null) TxtModalLanguageLabel.Text = loc["Settings_Language"];
        if (TxtModalLanguageDesc != null) TxtModalLanguageDesc.Text = loc["Settings_LanguageDesc"];
        if (TxtModalThemeLabel != null) TxtModalThemeLabel.Text = loc["Settings_Theme"];
        if (TxtModalThemeDesc != null) TxtModalThemeDesc.Text = loc["Settings_ThemeDesc"];
        if (TxtModalLyricsGlowLabel != null) TxtModalLyricsGlowLabel.Text = loc["Settings_LyricsGlowLabel"];
        if (TxtModalLyricsGlowDesc != null) TxtModalLyricsGlowDesc.Text = loc["Settings_LyricsGlowDesc"];
        if (TxtOverviewSensitivityLabel != null) TxtOverviewSensitivityLabel.Text = loc["Settings_MusicSensitivity"];
        if (TxtOverviewSensitivityDesc != null) TxtOverviewSensitivityDesc.Text = loc["Settings_MusicSensitivityDesc"];
        if (CbiOverviewSensLow != null) CbiOverviewSensLow.Content = loc["WelcomeV4_Sens_Calm"];
        if (CbiOverviewSensBalanced != null) CbiOverviewSensBalanced.Content = loc["WelcomeV4_Sens_Balanced"];
        if (CbiOverviewSensHigh != null) CbiOverviewSensHigh.Content = loc["WelcomeV4_Sens_Expressive"];
        if (CbiLightingSensLow != null) CbiLightingSensLow.Content = loc["WelcomeV4_Sens_Calm"];
        if (CbiLightingSensBalanced != null) CbiLightingSensBalanced.Content = loc["WelcomeV4_Sens_Balanced"];
        if (CbiLightingSensHigh != null) CbiLightingSensHigh.Content = loc["WelcomeV4_Sens_Expressive"];
        if (TxtOverviewFocusedFontLabel != null) TxtOverviewFocusedFontLabel.Text = loc["Settings_FocusedFontSize"];
        if (TxtOverviewUnfocusedFontLabel != null) TxtOverviewUnfocusedFontLabel.Text = loc["Settings_UnfocusedFontSize"];
        if (BtnOverviewCheckUpdates != null) BtnOverviewCheckUpdates.Content = loc["Settings_CheckUpdates"];
        if (TxtModalLyricsSourcesLabel != null) TxtModalLyricsSourcesLabel.Text = loc["Settings_LyricsSources"];
        if (TxtModalLyricsSourcesDesc != null) TxtModalLyricsSourcesDesc.Text = loc["Settings_LyricsSourcesDesc"];
        if (TxtModalLyricsSourcesDragHint != null) TxtModalLyricsSourcesDragHint.Text = loc["Settings_LyricsSourcesDragHint"];
        if (TxtModalPreferredSourceLabel != null) TxtModalPreferredSourceLabel.Text = loc["Settings_LyricsPreferredSource"];
        if (CbiLyricsSourceAuto != null) CbiLyricsSourceAuto.Content = loc["Settings_LyricsPreferredAuto"];
        PopulateStaticLyricsSourcesList();
        if (TxtModalAnimationsLabel != null) TxtModalAnimationsLabel.Text = loc["Settings_Animations"];
        if (TxtModalAnimationsDesc != null) TxtModalAnimationsDesc.Text = loc["Settings_AnimationsDesc"];
        if (TxtModalMasterOpacityLabel != null) TxtModalMasterOpacityLabel.Text = loc["Settings_MasterOpacity"];
        if (TxtModalBackdropOpacityLabel != null) TxtModalBackdropOpacityLabel.Text = loc["Settings_BackdropOpacity"];
        if (TxtModalFocusedFontLabel != null) TxtModalFocusedFontLabel.Text = loc["Settings_FocusedFontSize"];
        if (TxtModalUnfocusedFontLabel != null) TxtModalUnfocusedFontLabel.Text = loc["Settings_UnfocusedFontSize"];
        if (TxtModalStartupLabel != null) TxtModalStartupLabel.Text = loc["Settings_Startup"];
        if (TxtModalStartupDesc != null) TxtModalStartupDesc.Text = loc["Settings_StartupDesc"];
        if (TxtModalSystemTrayLabel != null) TxtModalSystemTrayLabel.Text = loc["Settings_SystemTray"];
        if (TxtModalSystemTrayDesc != null) TxtModalSystemTrayDesc.Text = loc["Settings_SystemTrayDesc"];
        if (TxtModalMinimizeToTrayLabel != null) TxtModalMinimizeToTrayLabel.Text = loc["Settings_MinimizeToTrayLabel"];
        if (TxtModalMinimizeToTrayDesc != null) TxtModalMinimizeToTrayDesc.Text = loc["Settings_MinimizeToTrayDesc"];
        if (TxtModalNotificationsLabel != null) TxtModalNotificationsLabel.Text = loc["Settings_Notifications"];
        if (TxtModalNotificationsDesc != null) TxtModalNotificationsDesc.Text = loc["Settings_NotificationsDesc"];
        if (TxtModalNotifyOnTrackChangeLabel != null) TxtModalNotifyOnTrackChangeLabel.Text = loc["Settings_NotifyTrackChange"];
        if (TxtModalNotifyOnTrackChangeDesc != null) TxtModalNotifyOnTrackChangeDesc.Text = loc["Settings_NotifyTrackChangeDesc"];

        if (TxtModalArtworkDynamicBgLabel != null) TxtModalArtworkDynamicBgLabel.Text = loc["Settings_ArtworkDynamicBg"];
        if (TxtModalArtworkDynamicBgDesc != null) TxtModalArtworkDynamicBgDesc.Text = loc["Settings_ArtworkDynamicBgDesc"];
        if (TxtModalArtworkSaturationLabel != null) TxtModalArtworkSaturationLabel.Text = loc["Settings_ArtworkSaturation"];
        if (TxtModalArtworkRadiusLabel != null) TxtModalArtworkRadiusLabel.Text = loc["Settings_ArtworkRadius"];

        if (TxtModalAmbientGlowLabel != null) TxtModalAmbientGlowLabel.Text = loc["Settings_AmbientGlowLabel"];
        if (TxtModalAmbientGlowDesc != null) TxtModalAmbientGlowDesc.Text = loc["Settings_AmbientGlowDesc"];
        if (TxtModalGlowIntensityLabel != null) TxtModalGlowIntensityLabel.Text = loc["Settings_GlowIntensity"];
        if (TxtModalGlowThicknessLabel != null) TxtModalGlowThicknessLabel.Text = loc["Settings_GlowThickness"];
        if (TxtEdgeGlowBrightnessPopupLabel != null) TxtEdgeGlowBrightnessPopupLabel.Text = loc["Settings_GlowIntensity"];
        if (TxtEdgeGlowThicknessPopupLabel != null) TxtEdgeGlowThicknessPopupLabel.Text = loc["Settings_GlowThickness"];
        if (TxtModalSyncAlbumArtLabel != null) TxtModalSyncAlbumArtLabel.Text = loc["Settings_SyncWithAlbumArt"];
        if (TxtModalFftSizeLabel != null) TxtModalFftSizeLabel.Text = loc["Settings_FftSize"];
        if (TxtModalFftSizeDesc != null) TxtModalFftSizeDesc.Text = loc["Settings_FftSizeDesc"];

        if (TxtModalSectionUpdates != null) TxtModalSectionUpdates.Text = loc["Section_Updates"];
        if (TxtModalAutoUpdatesLabel != null) TxtModalAutoUpdatesLabel.Text = loc["Settings_AutoUpdates"];
        if (TxtModalAutoUpdatesDesc != null) TxtModalAutoUpdatesDesc.Text = loc["Settings_AutoUpdatesDesc"];
        if (TxtModalUpdateFreqLabel != null) TxtModalUpdateFreqLabel.Text = loc["Settings_UpdateFreq"];
        if (BtnModalCheckUpdates != null) BtnModalCheckUpdates.Content = loc["Settings_CheckUpdates"];
        if (BtnModalInstallUpdate != null) BtnModalInstallUpdate.Content = loc["Updates_InstallNewVersion"];
        if (BtnModalRetryUpdate != null) BtnModalRetryUpdate.Content = loc["Updates_Retry"];
        if (BtnModalViewRelease != null) BtnModalViewRelease.Content = loc["Updates_ViewRelease"];

        if (TxtModalSectionShortcuts != null) TxtModalSectionShortcuts.Text = loc["Section_Shortcuts"];
        if (TxtModalShortcutGlowLabel != null) TxtModalShortcutGlowLabel.Text = loc["Settings_ShortcutGlow"];
        RenderKeystrokeRecorderVisual(TxtModalHotkeyGlow, PanelHotkeyGlowTokens, _glowHotkeyRecorder);
        RenderKeystrokeRecorderVisual(TxtModalHotkeyLyrics, PanelHotkeyLyricsTokens, _lyricsHotkeyRecorder);
        if (TxtModalShortcutLyricsLabel != null) TxtModalShortcutLyricsLabel.Text = loc["Settings_ShortcutLyrics"];
        if (BtnModalClearHotkeys != null) BtnModalClearHotkeys.Content = loc["Settings_ClearHotkeys"];
        if (BtnModalSaveHotkeys != null) BtnModalSaveHotkeys.Content = loc["Settings_ApplyHotkeys"];

        if (BtnModalResetSettings != null) BtnModalResetSettings.Content = loc["Settings_ResetDefaults"];
        if (BtnModalOpenLogs != null) BtnModalOpenLogs.Content = loc["Settings_OpenLog"];
        if (BtnModalShowWelcomeAbout != null) BtnModalShowWelcomeAbout.Content = loc["Settings_ShowWelcome"];

        if (TxtModalAboutSubtitle != null) TxtModalAboutSubtitle.Text = loc["About_Subtitle"];

        UpdateLyricsGlowVisualState();
        UpdateEdgeGlowVisualState();
        if (_updateCoordinator != null)
        {
            ApplyUpdateStateToUi(_updateCoordinator.CurrentState);
        }
    }

    /// <summary>
    /// Connects the shared UpdateCoordinator to both the top-right Fluent update toast
    /// and Settings > Updates, and triggers non-blocking startup update check when due.
    /// </summary>
    private void InitializeUpdateWorkflow()
    {
        _updateCoordinator = App.Services?.GetService(typeof(UpdateCoordinator)) as UpdateCoordinator;
        if (_updateCoordinator == null) return;

        _updateCoordinator.StateChanged += (_, snapshot) =>
        {
            DispatcherQueue?.TryEnqueue(() => ApplyUpdateStateToUi(snapshot));
        };

        _updateCoordinator.UpdateAvailableToastRequested += (_, result) =>
        {
            DispatcherQueue?.TryEnqueue(() => ShowTopRightUpdateToast(result));
        };

        ApplyUpdateStateToUi(_updateCoordinator.CurrentState);

        if (!App.IsSmokeTestMode)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2.5)).ConfigureAwait(false);
                    if (_updateCoordinator != null)
                    {
                        await _updateCoordinator.CheckForUpdatesAsync(isManual: false).ConfigureAwait(false);
                    }
                }
                catch { }
            });
        }
    }

    private void ShowTopRightUpdateToast(UpdateCheckResult result)
    {
        if (_toastManager == null || _updateCoordinator == null || !result.IsUpdateAvailable) return;

        var loc = LocalizationService.Instance;
        string title = loc["Updates_NewVersionFound"];
        string versionText = $"Lyrow v{result.LatestVersion}";
        string summary = UpdateCoordinator.FormatReleaseSummary(result.ReleaseNotes, maxLength: 120);
        string installNowLabel = loc["Updates_InstallNow"];

        _activeUpdateToast = _toastManager.ShowUpdateAvailableToast(
            title: title,
            versionMessage: versionText,
            actionLabel: installNowLabel,
            onStartDownload: async _ => await StartSharedUpdateInstallAsync(),
            releaseSummary: summary,
            onDismiss: () =>
            {
                _activeUpdateToast = null;
                _updateCoordinator?.DismissToastForSession();
            });
    }

    private async Task StartSharedUpdateInstallAsync()
    {
        if (_updateCoordinator == null) return;
        await _updateCoordinator.StartInstallAsync(onBeforeExit: () =>
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                try { Application.Current.Exit(); } catch { }
            });
        });
    }

    private void ApplyUpdateStateToUi(UpdateStateSnapshot snapshot)
    {
        var loc = LocalizationService.Instance;

        if (RingModalUpdateChecking != null)
        {
            bool checking = snapshot.WorkflowState == UpdateWorkflowState.Checking;
            RingModalUpdateChecking.IsActive = checking;
            RingModalUpdateChecking.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
        }
        if (RingOverviewUpdateChecking != null)
        {
            bool checking = snapshot.WorkflowState == UpdateWorkflowState.Checking;
            RingOverviewUpdateChecking.IsActive = checking;
            RingOverviewUpdateChecking.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
        }

        if (BtnModalCheckUpdates != null)
        {
            BtnModalCheckUpdates.IsEnabled = !snapshot.IsBusy;
        }
        if (BtnOverviewCheckUpdates != null)
        {
            BtnOverviewCheckUpdates.IsEnabled = !snapshot.IsBusy;
        }

        string statusLine = snapshot.WorkflowState switch
        {
            UpdateWorkflowState.Checking => loc["Updates_Checking"],
            UpdateWorkflowState.UpToDate => loc["Updates_UpToDate"],
            UpdateWorkflowState.UpdateAvailable => $"{loc["Updates_Available"]}: v{snapshot.LastResult?.LatestVersion}",
            UpdateWorkflowState.Downloading => snapshot.StatusMessage ?? loc["Updates_Downloading"],
            UpdateWorkflowState.Verifying => loc["Updates_Verifying"],
            UpdateWorkflowState.Installing => loc["Updates_StartingInstall"],
            UpdateWorkflowState.Completed => loc["Updates_Completed"],
            UpdateWorkflowState.UnsupportedDistribution => $"{loc["Updates_Available"]}: v{snapshot.LastResult?.LatestVersion}",
            UpdateWorkflowState.Failed => loc["Updates_Failed"],
            _ => $"Lyrow v{_updateCoordinator?.CurrentVersion ?? App.CurrentReleaseVersion} — {loc["Updates_Channel"]}"
        };

        if (TxtModalUpdateStatus != null) TxtModalUpdateStatus.Text = statusLine;
        if (TxtOverviewUpdateStatus != null) TxtOverviewUpdateStatus.Text = statusLine;

        bool showDetailsCard = snapshot.HasAvailableUpdate || snapshot.WorkflowState is
            UpdateWorkflowState.Downloading or
            UpdateWorkflowState.Verifying or
            UpdateWorkflowState.Installing or
            UpdateWorkflowState.Completed or
            UpdateWorkflowState.UnsupportedDistribution or
            UpdateWorkflowState.Failed;

        if (BorderModalUpdateDetails != null)
        {
            BorderModalUpdateDetails.Visibility = showDetailsCard ? Visibility.Visible : Visibility.Collapsed;
        }

        if (showDetailsCard)
        {
            var res = snapshot.LastResult;
            if (TxtModalUpdateVersionTitle != null)
            {
                TxtModalUpdateVersionTitle.Text = res?.IsUpdateAvailable == true
                    ? (string.IsNullOrWhiteSpace(res.ReleaseTitle) ? $"Lyrow v{res.LatestVersion}" : res.ReleaseTitle)
                    : loc["Updates_Failed"];
            }

            if (TxtModalUpdatePublishedDate != null)
            {
                if (res?.PublishedAtUtc is DateTimeOffset pub)
                {
                    string formattedDate = pub.ToLocalTime().ToString("d");
                    TxtModalUpdatePublishedDate.Text = string.Format(loc["Updates_PublishedOn"], formattedDate);
                    TxtModalUpdatePublishedDate.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtModalUpdatePublishedDate.Text = string.Empty;
                    TxtModalUpdatePublishedDate.Visibility = Visibility.Collapsed;
                }
            }

            if (TxtModalUpdateReleaseNotes != null)
            {
                string summary = UpdateCoordinator.FormatReleaseSummary(res?.ReleaseNotes, maxLength: 260);
                TxtModalUpdateReleaseNotes.Text = summary;
                TxtModalUpdateReleaseNotes.Visibility = string.IsNullOrWhiteSpace(summary) ? Visibility.Collapsed : Visibility.Visible;
            }

            bool showProgress = snapshot.WorkflowState is UpdateWorkflowState.Downloading or UpdateWorkflowState.Verifying or UpdateWorkflowState.Installing;
            if (ProgressModalUpdate != null)
            {
                ProgressModalUpdate.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
                if (showProgress)
                {
                    if (snapshot.WorkflowState is UpdateWorkflowState.Verifying or UpdateWorkflowState.Installing)
                    {
                        ProgressModalUpdate.IsIndeterminate = true;
                        ProgressModalUpdate.Value = 100;
                    }
                    else if (snapshot.Progress is UpdateDownloadProgress p)
                    {
                        ProgressModalUpdate.IsIndeterminate = p.IsIndeterminate;
                        if (!p.IsIndeterminate) ProgressModalUpdate.Value = p.Percent;
                    }
                }
            }

            if (TxtModalUpdateProgressDetails != null)
            {
                if (snapshot.WorkflowState == UpdateWorkflowState.Downloading && snapshot.Progress is UpdateDownloadProgress p)
                {
                    TxtModalUpdateProgressDetails.Text = p.TotalBytes.HasValue && p.TotalBytes.Value > 0
                        ? string.Format(loc["Updates_Bytes"], p.MegabytesRead, p.TotalMegabytes ?? 0)
                        : $"{p.MegabytesRead:0.0} MB";
                    TxtModalUpdateProgressDetails.Visibility = Visibility.Visible;
                }
                else if (snapshot.WorkflowState is UpdateWorkflowState.Verifying or UpdateWorkflowState.Installing or UpdateWorkflowState.Completed)
                {
                    TxtModalUpdateProgressDetails.Text = snapshot.StatusMessage ?? string.Empty;
                    TxtModalUpdateProgressDetails.Visibility = string.IsNullOrWhiteSpace(snapshot.StatusMessage) ? Visibility.Collapsed : Visibility.Visible;
                }
                else
                {
                    TxtModalUpdateProgressDetails.Visibility = Visibility.Collapsed;
                }
            }

            if (TxtModalUpdateError != null)
            {
                bool hasError = snapshot.WorkflowState is UpdateWorkflowState.Failed or UpdateWorkflowState.UnsupportedDistribution;
                TxtModalUpdateError.Text = hasError ? (snapshot.ErrorMessage ?? loc["Updates_Failed"]) : string.Empty;
                TxtModalUpdateError.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
            }

            if (BtnModalInstallUpdate != null)
            {
                bool canShowInstall = snapshot.HasAvailableUpdate &&
                    snapshot.DistributionKind == AppDistributionKind.InstalledSetup &&
                    snapshot.WorkflowState is UpdateWorkflowState.UpdateAvailable or UpdateWorkflowState.Downloading or UpdateWorkflowState.Verifying or UpdateWorkflowState.Installing;
                BtnModalInstallUpdate.Visibility = canShowInstall ? Visibility.Visible : Visibility.Collapsed;
                BtnModalInstallUpdate.IsEnabled = snapshot.CanInstall;
            }

            if (BtnModalRetryUpdate != null)
            {
                BtnModalRetryUpdate.Visibility = snapshot.CanRetry ? Visibility.Visible : Visibility.Collapsed;
                BtnModalRetryUpdate.IsEnabled = snapshot.CanRetry;
            }

            if (BtnModalViewRelease != null)
            {
                bool showReleaseLink = snapshot.HasAvailableUpdate && !string.IsNullOrWhiteSpace(res?.ReleaseUrl);
                BtnModalViewRelease.Visibility = showReleaseLink ? Visibility.Visible : Visibility.Collapsed;
                BtnModalViewRelease.IsEnabled = !snapshot.IsBusy;
            }
        }

        // Mirror shared progress & state to active top-right update toast (if open)
        if (_activeUpdateToast != null)
        {
            switch (snapshot.WorkflowState)
            {
                case UpdateWorkflowState.Downloading:
                    if (snapshot.Progress is UpdateDownloadProgress p)
                    {
                        _activeUpdateToast.ReportProgress(p.Percent, p.BytesRead, p.TotalBytes, snapshot.StatusMessage);
                    }
                    else
                    {
                        _activeUpdateToast.SetState(Controls.UpdateToastState.Downloading, snapshot.StatusMessage);
                    }
                    break;
                case UpdateWorkflowState.Verifying:
                    _activeUpdateToast.SetState(Controls.UpdateToastState.Verifying, loc["Updates_Verifying"]);
                    break;
                case UpdateWorkflowState.Installing:
                    _activeUpdateToast.SetState(Controls.UpdateToastState.Installing, loc["Updates_StartingInstall"]);
                    break;
                case UpdateWorkflowState.Completed:
                    _activeUpdateToast.SetState(Controls.UpdateToastState.Completed, loc["Updates_Completed"]);
                    _activeUpdateToast = null;
                    break;
                case UpdateWorkflowState.Failed:
                case UpdateWorkflowState.UnsupportedDistribution:
                    _activeUpdateToast.SetState(Controls.UpdateToastState.Failed, snapshot.ErrorMessage);
                    break;
            }
        }
    }

    /// <summary>
    /// Reopens the Windows 11 Fluent Welcome onboarding tour manually from Settings
    /// with all current user preferences pre-selected, without closing LyricsWindow.
    /// </summary>
    public void OpenWelcomeScreenFromSettings()
    {
        if (_isSettingsModalOpen)
        {
            CloseSettingsModal();
        }

        if (_activeWelcomeWindow != null)
        {
            try
            {
                _activeWelcomeWindow.Activate();
                return;
            }
            catch
            {
                _activeWelcomeWindow = null;
            }
        }

        var welcome = new WelcomeWindow(
            _settingsManager,
            _audioCaptureService,
            _audioRoutingService,
            isManualReopen: true,
            onFinishedCallback: _ =>
            {
                _activeWelcomeWindow = null;
                App.ActiveWelcomeWindowInstance = null;
                DispatcherQueue?.TryEnqueue(async () =>
                {
                    await ApplyOnboardingPreferencesAsync();
                    ShowAndActivate();
                });
            });

        _activeWelcomeWindow = welcome;
        App.ActiveWelcomeWindowInstance = welcome;
        welcome.Activate();
    }

    /// <summary>
    /// Synchronizes LyricsWindow theme, Dynamic Background, Edge Light, and Audio Output UI
    /// after first-run or manual Welcome onboarding completes.
    /// </summary>
    public async Task ApplyOnboardingPreferencesAsync()
    {
        var current = _settingsManager.Current;
        if (!string.IsNullOrWhiteSpace(current.Language))
        {
            LocalizationService.Instance.SetLanguage(current.Language);
        }
        ApplyTheme(current.Theme);

        if (AmbientBackgroundLayer != null)
        {
            AmbientBackgroundLayer.Visibility = current.VisualIntegrationEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (current.GlowEnabled != _isEdgeGlowActive)
        {
            await SetEdgeGlowStateAsync(current.GlowEnabled);
        }
        else
        {
            UpdateEdgeGlowVisualState();
        }

        SyncEdgeThicknessSliderFromSettings();
        SyncModalSettingsFromModel();
        ApplyV4Localization();
        ApplyHotkeysFromSettings();
        UpdateOutputDeviceUi();
    }

    private void UpdateLyricsSyncStatusUi()
    {
        // Sync mode is strictly Automatic in v5.0; active source code is displayed directly in the main lyrics toolbar (< CODE >).
    }

    /// <summary>
    /// Validates, detects internal conflicts, registers with Windows global hotkey manager,
    /// and persists keyboard shortcuts cleanly without blocking the UI thread.
    /// Rolls back and shows clear user feedback if validation or OS registration fails.
    /// </summary>
    public sealed record LyricsSourceListItem(
        Lyrow.Core.Lyrics.LyricsProviderId ProviderId,
        string Code,
        string DisplayName,
        string QualityLabel);

    private readonly System.Collections.ObjectModel.ObservableCollection<LyricsSourceListItem> _lyricsSourceItems = new();
    private bool _isLyricsSourceCollectionWired;
    private bool _isPopulatingLyricsSources;

    private void PopulateStaticLyricsSourcesList()
    {
        if (ListModalLyricsSources == null) return;

        var loc = LocalizationService.Instance;
        string qSyl = loc["LyricsQuality_Syllable"];
        string qWord = loc["LyricsQuality_Word"];
        string qLine = loc["LyricsQuality_Line"];
        string qUnsync = loc["LyricsQuality_Unsynced"];

        var metadata = new Dictionary<Lyrow.Core.Lyrics.LyricsProviderId, (string Name, string Code, string Quality)>
        {
            [Lyrow.Core.Lyrics.LyricsProviderId.BetterLyrics] = ("Better Lyrics (TTML)", "BLY", $"{qSyl} / {qWord} / {qLine}"),
            [Lyrow.Core.Lyrics.LyricsProviderId.AppleMusic] = ("Apple Music (LiriQo)", "APL", $"{qSyl} / {qWord} / {qLine}"),
            [Lyrow.Core.Lyrics.LyricsProviderId.BetterLyricsUnison] = ("Better Lyrics Unison", "BLU", $"{qSyl} / {qWord} / {qLine} / {qUnsync}"),
            [Lyrow.Core.Lyrics.LyricsProviderId.QQMusic] = ("QQ Music (LiriQo)", "QQ", $"{qWord} / {qLine}"),
            [Lyrow.Core.Lyrics.LyricsProviderId.NetEase] = ("NetEase (LiriQo)", "NET", $"{qWord} / {qLine}"),
            [Lyrow.Core.Lyrics.LyricsProviderId.KuGou] = ("KuGou (LiriQo)", "KUG", $"{qWord} / {qLine}"),
            [Lyrow.Core.Lyrics.LyricsProviderId.Kuwo] = ("Kuwo (LiriQo)", "KWO", $"{qWord} / {qLine}"),
            [Lyrow.Core.Lyrics.LyricsProviderId.Migu] = ("Migu (LiriQo)", "MIG", $"{qWord} / {qLine}"),
            [Lyrow.Core.Lyrics.LyricsProviderId.PetitLyrics] = ("PetitLyrics (LiriQo)", "PET", $"{qWord} / {qLine}"),
            [Lyrow.Core.Lyrics.LyricsProviderId.Lrclib] = ("LRCLIB", "LRC", $"{qLine} / {qUnsync}")
        };

        var orderedIds = Lyrow.Core.Lyrics.LyricsSourceOrderHelper.NormalizeOrder(_settingsManager.Current.Lyrics.SourcePriorityOrder);

        _isPopulatingLyricsSources = true;
        try
        {
            _lyricsSourceItems.Clear();
            foreach (var id in orderedIds)
            {
                if (metadata.TryGetValue(id, out var info))
                {
                    _lyricsSourceItems.Add(new LyricsSourceListItem(id, info.Code, info.Name, info.Quality));
                }
            }

            if (ListModalLyricsSources.ItemsSource == null)
            {
                ListModalLyricsSources.ItemsSource = _lyricsSourceItems;
            }
        }
        finally
        {
            _isPopulatingLyricsSources = false;
        }

        if (!_isLyricsSourceCollectionWired)
        {
            _isLyricsSourceCollectionWired = true;
            _lyricsSourceItems.CollectionChanged += async (_, e) =>
            {
                if (_isPopulatingLyricsSources || _isUpdatingModalSettingsUi) return;
                if (e.Action is System.Collections.Specialized.NotifyCollectionChangedAction.Add
                    or System.Collections.Specialized.NotifyCollectionChangedAction.Move
                    or System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
                {
                    if (_lyricsSourceItems.Count == Lyrow.Core.Lyrics.LyricsSourceOrderHelper.DefaultActiveProviderOrder.Count)
                    {
                        _settingsManager.Current.Lyrics.SourcePriorityOrder =
                            Lyrow.Core.Lyrics.LyricsSourceOrderHelper.ToSettingsList(_lyricsSourceItems.Select(x => x.ProviderId));
                        await _settingsManager.SaveAsync(_settingsManager.Current);
                    }
                }
            };
        }
    }

    private void InitializeKeystrokeRecorderControl(Button? button, StackPanel? tokenPanel, KeystrokeRecorderSession session)
    {
        if (button == null || tokenPanel == null) return;

        button.Click += (_, _) =>
        {
            if (session.IsRecording) return;
            StartHotkeyRecording(button, tokenPanel, session);
        };

        button.PreviewKeyDown += async (_, e) =>
        {
            if (!session.IsRecording) return;
            e.Handled = true;
            ResetHotkeyRecordTimeoutTimer();

            var result = session.HandleKeyDown((int)e.Key);
            RenderKeystrokeRecorderVisual(button, tokenPanel, session);

            switch (result.Kind)
            {
                case KeystrokeRecorderEventKind.ModifierUpdated:
                    ShowHotkeyFeedback(string.Empty, isError: false);
                    break;

                case KeystrokeRecorderEventKind.Completed:
                    StopHotkeyRecordTimeoutTimer();
                    _activeHotkeyRecorder = null;
                    bool saved = await SaveAndApplyHotkeysAsync();
                    if (!saved)
                    {
                        session.RollbackToPrevious();
                        RenderKeystrokeRecorderVisual(button, tokenPanel, session);
                        ApplyHotkeysFromSettings();
                    }
                    break;

                case KeystrokeRecorderEventKind.Cancelled:
                    StopHotkeyRecordTimeoutTimer();
                    _activeHotkeyRecorder = null;
                    ApplyHotkeysFromSettings();
                    ShowHotkeyFeedback(LocalizationService.Instance["Hotkey_Cancelled"], isError: false);
                    break;

                case KeystrokeRecorderEventKind.Cleared:
                    StopHotkeyRecordTimeoutTimer();
                    _activeHotkeyRecorder = null;
                    await SaveAndApplyHotkeysAsync();
                    break;

                case KeystrokeRecorderEventKind.InvalidKey:
                    ShowHotkeyFeedback(result.ErrorMessage ?? LocalizationService.Instance["Hotkey_InvalidError"], isError: true);
                    break;
            }
        };

        button.PreviewKeyUp += (_, e) =>
        {
            if (!session.IsRecording) return;
            e.Handled = true;
            session.HandleKeyUp((int)e.Key);
            RenderKeystrokeRecorderVisual(button, tokenPanel, session);
        };

        button.LostFocus += (_, _) =>
        {
            if (!session.IsRecording) return;
            CancelActiveHotkeyRecording(restoreHotkeys: true, feedbackKey: "Hotkey_Cancelled");
        };

        RenderKeystrokeRecorderVisual(button, tokenPanel, session);
    }

    private void StartHotkeyRecording(Button button, StackPanel tokenPanel, KeystrokeRecorderSession session)
    {
        if (IsHotkeyRecordingActive)
        {
            CancelActiveHotkeyRecording(restoreHotkeys: false, feedbackKey: null);
        }

        // Suspend active Win32 global hotkeys while recording so old commands never trigger and raw keys reach WinUI 3
        var hotkeyManager = App.Services?.GetService(typeof(IGlobalHotkeyManager)) as IGlobalHotkeyManager;
        if (hotkeyManager != null)
        {
            _ = Task.Run(() => hotkeyManager.UnregisterAll());
        }

        session.StartRecording();
        _activeHotkeyRecorder = session;
        ShowHotkeyFeedback(string.Empty, isError: false);
        RenderKeystrokeRecorderVisual(button, tokenPanel, session);
        button.Focus(FocusState.Programmatic);
        ResetHotkeyRecordTimeoutTimer();
    }

    private void CancelActiveHotkeyRecording(bool restoreHotkeys, string? feedbackKey)
    {
        StopHotkeyRecordTimeoutTimer();
        var active = _activeHotkeyRecorder;
        _activeHotkeyRecorder = null;

        if (active != null && active.IsRecording)
        {
            active.Cancel();
        }

        RenderKeystrokeRecorderVisual(TxtModalHotkeyGlow, PanelHotkeyGlowTokens, _glowHotkeyRecorder);
        RenderKeystrokeRecorderVisual(TxtModalHotkeyLyrics, PanelHotkeyLyricsTokens, _lyricsHotkeyRecorder);

        if (restoreHotkeys)
        {
            ApplyHotkeysFromSettings();
        }

        if (!string.IsNullOrEmpty(feedbackKey))
        {
            ShowHotkeyFeedback(LocalizationService.Instance[feedbackKey], isError: false);
        }
    }

    private void ResetHotkeyRecordTimeoutTimer()
    {
        if (DispatcherQueue == null) return;

        if (_hotkeyRecordTimeoutTimer == null)
        {
            _hotkeyRecordTimeoutTimer = DispatcherQueue.CreateTimer();
            _hotkeyRecordTimeoutTimer.Interval = _glowHotkeyRecorder.TimeoutDuration;
            _hotkeyRecordTimeoutTimer.IsRepeating = false;
            _hotkeyRecordTimeoutTimer.Tick += (_, _) =>
            {
                if (!IsHotkeyRecordingActive) return;
                _activeHotkeyRecorder?.HandleTimeout();
                _activeHotkeyRecorder = null;
                RenderKeystrokeRecorderVisual(TxtModalHotkeyGlow, PanelHotkeyGlowTokens, _glowHotkeyRecorder);
                RenderKeystrokeRecorderVisual(TxtModalHotkeyLyrics, PanelHotkeyLyricsTokens, _lyricsHotkeyRecorder);
                ApplyHotkeysFromSettings();
                ShowHotkeyFeedback(LocalizationService.Instance["Hotkey_Timeout"], isError: false);
            };
        }

        _hotkeyRecordTimeoutTimer.Stop();
        _hotkeyRecordTimeoutTimer.Start();
    }

    private void StopHotkeyRecordTimeoutTimer()
    {
        _hotkeyRecordTimeoutTimer?.Stop();
    }

    private void RenderKeystrokeRecorderVisual(Button? button, StackPanel? panel, KeystrokeRecorderSession session)
    {
        if (button == null || panel == null) return;

        var loc = LocalizationService.Instance;
        var resources = Application.Current.Resources;

        if (session.IsRecording)
        {
            if (resources.TryGetValue("AccentFillColorDefaultBrush", out var acc) && acc is Brush accBrush)
                button.BorderBrush = accBrush;
            button.BorderThickness = new Thickness(1.5);
        }
        else
        {
            if (resources.TryGetValue("ControlStrokeColorDefaultBrush", out var stroke) && stroke is Brush strokeBrush)
                button.BorderBrush = strokeBrush;
            button.BorderThickness = new Thickness(1);
        }

        panel.Children.Clear();
        var tokens = session.GetVisualTokens();

        if (tokens.Count == 0)
        {
            var placeholder = new TextBlock
            {
                Text = session.IsRecording ? loc["Hotkey_RecordingPrompt"] : loc["Hotkey_ClickToRecord"],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = session.IsRecording ? 0.92 : 0.65
            };
            if (session.IsRecording && resources.TryGetValue("AccentTextFillColorPrimaryBrush", out var recFg) && recFg is Brush recFgBrush)
            {
                placeholder.Foreground = recFgBrush;
            }
            else if (resources.TryGetValue("TextFillColorSecondaryBrush", out var secFg) && secFg is Brush secFgBrush)
            {
                placeholder.Foreground = secFgBrush;
            }
            panel.Children.Add(placeholder);
            return;
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (!session.IsRecording && i > 0)
            {
                var plus = new TextBlock
                {
                    Text = "+",
                    FontSize = 11.5,
                    Opacity = 0.6,
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(plus);
            }

            var keycap = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };

            var label = new TextBlock
            {
                Text = token.Label,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (token.IsActivePressed)
            {
                if (resources.TryGetValue("AccentFillColorDefaultBrush", out var activeBg) && activeBg is Brush activeBgBrush)
                {
                    keycap.Background = activeBgBrush;
                    keycap.BorderBrush = activeBgBrush;
                }
                if (resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var activeFg) && activeFg is Brush activeFgBrush)
                {
                    label.Foreground = activeFgBrush;
                }
            }
            else
            {
                if (resources.TryGetValue("SubtleFillColorSecondaryBrush", out var idleBg) && idleBg is Brush idleBgBrush)
                {
                    keycap.Background = idleBgBrush;
                }
                if (resources.TryGetValue("ControlStrokeColorDefaultBrush", out var idleBorder) && idleBorder is Brush idleBorderBrush)
                {
                    keycap.BorderBrush = idleBorderBrush;
                }
                if (resources.TryGetValue("TextFillColorPrimaryBrush", out var idleFg) && idleFg is Brush idleFgBrush)
                {
                    label.Foreground = idleFgBrush;
                }
            }

            keycap.Child = label;
            panel.Children.Add(keycap);
        }
    }

    /// <summary>
    /// Validates, detects internal conflicts, registers with Windows global hotkey manager,
    /// and persists keyboard shortcuts cleanly without blocking the UI thread.
    /// Rolls back and shows clear user feedback if validation or OS registration fails.
    /// </summary>
    private async Task<bool> SaveAndApplyHotkeysAsync()
    {
        var loc = LocalizationService.Instance;
        string rawGlow = _glowHotkeyRecorder.CurrentGesture?.Trim() ?? string.Empty;
        string rawLyrics = _lyricsHotkeyRecorder.CurrentGesture?.Trim() ?? string.Empty;

        // 1. Syntax & supported key validation
        if (!HotkeyValidator.IsValidGesture(rawGlow, out var glowErr))
        {
            ShowHotkeyFeedback(glowErr ?? loc["Hotkey_InvalidError"], isError: true);
            return false;
        }
        if (!HotkeyValidator.IsValidGesture(rawLyrics, out var lyricsErr))
        {
            ShowHotkeyFeedback(lyricsErr ?? loc["Hotkey_InvalidError"], isError: true);
            return false;
        }

        string normGlow = HotkeyValidator.NormalizeGesture(rawGlow);
        string normLyrics = HotkeyValidator.NormalizeGesture(rawLyrics);

        var candidate = new HotkeySettings
        {
            ToggleGlow = normGlow,
            ToggleLyrics = normLyrics,
            ToggleClickThrough = _settingsManager.Current.Hotkeys.ToggleClickThrough
        };

        // 2. Internal conflict check
        if (!HotkeyValidator.CheckInternalCollisions(candidate, out var collisionErr))
        {
            ShowHotkeyFeedback(collisionErr ?? loc["Hotkey_ConflictError"], isError: true);
            return false;
        }

        // 3. Register with Windows Global Hotkey Manager off the UI thread so UI never freezes
        var previous = new HotkeySettings
        {
            ToggleGlow = _settingsManager.Current.Hotkeys.ToggleGlow,
            ToggleLyrics = _settingsManager.Current.Hotkeys.ToggleLyrics,
            ToggleClickThrough = _settingsManager.Current.Hotkeys.ToggleClickThrough
        };

        var hotkeyManager = App.Services?.GetService(typeof(IGlobalHotkeyManager)) as IGlobalHotkeyManager;
        bool registeredOk = true;
        string? registerError = null;

        if (hotkeyManager != null)
        {
            if (string.IsNullOrWhiteSpace(candidate.ToggleGlow) && string.IsNullOrWhiteSpace(candidate.ToggleLyrics))
            {
                await Task.Run(() => hotkeyManager.UnregisterAll()).ConfigureAwait(true);
            }
            else
            {
                registeredOk = await Task.Run(() => hotkeyManager.RegisterHotkeys(
                    candidate,
                    onGlowToggle: ToggleEdgeGlowFromExternal,
                    onLyricsToggle: ToggleLyricsPanelFromExternal,
                    onClickThroughToggle: () => { })).ConfigureAwait(true);

                if (!registeredOk)
                {
                    var failedStatus = hotkeyManager.GetStatus()
                        .FirstOrDefault(s => s.State is HotkeyRegistrationState.Conflict or HotkeyRegistrationState.Failed);
                    registerError = failedStatus?.ErrorMessage ?? loc["Hotkey_RegisterFailed"];

                    // Roll back OS registration to previous valid hotkeys
                    await Task.Run(() =>
                    {
                        if (string.IsNullOrWhiteSpace(previous.ToggleGlow) && string.IsNullOrWhiteSpace(previous.ToggleLyrics))
                        {
                            hotkeyManager.UnregisterAll();
                        }
                        else
                        {
                            hotkeyManager.RegisterHotkeys(previous, ToggleEdgeGlowFromExternal, ToggleLyricsPanelFromExternal, () => { });
                        }
                    }).ConfigureAwait(true);
                }
            }
        }

        if (!registeredOk)
        {
            ShowHotkeyFeedback(registerError ?? loc["Hotkey_RegisterFailed"], isError: true);
            return false;
        }

        // 4. Persist normalized valid hotkeys cleanly
        try
        {
            _settingsManager.Current.Hotkeys.ToggleGlow = normGlow;
            _settingsManager.Current.Hotkeys.ToggleLyrics = normLyrics;
            _glowHotkeyRecorder.SetCommittedGesture(normGlow);
            _lyricsHotkeyRecorder.SetCommittedGesture(normLyrics);
            RenderKeystrokeRecorderVisual(TxtModalHotkeyGlow, PanelHotkeyGlowTokens, _glowHotkeyRecorder);
            RenderKeystrokeRecorderVisual(TxtModalHotkeyLyrics, PanelHotkeyLyricsTokens, _lyricsHotkeyRecorder);

            await _settingsManager.SaveAsync(_settingsManager.Current);
            ShowHotkeyFeedback(loc["Hotkey_SavedSuccess"], isError: false);
            _toastManager?.ShowToast(
                loc["Hotkey_SavedSuccess"],
                title: loc["Tab_Shortcuts"],
                severity: InfoBarSeverity.Success);
            return true;
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Failed to save keyboard shortcuts", ex);
            ShowHotkeyFeedback(ex.Message, isError: true);
            return false;
        }
    }

    private void ShowHotkeyFeedback(string message, bool isError)
    {
        if (TxtModalHotkeyFeedback == null) return;
        TxtModalHotkeyFeedback.Text = message;
        TxtModalHotkeyFeedback.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        if (Application.Current.Resources.TryGetValue(
                isError ? "SystemFillColorCriticalBrush" : "LyrowSuccessBrush",
                out var brushObj) && brushObj is Brush brush)
        {
            TxtModalHotkeyFeedback.Foreground = brush;
        }
    }
}
