using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App;

/// <summary>
/// Lyrow v5.0 Welcome V4: 5-Tab Modern Fluent Onboarding Experience.
/// Tabs: 1. Start (Language &amp; Theme), 2. Appearance (Edge Light &amp; Dynamic Background),
/// 3. Music Reactivity (Calm / Balanced / Expressive), 4. Keyboard Shortcuts (Real Keystroke Recorder),
/// 5. Finish (Preference Summary &amp; Launch Lyrow).
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly WelcomeOnboardingCoordinator _coordinator;
    private readonly ISettingsManager _settingsManager;
    private readonly Action<WelcomeCompletionReason>? _onFinishedCallback;
    private readonly KeystrokeRecorderSession _glowRecorder = new();
    private readonly KeystrokeRecorderSession _lyricsRecorder = new();
    private KeystrokeRecorderSession? _activeRecorder;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _hotkeyTimeoutTimer;
    private Storyboard? _reactivityStoryboard;
    private bool _isUpdatingUi;
    private bool _isClosingHandled;

    private bool IsShortcutRecordingActive => _activeRecorder?.IsRecording == true;

    public WelcomeWindow(
        ISettingsManager settingsManager,
        IAudioCaptureService? audioCaptureService = null,
        IAudioRoutingService? audioRoutingService = null,
        bool isManualReopen = false,
        Action<WelcomeCompletionReason>? onFinishedCallback = null)
    {
        InitializeComponent();

        _settingsManager = settingsManager;
        _onFinishedCallback = onFinishedCallback;
        _coordinator = new WelcomeOnboardingCoordinator(
            settingsManager,
            audioCaptureService,
            audioRoutingService,
            isManualReopen);

        _glowRecorder.SetCommittedGesture(_coordinator.SelectedShortcutGlow);
        _lyricsRecorder.SetCommittedGesture(_coordinator.SelectedShortcutLyrics);

        ConfigureWindowChromeAndCenteredBounds();
        PopulateSupportedLanguagesDropdown();
        ApplyTheme(_coordinator.SelectedTheme);
        ApplyLocalization();
        WireControls();
        SyncAllUiFromCoordinator();
        ShowStep(_coordinator.CurrentStep);
    }

    public WelcomeOnboardingCoordinator Coordinator => _coordinator;

    private void PopulateSupportedLanguagesDropdown()
    {
        if (ComboWelcomeLanguage == null) return;
        ComboWelcomeLanguage.Items.Clear();
        foreach (var (code, nativeName, _) in LocalizationService.SupportedLanguages)
        {
            ComboWelcomeLanguage.Items.Add(new ComboBoxItem
            {
                Content = nativeName,
                Tag = code
            });
        }
    }

    public void ApplyTheme(ThemeMode mode)
    {
        if (WelcomeRootLayout == null) return;
        WelcomeRootLayout.RequestedTheme = mode switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default
        };
    }

    private void ConfigureWindowChromeAndCenteredBounds()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WelcomeTitleBar);
        WelcomeTitleBar.Title = string.Empty;

        try
        {
            string appIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "lyrow.ico");
            if (File.Exists(appIconPath))
            {
                AppWindow.SetIcon(appIconPath);
            }
        }
        catch { }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi > 0 ? (dpi / 96.0) : 1.0;

        int targetWidth = (int)Math.Round(900 * scale);
        int targetHeight = (int)Math.Round(610 * scale);

        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            int maxWidth = Math.Max(640, workArea.Width - (int)Math.Round(48 * scale));
            int maxHeight = Math.Max(520, workArea.Height - (int)Math.Round(48 * scale));
            targetWidth = Math.Min(targetWidth, maxWidth);
            targetHeight = Math.Min(targetHeight, maxHeight);

            int x = workArea.X + Math.Max(0, (workArea.Width - targetWidth) / 2);
            int y = workArea.Y + Math.Max(0, (workArea.Height - targetHeight) / 2);
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, targetWidth, targetHeight));
        }
        catch
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(targetWidth, targetHeight));
        }

        if (AppWindow.Presenter is OverlappedPresenter overlapped)
        {
            overlapped.IsMaximizable = false;
            overlapped.IsMinimizable = true;
            overlapped.IsResizable = true;
            overlapped.PreferredMinimumWidth = (int)Math.Round(700 * scale);
            overlapped.PreferredMinimumHeight = (int)Math.Round(520 * scale);
        }
    }

    private void WireControls()
    {
        // Stepper tabs (free navigation)
        BtnWelcomeTab0.Click += (_, _) => NavigateToStep(WelcomeStep.Welcome);
        BtnWelcomeTab1.Click += (_, _) => NavigateToStep(WelcomeStep.Appearance);
        BtnWelcomeTab2.Click += (_, _) => NavigateToStep(WelcomeStep.Experience);
        BtnWelcomeTab3.Click += (_, _) => NavigateToStep(WelcomeStep.Audio);
        BtnWelcomeTab4.Click += (_, _) => NavigateToStep(WelcomeStep.Ready);

        // Footer navigation
        BtnWelcomeBack.Click += (_, _) => { if (_coordinator.Back()) ShowStep(_coordinator.CurrentStep); };
        BtnWelcomeNext.Click += (_, _) => { if (_coordinator.Continue()) ShowStep(_coordinator.CurrentStep); };
        BtnWelcomeUseDefaults.Click += async (_, _) => await ExecuteUseDefaultsAsync();

        // Tab 0: Language & Theme
        ComboWelcomeLanguage.SelectionChanged += (_, _) =>
        {
            if (_isUpdatingUi) return;
            if (ComboWelcomeLanguage.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
            {
                SelectLanguageLive(langCode);
            }
        };
        BtnWelcomeThemeSystem.Click += (_, _) => SelectThemeLive(ThemeMode.SystemDefault);
        BtnWelcomeThemeDark.Click += (_, _) => SelectThemeLive(ThemeMode.Dark);
        BtnWelcomeThemeLight.Click += (_, _) => SelectThemeLive(ThemeMode.Light);

        // Tab 1: Appearance toggles
        ToggleWelcomeEdgeLight.Toggled += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableEdgeLight = ToggleWelcomeEdgeLight.IsOn;
            SyncAppearancePreview();
        };
        ToggleWelcomeDynamicBg.Toggled += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableDynamicBackground = ToggleWelcomeDynamicBg.IsOn;
            SyncAppearancePreview();
        };

        // Tab 2: Music Reactivity Sensitivity cards (4096=Calm, 2048=Balanced, 1024=Expressive)
        BtnWelcomeSensCalm.Click += (_, _) => SelectReactivitySensitivity(4096);
        BtnWelcomeSensBalanced.Click += (_, _) => SelectReactivitySensitivity(2048);
        BtnWelcomeSensExpressive.Click += (_, _) => SelectReactivitySensitivity(1024);

        // Tab 3: Real Keystroke Recorders reusing KeystrokeRecorderSession
        WireWelcomeKeystrokeRecorder(BtnWelcomeHotkeyGlow, PanelWelcomeHotkeyGlowTokens, _glowRecorder, isGlow: true);
        WireWelcomeKeystrokeRecorder(BtnWelcomeHotkeyLyrics, PanelWelcomeHotkeyLyricsTokens, _lyricsRecorder, isGlow: false);
        BtnWelcomeDefaultShortcuts.Click += (_, _) =>
        {
            CancelActiveShortcutRecording(restoreOsHotkeys: true, feedbackKey: null);
            _coordinator.ResetShortcutsToDefaults();
            _glowRecorder.SetCommittedGesture(_coordinator.SelectedShortcutGlow);
            _lyricsRecorder.SetCommittedGesture(_coordinator.SelectedShortcutLyrics);
            RenderRecorderVisual(BtnWelcomeHotkeyGlow, PanelWelcomeHotkeyGlowTokens, _glowRecorder);
            RenderRecorderVisual(BtnWelcomeHotkeyLyrics, PanelWelcomeHotkeyLyricsTokens, _lyricsRecorder);
            ShowShortcutFeedback(LocalizationService.Instance["WelcomeV4_Shortcuts_UseDefaults"], isError: false);
        };

        // Tab 4: Launch Lyrow (both Launch button and footer Start button share exact same ExecuteStartListeningAsync / CompleteAsync)
        BtnWelcomeStartListening.Click += OnStartListeningClicked;
        BtnWelcomeFinishFooter.Click += OnStartListeningClicked;

        // Window-level keyboard, responsive layout, focus loss, and close safety
        WelcomeRootLayout.SizeChanged += (_, args) => ApplyResponsiveLayout(args.NewSize.Width);
        WelcomeRootLayout.KeyDown += OnRootKeyDown;
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && IsShortcutRecordingActive)
            {
                CancelActiveShortcutRecording(restoreOsHotkeys: true, feedbackKey: "Hotkey_Cancelled");
            }
        };
        Closed += OnWelcomeWindowClosed;

        ApplyResponsiveLayout(WelcomeRootLayout.ActualWidth > 0 ? WelcomeRootLayout.ActualWidth : 860);
    }

    private void ApplyResponsiveLayout(double viewportDipWidth)
    {
        if (WelcomeContentHost == null || WelcomeScrollViewer == null) return;

        var m = WelcomeResponsiveLayoutCalculator.Compute(viewportDipWidth);

        WelcomeHeaderBorder.Padding = new Thickness(m.HorizontalPadding, 4, m.HorizontalPadding, 10);
        WelcomeFooterBorder.Padding = new Thickness(m.HorizontalPadding, 10, m.HorizontalPadding, 14);
        WelcomeScrollViewer.Padding = new Thickness(m.HorizontalPadding, 18, m.HorizontalPadding, 18);

        WelcomeContentHost.MaxWidth = m.ContentMaxWidth;
        WelcomeContentHost.Width = m.ContentMaxWidth;

        // Compact stepper badge visibility on narrow viewports so all 5 tabs always fit cleanly
        Visibility badgeVis = m.UseCompactStepper ? Visibility.Collapsed : Visibility.Visible;
        BadgeWelcomeTab0.Visibility = badgeVis;
        BadgeWelcomeTab1.Visibility = badgeVis;
        BadgeWelcomeTab2.Visibility = badgeVis;
        BadgeWelcomeTab3.Visibility = badgeVis;
        BadgeWelcomeTab4.Visibility = badgeVis;

        // Language row responsive placement
        ComboWelcomeLanguage.MaxWidth = m.StackLanguageRow ? m.ContentMaxWidth : m.LanguageComboWidth;
        ComboWelcomeLanguage.Width = m.StackLanguageRow ? double.NaN : m.LanguageComboWidth;
        if (m.StackLanguageRow)
        {
            ColWelcomeLanguageCombo.Width = new GridLength(0);
            Grid.SetRow(ComboWelcomeLanguage, 1);
            Grid.SetColumn(ComboWelcomeLanguage, 0);
            Grid.SetColumnSpan(ComboWelcomeLanguage, 2);
        }
        else
        {
            ColWelcomeLanguageCombo.Width = GridLength.Auto;
            Grid.SetRow(ComboWelcomeLanguage, 0);
            Grid.SetColumn(ComboWelcomeLanguage, 2);
            Grid.SetColumnSpan(ComboWelcomeLanguage, 1);
        }

        // Theme cards: 3 equal columns -> 1-column stack on narrow viewports
        if (m.StackThemeCards)
        {
            ColTheme1.Width = new GridLength(0);
            ColTheme2.Width = new GridLength(0);
            Grid.SetRow(BtnWelcomeThemeSystem, 0); Grid.SetColumn(BtnWelcomeThemeSystem, 0);
            Grid.SetRow(BtnWelcomeThemeDark, 1); Grid.SetColumn(BtnWelcomeThemeDark, 0);
            Grid.SetRow(BtnWelcomeThemeLight, 2); Grid.SetColumn(BtnWelcomeThemeLight, 0);
        }
        else
        {
            ColTheme1.Width = new GridLength(1, GridUnitType.Star);
            ColTheme2.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(BtnWelcomeThemeSystem, 0); Grid.SetColumn(BtnWelcomeThemeSystem, 0);
            Grid.SetRow(BtnWelcomeThemeDark, 0); Grid.SetColumn(BtnWelcomeThemeDark, 1);
            Grid.SetRow(BtnWelcomeThemeLight, 0); Grid.SetColumn(BtnWelcomeThemeLight, 2);
        }

        // Music Reactivity cards: 3 equal columns -> 1-column stack on narrow viewports
        if (m.StackReactivityCards)
        {
            ColSens1.Width = new GridLength(0);
            ColSens2.Width = new GridLength(0);
            Grid.SetRow(BtnWelcomeSensCalm, 0); Grid.SetColumn(BtnWelcomeSensCalm, 0);
            Grid.SetRow(BtnWelcomeSensBalanced, 1); Grid.SetColumn(BtnWelcomeSensBalanced, 0);
            Grid.SetRow(BtnWelcomeSensExpressive, 2); Grid.SetColumn(BtnWelcomeSensExpressive, 0);
        }
        else
        {
            ColSens1.Width = new GridLength(1, GridUnitType.Star);
            ColSens2.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(BtnWelcomeSensCalm, 0); Grid.SetColumn(BtnWelcomeSensCalm, 0);
            Grid.SetRow(BtnWelcomeSensBalanced, 0); Grid.SetColumn(BtnWelcomeSensBalanced, 1);
            Grid.SetRow(BtnWelcomeSensExpressive, 0); Grid.SetColumn(BtnWelcomeSensExpressive, 2);
        }

        // Shortcut rows: side-by-side -> stacked on narrow viewports
        BtnWelcomeHotkeyGlow.MinWidth = m.ShortcutRecorderMinWidth;
        BtnWelcomeHotkeyLyrics.MinWidth = m.ShortcutRecorderMinWidth;
        if (m.StackShortcutRows)
        {
            ColShortcutGlowBtn.Width = new GridLength(0);
            Grid.SetRow(BtnWelcomeHotkeyGlow, 1); Grid.SetColumn(BtnWelcomeHotkeyGlow, 0); Grid.SetColumnSpan(BtnWelcomeHotkeyGlow, 2);
            ColShortcutLyricsBtn.Width = new GridLength(0);
            Grid.SetRow(BtnWelcomeHotkeyLyrics, 1); Grid.SetColumn(BtnWelcomeHotkeyLyrics, 0); Grid.SetColumnSpan(BtnWelcomeHotkeyLyrics, 2);
        }
        else
        {
            ColShortcutGlowBtn.Width = GridLength.Auto;
            Grid.SetRow(BtnWelcomeHotkeyGlow, 0); Grid.SetColumn(BtnWelcomeHotkeyGlow, 2); Grid.SetColumnSpan(BtnWelcomeHotkeyGlow, 1);
            ColShortcutLyricsBtn.Width = GridLength.Auto;
            Grid.SetRow(BtnWelcomeHotkeyLyrics, 0); Grid.SetColumn(BtnWelcomeHotkeyLyrics, 2); Grid.SetColumnSpan(BtnWelcomeHotkeyLyrics, 1);
        }

        // Summary grid: 2 columns -> 1 column on narrow viewports
        if (m.StackSummaryColumns)
        {
            ColSummary1.Width = new GridLength(0);
            Grid.SetRow(PanelSummaryLanguage, 0); Grid.SetColumn(PanelSummaryLanguage, 0);
            Grid.SetRow(PanelSummaryTheme, 1); Grid.SetColumn(PanelSummaryTheme, 0);
            Grid.SetRow(PanelSummaryEdgeLight, 2); Grid.SetColumn(PanelSummaryEdgeLight, 0);
            Grid.SetRow(PanelSummaryDynamicBg, 3); Grid.SetColumn(PanelSummaryDynamicBg, 0);
            Grid.SetRow(PanelSummaryReactivity, 4); Grid.SetColumn(PanelSummaryReactivity, 0);
            Grid.SetRow(PanelSummaryShortcuts, 5); Grid.SetColumn(PanelSummaryShortcuts, 0);
        }
        else
        {
            ColSummary1.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(PanelSummaryLanguage, 0); Grid.SetColumn(PanelSummaryLanguage, 0);
            Grid.SetRow(PanelSummaryTheme, 0); Grid.SetColumn(PanelSummaryTheme, 1);
            Grid.SetRow(PanelSummaryEdgeLight, 1); Grid.SetColumn(PanelSummaryEdgeLight, 0);
            Grid.SetRow(PanelSummaryDynamicBg, 1); Grid.SetColumn(PanelSummaryDynamicBg, 1);
            Grid.SetRow(PanelSummaryReactivity, 2); Grid.SetColumn(PanelSummaryReactivity, 0);
            Grid.SetRow(PanelSummaryShortcuts, 2); Grid.SetColumn(PanelSummaryShortcuts, 1);
        }
    }

    private void NavigateToStep(WelcomeStep step)
    {
        if (IsShortcutRecordingActive)
        {
            CancelActiveShortcutRecording(restoreOsHotkeys: true, feedbackKey: null);
        }
        _coordinator.GoToStep(step);
        ShowStep(step);
    }

    private void ShowStep(WelcomeStep step)
    {
        PanelWelcomeTabStart.Visibility = step == WelcomeStep.Welcome ? Visibility.Visible : Visibility.Collapsed;
        PanelWelcomeTabAppearance.Visibility = step == WelcomeStep.Appearance ? Visibility.Visible : Visibility.Collapsed;
        PanelWelcomeTabReactivity.Visibility = step == WelcomeStep.Experience ? Visibility.Visible : Visibility.Collapsed;
        PanelWelcomeTabShortcuts.Visibility = step == WelcomeStep.Audio ? Visibility.Visible : Visibility.Collapsed;
        PanelWelcomeTabFinish.Visibility = step == WelcomeStep.Ready ? Visibility.Visible : Visibility.Collapsed;

        WelcomeProgressBar.Value = ((int)step + 1) * 20.0;
        BtnWelcomeBack.Visibility = _coordinator.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        BtnWelcomeNext.Visibility = _coordinator.CanContinue ? Visibility.Visible : Visibility.Collapsed;
        BtnWelcomeFinishFooter.Visibility = _coordinator.IsReadyStep ? Visibility.Visible : Visibility.Collapsed;

        UpdateStepperVisuals(step);

        if (step == WelcomeStep.Experience)
        {
            UpdateReactivityPreviewAndAnimation();
        }
        else
        {
            StopReactivityAnimation();
        }

        if (step == WelcomeStep.Ready)
        {
            RefreshSummaryCard();
            TxtWelcomeSaveError.Visibility = Visibility.Collapsed;
        }

        WelcomeScrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void UpdateStepperVisuals(WelcomeStep activeStep)
    {
        var accentBg = new SolidColorBrush(Color.FromArgb(44, 14, 165, 233));
        var accentBorder = new SolidColorBrush(Color.FromArgb(200, 56, 189, 248));
        var idleBg = new SolidColorBrush(Colors.Transparent);
        var idleBorder = new SolidColorBrush(Colors.Transparent);

        Button[] tabs = { BtnWelcomeTab0, BtnWelcomeTab1, BtnWelcomeTab2, BtnWelcomeTab3, BtnWelcomeTab4 };
        for (int i = 0; i < tabs.Length; i++)
        {
            bool isActive = (int)activeStep == i;
            tabs[i].Background = isActive ? accentBg : idleBg;
            tabs[i].BorderBrush = isActive ? accentBorder : idleBorder;
            tabs[i].FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private void SelectLanguageLive(string languageCode)
    {
        _coordinator.SelectedLanguage = languageCode;
        LocalizationService.Instance.SetLanguage(languageCode);
        ApplyLocalization();
        RefreshSummaryCard();
        RenderRecorderVisual(BtnWelcomeHotkeyGlow, PanelWelcomeHotkeyGlowTokens, _glowRecorder);
        RenderRecorderVisual(BtnWelcomeHotkeyLyrics, PanelWelcomeHotkeyLyricsTokens, _lyricsRecorder);
    }

    private void SelectThemeLive(ThemeMode mode)
    {
        _coordinator.SelectedTheme = mode;
        ApplyTheme(mode);
        App.PrimaryWindowInstance?.ApplyTheme(mode);
        SyncThemeCardVisuals();
        RefreshSummaryCard();
    }

    private void SelectReactivitySensitivity(int fftSize)
    {
        _coordinator.SelectedFftSize = fftSize;
        SyncReactivityCardVisuals();
        UpdateReactivityPreviewAndAnimation();
        RefreshSummaryCard();
    }

    private void SyncAllUiFromCoordinator()
    {
        _isUpdatingUi = true;
        try
        {
            for (int i = 0; i < ComboWelcomeLanguage.Items.Count; i++)
            {
                if (ComboWelcomeLanguage.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), _coordinator.SelectedLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    ComboWelcomeLanguage.SelectedIndex = i;
                    break;
                }
            }

            ToggleWelcomeEdgeLight.IsOn = _coordinator.EnableEdgeLight;
            ToggleWelcomeDynamicBg.IsOn = _coordinator.EnableDynamicBackground;

            SyncThemeCardVisuals();
            SyncAppearancePreview();
            SyncReactivityCardVisuals();
            RenderRecorderVisual(BtnWelcomeHotkeyGlow, PanelWelcomeHotkeyGlowTokens, _glowRecorder);
            RenderRecorderVisual(BtnWelcomeHotkeyLyrics, PanelWelcomeHotkeyLyricsTokens, _lyricsRecorder);
            RefreshSummaryCard();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void SyncThemeCardVisuals()
    {
        ApplySelectionCardBorder(BtnWelcomeThemeSystem, _coordinator.SelectedTheme == ThemeMode.SystemDefault);
        ApplySelectionCardBorder(BtnWelcomeThemeDark, _coordinator.SelectedTheme == ThemeMode.Dark);
        ApplySelectionCardBorder(BtnWelcomeThemeLight, _coordinator.SelectedTheme == ThemeMode.Light);
    }

    private void SyncReactivityCardVisuals()
    {
        ApplySelectionCardBorder(BtnWelcomeSensCalm, _coordinator.SelectedFftSize == 4096);
        ApplySelectionCardBorder(BtnWelcomeSensBalanced, _coordinator.SelectedFftSize == 2048);
        ApplySelectionCardBorder(BtnWelcomeSensExpressive, _coordinator.SelectedFftSize == 1024);
    }

    private static void ApplySelectionCardBorder(Button card, bool selected)
    {
        if (selected)
        {
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(230, 56, 189, 248));
            card.BorderThickness = new Thickness(2);
        }
        else if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var stroke) && stroke is Brush brush)
        {
            card.BorderBrush = brush;
            card.BorderThickness = new Thickness(1);
        }
    }

    private void SyncAppearancePreview()
    {
        EdgeLightAuraOuter.Opacity = _coordinator.EnableEdgeLight ? 1.0 : 0.35;
        EdgeLightInnerRim.Opacity = _coordinator.EnableEdgeLight ? 0.78 : 0.15;
        AmbientBloomLayer.Opacity = _coordinator.EnableDynamicBackground ? 0.85 : 0.12;
        AmbientBloomLayerRight.Opacity = _coordinator.EnableDynamicBackground ? 0.80 : 0.12;
    }

    private void UpdateReactivityPreviewAndAnimation()
    {
        var loc = LocalizationService.Instance;
        (string title, string desc, int baseMs, double amp) = _coordinator.SelectedFftSize switch
        {
            4096 => (loc["WelcomeV4_Sens_Calm"], loc["WelcomeV4_Sens_CalmDesc"], 1100, 0.55),
            1024 => (loc["WelcomeV4_Sens_Expressive"], loc["WelcomeV4_Sens_ExpressiveDesc"], 380, 1.05),
            _ => (loc["WelcomeV4_Sens_Balanced"], loc["WelcomeV4_Sens_BalancedDesc"], 680, 0.82)
        };

        TxtReactivityPreviewTitle.Text = title;
        TxtReactivityPreviewDesc.Text = desc;

        StopReactivityAnimation();
        if (!_settingsManager.Current.EnableAnimations) return;

        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        ScaleTransform[] scales =
        {
            ReactivityEqScale1, ReactivityEqScale2, ReactivityEqScale3, ReactivityEqScale4,
            ReactivityEqScale5, ReactivityEqScale6, ReactivityEqScale7
        };

        for (int i = 0; i < scales.Length; i++)
        {
            var anim = new DoubleAnimation
            {
                From = 0.30,
                To = Math.Clamp(amp - (i % 3) * 0.08, 0.45, 1.0),
                Duration = new Duration(TimeSpan.FromMilliseconds(baseMs + (i * 75))),
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, scales[i]);
            Storyboard.SetTargetProperty(anim, "ScaleY");
            sb.Children.Add(anim);
        }

        _reactivityStoryboard = sb;
        sb.Begin();
    }

    private void StopReactivityAnimation()
    {
        _reactivityStoryboard?.Stop();
        _reactivityStoryboard = null;
    }

    #region Keystroke Recorder Integration (Reusing KeystrokeRecorderSession)

    private void WireWelcomeKeystrokeRecorder(Button button, StackPanel panel, KeystrokeRecorderSession session, bool isGlow)
    {
        button.Click += (_, _) =>
        {
            if (session.IsRecording) return;
            StartShortcutRecording(button, panel, session);
        };

        button.PreviewKeyDown += (_, e) =>
        {
            if (!session.IsRecording) return;
            e.Handled = true;
            ResetShortcutTimeoutTimer();

            var result = session.HandleKeyDown((int)e.Key);
            RenderRecorderVisual(button, panel, session);

            switch (result.Kind)
            {
                case KeystrokeRecorderEventKind.ModifierUpdated:
                    ShowShortcutFeedback(string.Empty, isError: false);
                    break;

                case KeystrokeRecorderEventKind.Completed:
                    StopShortcutTimeoutTimer();
                    _activeRecorder = null;

                    string candidateGlow = isGlow ? result.Gesture : _coordinator.SelectedShortcutGlow;
                    string candidateLyrics = isGlow ? _coordinator.SelectedShortcutLyrics : result.Gesture;
                    var candidate = new HotkeySettings
                    {
                        ToggleGlow = candidateGlow,
                        ToggleLyrics = candidateLyrics,
                        ToggleClickThrough = _settingsManager.Current.Hotkeys.ToggleClickThrough
                    };

                    if (!HotkeyValidator.CheckInternalCollisions(candidate, out string? colErr))
                    {
                        session.RollbackToPrevious();
                        RenderRecorderVisual(button, panel, session);
                        ShowShortcutFeedback(colErr ?? LocalizationService.Instance["Hotkey_ConflictError"], isError: true);
                        RestoreOsHotkeysIfApplicable();
                        return;
                    }

                    if (isGlow) _coordinator.SelectedShortcutGlow = result.Gesture;
                    else _coordinator.SelectedShortcutLyrics = result.Gesture;

                    ShowShortcutFeedback(string.Empty, isError: false);
                    RestoreOsHotkeysIfApplicable();
                    RefreshSummaryCard();
                    break;

                case KeystrokeRecorderEventKind.Cancelled:
                    StopShortcutTimeoutTimer();
                    _activeRecorder = null;
                    RestoreOsHotkeysIfApplicable();
                    ShowShortcutFeedback(LocalizationService.Instance["Hotkey_Cancelled"], isError: false);
                    break;

                case KeystrokeRecorderEventKind.Cleared:
                    StopShortcutTimeoutTimer();
                    _activeRecorder = null;
                    if (isGlow) _coordinator.SelectedShortcutGlow = string.Empty;
                    else _coordinator.SelectedShortcutLyrics = string.Empty;
                    RestoreOsHotkeysIfApplicable();
                    ShowShortcutFeedback(LocalizationService.Instance["Hotkey_Cleared"], isError: false);
                    RefreshSummaryCard();
                    break;

                case KeystrokeRecorderEventKind.InvalidKey:
                    ShowShortcutFeedback(result.ErrorMessage ?? LocalizationService.Instance["Hotkey_InvalidError"], isError: true);
                    break;
            }
        };

        button.PreviewKeyUp += (_, e) =>
        {
            if (!session.IsRecording) return;
            e.Handled = true;
            session.HandleKeyUp((int)e.Key);
            RenderRecorderVisual(button, panel, session);
        };

        button.LostFocus += (_, _) =>
        {
            if (!session.IsRecording) return;
            CancelActiveShortcutRecording(restoreOsHotkeys: true, feedbackKey: "Hotkey_Cancelled");
        };
    }

    private void StartShortcutRecording(Button button, StackPanel panel, KeystrokeRecorderSession session)
    {
        if (IsShortcutRecordingActive)
        {
            CancelActiveShortcutRecording(restoreOsHotkeys: false, feedbackKey: null);
        }

        // Temporarily suspend global OS hotkeys so existing shortcuts do not fire during recording
        var hotkeyManager = App.Services?.GetService(typeof(IGlobalHotkeyManager)) as IGlobalHotkeyManager;
        if (hotkeyManager != null)
        {
            _ = Task.Run(() => hotkeyManager.UnregisterAll());
        }

        session.StartRecording();
        _activeRecorder = session;
        ShowShortcutFeedback(string.Empty, isError: false);
        RenderRecorderVisual(button, panel, session);
        button.Focus(FocusState.Programmatic);
        ResetShortcutTimeoutTimer();
    }

    private void CancelActiveShortcutRecording(bool restoreOsHotkeys, string? feedbackKey)
    {
        StopShortcutTimeoutTimer();
        var active = _activeRecorder;
        _activeRecorder = null;
        if (active != null && active.IsRecording)
        {
            active.Cancel();
        }

        RenderRecorderVisual(BtnWelcomeHotkeyGlow, PanelWelcomeHotkeyGlowTokens, _glowRecorder);
        RenderRecorderVisual(BtnWelcomeHotkeyLyrics, PanelWelcomeHotkeyLyricsTokens, _lyricsRecorder);

        if (restoreOsHotkeys)
        {
            RestoreOsHotkeysIfApplicable();
        }

        if (!string.IsNullOrEmpty(feedbackKey))
        {
            ShowShortcutFeedback(LocalizationService.Instance[feedbackKey], isError: false);
        }
    }

    private void RestoreOsHotkeysIfApplicable()
    {
        var hotkeyManager = App.Services?.GetService(typeof(IGlobalHotkeyManager)) as IGlobalHotkeyManager;
        var primary = App.PrimaryWindowInstance;
        if (hotkeyManager == null || primary == null) return;

        var hk = _settingsManager.Current.Hotkeys;
        if (!string.IsNullOrWhiteSpace(hk.ToggleGlow) || !string.IsNullOrWhiteSpace(hk.ToggleLyrics))
        {
            _ = Task.Run(() => hotkeyManager.RegisterHotkeys(
                hk,
                onGlowToggle: primary.ToggleEdgeGlowFromExternal,
                onLyricsToggle: primary.ToggleLyricsPanelFromExternal,
                onClickThroughToggle: () => { }));
        }
    }

    private void ResetShortcutTimeoutTimer()
    {
        if (DispatcherQueue == null) return;
        if (_hotkeyTimeoutTimer == null)
        {
            _hotkeyTimeoutTimer = DispatcherQueue.CreateTimer();
            _hotkeyTimeoutTimer.Interval = _glowRecorder.TimeoutDuration;
            _hotkeyTimeoutTimer.IsRepeating = false;
            _hotkeyTimeoutTimer.Tick += (_, _) =>
            {
                if (!IsShortcutRecordingActive) return;
                _activeRecorder?.HandleTimeout();
                _activeRecorder = null;
                RenderRecorderVisual(BtnWelcomeHotkeyGlow, PanelWelcomeHotkeyGlowTokens, _glowRecorder);
                RenderRecorderVisual(BtnWelcomeHotkeyLyrics, PanelWelcomeHotkeyLyricsTokens, _lyricsRecorder);
                RestoreOsHotkeysIfApplicable();
                ShowShortcutFeedback(LocalizationService.Instance["Hotkey_Timeout"], isError: false);
            };
        }

        _hotkeyTimeoutTimer.Stop();
        _hotkeyTimeoutTimer.Start();
    }

    private void StopShortcutTimeoutTimer() => _hotkeyTimeoutTimer?.Stop();

    private void RenderRecorderVisual(Button button, StackPanel panel, KeystrokeRecorderSession session)
    {
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
            panel.Children.Add(placeholder);
            return;
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!session.IsRecording && i > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "+",
                    FontSize = 11.5,
                    Opacity = 0.6,
                    VerticalAlignment = VerticalAlignment.Center
                });
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
                if (resources.TryGetValue("AccentFillColorDefaultBrush", out var activeBg) && activeBg is Brush bg)
                {
                    keycap.Background = bg;
                    keycap.BorderBrush = bg;
                }
                if (resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var activeFg) && activeFg is Brush fg)
                {
                    label.Foreground = fg;
                }
            }
            else
            {
                if (resources.TryGetValue("SubtleFillColorSecondaryBrush", out var idleBg) && idleBg is Brush bg)
                    keycap.Background = bg;
                if (resources.TryGetValue("ControlStrokeColorDefaultBrush", out var idleBorder) && idleBorder is Brush border)
                    keycap.BorderBrush = border;
                if (resources.TryGetValue("TextFillColorPrimaryBrush", out var idleFg) && idleFg is Brush fg)
                    label.Foreground = fg;
            }

            keycap.Child = label;
            panel.Children.Add(keycap);
        }
    }

    private void ShowShortcutFeedback(string message, bool isError)
    {
        TxtWelcomeHotkeyFeedback.Text = message;
        TxtWelcomeHotkeyFeedback.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        if (Application.Current.Resources.TryGetValue(
                isError ? "SystemFillColorCriticalBrush" : "LyrowSuccessBrush",
                out var brushObj) && brushObj is Brush brush)
        {
            TxtWelcomeHotkeyFeedback.Foreground = brush;
        }
    }

    #endregion

    private void RefreshSummaryCard()
    {
        var loc = LocalizationService.Instance;
        string langDisplay = LocalizationService.SupportedLanguages
            .FirstOrDefault(l => string.Equals(l.Code, _coordinator.SelectedLanguage, StringComparison.OrdinalIgnoreCase)).NativeName
            ?? _coordinator.SelectedLanguage;

        TxtSummaryLanguageVal.Text = langDisplay;
        TxtSummaryThemeVal.Text = _coordinator.SelectedTheme switch
        {
            ThemeMode.Dark => loc["Welcome_Theme_DarkShort"],
            ThemeMode.Light => loc["Welcome_Theme_LightShort"],
            _ => loc["Welcome_Theme_SystemShort"]
        };
        TxtSummaryEdgeLightVal.Text = _coordinator.EnableEdgeLight ? loc["WelcomeV4_State_On"] : loc["WelcomeV4_State_Off"];
        TxtSummaryDynamicBgVal.Text = _coordinator.EnableDynamicBackground ? loc["WelcomeV4_State_On"] : loc["WelcomeV4_State_Off"];
        TxtSummaryReactivityVal.Text = _coordinator.SelectedFftSize switch
        {
            4096 => loc["WelcomeV4_Sens_Calm"],
            1024 => loc["WelcomeV4_Sens_Expressive"],
            _ => loc["WelcomeV4_Sens_Balanced"]
        };

        string glowText = string.IsNullOrWhiteSpace(_coordinator.SelectedShortcutGlow)
            ? loc["WelcomeV4_Shortcuts_NotSet"]
            : _coordinator.SelectedShortcutGlow;
        string lyricsText = string.IsNullOrWhiteSpace(_coordinator.SelectedShortcutLyrics)
            ? loc["WelcomeV4_Shortcuts_NotSet"]
            : _coordinator.SelectedShortcutLyrics;
        TxtSummaryShortcutsVal.Text = $"{glowText}  •  {lyricsText}";
    }

    private async void OnStartListeningClicked(object sender, RoutedEventArgs e)
    {
        await ExecuteStartListeningAsync();
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsShortcutRecordingActive)
        {
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            _ = DismissWithoutSavingAsync();
        }
    }

    private async void OnWelcomeWindowClosed(object sender, WindowEventArgs args)
    {
        StopReactivityAnimation();
        StopShortcutTimeoutTimer();
        if (_isClosingHandled) return;
        _isClosingHandled = true;

        LocalizationService.Instance.SetLanguage(_coordinator.InitialLanguage);
        App.PrimaryWindowInstance?.ApplyTheme(_coordinator.InitialTheme);
        RestoreOsHotkeysIfApplicable();

        await _coordinator.CloseWindowAsync();
        _onFinishedCallback?.Invoke(WelcomeCompletionReason.WindowClosed);
    }

    /// <summary>
    /// Validates and persists all Welcome V4 preferences, applies them to the running app,
    /// and launches/returns to the main Lyrow window. Keeps Welcome open with an error message if saving fails.
    /// </summary>
    private async Task ExecuteStartListeningAsync()
    {
        if (_isClosingHandled) return;

        if (IsShortcutRecordingActive)
        {
            CancelActiveShortcutRecording(restoreOsHotkeys: false, feedbackKey: null);
        }

        BtnWelcomeStartListening.IsEnabled = false;
        BtnWelcomeFinishFooter.IsEnabled = false;
        TxtWelcomeSaveError.Visibility = Visibility.Collapsed;

        bool saved = await _coordinator.CompleteAsync(CancellationToken.None);
        if (!saved)
        {
            BtnWelcomeStartListening.IsEnabled = true;
            BtnWelcomeFinishFooter.IsEnabled = true;
            TxtWelcomeSaveError.Text = _coordinator.LastError ?? LocalizationService.Instance["WelcomeV4_SaveError"];
            TxtWelcomeSaveError.Visibility = Visibility.Visible;
            return;
        }

        _isClosingHandled = true;
        StopReactivityAnimation();
        StopShortcutTimeoutTimer();

        LocalizationService.Instance.SetLanguage(_coordinator.SelectedLanguage);
        App.PrimaryWindowInstance?.ApplyTheme(_coordinator.SelectedTheme);

        _onFinishedCallback?.Invoke(WelcomeCompletionReason.Completed);

        try
        {
            Close();
        }
        catch { }
    }

    private async Task ExecuteUseDefaultsAsync()
    {
        if (_isClosingHandled) return;
        _isClosingHandled = true;
        StopReactivityAnimation();
        StopShortcutTimeoutTimer();

        LocalizationService.Instance.SetLanguage(_coordinator.InitialLanguage);
        App.PrimaryWindowInstance?.ApplyTheme(_coordinator.InitialTheme);

        await _coordinator.SkipAsync(CancellationToken.None);
        _onFinishedCallback?.Invoke(WelcomeCompletionReason.Skipped);

        try
        {
            Close();
        }
        catch { }
    }

    /// <summary>
    /// Escape close handler: discards temporary Welcome edits, restores pre-Welcome
    /// language &amp; theme without marking Welcome as completed, and closes the window.
    /// </summary>
    private async Task DismissWithoutSavingAsync()
    {
        if (_isClosingHandled) return;
        _isClosingHandled = true;
        StopReactivityAnimation();
        StopShortcutTimeoutTimer();

        BtnWelcomeStartListening.IsEnabled = false;

        LocalizationService.Instance.SetLanguage(_coordinator.InitialLanguage);
        App.PrimaryWindowInstance?.ApplyTheme(_coordinator.InitialTheme);
        RestoreOsHotkeysIfApplicable();

        await _coordinator.CloseWindowAsync(CancellationToken.None);
        _onFinishedCallback?.Invoke(WelcomeCompletionReason.WindowClosed);

        try
        {
            Close();
        }
        catch { }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        Title = loc["Welcome_WindowTitle"];

        // Stepper labels
        TxtWelcomeTab0.Text = loc["WelcomeV4_Tab_Start"];
        TxtWelcomeTab1.Text = loc["WelcomeV4_Tab_Appearance"];
        TxtWelcomeTab2.Text = loc["WelcomeV4_Tab_Reactivity"];
        TxtWelcomeTab3.Text = loc["WelcomeV4_Tab_Shortcuts"];
        TxtWelcomeTab4.Text = loc["WelcomeV4_Tab_Finish"];

        // Tab 0
        TxtV4StartTitle.Text = loc["WelcomeV4_Start_Title"];
        TxtV4StartSubtitle.Text = loc["WelcomeV4_Start_Subtitle"];
        TxtPrefLanguageLabel.Text = loc["Welcome_Pref_Language"];
        TxtPrefLanguageDesc.Text = loc["Settings_LanguageDesc"];
        TxtPrefThemeLabel.Text = loc["Welcome_Pref_Theme"];
        TxtWelcomeThemeSystem.Text = loc["Welcome_Theme_SystemShort"];
        TxtWelcomeThemeDark.Text = loc["Welcome_Theme_DarkShort"];
        TxtWelcomeThemeLight.Text = loc["Welcome_Theme_LightShort"];
        AutomationProperties.SetName(BtnWelcomeThemeSystem, loc["Welcome_Theme_SystemShort"]);
        AutomationProperties.SetName(BtnWelcomeThemeDark, loc["Welcome_Theme_DarkShort"]);
        AutomationProperties.SetName(BtnWelcomeThemeLight, loc["Welcome_Theme_LightShort"]);

        // Tab 1
        TxtV4AppearanceTitle.Text = loc["WelcomeV4_Appearance_Title"];
        TxtV4AppearanceSubtitle.Text = loc["WelcomeV4_Appearance_Subtitle"];
        TxtPreviewLyricPrev.Text = loc["Welcome_V47_PreviewLyricPrev"];
        TxtPreviewLyricActive.Text = loc["Welcome_V47_PreviewLyricActive"];
        TxtPreviewLyricNext.Text = loc["Welcome_V47_PreviewLyricNext"];
        TxtWelcomeEdgeLightTitle.Text = loc["Welcome_Pref_EdgeLightShort"];
        TxtWelcomeEdgeLightDesc.Text = loc["WelcomeV4_EdgeLight_Desc"];
        TxtWelcomeDynamicBgTitle.Text = loc["Welcome_Pref_DynamicBgShort"];
        TxtWelcomeDynamicBgDesc.Text = loc["WelcomeV4_DynamicBg_Desc"];

        // Tab 2
        TxtV4ReactivityTitle.Text = loc["WelcomeV4_Reactivity_Title"];
        TxtV4ReactivitySubtitle.Text = loc["WelcomeV4_Reactivity_Subtitle"];
        TxtWelcomeSensCalm.Text = loc["WelcomeV4_Sens_Calm"];
        TxtWelcomeSensCalmDesc.Text = loc["WelcomeV4_Sens_CalmDesc"];
        TxtWelcomeSensBalanced.Text = loc["WelcomeV4_Sens_Balanced"];
        TxtWelcomeSensBalancedDesc.Text = loc["WelcomeV4_Sens_BalancedDesc"];
        TxtWelcomeSensExpressive.Text = loc["WelcomeV4_Sens_Expressive"];
        TxtWelcomeSensExpressiveDesc.Text = loc["WelcomeV4_Sens_ExpressiveDesc"];
        TxtWelcomeSensRecommended.Text = loc["WelcomeV4_Badge_Recommended"];
        if (_coordinator.CurrentStep == WelcomeStep.Experience)
        {
            UpdateReactivityPreviewAndAnimation();
        }

        // Tab 3
        TxtV4ShortcutsTitle.Text = loc["WelcomeV4_Shortcuts_Title"];
        TxtV4ShortcutsSubtitle.Text = loc["WelcomeV4_Shortcuts_Subtitle"];
        TxtWelcomeShortcutGlowLabel.Text = loc["Settings_ShortcutGlow"];
        TxtWelcomeShortcutLyricsLabel.Text = loc["Settings_ShortcutLyrics"];
        BtnWelcomeDefaultShortcuts.Content = loc["WelcomeV4_Shortcuts_UseDefaults"];

        // Tab 4
        TxtV4FinishTitle.Text = loc["WelcomeV4_Finish_Title"];
        TxtV4FinishSubtitle.Text = loc["WelcomeV4_Finish_Subtitle"];
        TxtSummaryLanguageKey.Text = loc["Welcome_Pref_Language"];
        TxtSummaryThemeKey.Text = loc["Welcome_Pref_Theme"];
        TxtSummaryEdgeLightKey.Text = loc["Welcome_Pref_EdgeLightShort"];
        TxtSummaryDynamicBgKey.Text = loc["Welcome_Pref_DynamicBgShort"];
        TxtSummaryReactivityKey.Text = loc["WelcomeV4_Reactivity_Title"];
        TxtSummaryShortcutsKey.Text = loc["WelcomeV4_Tab_Shortcuts"];
        TxtBtnStartListening.Text = loc["WelcomeV4_Btn_Launch"];
        AutomationProperties.SetName(BtnWelcomeStartListening, loc["WelcomeV4_Btn_Launch"]);

        // Footer
        BtnWelcomeUseDefaults.Content = loc["WelcomeV4_Btn_UseDefaults"];
        BtnWelcomeBack.Content = loc["WelcomeV4_Btn_Back"];
        BtnWelcomeNext.Content = loc["WelcomeV4_Btn_Next"];
        BtnWelcomeFinishFooter.Content = loc["WelcomeV4_Btn_Start"];
        AutomationProperties.SetName(BtnWelcomeFinishFooter, loc["WelcomeV4_Btn_Start"]);
    }
}
