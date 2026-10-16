using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Lyrow.App;

public partial class MainWindow : Window
{
    private readonly IModuleManager _moduleManager;
    private readonly ISettingsManager _settingsManager;
    private readonly IWindowPlatformAdapter _platformAdapter;
    private readonly IEventBus? _eventBus;

    private readonly List<IDisposable> _subscriptions = new();

    private static readonly SolidColorBrush ModeBothEnabledBrush = new(Color.Parse("#1E3A8A"));
    private static readonly SolidColorBrush ModeBothDisabledBrush = new(Color.Parse("#334155"));
    private static readonly SolidColorBrush ModeSingleBrush = new(Color.Parse("#1E293B"));
    private static readonly SolidColorBrush StatusRunningBorderBrush = new(Color.Parse("#065F46"));
    private static readonly SolidColorBrush StatusStoppedBorderBrush = new(Color.Parse("#334155"));
    private static readonly SolidColorBrush StatusRunningTextBrush = new(Color.Parse("#34D399"));
    private static readonly SolidColorBrush StatusStoppedTextBrush = new(Color.Parse("#94A3B8"));

    // Parameterless constructor for XAML Designer / Previewer
    public MainWindow() : this(
        App.Services?.GetService<IModuleManager>() ?? new Core.Services.ModuleManager(),
        App.Services?.GetService<ISettingsManager>() ?? new Core.Services.SettingsManager(),
        App.Services?.GetService<IWindowPlatformAdapter>() ?? new SyntheticWindowPlatformAdapter())
    {
    }

    public MainWindow(IModuleManager moduleManager, ISettingsManager settingsManager, IWindowPlatformAdapter? platformAdapter = null)
    {
        InitializeComponent();
        _moduleManager = moduleManager;
        _settingsManager = settingsManager;
        _platformAdapter = platformAdapter ?? new SyntheticWindowPlatformAdapter();
        _eventBus = App.Services?.GetService<IEventBus>();

        _moduleManager.ModuleStateChanged += OnModuleStateChanged;

        HookEventBus();
        
        Loaded += OnWindowLoaded;
        Closing += OnMainWindowClosing;
    }

    private void HookEventBus()
    {
        if (_eventBus == null) return;

        _subscriptions.Add(_eventBus.Subscribe<MediaTrackChangedEvent>(OnMediaTrackChanged));
        _subscriptions.Add(_eventBus.Subscribe<LyricsLoadedEvent>(OnLyricsLoaded));
        _subscriptions.Add(_eventBus.Subscribe<AudioSpectrumUpdatedEvent>(OnAudioSpectrumUpdated));
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
                LivePlaybackBadge.Background = StatusRunningBorderBrush;
            }
            else
            {
                LiveTrackTitleText.Text = "Şarkı Bekleniyor...";
                LiveTrackArtistText.Text = "Spotify veya Medya Oynatıcıyı Başlatın";
                LivePlaybackText.Text = "Boşta";
                LivePlaybackBadge.Background = ModeSingleBrush;
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
                int lineCount = e.Lyrics.Lines.Count;
                LiveLyricsStatusText.Text = $"Hazır ({lineCount} satır - {e.Source})";
                LiveLyricsStatusText.Foreground = new SolidColorBrush(Color.Parse("#89B4FA"));
            }
            else if (!string.IsNullOrEmpty(e.Lyrics?.PlainLyrics))
            {
                LiveLyricsStatusText.Text = $"Düz Metin ({e.Source})";
                LiveLyricsStatusText.Foreground = new SolidColorBrush(Color.Parse("#89B4FA"));
            }
            else
            {
                LiveLyricsStatusText.Text = "Söz Bulunamadı";
                LiveLyricsStatusText.Foreground = new SolidColorBrush(Color.Parse("#F38BA8"));
            }
        });
    }

    private int _spectrumThrottleCounter;
    private void OnAudioSpectrumUpdated(AudioSpectrumUpdatedEvent e)
    {
        // Throttle UI text updates to ~10 FPS for audio meter
        if (Interlocked.Increment(ref _spectrumThrottleCounter) % 6 != 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            var levels = e.Spectrum.Levels;
            if (levels.IsSilent)
            {
                LiveAudioMeterText.Text = "-90 dBFS (Sessiz)";
                LiveAudioMeterText.Foreground = StatusStoppedTextBrush;
            }
            else
            {
                LiveAudioMeterText.Text = $"{levels.Decibels:F1} dB (RMS: {levels.Rms:F2})";
                LiveAudioMeterText.Foreground = StatusRunningTextBrush;
            }
        });
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _moduleManager.ModuleStateChanged -= OnModuleStateChanged;

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }
        _subscriptions.Clear();

        _lumnWindow?.ShutdownWindow();
        _lumnWindow = null;

        _verciWindow?.ShutdownWindow();
        _verciWindow = null;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        PopulateMonitorAndPlacementChoices();
        LoadSettingsToUi();
        RefreshUiStates();
    }

    private void PopulateMonitorAndPlacementChoices()
    {
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

        // Wire change events
        CboOverlayPosition.SelectionChanged += OnOverlayPositionSelectionChanged;
        CboTargetMonitor.SelectionChanged += OnTargetMonitorSelectionChanged;
        SliderLumnSensitivity.ValueChanged += OnLumnSensitivityChanged;
        ChkAutoFetchLyrics.IsCheckedChanged += OnVerciSettingsChanged;
        ChkCacheLyrics.IsCheckedChanged += OnVerciSettingsChanged;
    }

    private void LoadSettingsToUi()
    {
        var settings = _settingsManager.Current;

        RbThemeDark.IsChecked = settings.Theme == ThemeMode.Dark;
        RbThemeLight.IsChecked = settings.Theme == ThemeMode.Light;
        RbThemeSystem.IsChecked = settings.Theme == ThemeMode.SystemDefault;

        ChkClickThrough.IsChecked = settings.ClickThroughEnabled;

        int posIdx = (int)settings.OverlayPosition;
        if (posIdx >= 0 && posIdx < 7)
        {
            CboOverlayPosition.SelectedIndex = posIdx;
        }

        var screensCount = (CboTargetMonitor.ItemsSource as IList<string>)?.Count ?? 1;
        CboTargetMonitor.SelectedIndex = Math.Clamp(settings.TargetMonitorIndex, 0, screensCount - 1);

        SliderLumnSensitivity.Value = settings.Lumn.Sensitivity;
        TxtSensitivityValue.Text = $"{settings.Lumn.Sensitivity:F1}x";

        ChkAutoFetchLyrics.IsChecked = settings.Verci.IsEnabled;
        ChkCacheLyrics.IsChecked = settings.Verci.CacheLyricsLocally;
    }

    private void OnModuleStateChanged(object? sender, ModuleStateChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshUiStates);
    }

    private void RefreshUiStates()
    {
        // 1. Update overall execution mode badge
        var mode = _moduleManager.CurrentExecutionMode;
        ModeBadgeText.Text = mode switch
        {
            ModuleExecutionMode.BothDisabled => "Mod: İkisi Kapalı",
            ModuleExecutionMode.LumnOnly => "Mod: Yalnızca Lumn",
            ModuleExecutionMode.VerciOnly => "Mod: Yalnızca Verci",
            ModuleExecutionMode.BothEnabled => "Mod: İkisi Birlikte",
            _ => "Mod: Belirsiz"
        };

        ModeBadgeBorder.Background = mode switch
        {
            ModuleExecutionMode.BothEnabled => ModeBothEnabledBrush,
            ModuleExecutionMode.BothDisabled => ModeBothDisabledBrush,
            _ => ModeSingleBrush
        };

        // 2. Update Lumn card
        var lumn = _moduleManager.GetModule("lumn");
        var lumnRunning = lumn?.State == ModuleState.Running;
        LumnStatusText.Text = lumn?.State.ToString() ?? "Unknown";
        LumnStatusBorder.Background = lumnRunning ? StatusRunningBorderBrush : StatusStoppedBorderBrush;
        LumnStatusText.Foreground = lumnRunning ? StatusRunningTextBrush : StatusStoppedTextBrush;
        BtnToggleLumn.Content = lumnRunning ? "Lumn'u Durdur" : "Lumn'u Başlat";

        bool isLumnWindowOpen = _lumnWindow != null && _lumnWindow.IsVisible && _lumnWindow.WindowState != WindowState.Minimized;
        LumnWindowStateText.Text = isLumnWindowOpen ? "Açık (Görünür)" : "Kapalı";
        LumnWindowStateText.Foreground = isLumnWindowOpen ? StatusRunningTextBrush : StatusStoppedTextBrush;

        // 3. Update Verci card
        var verci = _moduleManager.GetModule("verci");
        var verciRunning = verci?.State == ModuleState.Running;
        VerciStatusText.Text = verci?.State.ToString() ?? "Unknown";
        VerciStatusBorder.Background = verciRunning ? StatusRunningBorderBrush : StatusStoppedBorderBrush;
        VerciStatusText.Foreground = verciRunning ? StatusRunningTextBrush : StatusStoppedTextBrush;
        BtnToggleVerci.Content = verciRunning ? "Verci'yi Durdur" : "Verci'yi Başlat";

        bool isVerciWindowOpen = _verciWindow != null && _verciWindow.IsVisible && _verciWindow.WindowState != WindowState.Minimized;
        VerciWindowStateText.Text = isVerciWindowOpen ? "Açık (Görünür)" : "Kapalı";
        VerciWindowStateText.Foreground = isVerciWindowOpen ? StatusRunningTextBrush : StatusStoppedTextBrush;
    }

    private LumnWindow? _lumnWindow;

    private async void OnToggleLumnClick(object? sender, RoutedEventArgs e)
    {
        var lumn = _moduleManager.GetModule("lumn");
        var target = lumn?.State != ModuleState.Running;
        await _moduleManager.SetModuleActiveAsync("lumn", target);
        await _settingsManager.UpdateAsync(s => s.LumnEnabled = target);
        StatusMessageText.Text = $"Lumn {(target ? "başlatıldı" : "durduruldu")}. Ayarlar kaydedildi.";
        RefreshUiStates();
    }

    private void OnOpenVisualizerClick(object? sender, RoutedEventArgs e)
    {
        var lumn = _moduleManager.GetModule("lumn") as Lyrow.Core.Modules.LumnModule;
        if (lumn == null) return;

        if (_lumnWindow == null)
        {
            _lumnWindow = new LumnWindow(lumn, _settingsManager);
        }

        if (_lumnWindow.WindowState == WindowState.Minimized)
        {
            _lumnWindow.WindowState = WindowState.Normal;
        }

        _lumnWindow.Show();
        _lumnWindow.Activate();

        // Apply multi-monitor positioning & click-through
        ApplyWindowGeometryAndStyles(_lumnWindow);
        RefreshUiStates();
    }

    private VerciWindow? _verciWindow;

    private async void OnToggleVerciClick(object? sender, RoutedEventArgs e)
    {
        var verci = _moduleManager.GetModule("verci");
        var target = verci?.State != ModuleState.Running;
        await _moduleManager.SetModuleActiveAsync("verci", target);
        await _settingsManager.UpdateAsync(s => s.VerciEnabled = target);
        StatusMessageText.Text = $"Verci {(target ? "başlatıldı" : "durduruldu")}. Ayarlar kaydedildi.";
        RefreshUiStates();
    }

    private void OnOpenLyricsClick(object? sender, RoutedEventArgs e)
    {
        var verci = _moduleManager.GetModule("verci") as Lyrow.Core.Modules.VerciModule;
        if (verci == null) return;

        if (_verciWindow == null)
        {
            _verciWindow = new VerciWindow(verci, _settingsManager);
        }

        if (_verciWindow.WindowState == WindowState.Minimized)
        {
            _verciWindow.WindowState = WindowState.Normal;
        }

        _verciWindow.Show();
        _verciWindow.Activate();

        // Apply multi-monitor positioning & click-through
        ApplyWindowGeometryAndStyles(_verciWindow);
        RefreshUiStates();
    }

    private void ApplyWindowGeometryAndStyles(Window window)
    {
        var settings = _settingsManager.Current;
        OverlayPositionManager.ApplyPosition(window, settings.OverlayPosition, settings.TargetMonitorIndex);

        if (window is LumnWindow lw)
        {
            lw.ApplyClickThrough(settings.ClickThroughEnabled, _platformAdapter);
        }
        else if (window is VerciWindow vw)
        {
            vw.ApplyClickThrough(settings.ClickThroughEnabled, _platformAdapter);
        }
    }

    private async void OnModeBothDisabledClick(object? sender, RoutedEventArgs e)
    {
        await _moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothDisabled);
        await _settingsManager.UpdateAsync(s => { s.LumnEnabled = false; s.VerciEnabled = false; });
        StatusMessageText.Text = "Hızlı Mod uygulandı: Tüm modüller durduruldu.";
    }

    private async void OnModeLumnOnlyClick(object? sender, RoutedEventArgs e)
    {
        await _moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.LumnOnly);
        await _settingsManager.UpdateAsync(s => { s.LumnEnabled = true; s.VerciEnabled = false; });
        StatusMessageText.Text = "Hızlı Mod uygulandı: Yalnızca Lumn aktif.";
    }

    private async void OnModeVerciOnlyClick(object? sender, RoutedEventArgs e)
    {
        await _moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.VerciOnly);
        await _settingsManager.UpdateAsync(s => { s.LumnEnabled = false; s.VerciEnabled = true; });
        StatusMessageText.Text = "Hızlı Mod uygulandı: Yalnızca Verci aktif.";
    }

    private async void OnModeBothEnabledClick(object? sender, RoutedEventArgs e)
    {
        await _moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothEnabled);
        await _settingsManager.UpdateAsync(s => { s.LumnEnabled = true; s.VerciEnabled = true; });
        StatusMessageText.Text = "Hızlı Mod uygulandı: Lumn ve Verci eşzamanlı aktif.";
    }

    private async void OnThemeChanged(object? sender, RoutedEventArgs e)
    {
        var theme = ThemeMode.SystemDefault;
        if (RbThemeDark.IsChecked == true) theme = ThemeMode.Dark;
        else if (RbThemeLight.IsChecked == true) theme = ThemeMode.Light;

        App.ApplyTheme(theme);
        await _settingsManager.UpdateAsync(s => s.Theme = theme);
        StatusMessageText.Text = $"Tema '{theme}' olarak güncellendi ve kaydedildi.";
    }

    private async void OnClickThroughChanged(object? sender, RoutedEventArgs e)
    {
        var isChecked = ChkClickThrough.IsChecked ?? false;
        await _settingsManager.UpdateAsync(s => s.ClickThroughEnabled = isChecked);

        // Apply native click-through state to open overlay windows
        if (_lumnWindow != null && _lumnWindow.IsVisible)
        {
            _lumnWindow.ApplyClickThrough(isChecked, _platformAdapter);
        }
        if (_verciWindow != null && _verciWindow.IsVisible)
        {
            _verciWindow.ApplyClickThrough(isChecked, _platformAdapter);
        }

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
}