using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Lyrow.App;

public partial class MainWindow : Window
{
    private readonly IModuleManager _moduleManager;
    private readonly ISettingsManager _settingsManager;
    private readonly IWindowPlatformAdapter _platformAdapter;
    private readonly IWindowsStartupManager _startupManager;
    private readonly IGlobalHotkeyManager _hotkeyManager;
    private readonly LyrowTrayIconManager? _trayIconManager;
    private readonly IEventBus? _eventBus;
    private readonly ArtworkBitmapManager? _artworkBitmapManager;
    private readonly IToastNotificationService _toastService;

    private readonly List<IDisposable> _subscriptions = new();
    private bool _isExplicitExit;

    private static readonly SolidColorBrush StatusRunningBorderBrush = new(Color.Parse("#10B981"));
    private static readonly SolidColorBrush StatusStoppedBorderBrush = new(Color.Parse("#27272A"));
    private static readonly SolidColorBrush StatusRunningTextBrush = new(Color.Parse("#10B981"));
    private static readonly SolidColorBrush StatusStoppedTextBrush = new(Color.Parse("#71717A"));

    private LumnWindow? _lumnWindow;
    private GlowOverlayWindow? _glowOverlayWindow;
    private VerciWindow? _verciWindow;

    // Parameterless constructor for XAML Designer / Previewer
    public MainWindow() : this(
        App.Services?.GetService<IModuleManager>() ?? new Core.Services.ModuleManager(),
        App.Services?.GetService<ISettingsManager>() ?? new Core.Services.SettingsManager(),
        App.Services?.GetService<IWindowPlatformAdapter>() ?? new SyntheticWindowPlatformAdapter(),
        App.Services?.GetService<IWindowsStartupManager>() ?? new SyntheticStartupManager(),
        App.Services?.GetService<IGlobalHotkeyManager>() ?? new SyntheticGlobalHotkeyManager(),
        App.Services?.GetService<LyrowTrayIconManager>(),
        App.Services?.GetService<ArtworkBitmapManager>(),
        App.Services?.GetService<IToastNotificationService>() ?? new ToastNotificationService())
    {
    }

    public MainWindow(
        IModuleManager moduleManager,
        ISettingsManager settingsManager,
        IWindowPlatformAdapter? platformAdapter = null,
        IWindowsStartupManager? startupManager = null,
        IGlobalHotkeyManager? hotkeyManager = null,
        LyrowTrayIconManager? trayIconManager = null,
        ArtworkBitmapManager? artworkBitmapManager = null,
        IToastNotificationService? toastService = null)
    {
        InitializeComponent();
        _moduleManager = moduleManager;
        _settingsManager = settingsManager;
        _platformAdapter = platformAdapter ?? new SyntheticWindowPlatformAdapter();
        _startupManager = startupManager ?? new SyntheticStartupManager();
        _hotkeyManager = hotkeyManager ?? new SyntheticGlobalHotkeyManager();
        _trayIconManager = trayIconManager;
        _eventBus = App.Services?.GetService<IEventBus>();
        _artworkBitmapManager = artworkBitmapManager ?? App.Services?.GetService<ArtworkBitmapManager>();
        _toastService = toastService ?? App.Services?.GetService<IToastNotificationService>() ?? new ToastNotificationService();

        _moduleManager.ModuleStateChanged += OnModuleStateChanged;
        _hotkeyManager.DisplayConfigurationChanged += OnDisplayConfigurationChanged;
        _toastService.ToastReceived += OnToastReceived;

        HookEventBus();
        
        Loaded += OnWindowLoaded;
        Closing += OnMainWindowClosing;
    }

    private void HookEventBus()
    {
        if (_eventBus == null) return;

        _subscriptions.Add(_eventBus.Subscribe<MediaTrackChangedEvent>(OnMediaTrackChanged));
        _subscriptions.Add(_eventBus.Subscribe<LyricsLoadedEvent>(OnLyricsLoaded));
        _subscriptions.Add(_eventBus.Subscribe<LyricsFetchFailedEvent>(OnLyricsFetchFailed));
        _subscriptions.Add(_eventBus.Subscribe<AudioLevelUpdatedEvent>(OnAudioLevelUpdated));
    }

    private void OnMediaTrackChanged(MediaTrackChangedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var track = e.Track;
            if (!track.IsEmpty)
            {
                LiveTrackTitleText.Text = track.Title;
                ToolTip.SetTip(LiveTrackTitleText, track.Title);
                string artistAlbum = string.IsNullOrEmpty(track.Album) ? track.Artist : $"{track.Artist} — {track.Album}";
                LiveTrackArtistText.Text = artistAlbum;
                ToolTip.SetTip(LiveTrackArtistText, artistAlbum);

                LivePlaybackText.Text = "Çalıyor";
                LivePlaybackBadge.BorderBrush = StatusRunningBorderBrush;

                _ = UpdateLiveArtworkAsync(track);
            }
            else
            {
                LiveTrackTitleText.Text = "Şarkı Bekleniyor...";
                ToolTip.SetTip(LiveTrackTitleText, null);
                LiveTrackArtistText.Text = "Spotify veya Medya Oynatıcıyı Başlatın";
                ToolTip.SetTip(LiveTrackArtistText, null);

                LivePlaybackText.Text = "Boşta";
                LivePlaybackBadge.BorderBrush = StatusStoppedBorderBrush;
                LiveLyricsStatusText.Text = "Bekleniyor";

                LiveArtworkImage.Source = null;
                LiveArtworkImage.IsVisible = false;
                LiveArtworkPlaceholder.IsVisible = true;
            }
        });
    }

    private async Task UpdateLiveArtworkAsync(MediaTrackInfo track)
    {
        if (_artworkBitmapManager == null) return;
        try
        {
            var bitmap = await _artworkBitmapManager.GetArtworkBitmapAsync(track).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (track.Title == LiveTrackTitleText.Text && bitmap != null)
                {
                    LiveArtworkImage.Source = bitmap;
                    LiveArtworkImage.IsVisible = true;
                    LiveArtworkPlaceholder.IsVisible = false;
                }
                else if (track.Title == LiveTrackTitleText.Text)
                {
                    LiveArtworkImage.Source = null;
                    LiveArtworkImage.IsVisible = false;
                    LiveArtworkPlaceholder.IsVisible = true;
                }
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LiveArtworkImage.Source = null;
                LiveArtworkImage.IsVisible = false;
                LiveArtworkPlaceholder.IsVisible = true;
            });
        }
    }

    private void OnLyricsLoaded(LyricsLoadedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Lyrics != null && e.Lyrics.Lines.Count > 0)
            {
                LiveLyricsStatusText.Text = $"Hazır ({e.Lyrics.Lines.Count} satır - {e.Source})";
                LiveLyricsStatusText.Foreground = StatusRunningTextBrush;
                _toastService.ShowInfo($"Şarkı sözleri yüklendi ({e.Lyrics.Lines.Count} satır)");
            }
            else if (e.Lyrics != null && !string.IsNullOrWhiteSpace(e.Lyrics.PlainLyrics))
            {
                LiveLyricsStatusText.Text = $"Düz Metin ({e.Source})";
                LiveLyricsStatusText.Foreground = StatusRunningTextBrush;
            }
            else
            {
                LiveLyricsStatusText.Text = "Söz Bulunamadı";
                LiveLyricsStatusText.Foreground = StatusStoppedTextBrush;
            }
        });
    }

    private void OnLyricsFetchFailed(LyricsFetchFailedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LiveLyricsStatusText.Text = e.IsRateLimited ? "Limit Aşıldı" : "Söz Bulunamadı";
            LiveLyricsStatusText.Foreground = StatusStoppedTextBrush;
        });
    }

    private void OnAudioLevelUpdated(AudioLevelUpdatedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var levels = e.Levels;
            if (levels.IsSilent)
            {
                LiveAudioMeterText.Text = "-90 dBFS (Sessiz)";
                VolumeProgressBar.Value = 0;
            }
            else
            {
                LiveAudioMeterText.Text = $"{levels.Decibels:F1} dBFS (RMS: {levels.Rms:F2})";
                VolumeProgressBar.Value = Math.Clamp(levels.Rms * 120.0, 0, 100);
            }
        });
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExplicitExit)
        {
            PerformShutdownCleanup();
            return;
        }

        if (_settingsManager.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        PerformShutdownCleanup();
    }

    public void RequestExit()
    {
        _isExplicitExit = true;
        Dispatcher.UIThread.Post(() =>
        {
            Close();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        });
    }

    public void ShowAndActivate()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    public void ToggleLumnFromExternal()
    {
        Dispatcher.UIThread.Post(() => OnToggleLumnClick(null, null!));
    }

    public void ToggleVerciFromExternal()
    {
        Dispatcher.UIThread.Post(() => OnToggleVerciClick(null, null!));
    }

    public void ToggleClickThroughFromExternal()
    {
        Dispatcher.UIThread.Post(() =>
        {
            bool newVal = !_settingsManager.Current.ClickThroughEnabled;
            ChkClickThrough.IsChecked = newVal;
        });
    }

    private void PerformShutdownCleanup()
    {
        _moduleManager.ModuleStateChanged -= OnModuleStateChanged;
        _hotkeyManager.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
        _toastService.ToastReceived -= OnToastReceived;
        _hotkeyManager.UnregisterAll();

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();

        _lumnWindow?.ShutdownWindow();
        _lumnWindow = null;

        _glowOverlayWindow?.ShutdownWindow();
        _glowOverlayWindow = null;

        _verciWindow?.ShutdownWindow();
        _verciWindow = null;

        _trayIconManager?.Dispose();
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        PopulateMonitorAndPlacementChoices();
        LoadSettingsToUi();
        RefreshUiStates();
        RegisterGlobalHotkeys();
        UpdateTrayMenuStates();
    }

    private void OnDisplayConfigurationChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PopulateMonitorAndPlacementChoices();
            if (_lumnWindow != null && _lumnWindow.IsVisible)
            {
                OverlayPositionManager.ApplyPosition(_lumnWindow, _settingsManager.Current.OverlayPosition, _settingsManager.Current.TargetMonitorIndex);
            }
            if (_glowOverlayWindow != null && _glowOverlayWindow.IsVisible)
            {
                _glowOverlayWindow.PositionToTargetMonitor(_settingsManager.Current.TargetMonitorIndex);
            }
            if (_verciWindow != null && _verciWindow.IsVisible)
            {
                OverlayPositionManager.ApplyPosition(_verciWindow, _settingsManager.Current.OverlayPosition, _settingsManager.Current.TargetMonitorIndex);
            }
            _toastService.ShowInfo("Ekran yapılandırması güncellendi.");
        });
    }

    private void RegisterGlobalHotkeys()
    {
        if (_hotkeyManager == null || !_hotkeyManager.IsSupported)
        {
            TxtHotkeyStatus.Text = "⚠️ Global kısayollar bu platformda desteklenmiyor.";
            TxtHotkeyStatus.Foreground = StatusStoppedTextBrush;
            return;
        }

        bool ok = _hotkeyManager.RegisterHotkeys(
            _settingsManager.Current.Hotkeys,
            onLumnToggle: ToggleLumnFromExternal,
            onVerciToggle: ToggleVerciFromExternal,
            onClickThroughToggle: ToggleClickThroughFromExternal);

        UpdateHotkeyStatusSummary();
    }

    private void UpdateHotkeyStatusSummary()
    {
        var hotkeys = _settingsManager.Current.Hotkeys;
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(hotkeys.ToggleLumn)) list.Add($"Glow: {hotkeys.ToggleLumn}");
        if (!string.IsNullOrWhiteSpace(hotkeys.ToggleVerci)) list.Add($"Lyrics: {hotkeys.ToggleVerci}");
        if (!string.IsNullOrWhiteSpace(hotkeys.ToggleClickThrough)) list.Add($"Click-Through: {hotkeys.ToggleClickThrough}");

        if (list.Count == 0)
        {
            TxtHotkeyStatus.Text = "Varsayılan olarak hiçbir global kısayol atanmamıştır. Dilediğiniz kombinasyonu tanımlayabilirsiniz.";
            TxtHotkeyStatus.Foreground = StatusStoppedTextBrush;
        }
        else
        {
            TxtHotkeyStatus.Text = $"🟢 Aktif global kısayollar: {string.Join(" | ", list)}";
            TxtHotkeyStatus.Foreground = StatusRunningTextBrush;
        }
    }

    private void PopulateMonitorAndPlacementChoices()
    {
        CboOverlayPosition.SelectionChanged -= OnOverlayPositionSelectionChanged;
        CboTargetMonitor.SelectionChanged -= OnTargetMonitorSelectionChanged;

        CboOverlayPosition.ItemsSource = new[]
        {
            "Merkez (Center)",
            "Sol Üst (Top-Left)",
            "Üst Orta (Top-Center)",
            "Sağ Üst (Top-Right)",
            "Sol Alt (Bottom-Left)",
            "Alt Orta (Bottom-Center)",
            "Sağ Alt (Bottom-Right)"
        };

        var screens = Screens?.All;
        var screenItems = new List<string>();
        if (screens != null && screens.Count > 0)
        {
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                string tag = s.IsPrimary ? " (Birincil)" : "";
                screenItems.Add($"{i + 1}. Ekran{tag} — {s.Bounds.Width}x{s.Bounds.Height} (ölçek %{(int)(s.Scaling * 100)})");
            }
        }
        else
        {
            screenItems.Add("1. Ekran (Varsayılan)");
        }
        CboTargetMonitor.ItemsSource = screenItems;

        CboOverlayPosition.SelectedIndex = (int)_settingsManager.Current.OverlayPosition;
        int targetIdx = _settingsManager.Current.TargetMonitorIndex;
        if (targetIdx >= 0 && targetIdx < screenItems.Count)
        {
            CboTargetMonitor.SelectedIndex = targetIdx;
        }
        else
        {
            CboTargetMonitor.SelectedIndex = 0;
        }

        CboOverlayPosition.SelectionChanged += OnOverlayPositionSelectionChanged;
        CboTargetMonitor.SelectionChanged += OnTargetMonitorSelectionChanged;
    }

    private void LoadSettingsToUi()
    {
        var settings = _settingsManager.Current;

        RbThemeDark.IsChecked = settings.Theme == ThemeMode.Dark;
        RbThemeLight.IsChecked = settings.Theme == ThemeMode.Light;
        RbThemeSystem.IsChecked = settings.Theme == ThemeMode.SystemDefault;

        ChkClickThrough.IsChecked = settings.ClickThroughEnabled;

        SliderLumnSensitivity.Value = settings.Lumn.Sensitivity;
        TxtSensitivityValue.Text = $"{settings.Lumn.Sensitivity:F1}x";

        ChkAutoFetchLyrics.IsChecked = settings.AutoFetchLyrics;
        ChkCacheLyrics.IsChecked = settings.Verci.CacheLyricsLocally;

        SliderSyncOffset.Value = settings.Verci.SyncOffsetMs;
        TxtSyncOffsetValue.Text = $"{settings.Verci.SyncOffsetMs} ms";
        SliderSyncOffset.ValueChanged += OnSyncOffsetChanged;

        ChkMinimizeToTray.IsChecked = settings.MinimizeToTray;
        ChkStartWithWindows.IsChecked = _startupManager.IsStartupEnabled();

        // Populate Hotkeys to inputs
        TxtGlowHotkey.Text = settings.Hotkeys.ToggleLumn;
        TxtLyricsHotkey.Text = settings.Hotkeys.ToggleVerci;
        TxtClickThroughHotkey.Text = settings.Hotkeys.ToggleClickThrough;

        SliderLumnSensitivity.ValueChanged += OnLumnSensitivityChanged;
        ChkAutoFetchLyrics.IsCheckedChanged += OnVerciSettingsChanged;
        ChkCacheLyrics.IsCheckedChanged += OnVerciSettingsChanged;
    }

    private void UpdateTrayMenuStates()
    {
        bool glowVis = (_glowOverlayWindow?.IsVisible ?? false) || (_lumnWindow?.IsVisible ?? false);
        _trayIconManager?.UpdateStates(
            glowVis,
            _verciWindow?.IsVisible ?? false,
            _settingsManager.Current.ClickThroughEnabled);
    }

    private void OnModuleStateChanged(object? sender, ModuleStateChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshUiStates();
            UpdateTrayMenuStates();
        });
    }

    private void RefreshUiStates()
    {
        var lumn = _moduleManager.GetModule("lumn");
        var verci = _moduleManager.GetModule("verci");

        bool lumnRunning = lumn?.State == ModuleState.Running;
        bool verciRunning = verci?.State == ModuleState.Running;

        // Glow state
        LumnStatusBorder.BorderBrush = lumnRunning ? StatusRunningBorderBrush : StatusStoppedBorderBrush;
        LumnStatusText.Text = lumnRunning ? "Aktif" : "Durduruldu";
        LumnStatusText.Foreground = lumnRunning ? StatusRunningTextBrush : StatusStoppedTextBrush;
        BtnToggleLumn.Content = lumnRunning ? "Glow'u Durdur" : "Glow'u Başlat";

        // Lyrics state
        VerciStatusBorder.BorderBrush = verciRunning ? StatusRunningBorderBrush : StatusStoppedBorderBrush;
        VerciStatusText.Text = verciRunning ? "Aktif" : "Durduruldu";
        VerciStatusText.Foreground = verciRunning ? StatusRunningTextBrush : StatusStoppedTextBrush;
        BtnToggleVerci.Content = verciRunning ? "Lyrics'i Durdur" : "Lyrics'i Başlat";

        // Window buttons & states
        bool lumnWinVisible = (_glowOverlayWindow != null && _glowOverlayWindow.IsVisible) || (_lumnWindow != null && _lumnWindow.IsVisible);
        bool verciWinVisible = _verciWindow != null && _verciWindow.IsVisible;

        BtnOpenVisualizer.Content = lumnWinVisible ? "Işığı Gizle" : "Kenar Işığı Aç";
        BtnOpenLyrics.Content = verciWinVisible ? "Sözleri Gizle" : "Sözler Penceresi";

        LumnWindowStateText.Text = lumnWinVisible ? "Açık / Görünür" : "Kapalı";
        LumnWindowStateText.Foreground = lumnWinVisible ? StatusRunningTextBrush : StatusStoppedTextBrush;

        VerciWindowStateText.Text = verciWinVisible ? "Açık / Görünür" : "Kapalı";
        VerciWindowStateText.Foreground = verciWinVisible ? StatusRunningTextBrush : StatusStoppedTextBrush;
    }

    private async void OnToggleLumnClick(object? sender, RoutedEventArgs e)
    {
        var lumn = _moduleManager.GetModule("lumn");
        bool currentlyRunning = lumn?.State == ModuleState.Running;

        await _moduleManager.SetModuleActiveAsync("lumn", !currentlyRunning);
        await _settingsManager.UpdateAsync(s => s.LumnEnabled = !currentlyRunning);

        if (!currentlyRunning)
        {
            OpenGlowOverlayWindow();
        }
        else
        {
            _glowOverlayWindow?.Hide();
            _lumnWindow?.Hide();
        }

        RefreshUiStates();
        UpdateTrayMenuStates();
    }

    private async void OnToggleVerciClick(object? sender, RoutedEventArgs e)
    {
        var verci = _moduleManager.GetModule("verci");
        bool currentlyRunning = verci?.State == ModuleState.Running;

        await _moduleManager.SetModuleActiveAsync("verci", !currentlyRunning);
        await _settingsManager.UpdateAsync(s => s.VerciEnabled = !currentlyRunning);

        if (!currentlyRunning && (_verciWindow == null || !_verciWindow.IsVisible))
        {
            OpenVerciWindow();
        }
        else if (currentlyRunning && _verciWindow != null && _verciWindow.IsVisible)
        {
            _verciWindow.Hide();
        }

        RefreshUiStates();
        UpdateTrayMenuStates();
    }

    private void OnOpenVisualizerClick(object? sender, RoutedEventArgs e)
    {
        if (_glowOverlayWindow != null && _glowOverlayWindow.IsVisible)
        {
            _glowOverlayWindow.Hide();
        }
        else
        {
            OpenGlowOverlayWindow();
        }
        RefreshUiStates();
        UpdateTrayMenuStates();
    }

    private void OnOpenLyricsClick(object? sender, RoutedEventArgs e)
    {
        if (_verciWindow != null && _verciWindow.IsVisible)
        {
            _verciWindow.Hide();
        }
        else
        {
            OpenVerciWindow();
        }
        RefreshUiStates();
        UpdateTrayMenuStates();
    }

    private void OpenGlowOverlayWindow()
    {
        if (_glowOverlayWindow == null)
        {
            _glowOverlayWindow = new GlowOverlayWindow();
            _glowOverlayWindow.Closed += (_, _) =>
            {
                _glowOverlayWindow = null;
                RefreshUiStates();
                UpdateTrayMenuStates();
            };
        }

        _glowOverlayWindow.Show();
        _glowOverlayWindow.PositionToTargetMonitor(_settingsManager.Current.TargetMonitorIndex);
        _glowOverlayWindow.ApplyClickThrough(_settingsManager.Current.ClickThroughEnabled, _platformAdapter);
    }

    private void OpenVerciWindow()
    {
        if (_verciWindow == null)
        {
            _verciWindow = new VerciWindow();
            _verciWindow.Closed += (_, _) =>
            {
                _verciWindow = null;
                RefreshUiStates();
                UpdateTrayMenuStates();
            };
        }

        _verciWindow.Show();
        OverlayPositionManager.ApplyPosition(_verciWindow, _settingsManager.Current.OverlayPosition, _settingsManager.Current.TargetMonitorIndex);
        _verciWindow.ApplyClickThrough(_settingsManager.Current.ClickThroughEnabled, _platformAdapter);
    }

    private async void OnThemeChanged(object? sender, RoutedEventArgs e)
    {
        ThemeMode selectedTheme = ThemeMode.SystemDefault;
        string themeName = "Sistem Varsayılanı";
        if (RbThemeDark.IsChecked == true)
        {
            selectedTheme = ThemeMode.Dark;
            themeName = "Koyu Tema";
        }
        else if (RbThemeLight.IsChecked == true)
        {
            selectedTheme = ThemeMode.Light;
            themeName = "Açık Tema";
        }

        App.ApplyTheme(selectedTheme);
        await _settingsManager.UpdateAsync(s => s.Theme = selectedTheme);
        _toastService.ShowSuccess($"Tema değiştirildi: {themeName}");
    }

    private async void OnClickThroughChanged(object? sender, RoutedEventArgs e)
    {
        bool isChecked = ChkClickThrough.IsChecked ?? false;
        await _settingsManager.UpdateAsync(s => s.ClickThroughEnabled = isChecked);

        if (_glowOverlayWindow != null && _glowOverlayWindow.IsVisible)
        {
            _glowOverlayWindow.ApplyClickThrough(isChecked, _platformAdapter);
        }
        if (_lumnWindow != null && _lumnWindow.IsVisible)
        {
            _lumnWindow.ApplyClickThrough(isChecked, _platformAdapter);
        }
        if (_verciWindow != null && _verciWindow.IsVisible)
        {
            _verciWindow.ApplyClickThrough(isChecked, _platformAdapter);
        }

        UpdateTrayMenuStates();
        _toastService.ShowInfo(isChecked ? "Tıklama geçirgenliği açıldı." : "Tıklama geçirgenliği kapatıldı.");
    }

    private async void OnOverlayPositionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int idx = CboOverlayPosition.SelectedIndex;
        if (idx < 0 || idx > 6) return;

        var position = (OverlayPosition)idx;
        await _settingsManager.UpdateAsync(s => s.OverlayPosition = position);

        if (_verciWindow != null && _verciWindow.IsVisible)
        {
            OverlayPositionManager.ApplyPosition(_verciWindow, position, _settingsManager.Current.TargetMonitorIndex);
        }

        _toastService.ShowInfo($"Overlay konumu: {position}");
    }

    private async void OnTargetMonitorSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int idx = CboTargetMonitor.SelectedIndex;
        if (idx < 0) return;

        await _settingsManager.UpdateAsync(s => s.TargetMonitorIndex = idx);

        if (_glowOverlayWindow != null && _glowOverlayWindow.IsVisible)
        {
            _glowOverlayWindow.PositionToTargetMonitor(idx);
        }
        if (_verciWindow != null && _verciWindow.IsVisible)
        {
            OverlayPositionManager.ApplyPosition(_verciWindow, _settingsManager.Current.OverlayPosition, idx);
        }

        _toastService.ShowInfo($"Hedef monitör: {idx + 1}. Ekran");
    }

    private async void OnLumnSensitivityChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        float val = (float)e.NewValue;
        TxtSensitivityValue.Text = $"{val:F1}x";
        await _settingsManager.UpdateAsync(s => s.Lumn.Sensitivity = val);
    }

    private async void OnSyncOffsetChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        int ms = (int)e.NewValue;
        TxtSyncOffsetValue.Text = $"{ms} ms";
        await _settingsManager.UpdateAsync(s => s.Verci.SyncOffsetMs = ms);
        var verci = _moduleManager.GetModule("verci") as VerciModule;
        verci?.SetSyncOffset(ms);
    }

    private async void OnVerciSettingsChanged(object? sender, RoutedEventArgs e)
    {
        bool autoFetch = ChkAutoFetchLyrics.IsChecked ?? true;
        bool cacheLyrics = ChkCacheLyrics.IsChecked ?? true;

        await _settingsManager.UpdateAsync(s =>
        {
            s.AutoFetchLyrics = autoFetch;
            s.Verci.CacheLyricsLocally = cacheLyrics;
        });

        _toastService.ShowInfo("Şarkı sözü tercihleri güncellendi.");
    }

    private async void OnTraySettingsChanged(object? sender, RoutedEventArgs e)
    {
        bool minToTray = ChkMinimizeToTray.IsChecked ?? true;
        await _settingsManager.UpdateAsync(s => s.MinimizeToTray = minToTray);
        _toastService.ShowInfo(minToTray ? "Sistem tepsisine küçültme aktif." : "Pencere kapatıldığında çıkış modu aktif.");
    }

    private async void OnStartupSettingChanged(object? sender, RoutedEventArgs e)
    {
        bool start = ChkStartWithWindows.IsChecked ?? false;
        bool ok = _startupManager.SetStartupEnabled(start);
        if (ok)
        {
            await _settingsManager.UpdateAsync(s => s.StartWithWindows = start);
            _toastService.ShowSuccess(start ? "Windows başlangıcına eklendi." : "Windows başlangıcından kaldırıldı.");
        }
        else
        {
            ChkStartWithWindows.IsChecked = !start;
            _toastService.ShowError("Başlangıç ayarı güncellenemedi.");
        }
    }

    // --- Interactive Custom Hotkey Handlers ---
    private void OnSaveGlowHotkeyClick(object? sender, RoutedEventArgs e)
    {
        HandleHotkeySave("Glow", TxtGlowHotkey.Text ?? string.Empty, (s, val) => s.ToggleLumn = val, TxtGlowHotkey);
    }

    private void OnClearGlowHotkeyClick(object? sender, RoutedEventArgs e)
    {
        HandleHotkeyClear("Glow", (s, val) => s.ToggleLumn = val, TxtGlowHotkey);
    }

    private void OnSaveLyricsHotkeyClick(object? sender, RoutedEventArgs e)
    {
        HandleHotkeySave("Lyrics", TxtLyricsHotkey.Text ?? string.Empty, (s, val) => s.ToggleVerci = val, TxtLyricsHotkey);
    }

    private void OnClearLyricsHotkeyClick(object? sender, RoutedEventArgs e)
    {
        HandleHotkeyClear("Lyrics", (s, val) => s.ToggleVerci = val, TxtLyricsHotkey);
    }

    private void OnSaveClickThroughHotkeyClick(object? sender, RoutedEventArgs e)
    {
        HandleHotkeySave("Click-Through", TxtClickThroughHotkey.Text ?? string.Empty, (s, val) => s.ToggleClickThrough = val, TxtClickThroughHotkey);
    }

    private void OnClearClickThroughHotkeyClick(object? sender, RoutedEventArgs e)
    {
        HandleHotkeyClear("Click-Through", (s, val) => s.ToggleClickThrough = val, TxtClickThroughHotkey);
    }

    private async void HandleHotkeySave(
        string actionName,
        string newGesture,
        Action<HotkeySettings, string> applyToSettings,
        TextBox inputField)
    {
        newGesture = (newGesture ?? string.Empty).Trim();

        if (!string.IsNullOrEmpty(newGesture))
        {
            if (!HotkeyValidator.IsValidGesture(newGesture, out var syntaxError))
            {
                _toastService.ShowError($"Geçersiz kısayol: {syntaxError}");
                return;
            }
        }

        // 1. Create trial settings to test locally
        var trialSettings = new HotkeySettings
        {
            ToggleLumn = _settingsManager.Current.Hotkeys.ToggleLumn,
            ToggleVerci = _settingsManager.Current.Hotkeys.ToggleVerci,
            ToggleClickThrough = _settingsManager.Current.Hotkeys.ToggleClickThrough
        };
        applyToSettings(trialSettings, newGesture);

        // 2. Check internal duplicate collisions within Lyrow
        if (!HotkeyValidator.CheckInternalCollisions(trialSettings, out var collisionError))
        {
            _toastService.ShowError($"Kısayol çakışması: {collisionError}");
            return;
        }

        // 3. Attempt registration with the OS
        bool ok = _hotkeyManager.RegisterHotkeys(
            trialSettings,
            onLumnToggle: ToggleLumnFromExternal,
            onVerciToggle: ToggleVerciFromExternal,
            onClickThroughToggle: ToggleClickThroughFromExternal);

        if (!ok)
        {
            var failedStatus = _hotkeyManager.GetStatus().FirstOrDefault(s => s.ActionName.Equals(actionName, StringComparison.OrdinalIgnoreCase) && !s.IsRegistered);
            string err = failedStatus?.ErrorMessage ?? "Bu kısayol Windows veya başka bir uygulama tarafından kullanılıyor.";
            _toastService.ShowError($"Kayıt reddedildi: {err}");

            // Revert registration back to existing valid settings
            _hotkeyManager.RegisterHotkeys(
                _settingsManager.Current.Hotkeys,
                ToggleLumnFromExternal,
                ToggleVerciFromExternal,
                ToggleClickThroughFromExternal);
            return;
        }

        // 4. Persistence
        applyToSettings(_settingsManager.Current.Hotkeys, newGesture);
        await _settingsManager.SaveAsync(_settingsManager.Current);

        inputField.Text = HotkeyValidator.NormalizeGesture(newGesture);
        UpdateHotkeyStatusSummary();

        if (string.IsNullOrEmpty(newGesture))
        {
            _toastService.ShowInfo($"{actionName} kısayolu kaldırıldı.");
        }
        else
        {
            _toastService.ShowSuccess($"{actionName} kısayolu kaydedildi: {inputField.Text}");
        }
    }

    private void HandleHotkeyClear(
        string actionName,
        Action<HotkeySettings, string> applyToSettings,
        TextBox inputField)
    {
        HandleHotkeySave(actionName, string.Empty, applyToSettings, inputField);
    }

    // --- Modern Top-Right Minimal Toast UI ---
    private void OnToastReceived(object? sender, ToastItem toast)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var toastCard = new Border
            {
                Classes = { "toast-card" },
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };

            var dotColor = toast.Type switch
            {
                ToastType.Success => Color.Parse("#10B981"),
                ToastType.Error => Color.Parse("#EF4444"),
                ToastType.Warning => Color.Parse("#F59E0B"),
                _ => Color.Parse("#3B82F6")
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };

            var indicator = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(dotColor),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = toast.Message,
                FontSize = 12,
                FontWeight = FontWeight.Medium,
                Foreground = (IBrush)this.FindResource("LyrowTextPrimary")!,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            Grid.SetColumn(indicator, 0);
            Grid.SetColumn(text, 1);
            grid.Children.Add(indicator);
            grid.Children.Add(text);

            toastCard.Child = grid;

            // Dismiss immediately when clicked
            toastCard.PointerPressed += (_, _) =>
            {
                ToastContainer.Children.Remove(toastCard);
            };

            // Auto dismiss timer (2.8 seconds)
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2800)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ToastContainer.Children.Remove(toastCard);
            };
            timer.Start();

            // Limit simultaneous toasts to 3
            while (ToastContainer.Children.Count >= 3)
            {
                ToastContainer.Children.RemoveAt(0);
            }

            ToastContainer.Children.Add(toastCard);
        });
    }
}