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

    private readonly List<IDisposable> _subscriptions = new();
    private bool _isExplicitExit;

    private static readonly SolidColorBrush StatusRunningBorderBrush = new(Color.Parse("#10B981"));
    private static readonly SolidColorBrush StatusStoppedBorderBrush = new(Color.Parse("#27272A"));
    private static readonly SolidColorBrush StatusRunningTextBrush = new(Color.Parse("#10B981"));
    private static readonly SolidColorBrush StatusStoppedTextBrush = new(Color.Parse("#71717A"));

    private LumnWindow? _lumnWindow;
    private VerciWindow? _verciWindow;

    // Parameterless constructor for XAML Designer / Previewer
    public MainWindow() : this(
        App.Services?.GetService<IModuleManager>() ?? new Core.Services.ModuleManager(),
        App.Services?.GetService<ISettingsManager>() ?? new Core.Services.SettingsManager(),
        App.Services?.GetService<IWindowPlatformAdapter>() ?? new SyntheticWindowPlatformAdapter(),
        App.Services?.GetService<IWindowsStartupManager>() ?? new SyntheticStartupManager(),
        App.Services?.GetService<IGlobalHotkeyManager>() ?? new SyntheticGlobalHotkeyManager(),
        App.Services?.GetService<LyrowTrayIconManager>())
    {
    }

    public MainWindow(
        IModuleManager moduleManager,
        ISettingsManager settingsManager,
        IWindowPlatformAdapter? platformAdapter = null,
        IWindowsStartupManager? startupManager = null,
        IGlobalHotkeyManager? hotkeyManager = null,
        LyrowTrayIconManager? trayIconManager = null)
    {
        InitializeComponent();
        _moduleManager = moduleManager;
        _settingsManager = settingsManager;
        _platformAdapter = platformAdapter ?? new SyntheticWindowPlatformAdapter();
        _startupManager = startupManager ?? new SyntheticStartupManager();
        _hotkeyManager = hotkeyManager ?? new SyntheticGlobalHotkeyManager();
        _trayIconManager = trayIconManager;
        _eventBus = App.Services?.GetService<IEventBus>();

        _moduleManager.ModuleStateChanged += OnModuleStateChanged;
        _hotkeyManager.DisplayConfigurationChanged += OnDisplayConfigurationChanged;

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
                LiveTrackArtistText.Text = string.IsNullOrEmpty(track.Album) ? track.Artist : $"{track.Artist} — {track.Album}";
                LivePlaybackText.Text = "Çalıyor";
                LivePlaybackBadge.BorderBrush = StatusRunningBorderBrush;
            }
            else
            {
                LiveTrackTitleText.Text = "Şarkı Bekleniyor...";
                LiveTrackArtistText.Text = "Spotify veya Medya Oynatıcıyı Başlatın";
                LivePlaybackText.Text = "Boşta";
                LivePlaybackBadge.BorderBrush = StatusStoppedBorderBrush;
                LiveLyricsStatusText.Text = "Bekleniyor";
            }
        });
    }

    private void OnLyricsLoaded(LyricsLoadedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Lyrics != null && e.Lyrics.Lines.Count > 0)
            {
                LiveLyricsStatusText.Text = $"Hazır ({e.Lyrics.Lines.Count} satır - {e.Source})";
                LiveLyricsStatusText.Foreground = StatusRunningTextBrush;
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
            if (e.IsRateLimited)
            {
                LiveLyricsStatusText.Text = $"Limit Aşımı ({e.RetryAfterSeconds}s)";
                LiveLyricsStatusText.Foreground = new SolidColorBrush(Color.Parse("#EF4444"));
                StatusMessageText.Text = $"⏱️ LRCLIB istek limiti aşıldı. {e.RetryAfterSeconds} saniye sonra tekrar denenecek.";
            }
            else
            {
                LiveLyricsStatusText.Text = "Söz Bulunamadı";
                LiveLyricsStatusText.Foreground = StatusStoppedTextBrush;
            }
        });
    }

    private void OnAudioLevelUpdated(AudioLevelUpdatedEvent e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var levels = e.Levels;
            if (levels.IsSilent)
            {
                LiveAudioMeterText.Text = "Sessiz (-90 dBFS)";
                LiveAudioMeterText.Foreground = StatusStoppedTextBrush;
                VolumeProgressBar.Value = 0;
            }
            else
            {
                LiveAudioMeterText.Text = $"{levels.Decibels:F1} dB (RMS: {levels.Rms:F2})";
                LiveAudioMeterText.Foreground = StatusRunningTextBrush;
                VolumeProgressBar.Value = Math.Clamp(levels.Rms * 100.0 * 2.0, 0, 100);
            }
        });
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isExplicitExit && _settingsManager.Current.MinimizeToTray)
        {
            e.Cancel = true;
            this.Hide();
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
        Dispatcher.UIThread.Post(() => OnToggleLumnWindowClick(null, null));
    }

    public void ToggleVerciFromExternal()
    {
        Dispatcher.UIThread.Post(() => OnToggleVerciWindowClick(null, null));
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
        _hotkeyManager.UnregisterAll();

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();

        _lumnWindow?.ShutdownWindow();
        _lumnWindow = null;

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
            if (_verciWindow != null && _verciWindow.IsVisible)
            {
                OverlayPositionManager.ApplyPosition(_verciWindow, _settingsManager.Current.OverlayPosition, _settingsManager.Current.TargetMonitorIndex);
            }
            StatusMessageText.Text = "Ekran yapılandırması değişikliği algılandı; overlay pencereleri yeniden konumlandırıldı.";
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

        var statuses = _hotkeyManager.GetStatus();
        var failed = statuses.FirstOrDefault(s => !s.IsRegistered);
        if (failed != null)
        {
            TxtHotkeyStatus.Text = $"⚠️ Kısayol: {failed.ErrorMessage}";
            TxtHotkeyStatus.Foreground = new SolidColorBrush(Color.Parse("#F59E0B"));
        }
        else
        {
            TxtHotkeyStatus.Text = "🟢 Global kısayollar aktif (Glow: Ctrl+Shift+L, Lyrics: Ctrl+Shift+V, Click-through: Ctrl+Shift+C)";
            TxtHotkeyStatus.Foreground = StatusRunningTextBrush;
        }
    }

    private void PopulateMonitorAndPlacementChoices()
    {
        CboOverlayPosition.SelectionChanged -= OnOverlayPositionSelectionChanged;
        CboTargetMonitor.SelectionChanged -= OnTargetMonitorSelectionChanged;

        // 1. Overlay position options
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

        // 2. Multi-monitor detection
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

        // Wire change events
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

        SliderLumnSensitivity.ValueChanged += OnLumnSensitivityChanged;
        ChkAutoFetchLyrics.IsCheckedChanged += OnVerciSettingsChanged;
        ChkCacheLyrics.IsCheckedChanged += OnVerciSettingsChanged;
    }

    private void UpdateTrayMenuStates()
    {
        _trayIconManager?.UpdateStates(
            _lumnWindow?.IsVisible ?? false,
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

        // Glow (Lumn) state
        LumnStatusBorder.BorderBrush = lumnRunning ? StatusRunningBorderBrush : StatusStoppedBorderBrush;
        LumnStatusText.Text = lumnRunning ? "Aktif" : "Durduruldu";
        LumnStatusText.Foreground = lumnRunning ? StatusRunningTextBrush : StatusStoppedTextBrush;
        BtnToggleLumn.Content = lumnRunning ? "Glow'u Durdur" : "Glow'u Başlat";

        // Lyrics (Verci) state
        VerciStatusBorder.BorderBrush = verciRunning ? StatusRunningBorderBrush : StatusStoppedBorderBrush;
        VerciStatusText.Text = verciRunning ? "Aktif" : "Durduruldu";
        VerciStatusText.Foreground = verciRunning ? StatusRunningTextBrush : StatusStoppedTextBrush;
        BtnToggleVerci.Content = verciRunning ? "Lyrics'i Durdur" : "Lyrics'i Başlat";

        // Window buttons & states
        bool lumnWinVisible = _lumnWindow != null && _lumnWindow.IsVisible;
        bool verciWinVisible = _verciWindow != null && _verciWindow.IsVisible;

        BtnOpenVisualizer.Content = lumnWinVisible ? "Pencereyi Gizle" : "Spektrum Penceresi";
        BtnOpenLyrics.Content = verciWinVisible ? "Pencereyi Gizle" : "Sözler Penceresi";

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

        if (!currentlyRunning && (_lumnWindow == null || !_lumnWindow.IsVisible))
        {
            OpenLumnWindow();
        }
        else if (currentlyRunning && _lumnWindow != null && _lumnWindow.IsVisible)
        {
            _lumnWindow.Hide();
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

    private void OnToggleLumnWindowClick(object? sender, RoutedEventArgs? e)
    {
        if (_lumnWindow != null && _lumnWindow.IsVisible)
        {
            _lumnWindow.Hide();
        }
        else
        {
            OpenLumnWindow();
        }
        RefreshUiStates();
        UpdateTrayMenuStates();
    }

    private void OnToggleVerciWindowClick(object? sender, RoutedEventArgs? e)
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

    private void OnOpenVisualizerClick(object? sender, RoutedEventArgs e) => OnToggleLumnWindowClick(sender, e);
    private void OnOpenLyricsClick(object? sender, RoutedEventArgs e) => OnToggleVerciWindowClick(sender, e);

    private void OpenLumnWindow()
    {
        if (_lumnWindow == null)
        {
            _lumnWindow = new LumnWindow();
            _lumnWindow.Closed += (_, _) =>
            {
                _lumnWindow = null;
                RefreshUiStates();
                UpdateTrayMenuStates();
            };
        }

        _lumnWindow.Show();
        OverlayPositionManager.ApplyPosition(_lumnWindow, _settingsManager.Current.OverlayPosition, _settingsManager.Current.TargetMonitorIndex);
        _lumnWindow.ApplyClickThrough(_settingsManager.Current.ClickThroughEnabled, _platformAdapter);
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
        if (RbThemeDark.IsChecked == true) selectedTheme = ThemeMode.Dark;
        else if (RbThemeLight.IsChecked == true) selectedTheme = ThemeMode.Light;

        App.ApplyTheme(selectedTheme);
        await _settingsManager.UpdateAsync(s => s.Theme = selectedTheme);
        StatusMessageText.Text = $"Tema başarıyla '{selectedTheme}' olarak değiştirildi.";
    }

    private async void OnClickThroughChanged(object? sender, RoutedEventArgs e)
    {
        bool isChecked = ChkClickThrough.IsChecked ?? false;
        await _settingsManager.UpdateAsync(s => s.ClickThroughEnabled = isChecked);

        if (_lumnWindow != null && _lumnWindow.IsVisible)
        {
            _lumnWindow.ApplyClickThrough(isChecked, _platformAdapter);
        }
        if (_verciWindow != null && _verciWindow.IsVisible)
        {
            _verciWindow.ApplyClickThrough(isChecked, _platformAdapter);
        }

        UpdateTrayMenuStates();

        StatusMessageText.Text = isChecked
            ? "Tıklama geçirgenliği (Click-Through) AÇILDI. Pencereler artık tıklamaları arkadaki uygulamalara geçirir."
            : "Tıklama geçirgenliği (Click-Through) KAPATILDI. Normal fare etkileşimi devrede.";
    }

    private async void OnOverlayPositionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int idx = CboOverlayPosition.SelectedIndex;
        if (idx < 0 || idx > 6) return;

        var position = (OverlayPosition)idx;
        await _settingsManager.UpdateAsync(s => s.OverlayPosition = position);

        if (_lumnWindow != null && _lumnWindow.IsVisible)
        {
            OverlayPositionManager.ApplyPosition(_lumnWindow, position, _settingsManager.Current.TargetMonitorIndex);
        }
        if (_verciWindow != null && _verciWindow.IsVisible)
        {
            OverlayPositionManager.ApplyPosition(_verciWindow, position, _settingsManager.Current.TargetMonitorIndex);
        }

        StatusMessageText.Text = $"Overlay pencere konumu '{position}' olarak ayarlandı.";
    }

    private async void OnTargetMonitorSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int idx = CboTargetMonitor.SelectedIndex;
        if (idx < 0) return;

        await _settingsManager.UpdateAsync(s => s.TargetMonitorIndex = idx);

        if (_lumnWindow != null && _lumnWindow.IsVisible)
        {
            OverlayPositionManager.ApplyPosition(_lumnWindow, _settingsManager.Current.OverlayPosition, idx);
        }
        if (_verciWindow != null && _verciWindow.IsVisible)
        {
            OverlayPositionManager.ApplyPosition(_verciWindow, _settingsManager.Current.OverlayPosition, idx);
        }

        StatusMessageText.Text = $"Hedef monitör {idx + 1}. Ekran olarak seçildi.";
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

        StatusMessageText.Text = "Şarkı sözü tercihleri güncellendi.";
    }

    private async void OnTraySettingsChanged(object? sender, RoutedEventArgs e)
    {
        bool minToTray = ChkMinimizeToTray.IsChecked ?? true;
        await _settingsManager.UpdateAsync(s => s.MinimizeToTray = minToTray);
        StatusMessageText.Text = minToTray ? "Kapatıldığında sistem tepsisine küçültme aktif." : "Pencere kapatıldığında uygulama çıkacak.";
    }

    private async void OnStartupSettingChanged(object? sender, RoutedEventArgs e)
    {
        bool start = ChkStartWithWindows.IsChecked ?? false;
        bool ok = _startupManager.SetStartupEnabled(start);
        if (ok)
        {
            await _settingsManager.UpdateAsync(s => s.StartWithWindows = start);
            StatusMessageText.Text = start ? "Lyrow Windows başlangıcına eklendi." : "Lyrow Windows başlangıcından kaldırıldı.";
        }
        else
        {
            ChkStartWithWindows.IsChecked = !start;
            StatusMessageText.Text = "Başlangıç ayarı güncellenemedi.";
        }
    }
}