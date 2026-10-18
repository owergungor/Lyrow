using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;

namespace Lyrow.App;

/// <summary>
/// Main application shell implementing Windows 11 native WinUI 3 design language,
/// Mica backdrop, NavigationView, and complete synchronization with Glow and Lyrics modules.
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
    private readonly IEventBus? _eventBus;
    private readonly IMediaSessionMonitor? _mediaMonitor;

    private readonly List<IDisposable> _subscriptions = new();
    private LyricsWindow? _lyricsWindow;
    private bool _isExplicitExit = false;
    private bool _isUpdatingToggles = false;

    public MainWindow(
        IModuleManager moduleManager,
        ISettingsManager settingsManager,
        IWindowPlatformAdapter? platformAdapter = null,
        IWindowsStartupManager? startupManager = null,
        IGlobalHotkeyManager? hotkeyManager = null,
        LyrowTrayIconManager? trayIconManager = null,
        GlowOverlayManager? glowOverlayManager = null,
        ArtworkBitmapManager? artworkBitmapManager = null,
        IToastNotificationService? toastService = null)
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
        _eventBus = App.Services?.GetService<IEventBus>();
        _mediaMonitor = App.Services?.GetService<IMediaSessionMonitor>();

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
        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(960, 700));
        }
        catch { }

        NavView.SelectedItem = NavHome;

        InitializeSettingsUi();
        BindModuleToggles();
        HookEvents();

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
            onOpenSettings: () => DispatcherQueue.TryEnqueue(() => { AppWindow.Show(); ShowPage(PageSettings); }),
            onExit: () => DispatcherQueue.TryEnqueue(RequestExit));

        Closed += OnWindowClosed;
    }

    private void InitializeSettingsUi()
    {
        var settings = _settingsManager.Current;

        // Startup State: Always ensure toggles start in OFF state as requested
        SwitchHomeGlow.IsOn = false;
        SwitchGlowMaster.IsOn = false;
        TxtGlowStatus.Text = "Stopped";
        TxtGlowStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LyrowStoppedBrush"];

        SwitchHomeLyrics.IsOn = false;
        SwitchLyricsMaster.IsOn = false;
        TxtLyricsStatus.Text = "Stopped";
        TxtLyricsStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LyrowStoppedBrush"];

        // Glow controls
        SliderGlowIntensity.Value = settings.GlowIntensity;
        TxtGlowIntensityVal.Text = $"{settings.GlowIntensity}%";
        SliderGlowThickness.Value = settings.GlowThickness;
        TxtGlowThicknessVal.Text = $"{settings.GlowThickness} px";
        SwitchAlbumArtSync.IsOn = settings.SyncWithAlbumArt;
        ChkMultiMonitor.IsChecked = settings.MultiMonitorGlow;

        // Lyrics controls
        SliderLyricsOffset.Value = settings.Verci.SyncOffsetMs;
        TxtPageLyricsOffset.Text = $"{settings.Verci.SyncOffsetMs:+0;-0;0} ms";
        SwitchLyricsAutoScroll.IsOn = settings.Verci.AutoScroll;

        // App Settings
        SwitchStartWithWindows.IsOn = settings.StartWithWindows;
        SwitchSystemTray.IsOn = settings.SystemTrayEnabled;

        TxtHotkeyGlow.Text = settings.Hotkeys.ToggleLumn;
        TxtHotkeyLyrics.Text = settings.Hotkeys.ToggleVerci;

        // Theme ComboBox
        ComboTheme.SelectedIndex = settings.Theme switch
        {
            ThemeMode.Dark => 1,
            ThemeMode.Light => 2,
            _ => 0
        };

        // Volume slider initial percentage
        SliderVolume.Value = 78;
        TxtVolumePercent.Text = "78%";
    }

    private void BindModuleToggles()
    {
        // Glow Toggles
        SwitchHomeGlow.Toggled += async (_, _) => await SetGlowStateAsync(SwitchHomeGlow.IsOn);
        SwitchGlowMaster.Toggled += async (_, _) => await SetGlowStateAsync(SwitchGlowMaster.IsOn);

        // Lyrics Toggles
        SwitchHomeLyrics.Toggled += async (_, _) => await SetLyricsStateAsync(SwitchHomeLyrics.IsOn);
        SwitchLyricsMaster.Toggled += async (_, _) => await SetLyricsStateAsync(SwitchLyricsMaster.IsOn);

        // Slider events
        SliderVolume.ValueChanged += (_, e) =>
        {
            int percent = (int)Math.Round(e.NewValue);
            TxtVolumePercent.Text = $"{percent}%";
        };

        SliderGlowIntensity.ValueChanged += (_, e) =>
        {
            int val = (int)Math.Round(e.NewValue);
            TxtGlowIntensityVal.Text = $"{val}%";
            _settingsManager.Current.GlowIntensity = val;
        };

        SliderGlowThickness.ValueChanged += (_, e) =>
        {
            int val = (int)Math.Round(e.NewValue);
            TxtGlowThicknessVal.Text = $"{val} px";
            _settingsManager.Current.GlowThickness = val;
        };

        SwitchAlbumArtSync.Toggled += (_, _) =>
        {
            _settingsManager.Current.SyncWithAlbumArt = SwitchAlbumArtSync.IsOn;
        };

        ChkMultiMonitor.Checked += (_, _) => { _settingsManager.Current.MultiMonitorGlow = true; };
        ChkMultiMonitor.Unchecked += (_, _) => { _settingsManager.Current.MultiMonitorGlow = false; };

        // Lyrics offset sliders
        SliderLyricsOffset.ValueChanged += (_, e) =>
        {
            int val = (int)Math.Round(e.NewValue);
            TxtPageLyricsOffset.Text = $"{val:+0;-0;0} ms";
            _settingsManager.Current.Verci.SyncOffsetMs = val;
            var verci = App.Services?.GetService<VerciModule>();
            verci?.SetSyncOffset(val);
        };

        BtnOffsetMinus500.Click += (_, _) => SliderLyricsOffset.Value -= 500;
        BtnOffsetPlus500.Click += (_, _) => SliderLyricsOffset.Value += 500;
        BtnOffsetReset.Click += (_, _) => SliderLyricsOffset.Value = 0;

        SwitchLyricsAutoScroll.Toggled += (_, _) =>
        {
            _settingsManager.Current.Verci.AutoScroll = SwitchLyricsAutoScroll.IsOn;
        };

        // App settings
        SwitchStartWithWindows.Toggled += (_, _) =>
        {
            bool enable = SwitchStartWithWindows.IsOn;
            _settingsManager.Current.StartWithWindows = enable;
            _startupManager.SetStartupEnabled(enable);
            ShowToast(enable ? "Lyrow will start with Windows." : "Startup with Windows disabled.", InfoBarSeverity.Success);
        };

        SwitchSystemTray.Toggled += (_, _) =>
        {
            _settingsManager.Current.SystemTrayEnabled = SwitchSystemTray.IsOn;
        };

        BtnSaveHotkeys.Click += async (_, _) =>
        {
            _settingsManager.Current.Hotkeys.ToggleLumn = TxtHotkeyGlow.Text.Trim();
            _settingsManager.Current.Hotkeys.ToggleVerci = TxtHotkeyLyrics.Text.Trim();
            await _settingsManager.SaveAsync(_settingsManager.Current);
            ShowToast("Hotkeys saved successfully.", InfoBarSeverity.Success);
        };

        BtnResetSettings.Click += async (_, _) =>
        {
            var def = LyrowSettings.CreateDefault();
            await _settingsManager.SaveAsync(def);
            InitializeSettingsUi();
            ShowToast("Settings reset to defaults.", InfoBarSeverity.Informational);
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

        // Media buttons
        BtnPlayPause.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TryTogglePlayPauseAsync();
        };
        BtnNextTrack.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TrySkipNextAsync();
        };
        BtnPrevTrack.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TrySkipPreviousAsync();
        };

        // Navigation and theme events
        NavView.SelectionChanged += NavView_SelectionChanged;
        ComboTheme.SelectionChanged += ComboTheme_SelectionChanged;
    }

    private async Task SetGlowStateAsync(bool active)
    {
        if (_isUpdatingToggles) return;
        _isUpdatingToggles = true;
        try
        {
            SwitchHomeGlow.IsOn = active;
            SwitchGlowMaster.IsOn = active;

            await _moduleManager.SetModuleActiveAsync("lumn", active);

            if (active)
            {
                TxtGlowStatus.Text = "Running (Active)";
                TxtGlowStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LyrowSuccessBrush"];
                _glowOverlayManager?.StartOverlays();
                ShowToast("Ambient Glow activated — 4-edge ambient lighting active.", InfoBarSeverity.Success);
            }
            else
            {
                TxtGlowStatus.Text = "Stopped";
                TxtGlowStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LyrowStoppedBrush"];
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
                TxtLyricsStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LyrowSuccessBrush"];

                if (_lyricsWindow == null)
                {
                    var verci = App.Services?.GetService<VerciModule>();
                    if (verci != null)
                    {
                        _lyricsWindow = new LyricsWindow(verci, _settingsManager, _artworkBitmapManager);
                        _lyricsWindow.Closed += (_, _) =>
                        {
                            _lyricsWindow = null;
                            _ = SetLyricsStateAsync(false);
                        };
                    }
                }
                _lyricsWindow?.Activate();
                ShowToast("Lyrics window opened.", InfoBarSeverity.Success);
            }
            else
            {
                TxtLyricsStatus.Text = "Stopped";
                TxtLyricsStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["LyrowStoppedBrush"];
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
        if (_eventBus == null) return;

        _subscriptions.Add(_eventBus.Subscribe<MediaTrackChangedEvent>(OnMediaTrackChanged));
        _subscriptions.Add(_eventBus.Subscribe<AudioLevelUpdatedEvent>(OnAudioLevelUpdated));
        _subscriptions.Add(_eventBus.Subscribe<LyricsLoadedEvent>(OnLyricsLoaded));
    }

    private void OnMediaTrackChanged(MediaTrackChangedEvent e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            var track = e.Track;
            if (track.IsEmpty)
            {
                HomeTxtTitle.Text = "Waiting for playback...";
                HomeTxtArtist.Text = "Launch Spotify, Apple Music, or Chrome";
                HomeProgressTimeline.Value = 0;
                HomeTxtTimestamp.Text = "00:00 / 00:00";
                HomeArtworkPlaceholder.Visibility = Visibility.Visible;
                HomeTrackArtwork.Source = null;
                return;
            }

            HomeTxtTitle.Text = track.Title;
            HomeTxtArtist.Text = track.Artist;
            HomeTxtTimestamp.Text = $"00:00 / {track.Duration:mm\\:ss}";

            // Update Album Artwork & Palette
            if (_artworkBitmapManager != null)
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

                // Update Glow Overlay with extracted dominant and accent colors
                if (_settingsManager.Current.SyncWithAlbumArt)
                {
                    var rawBytes = await _artworkBitmapManager.GetArtworkBytesAsync(track);
                    _glowOverlayManager?.UpdateArtworkPalette(rawBytes);
                }
            }
        });
    }

    private void OnAudioLevelUpdated(AudioLevelUpdatedEvent e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // UI representation: strictly 0% to 100% percentage display, NO dBFS shown!
            int levelPercent = Math.Clamp((int)Math.Round(e.Levels.Peak * 100.0f), 0, 100);
            TxtAudioStatus.Text = levelPercent > 0 ? $"Audio Signal: {levelPercent}%" : "Silence / Idle";
        });
    }

    private void OnLyricsLoaded(LyricsLoadedEvent e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ShowToast($"Lyrics synchronized: {e.Lyrics.Lines.Count} timed lines loaded.", InfoBarSeverity.Success);
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
    }

    private void ComboTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsManager == null || ComboTheme == null) return;
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
}
