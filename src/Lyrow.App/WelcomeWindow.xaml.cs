using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App;

/// <summary>
/// Windows 11 Fluent Design First-Run Welcome &amp; Onboarding Window (Lyrow v4.6).
/// Wraps <see cref="WelcomeOnboardingCoordinator"/> with live theme preview,
/// non-blocking audio device discovery, keyboard accessibility, and safe lifecycle completion.
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly WelcomeOnboardingCoordinator _coordinator;
    private readonly ISettingsManager _settingsManager;
    private readonly Action<WelcomeCompletionReason>? _onFinishedCallback;
    private bool _isUpdatingUi;
    private bool _isClosingHandled;

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

        ConfigureWindowChromeAndCenteredBounds();
        ApplyTheme(_coordinator.SelectedTheme);
        ApplyLocalization();
        WireControls();
        SyncStepVisuals(animate: false);

        _ = LoadAudioDevicesNonBlockingAsync();
    }

    public WelcomeOnboardingCoordinator Coordinator => _coordinator;

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

        int targetWidth = (int)Math.Round(820 * scale);
        int targetHeight = (int)Math.Round(580 * scale);

        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            int maxWidth = Math.Max(480, workArea.Width - (int)Math.Round(48 * scale));
            int maxHeight = Math.Max(420, workArea.Height - (int)Math.Round(48 * scale));
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
            overlapped.PreferredMinimumWidth = (int)Math.Round(680 * scale);
            overlapped.PreferredMinimumHeight = (int)Math.Round(480 * scale);
        }
    }

    private void WireControls()
    {
        BtnWelcomeGetStarted.Click += (_, _) => _coordinator.GetStarted();
        BtnWelcomeContinue.Click += (_, _) => _coordinator.Continue();
        BtnWelcomeBack.Click += (_, _) => _coordinator.Back();
        BtnWelcomeSkip.Click += async (_, _) => await CompleteAndCloseAsync(_coordinator.SkipAsync);
        BtnWelcomeStartListening.Click += async (_, _) => await CompleteAndCloseAsync(_coordinator.CompleteAsync);

        BtnThemeCardSystem.Click += (_, _) => SelectThemeLive(ThemeMode.SystemDefault);
        BtnThemeCardDark.Click += (_, _) => SelectThemeLive(ThemeMode.Dark);
        BtnThemeCardLight.Click += (_, _) => SelectThemeLive(ThemeMode.Light);

        ChkWelcomeEdgeLight.Checked += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableEdgeLight = true;
        };
        ChkWelcomeEdgeLight.Unchecked += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableEdgeLight = false;
        };

        ChkWelcomeDynamicBg.Checked += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableDynamicBackground = true;
        };
        ChkWelcomeDynamicBg.Unchecked += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableDynamicBackground = false;
        };

        _coordinator.StepChanged += (_, _) => DispatcherQueue.TryEnqueue(() => SyncStepVisuals(animate: true));
        _coordinator.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(SyncOptionStates);

        WelcomeRootLayout.KeyDown += OnRootKeyDown;
        Closed += OnWelcomeWindowClosed;
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && _coordinator.IsManualReopen)
        {
            e.Handled = true;
            _ = CompleteAndCloseAsync(_coordinator.CloseWindowAsync);
        }
    }

    private async void OnWelcomeWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isClosingHandled) return;
        _isClosingHandled = true;
        await _coordinator.CloseWindowAsync();
        _onFinishedCallback?.Invoke(WelcomeCompletionReason.WindowClosed);
    }

    private async Task CompleteAndCloseAsync(Func<CancellationToken, Task<bool>> finalizeAction)
    {
        if (_isClosingHandled) return;
        _isClosingHandled = true;

        BtnWelcomeStartListening.IsEnabled = false;
        BtnWelcomeSkip.IsEnabled = false;
        BtnWelcomeContinue.IsEnabled = false;

        WelcomeCompletionReason reason = WelcomeCompletionReason.Completed;
        _coordinator.OnboardingFinished += (_, r) => reason = r;

        await finalizeAction(CancellationToken.None);
        _onFinishedCallback?.Invoke(reason);

        try
        {
            Close();
        }
        catch { }
    }

    private void SelectThemeLive(ThemeMode mode)
    {
        _coordinator.SelectedTheme = mode;
        ApplyTheme(mode);
        App.PrimaryWindowInstance?.ApplyTheme(mode);
        UpdateThemeCardSelectionVisuals();
    }

    private async Task LoadAudioDevicesNonBlockingAsync()
    {
        await _coordinator.RefreshAudioDevicesAsync();
        DispatcherQueue.TryEnqueue(() =>
        {
            RebuildAudioDeviceOptions();
            SyncOptionStates();
        });
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;
        Title = loc["Welcome_WindowTitle"];

        BtnWelcomeGetStarted.Content = loc["Welcome_Btn_GetStarted"];
        BtnWelcomeSkip.Content = loc["Welcome_Btn_Skip"];
        BtnWelcomeBack.Content = loc["Welcome_Btn_Back"];
        BtnWelcomeContinue.Content = loc["Welcome_Btn_Continue"];
        BtnWelcomeStartListening.Content = loc["Welcome_Btn_StartListening"];

        AutomationProperties.SetName(BtnWelcomeGetStarted, loc["Welcome_Btn_GetStarted"]);
        AutomationProperties.SetName(BtnWelcomeSkip, loc["Welcome_Btn_Skip"]);
        AutomationProperties.SetName(BtnWelcomeBack, loc["Welcome_Btn_Back"]);
        AutomationProperties.SetName(BtnWelcomeContinue, loc["Welcome_Btn_Continue"]);
        AutomationProperties.SetName(BtnWelcomeStartListening, loc["Welcome_Btn_StartListening"]);

        TxtWelcomeStep1Title.Text = loc["Welcome_Step1_Title"];
        TxtWelcomeStep1Subtitle.Text = loc["Welcome_Step1_Subtitle"];
        TxtWelcomeStep1Desc.Text = loc["Welcome_Step1_Desc"];
        TxtWelcomeFeatLyrics.Text = loc["Welcome_Feat_Lyrics"];
        TxtWelcomeFeatLyricsDesc.Text = loc["Welcome_Feat_LyricsDesc"];
        TxtWelcomeFeatVisuals.Text = loc["Welcome_Feat_Visuals"];
        TxtWelcomeFeatVisualsDesc.Text = loc["Welcome_Feat_VisualsDesc"];
        TxtWelcomeFeatLighting.Text = loc["Welcome_Feat_Lighting"];
        TxtWelcomeFeatLightingDesc.Text = loc["Welcome_Feat_LightingDesc"];

        TxtWelcomeStep2Title.Text = loc["Welcome_Step2_Title"];
        TxtWelcomeStep2Desc.Text = loc["Welcome_Step2_Desc"];
        TxtWelcomeThemeSystem.Text = loc["Welcome_Theme_System"];
        TxtWelcomeThemeSystemDesc.Text = loc["Welcome_Theme_SystemDesc"];
        TxtWelcomeThemeDark.Text = loc["Welcome_Theme_Dark"];
        TxtWelcomeThemeDarkDesc.Text = loc["Welcome_Theme_DarkDesc"];
        TxtWelcomeThemeLight.Text = loc["Welcome_Theme_Light"];
        TxtWelcomeThemeLightDesc.Text = loc["Welcome_Theme_LightDesc"];

        TxtWelcomeStep3Title.Text = loc["Welcome_Step3_Title"];
        TxtWelcomeStep3Desc.Text = loc["Welcome_Step3_Desc"];
        TxtWelcomeEdgeLightLabel.Text = loc["Welcome_EdgeLight_Label"];
        TxtWelcomeEdgeLightDesc.Text = loc["Welcome_EdgeLight_Desc"];
        TxtWelcomeEdgeLightNoAudio.Text = loc["Welcome_EdgeLight_NoAudioNotice"];
        TxtWelcomeDynamicBgLabel.Text = loc["Welcome_DynamicBg_Label"];
        TxtWelcomeDynamicBgDesc.Text = loc["Welcome_DynamicBg_Desc"];
        TxtWelcomeCompactTitle.Text = loc["Welcome_CompactMode_Title"];
        TxtWelcomeCompactDesc.Text = loc["Welcome_CompactMode_Desc"];

        TxtWelcomeStep4Title.Text = loc["Welcome_Step4_Title"];
        TxtWelcomeStep4Desc.Text = loc["Welcome_Step4_Desc"];
        TxtWelcomeNoAudioDevices.Text = loc["Welcome_Audio_NoDevices"];
        TxtWelcomeAudioEdgeLightNote.Text = loc["Welcome_Audio_EdgeLightNote"];

        TxtWelcomeStep5Title.Text = loc["Welcome_Step5_Title"];
        TxtWelcomeStep5Subtitle.Text = loc["Welcome_Step5_Subtitle"];
        TxtWelcomeStep5Desc.Text = loc["Welcome_Step5_Desc"];
        TxtWelcomeSummaryTitle.Text = loc["Welcome_Summary_Title"];
        TxtSummaryLabelTheme.Text = loc["Welcome_Summary_Theme"];
        TxtSummaryLabelEdgeLight.Text = loc["Welcome_Summary_EdgeLight"];
        TxtSummaryLabelDynamicBg.Text = loc["Welcome_Summary_DynamicBg"];
        TxtSummaryLabelAudio.Text = loc["Welcome_Summary_Audio"];
    }

    private void RebuildAudioDeviceOptions()
    {
        var loc = LocalizationService.Instance;
        PanelWelcomeAudioDevices.Children.Clear();

        var devices = _coordinator.AvailableAudioDevices;
        BorderWelcomeNoAudioDevices.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Always offer optional "Windows System Default Output" card first
        PanelWelcomeAudioDevices.Children.Add(CreateAudioDeviceCard(
            deviceId: null,
            title: loc["Welcome_Audio_DefaultOption"],
            subtitle: loc["Welcome_Audio_DefaultDesc"],
            glyph: "\uE7F5"));

        foreach (var dev in devices)
        {
            string label = dev.IsDefault
                ? $"{dev.Name} ({loc["Welcome_Audio_DefaultBadge"]})"
                : dev.Name;
            string lower = dev.Name.ToLowerInvariant();
            string glyph = lower.Contains("headphone") || lower.Contains("headset")
                ? "\uE7F6"
                : (lower.Contains("bluetooth") ? "\uE702" : "\uE767");

            PanelWelcomeAudioDevices.Children.Add(CreateAudioDeviceCard(
                deviceId: dev.Id,
                title: label,
                subtitle: string.Empty,
                glyph: glyph));
        }
    }

    private Button CreateAudioDeviceCard(string? deviceId, string title, string subtitle, string glyph)
    {
        bool isSelected = string.IsNullOrWhiteSpace(_coordinator.SelectedAudioDeviceId)
            ? string.IsNullOrWhiteSpace(deviceId)
            : string.Equals(_coordinator.SelectedAudioDeviceId, deviceId, StringComparison.OrdinalIgnoreCase);

        var accentBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var defaultStroke = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        var cardBg = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];

        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            FontSize = 13.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            textStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11.5,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });
        }

        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(textStack, 1);

        var checkIcon = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 14,
            Foreground = accentBrush,
            Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(checkIcon, 2);

        row.Children.Add(icon);
        row.Children.Add(textStack);
        row.Children.Add(checkIcon);

        var border = new Border
        {
            Padding = new Thickness(14, 11, 14, 11),
            CornerRadius = new CornerRadius(10),
            Background = cardBg,
            BorderBrush = isSelected ? accentBrush : defaultStroke,
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            Child = row
        };

        var btn = new Button
        {
            Tag = deviceId,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            Content = border
        };
        AutomationProperties.SetName(btn, title);

        btn.Click += (_, _) =>
        {
            _coordinator.SelectedAudioDeviceId = deviceId;
            RebuildAudioDeviceOptions();
        };

        return btn;
    }

    private void SyncStepVisuals(bool animate)
    {
        WelcomeStep step = _coordinator.CurrentStep;
        int idx = _coordinator.CurrentStepIndex;

        StepPanelWelcome.Visibility = step == WelcomeStep.Welcome ? Visibility.Visible : Visibility.Collapsed;
        StepPanelAppearance.Visibility = step == WelcomeStep.Appearance ? Visibility.Visible : Visibility.Collapsed;
        StepPanelExperience.Visibility = step == WelcomeStep.Experience ? Visibility.Visible : Visibility.Collapsed;
        StepPanelAudio.Visibility = step == WelcomeStep.Audio ? Visibility.Visible : Visibility.Collapsed;
        StepPanelReady.Visibility = step == WelcomeStep.Ready ? Visibility.Visible : Visibility.Collapsed;

        WelcomeStepScrollViewer.ChangeView(0, 0, null, disableAnimation: true);

        // Progress dots
        var dots = new[] { StepDot0, StepDot1, StepDot2, StepDot3, StepDot4 };
        var activeBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var inactiveBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].Width = i == idx ? 28 : 18;
            dots[i].Background = i <= idx ? activeBrush : inactiveBrush;
            dots[i].Opacity = i < idx ? 0.65 : 1.0;
        }

        TxtWelcomeStepCounter.Text = string.Format(
            LocalizationService.Instance["Welcome_StepCounter"],
            idx + 1,
            WelcomeOnboardingCoordinator.TotalSteps);

        // Navigation buttons
        BtnWelcomeBack.Visibility = _coordinator.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        BtnWelcomeSkip.Visibility = step == WelcomeStep.Ready ? Visibility.Collapsed : Visibility.Visible;
        BtnWelcomeGetStarted.Visibility = step == WelcomeStep.Welcome ? Visibility.Visible : Visibility.Collapsed;
        BtnWelcomeContinue.Visibility = (step is WelcomeStep.Appearance or WelcomeStep.Experience or WelcomeStep.Audio)
            ? Visibility.Visible
            : Visibility.Collapsed;
        BtnWelcomeStartListening.Visibility = step == WelcomeStep.Ready ? Visibility.Visible : Visibility.Collapsed;

        SyncOptionStates();

        if (animate && _settingsManager.Current.EnableAnimations)
        {
            AnimateStepTransition();
        }
    }

    private void SyncOptionStates()
    {
        _isUpdatingUi = true;
        try
        {
            UpdateThemeCardSelectionVisuals();
            ChkWelcomeEdgeLight.IsChecked = _coordinator.EnableEdgeLight;
            ChkWelcomeDynamicBg.IsChecked = _coordinator.EnableDynamicBackground;
            TxtWelcomeEdgeLightNoAudio.Visibility = (_coordinator.EnableEdgeLight && !_coordinator.HasAudioDevices)
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Update Ready step summary values
            var loc = LocalizationService.Instance;
            TxtSummaryValueTheme.Text = _coordinator.SelectedTheme switch
            {
                ThemeMode.Dark => loc["Welcome_Theme_Dark"],
                ThemeMode.Light => loc["Welcome_Theme_Light"],
                _ => loc["Welcome_Theme_System"]
            };
            TxtSummaryValueEdgeLight.Text = _coordinator.EnableEdgeLight
                ? loc["Welcome_State_Enabled"]
                : loc["Welcome_State_Disabled"];
            TxtSummaryValueDynamicBg.Text = _coordinator.EnableDynamicBackground
                ? loc["Welcome_State_Enabled"]
                : loc["Welcome_State_Disabled"];
            TxtSummaryValueAudio.Text = _coordinator.GetSelectedAudioDeviceDisplayName(loc["Welcome_Audio_DefaultBadge"]);
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void UpdateThemeCardSelectionVisuals()
    {
        var accentBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var defaultStroke = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

        void ApplyCard(Border border, FontIcon check, bool active)
        {
            border.BorderBrush = active ? accentBrush : defaultStroke;
            border.BorderThickness = new Thickness(active ? 2 : 1);
            check.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }

        ApplyCard(BorderThemeCardSystem, IconCheckThemeSystem, _coordinator.SelectedTheme == ThemeMode.SystemDefault);
        ApplyCard(BorderThemeCardDark, IconCheckThemeDark, _coordinator.SelectedTheme == ThemeMode.Dark);
        ApplyCard(BorderThemeCardLight, IconCheckThemeLight, _coordinator.SelectedTheme == ThemeMode.Light);
    }

    private void AnimateStepTransition()
    {
        StepContentHost.Opacity = 0.0;
        StepContentTranslate.Y = 10;

        var sb = new Storyboard();

        var fade = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, StepContentHost);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = 10.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, StepContentTranslate);
        Storyboard.SetTargetProperty(slide, "Y");

        sb.Children.Add(fade);
        sb.Children.Add(slide);
        sb.Begin();
    }
}
