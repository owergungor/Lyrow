using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App;

public sealed partial class LyricsWindow
{
    private Storyboard? _activeModalStoryboard;
    private bool _isTogglingEdgeGlow = false;

    private void InitializeV4LyricsGlowAndSettingsModal()
    {
        // 1. Initialize Lyrics Glow state from persisted settings (defaults to false on fresh install)
        _isLyricsGlowEnabled = _settingsManager.Current.Lyrics.LyricsTextGlowEnabled;
        _currentLyricsGlowOpacity = _isLyricsGlowEnabled ? 0.34 : 0.0;
        UpdateLyricsGlowVisualState();

        if (BtnToggleLyricsGlow != null)
        {
            BtnToggleLyricsGlow.Click += (_, _) => ToggleLyricsGlow();
        }

        // 2. Wire Edge Light (Kenar Işığı) button directly to GlowModule + GlowOverlayManager
        if (BtnToggleEdgeGlow != null)
        {
            BtnToggleEdgeGlow.Click += async (_, _) => await ToggleEdgeGlowAsync();
        }
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
            SettingsModalBackdrop.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                CloseSettingsModal();
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

        // Category pills (No 'All' / 'Tümü' tab)
        WireModalTabButton(BtnModalTabAppearance, "Appearance");
        WireModalTabButton(BtnModalTabArtwork, "Artwork");
        WireModalTabButton(BtnModalTabAudio, "Audio");
        WireModalTabButton(BtnModalTabUpdates, "Updates");
        WireModalTabButton(BtnModalTabShortcuts, "Shortcuts");
        WireModalTabButton(BtnModalTabAbout, "About");

        WireModalSettingsControls();
        _ = InitializePrimaryAppServicesAsync();
    }

    private async Task InitializePrimaryAppServicesAsync()
    {
        try
        {
            if (_mediaMonitor != null && !_mediaMonitor.IsMonitoring)
            {
                await _mediaMonitor.StartAsync();
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

    public void ToggleEdgeGlowFromExternal()
    {
        DispatcherQueue?.TryEnqueue(async () => await ToggleEdgeGlowAsync());
    }

    public void ToggleLyricsPanelFromExternal()
    {
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
            if (active)
            {
                glowOverlayManager?.StartOverlays();
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
            IconToggleEdgeGlow.Glyph = _isEdgeGlowActive ? "\uE793" : "\uE793";
            IconToggleEdgeGlow.Opacity = _isEdgeGlowActive ? 1.0 : 0.70;
        }
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

        _isSettingsModalOpen = false;
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
        if (string.IsNullOrWhiteSpace(category) || string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            category = "Appearance";
        }
        _activeModalSettingsCategory = category;

        if (ModalSectionAppearance != null) ModalSectionAppearance.Visibility = category == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionArtwork != null) ModalSectionArtwork.Visibility = category == "Artwork" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionAudio != null) ModalSectionAudio.Visibility = category == "Audio" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionUpdates != null) ModalSectionUpdates.Visibility = category == "Updates" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionShortcuts != null) ModalSectionShortcuts.Visibility = category == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        if (ModalSectionAbout != null) ModalSectionAbout.Visibility = category == "About" ? Visibility.Visible : Visibility.Collapsed;

        var accentStyle = Application.Current.Resources.TryGetValue("AccentButtonStyle", out var acc) ? acc as Style : null;
        var defaultStyle = Application.Current.Resources.TryGetValue("DefaultButtonStyle", out var def) ? def as Style : null;

        void SetPillStyle(Button? btn, string tag)
        {
            if (btn == null) return;
            btn.Style = string.Equals(category, tag, StringComparison.OrdinalIgnoreCase) ? accentStyle : defaultStyle;
        }

        SetPillStyle(BtnModalTabAppearance, "Appearance");
        SetPillStyle(BtnModalTabArtwork, "Artwork");
        SetPillStyle(BtnModalTabAudio, "Audio");
        SetPillStyle(BtnModalTabUpdates, "Updates");
        SetPillStyle(BtnModalTabShortcuts, "Shortcuts");
        SetPillStyle(BtnModalTabAbout, "About");

        SettingsModalScrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
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
            SelectComboByTag(ComboModalUpdateFrequency, s.UpdateFrequency.ToString());

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

            double unfocusedFont = Math.Clamp(s.UnfocusedLyricFontSize > 0 ? s.UnfocusedLyricFontSize : 20.0, 14.0, 32.0);
            if (SliderModalUnfocusedFontSize != null) SliderModalUnfocusedFontSize.Value = unfocusedFont;
            if (TxtModalUnfocusedFontValue != null) TxtModalUnfocusedFontValue.Text = $"{(int)Math.Round(unfocusedFont)} px";

            if (SwitchModalStartWithWindows != null) SwitchModalStartWithWindows.IsOn = s.StartWithWindows;
            if (SwitchModalSystemTray != null) SwitchModalSystemTray.IsOn = s.SystemTrayEnabled;
            if (SwitchModalMinimizeToTray != null) SwitchModalMinimizeToTray.IsOn = s.MinimizeToTray;
            if (SwitchModalNotifications != null) SwitchModalNotifications.IsOn = s.NotificationsEnabled;
            if (SwitchModalNotifyOnTrackChange != null) SwitchModalNotifyOnTrackChange.IsOn = s.NotifyOnTrackChange;

            if (SwitchModalArtworkDynamicBg != null) SwitchModalArtworkDynamicBg.IsOn = s.VisualIntegrationEnabled;
            int satPct = Math.Clamp((int)Math.Round(s.ArtworkBackgroundSaturation * 100.0), 10, 150);
            if (SliderModalArtworkSaturation != null) SliderModalArtworkSaturation.Value = satPct;
            if (TxtModalArtworkSaturationPercent != null) TxtModalArtworkSaturationPercent.Text = $"{satPct}%";

            int radius = Math.Clamp((int)Math.Round(s.ArtworkCornerRadius), 4, 24);
            if (SliderModalArtworkCornerRadius != null) SliderModalArtworkCornerRadius.Value = radius;
            if (TxtModalArtworkRadiusValue != null) TxtModalArtworkRadiusValue.Text = $"{radius} px";

            if (SwitchModalAmbientEdgeGlow != null) SwitchModalAmbientEdgeGlow.IsOn = _isEdgeGlowActive;
            if (SliderModalGlowIntensity != null) SliderModalGlowIntensity.Value = Math.Clamp(s.GlowIntensity, 10, 100);
            if (TxtModalGlowIntensityPercent != null) TxtModalGlowIntensityPercent.Text = $"{Math.Clamp(s.GlowIntensity, 10, 100)}%";
            if (SliderModalGlowThickness != null) SliderModalGlowThickness.Value = Math.Clamp(s.GlowThickness, 10, 60);
            if (TxtModalGlowThicknessValue != null) TxtModalGlowThicknessValue.Text = $"{Math.Clamp(s.GlowThickness, 10, 60)} px";
            if (SwitchModalSyncWithAlbumArt != null) SwitchModalSyncWithAlbumArt.IsOn = s.SyncWithAlbumArt;

            if (SwitchModalAutoUpdate != null) SwitchModalAutoUpdate.IsOn = s.AutoUpdateEnabled;

            if (TxtModalHotkeyGlow != null) TxtModalHotkeyGlow.Text = s.Hotkeys.ToggleGlow ?? string.Empty;
            if (TxtModalHotkeyLyrics != null) TxtModalHotkeyLyrics.Text = s.Hotkeys.ToggleLyrics ?? string.Empty;
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
                int r = Math.Clamp((int)Math.Round(e.NewValue), 4, 24);
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
            SliderModalGlowIntensity.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                int val = Math.Clamp((int)Math.Round(e.NewValue), 10, 100);
                if (TxtModalGlowIntensityPercent != null) TxtModalGlowIntensityPercent.Text = $"{val}%";
                _settingsManager.Current.GlowIntensity = val;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SliderModalGlowThickness != null)
        {
            SliderModalGlowThickness.ValueChanged += async (_, e) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                int val = Math.Clamp((int)Math.Round(e.NewValue), 10, 60);
                if (TxtModalGlowThicknessValue != null) TxtModalGlowThicknessValue.Text = $"{val} px";
                _settingsManager.Current.GlowThickness = val;
                await _settingsManager.SaveAsync(_settingsManager.Current);
            };
        }

        if (SwitchModalSyncWithAlbumArt != null)
        {
            SwitchModalSyncWithAlbumArt.Toggled += async (_, _) =>
            {
                if (_isUpdatingModalSettingsUi) return;
                _settingsManager.Current.SyncWithAlbumArt = SwitchModalSyncWithAlbumArt.IsOn;
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
                var loc = LocalizationService.Instance;
                if (TxtModalUpdateStatus != null) TxtModalUpdateStatus.Text = loc["Updates_Checking"];
                try
                {
                    var updateService = App.Services?.GetService(typeof(IUpdateService)) as IUpdateService;
                    if (updateService != null)
                    {
                        var result = await updateService.CheckForUpdatesAsync();
                        if (TxtModalUpdateStatus != null)
                        {
                            TxtModalUpdateStatus.Text = result.IsUpdateAvailable
                                ? $"{loc["Updates_Available"]}: v{result.LatestVersion}"
                                : loc["Updates_UpToDate"];
                        }
                    }
                }
                catch
                {
                    if (TxtModalUpdateStatus != null) TxtModalUpdateStatus.Text = loc["Updates_Failed"];
                }
            };
        }

        if (BtnModalClearHotkeys != null)
        {
            BtnModalClearHotkeys.Click += async (_, _) =>
            {
                if (TxtModalHotkeyGlow != null) TxtModalHotkeyGlow.Text = string.Empty;
                if (TxtModalHotkeyLyrics != null) TxtModalHotkeyLyrics.Text = string.Empty;
                _settingsManager.Current.Hotkeys.ToggleGlow = string.Empty;
                _settingsManager.Current.Hotkeys.ToggleLyrics = string.Empty;
                await _settingsManager.SaveAsync(_settingsManager.Current);
                ApplyHotkeysFromSettings();
            };
        }

        if (BtnModalSaveHotkeys != null)
        {
            BtnModalSaveHotkeys.Click += async (_, _) =>
            {
                _settingsManager.Current.Hotkeys.ToggleGlow = TxtModalHotkeyGlow?.Text?.Trim() ?? string.Empty;
                _settingsManager.Current.Hotkeys.ToggleLyrics = TxtModalHotkeyLyrics?.Text?.Trim() ?? string.Empty;
                await _settingsManager.SaveAsync(_settingsManager.Current);
                ApplyHotkeysFromSettings();
                _toastManager?.ShowToast(
                    LocalizationService.Instance["Settings_ApplyHotkeys"],
                    title: LocalizationService.Instance["Tab_Shortcuts"],
                    severity: InfoBarSeverity.Success);
            };
        }

        if (BtnModalResetSettings != null)
        {
            BtnModalResetSettings.Click += async (_, _) =>
            {
                var defaults = LyrowSettings.CreateDefault();
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

        if (BtnModalTabAppearance != null) BtnModalTabAppearance.Content = loc["Tab_Appearance"];
        if (BtnModalTabArtwork != null) BtnModalTabArtwork.Content = loc["Tab_Artwork"];
        if (BtnModalTabAudio != null) BtnModalTabAudio.Content = loc["Tab_Audio"];
        if (BtnModalTabUpdates != null) BtnModalTabUpdates.Content = loc["Tab_Updates"];
        if (BtnModalTabShortcuts != null) BtnModalTabShortcuts.Content = loc["Tab_Shortcuts"];
        if (BtnModalTabAbout != null) BtnModalTabAbout.Content = loc["Tab_About"];

        if (TxtModalSectionAppearance != null) TxtModalSectionAppearance.Text = loc["Section_Appearance"];
        if (TxtModalLanguageLabel != null) TxtModalLanguageLabel.Text = loc["Settings_Language"];
        if (TxtModalLanguageDesc != null) TxtModalLanguageDesc.Text = loc["Settings_LanguageDesc"];
        if (TxtModalThemeLabel != null) TxtModalThemeLabel.Text = loc["Settings_Theme"];
        if (TxtModalThemeDesc != null) TxtModalThemeDesc.Text = loc["Settings_ThemeDesc"];
        if (TxtModalLyricsGlowLabel != null) TxtModalLyricsGlowLabel.Text = loc["Settings_LyricsGlowLabel"];
        if (TxtModalLyricsGlowDesc != null) TxtModalLyricsGlowDesc.Text = loc["Settings_LyricsGlowDesc"];
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

        if (TxtModalSectionArtwork != null) TxtModalSectionArtwork.Text = loc["Section_Artwork"];
        if (TxtModalArtworkDynamicBgLabel != null) TxtModalArtworkDynamicBgLabel.Text = loc["Settings_ArtworkDynamicBg"];
        if (TxtModalArtworkDynamicBgDesc != null) TxtModalArtworkDynamicBgDesc.Text = loc["Settings_ArtworkDynamicBgDesc"];
        if (TxtModalArtworkSaturationLabel != null) TxtModalArtworkSaturationLabel.Text = loc["Settings_ArtworkSaturation"];
        if (TxtModalArtworkRadiusLabel != null) TxtModalArtworkRadiusLabel.Text = loc["Settings_ArtworkRadius"];

        if (TxtModalSectionAudio != null) TxtModalSectionAudio.Text = loc["Section_Audio"];
        if (TxtModalAmbientGlowLabel != null) TxtModalAmbientGlowLabel.Text = loc["Settings_AmbientGlowLabel"];
        if (TxtModalAmbientGlowDesc != null) TxtModalAmbientGlowDesc.Text = loc["Settings_AmbientGlowDesc"];
        if (TxtModalGlowIntensityLabel != null) TxtModalGlowIntensityLabel.Text = loc["Settings_GlowIntensity"];
        if (TxtModalGlowThicknessLabel != null) TxtModalGlowThicknessLabel.Text = loc["Settings_GlowThickness"];
        if (TxtModalSyncAlbumArtLabel != null) TxtModalSyncAlbumArtLabel.Text = loc["Settings_SyncWithAlbumArt"];
        if (TxtModalFftSizeLabel != null) TxtModalFftSizeLabel.Text = loc["Settings_FftSize"];
        if (TxtModalFftSizeDesc != null) TxtModalFftSizeDesc.Text = loc["Settings_FftSizeDesc"];

        if (TxtModalSectionUpdates != null) TxtModalSectionUpdates.Text = loc["Section_Updates"];
        if (TxtModalAutoUpdatesLabel != null) TxtModalAutoUpdatesLabel.Text = loc["Settings_AutoUpdates"];
        if (TxtModalAutoUpdatesDesc != null) TxtModalAutoUpdatesDesc.Text = loc["Settings_AutoUpdatesDesc"];
        if (TxtModalUpdateFreqLabel != null) TxtModalUpdateFreqLabel.Text = loc["Settings_UpdateFreq"];
        if (BtnModalCheckUpdates != null) BtnModalCheckUpdates.Content = loc["Settings_CheckUpdates"];

        if (TxtModalSectionShortcuts != null) TxtModalSectionShortcuts.Text = loc["Section_Shortcuts"];
        if (TxtModalShortcutGlowLabel != null) TxtModalShortcutGlowLabel.Text = loc["Settings_ShortcutGlow"];
        if (TxtModalShortcutLyricsLabel != null) TxtModalShortcutLyricsLabel.Text = loc["Settings_ShortcutLyrics"];
        if (BtnModalClearHotkeys != null) BtnModalClearHotkeys.Content = loc["Settings_ClearHotkeys"];
        if (BtnModalSaveHotkeys != null) BtnModalSaveHotkeys.Content = loc["Settings_ApplyHotkeys"];

        if (BtnModalResetSettings != null) BtnModalResetSettings.Content = loc["Settings_ResetDefaults"];
        if (BtnModalOpenLogs != null) BtnModalOpenLogs.Content = loc["Settings_OpenLog"];

        if (TxtModalAboutSubtitle != null) TxtModalAboutSubtitle.Text = loc["About_Subtitle"];

        UpdateLyricsGlowVisualState();
        UpdateEdgeGlowVisualState();
    }
}
