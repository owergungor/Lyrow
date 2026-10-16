using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Lyrow.App;

public partial class MainWindow : Window
{
    private readonly IModuleManager _moduleManager;
    private readonly ISettingsManager _settingsManager;

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
        App.Services?.GetService<ISettingsManager>() ?? new Core.Services.SettingsManager())
    {
    }

    public MainWindow(IModuleManager moduleManager, ISettingsManager settingsManager)
    {
        InitializeComponent();
        _moduleManager = moduleManager;
        _settingsManager = settingsManager;

        _moduleManager.ModuleStateChanged += OnModuleStateChanged;
        
        Loaded += OnWindowLoaded;
        Closing += OnMainWindowClosing;
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _moduleManager.ModuleStateChanged -= OnModuleStateChanged;

        _lumnWindow?.ShutdownWindow();
        _lumnWindow = null;

        _verciWindow?.ShutdownWindow();
        _verciWindow = null;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        LoadSettingsToUi();
        RefreshUiStates();
    }

    private void LoadSettingsToUi()
    {
        var settings = _settingsManager.Current;

        RbThemeDark.IsChecked = settings.Theme == ThemeMode.Dark;
        RbThemeLight.IsChecked = settings.Theme == ThemeMode.Light;
        RbThemeSystem.IsChecked = settings.Theme == ThemeMode.SystemDefault;

        ChkClickThrough.IsChecked = settings.ClickThroughEnabled;
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

        // 3. Update Verci card
        var verci = _moduleManager.GetModule("verci");
        var verciRunning = verci?.State == ModuleState.Running;
        VerciStatusText.Text = verci?.State.ToString() ?? "Unknown";
        VerciStatusBorder.Background = verciRunning ? StatusRunningBorderBrush : StatusStoppedBorderBrush;
        VerciStatusText.Foreground = verciRunning ? StatusRunningTextBrush : StatusStoppedTextBrush;
        BtnToggleVerci.Content = verciRunning ? "Verci'yi Durdur" : "Verci'yi Başlat";
    }

    private LumnWindow? _lumnWindow;

    private async void OnToggleLumnClick(object? sender, RoutedEventArgs e)
    {
        var lumn = _moduleManager.GetModule("lumn");
        var target = lumn?.State != ModuleState.Running;
        await _moduleManager.SetModuleActiveAsync("lumn", target);
        await _settingsManager.UpdateAsync(s => s.LumnEnabled = target);
        StatusMessageText.Text = $"Lumn {(target ? "başlatıldı" : "durduruldu")}. Ayarlar kaydedildi.";
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
    }

    private VerciWindow? _verciWindow;

    private async void OnToggleVerciClick(object? sender, RoutedEventArgs e)
    {
        var verci = _moduleManager.GetModule("verci");
        var target = verci?.State != ModuleState.Running;
        await _moduleManager.SetModuleActiveAsync("verci", target);
        await _settingsManager.UpdateAsync(s => s.VerciEnabled = target);
        StatusMessageText.Text = $"Verci {(target ? "başlatıldı" : "durduruldu")}. Ayarlar kaydedildi.";
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
        StatusMessageText.Text = $"Tıklama geçirgenliği (Click-Through) {(isChecked ? "açıldı" : "kapatıldı")}.";
    }
}