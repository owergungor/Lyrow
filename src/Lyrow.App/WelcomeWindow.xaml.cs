using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App;

/// <summary>
/// Windows 11 Fluent Design Immersive Single-Screen Welcome Window (Lyrow v4.7).
/// Showcases a live-feeling lyrics, dynamic atmosphere, and sound-reactive Edge Light hero preview
/// with a vibrant primary CTA, subtle secondary dismiss option, EN/TR localization, and DPI-safe layout.
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly WelcomeOnboardingCoordinator _coordinator;
    private readonly ISettingsManager _settingsManager;
    private readonly Action<WelcomeCompletionReason>? _onFinishedCallback;
    private Storyboard? _ambientStoryboard;
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
        ApplyTheme(_settingsManager.Current.Theme);
        ApplyLocalization();
        WireControls();
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

        int targetWidth = (int)Math.Round(860 * scale);
        int targetHeight = (int)Math.Round(580 * scale);

        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            int maxWidth = Math.Max(560, workArea.Width - (int)Math.Round(48 * scale));
            int maxHeight = Math.Max(480, workArea.Height - (int)Math.Round(48 * scale));
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
            overlapped.PreferredMinimumWidth = (int)Math.Round(660 * scale);
            overlapped.PreferredMinimumHeight = (int)Math.Round(510 * scale);
        }
    }

    private void WireControls()
    {
        BtnWelcomeStartListening.Click += async (_, _) => await CompleteAndCloseAsync(_coordinator.CompleteAsync);
        BtnWelcomeCustomizeLater.Click += async (_, _) => await CompleteAndCloseAsync(_coordinator.SkipAsync);

        WelcomeRootLayout.KeyDown += OnRootKeyDown;
        Closed += OnWelcomeWindowClosed;
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = CompleteAndCloseAsync(_coordinator.CompleteAsync);
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            var action = _coordinator.IsManualReopen
                ? (Func<CancellationToken, Task<bool>>)_coordinator.CloseWindowAsync
                : _coordinator.SkipAsync;
            _ = CompleteAndCloseAsync(action);
        }
    }

    private async void OnWelcomeWindowClosed(object sender, WindowEventArgs args)
    {
        StopAmbientAnimations();
        if (_isClosingHandled) return;
        _isClosingHandled = true;
        await _coordinator.CloseWindowAsync();
        _onFinishedCallback?.Invoke(WelcomeCompletionReason.WindowClosed);
    }

    private async Task CompleteAndCloseAsync(Func<CancellationToken, Task<bool>> finalizeAction)
    {
        if (_isClosingHandled) return;
        _isClosingHandled = true;
        StopAmbientAnimations();

        BtnWelcomeStartListening.IsEnabled = false;
        BtnWelcomeCustomizeLater.IsEnabled = false;

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
        TxtWelcomeCue1.Text = loc["Welcome_V47_Cue1"];
        TxtWelcomeCue2.Text = loc["Welcome_V47_Cue2"];
        TxtWelcomeCue3.Text = loc["Welcome_V47_Cue3"];

        TxtBtnStartListening.Text = loc["Welcome_Btn_StartListening"];
        BtnWelcomeCustomizeLater.Content = loc["Welcome_Btn_CustomizeLater"];

        AutomationProperties.SetName(BtnWelcomeStartListening, loc["Welcome_Btn_StartListening"]);
        AutomationProperties.SetName(BtnWelcomeCustomizeLater, loc["Welcome_Btn_CustomizeLater"]);
    }

    private void StartSubtleAnimationsIfEnabled()
    {
        if (!_settingsManager.Current.EnableAnimations) return;

        // 1. Fast, smooth entrance (180 ms)
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

        // 2. Gentle, GPU-friendly ambient breathing + spectrum bar motion
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
