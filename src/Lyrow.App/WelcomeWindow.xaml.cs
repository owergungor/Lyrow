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
/// Windows 11 Fluent Design Immersive Single-Screen Welcome Window (Lyrow v4.7 V3).
/// Combines the cinematic live music preview hero with compact live-previewing Language,
/// Theme, Edge Light, and Dynamic Background controls and a prominent primary CTA.
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly WelcomeOnboardingCoordinator _coordinator;
    private readonly ISettingsManager _settingsManager;
    private readonly Action<WelcomeCompletionReason>? _onFinishedCallback;
    private Storyboard? _ambientStoryboard;
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
        SyncPreferenceControls();
        StartSubtleAnimationsIfEnabled();
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

        int targetWidth = (int)Math.Round(880 * scale);
        int targetHeight = (int)Math.Round(576 * scale);

        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            int maxWidth = Math.Max(580, workArea.Width - (int)Math.Round(44 * scale));
            int maxHeight = Math.Max(500, workArea.Height - (int)Math.Round(44 * scale));
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
            overlapped.PreferredMinimumWidth = (int)Math.Round(720 * scale);
            overlapped.PreferredMinimumHeight = (int)Math.Round(540 * scale);
        }
    }

    private void WireControls()
    {
        BtnWelcomeLangTr.Click += (_, _) => SelectLanguageLive("tr");
        BtnWelcomeLangEn.Click += (_, _) => SelectLanguageLive("en");

        BtnWelcomeThemeSystem.Click += (_, _) => SelectThemeLive(ThemeMode.SystemDefault);
        BtnWelcomeThemeDark.Click += (_, _) => SelectThemeLive(ThemeMode.Dark);
        BtnWelcomeThemeLight.Click += (_, _) => SelectThemeLive(ThemeMode.Light);

        ChkWelcomeEdgeLight.Checked += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableEdgeLight = true;
            SyncHeroPreviewEffects();
        };
        ChkWelcomeEdgeLight.Unchecked += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableEdgeLight = false;
            SyncHeroPreviewEffects();
        };

        ChkWelcomeDynamicBg.Checked += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableDynamicBackground = true;
            SyncHeroPreviewEffects();
        };
        ChkWelcomeDynamicBg.Unchecked += (_, _) =>
        {
            if (_isUpdatingUi) return;
            _coordinator.EnableDynamicBackground = false;
            SyncHeroPreviewEffects();
        };

        BtnWelcomeStartListening.Click += OnStartListeningClicked;

        WelcomeRootLayout.KeyDown += OnRootKeyDown;
        Closed += OnWelcomeWindowClosed;
    }

    private async void OnStartListeningClicked(object sender, RoutedEventArgs e)
    {
        await ExecuteStartListeningAsync();
    }

    private void SelectLanguageLive(string languageCode)
    {
        _coordinator.SelectedLanguage = languageCode;
        LocalizationService.Instance.SetLanguage(languageCode);
        ApplyLocalization();
        SyncSegmentedPillVisuals();
    }

    private void SelectThemeLive(ThemeMode mode)
    {
        _coordinator.SelectedTheme = mode;
        ApplyTheme(mode);
        App.PrimaryWindowInstance?.ApplyTheme(mode);
        SyncSegmentedPillVisuals();
    }

    private void SyncPreferenceControls()
    {
        _isUpdatingUi = true;
        try
        {
            ChkWelcomeEdgeLight.IsChecked = _coordinator.EnableEdgeLight;
            ChkWelcomeDynamicBg.IsChecked = _coordinator.EnableDynamicBackground;
            SyncSegmentedPillVisuals();
            SyncHeroPreviewEffects();
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void SyncSegmentedPillVisuals()
    {
        var activeBg = new SolidColorBrush(Color.FromArgb(46, 14, 165, 233));     // Crisp luminous cyan tint
        var activeBorder = new SolidColorBrush(Color.FromArgb(200, 56, 189, 248)); // Clear Fluent accent border
        var inactiveBg = new SolidColorBrush(Colors.Transparent);
        var inactiveBorder = new SolidColorBrush(Colors.Transparent);

        void ApplyPill(Button btn, bool active)
        {
            btn.Background = active ? activeBg : inactiveBg;
            btn.BorderBrush = active ? activeBorder : inactiveBorder;
            btn.FontWeight = active
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
        }

        bool isTr = string.Equals(_coordinator.SelectedLanguage, "tr", StringComparison.OrdinalIgnoreCase);
        ApplyPill(BtnWelcomeLangTr, isTr);
        ApplyPill(BtnWelcomeLangEn, !isTr);

        ApplyPill(BtnWelcomeThemeSystem, _coordinator.SelectedTheme == ThemeMode.SystemDefault);
        ApplyPill(BtnWelcomeThemeDark, _coordinator.SelectedTheme == ThemeMode.Dark);
        ApplyPill(BtnWelcomeThemeLight, _coordinator.SelectedTheme == ThemeMode.Light);
    }

    private void SyncHeroPreviewEffects()
    {
        // Subtle visual feedback in the hero stage while always keeping the showcase vibrant
        EdgeLightAuraOuter.Opacity = _coordinator.EnableEdgeLight ? 1.0 : 0.72;
        EdgeLightInnerRim.Opacity = _coordinator.EnableEdgeLight ? 0.75 : 0.35;
        AmbientBloomLayerRight.Opacity = _coordinator.EnableDynamicBackground ? 0.72 : 0.30;
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = ExecuteStartListeningAsync();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            _ = DismissWithoutSavingAsync();
        }
    }

    private async void OnWelcomeWindowClosed(object sender, WindowEventArgs args)
    {
        StopAmbientAnimations();
        if (_isClosingHandled) return;
        _isClosingHandled = true;

        // Roll back any temporary live preview edits and do NOT mark Welcome as completed
        LocalizationService.Instance.SetLanguage(_coordinator.InitialLanguage);
        App.PrimaryWindowInstance?.ApplyTheme(_coordinator.InitialTheme);

        await _coordinator.CloseWindowAsync();
        _onFinishedCallback?.Invoke(WelcomeCompletionReason.WindowClosed);
    }

    /// <summary>
    /// Single primary CTA handler: persists all selected preferences, applies chosen
    /// language &amp; theme, marks Welcome as completed, and launches/returns to the main Lyrow window.
    /// </summary>
    private async Task ExecuteStartListeningAsync()
    {
        if (_isClosingHandled) return;
        _isClosingHandled = true;
        StopAmbientAnimations();

        BtnWelcomeStartListening.IsEnabled = false;

        await _coordinator.CompleteAsync(CancellationToken.None);
        LocalizationService.Instance.SetLanguage(_coordinator.SelectedLanguage);
        App.PrimaryWindowInstance?.ApplyTheme(_coordinator.SelectedTheme);

        _onFinishedCallback?.Invoke(WelcomeCompletionReason.Completed);

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
        StopAmbientAnimations();

        BtnWelcomeStartListening.IsEnabled = false;

        LocalizationService.Instance.SetLanguage(_coordinator.InitialLanguage);
        App.PrimaryWindowInstance?.ApplyTheme(_coordinator.InitialTheme);

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

        TxtPreviewTrackTitle.Text = loc["Welcome_V47_PreviewTrack"];
        TxtPreviewArtist.Text = loc["Welcome_V47_PreviewArtist"];
        TxtPreviewLyricPrev.Text = loc["Welcome_V47_PreviewLyricPrev"];
        TxtPreviewLyricActive.Text = loc["Welcome_V47_PreviewLyricActive"];
        TxtPreviewLyricNext.Text = loc["Welcome_V47_PreviewLyricNext"];
        TxtPreviewLyricLater.Text = loc["Welcome_V47_PreviewLyricLater"];

        TxtWelcomeHeadline.Text = loc["Welcome_V47_Headline"];
        TxtWelcomeSubtitle.Text = loc["Welcome_V47_Subtitle"];

        TxtPrefLanguageLabel.Text = loc["Welcome_Pref_Language"];
        TxtPrefThemeLabel.Text = loc["Welcome_Pref_Theme"];
        TxtPrefExperienceLabel.Text = loc["Welcome_Pref_Experience"];

        BtnWelcomeThemeSystem.Content = loc["Welcome_Theme_SystemShort"];
        BtnWelcomeThemeDark.Content = loc["Welcome_Theme_DarkShort"];
        BtnWelcomeThemeLight.Content = loc["Welcome_Theme_LightShort"];
        AutomationProperties.SetName(BtnWelcomeThemeSystem, loc["Welcome_Theme_SystemShort"]);
        AutomationProperties.SetName(BtnWelcomeThemeDark, loc["Welcome_Theme_DarkShort"]);
        AutomationProperties.SetName(BtnWelcomeThemeLight, loc["Welcome_Theme_LightShort"]);

        ChkWelcomeEdgeLight.Content = loc["Welcome_Pref_EdgeLightShort"];
        ChkWelcomeDynamicBg.Content = loc["Welcome_Pref_DynamicBgShort"];
        AutomationProperties.SetName(ChkWelcomeEdgeLight, loc["Welcome_Pref_EdgeLightShort"]);
        AutomationProperties.SetName(ChkWelcomeDynamicBg, loc["Welcome_Pref_DynamicBgShort"]);

        TxtBtnStartListening.Text = loc["Welcome_Btn_StartListening"];
        AutomationProperties.SetName(BtnWelcomeStartListening, loc["Welcome_Btn_StartListening"]);
    }

    private void StartSubtleAnimationsIfEnabled()
    {
        if (!_settingsManager.Current.EnableAnimations) return;

        WelcomeContentHost.Opacity = 0.0;
        WelcomeContentTranslate.Y = 10;

        var entrance = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, WelcomeContentHost);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = 10.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, WelcomeContentTranslate);
        Storyboard.SetTargetProperty(slide, "Y");

        entrance.Children.Add(fade);
        entrance.Children.Add(slide);
        entrance.Begin();

        var ambient = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        var bloomPulse = new DoubleAnimation
        {
            From = 0.68,
            To = 0.96,
            Duration = new Duration(TimeSpan.FromMilliseconds(2600)),
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(bloomPulse, AmbientBloomLayer);
        Storyboard.SetTargetProperty(bloomPulse, "Opacity");
        ambient.Children.Add(bloomPulse);

        void AddEqPulse(DependencyObject target, double from, double to, int ms)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(ms)),
                AutoReverse = true,
                EnableDependentAnimation = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, "ScaleY");
            ambient.Children.Add(anim);
        }

        AddEqPulse(EqScale1, 0.45, 1.15, 760);
        AddEqPulse(EqScale2, 0.65, 1.05, 620);
        AddEqPulse(EqScale3, 0.40, 1.20, 840);
        AddEqPulse(EqScale4, 0.55, 1.00, 690);
        AddEqPulse(EqScale5, 0.50, 1.25, 780);

        _ambientStoryboard = ambient;
        _ambientStoryboard.Begin();
    }

    private void StopAmbientAnimations()
    {
        try
        {
            _ambientStoryboard?.Stop();
            _ambientStoryboard = null;
        }
        catch { }
    }
}
