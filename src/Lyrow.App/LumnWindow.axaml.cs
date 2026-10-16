using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Lyrow.App;

public partial class LumnWindow : Window
{
    private readonly LumnModule _lumnModule;
    private readonly ISettingsManager _settingsManager;
    private readonly DispatcherTimer _metricsTimer;

    // Parameterless constructor for XAML loader and previewer
    public LumnWindow() : this(
        App.Services?.GetService<LumnModule>() ?? new LumnModule(),
        App.Services?.GetService<ISettingsManager>() ?? new Lyrow.Core.Services.SettingsManager())
    {
    }

    public LumnWindow(LumnModule lumnModule, ISettingsManager settingsManager)
    {
        InitializeComponent();
        _lumnModule = lumnModule;
        _settingsManager = settingsManager;

        VisualizerControl.AttachModule(_lumnModule);

        // Bind drag window behavior
        PointerPressed += OnWindowPointerPressed;

        CloseButton.Click += (_, _) => Hide();
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;

        SensitivitySlider.Value = _settingsManager.Current.Lumn.Sensitivity;
        SensitivitySlider.ValueChanged += (_, e) =>
        {
            _settingsManager.Current.Lumn.Sensitivity = (float)e.NewValue;
        };

        // Low-frequency text metrics updater (10 FPS is enough for human readability)
        _metricsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _metricsTimer.Tick += OnMetricsTick;
        _metricsTimer.Start();

        PropertyChanged += OnWindowPropertyChanged;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty)
        {
            bool isActive = IsVisible && WindowState != WindowState.Minimized;
            if (isActive)
            {
                if (!_metricsTimer.IsEnabled) _metricsTimer.Start();
                VisualizerControl.ResumeRendering();
            }
            else
            {
                if (_metricsTimer.IsEnabled) _metricsTimer.Stop();
                VisualizerControl.PauseRendering();
            }
        }
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMetricsTick(object? sender, EventArgs e)
    {
        var spectrum = _lumnModule.LatestSpectrum;
        var levels = spectrum.Levels;

        RmsText.Text = $"RMS: {levels.Rms:F2}";
        DbText.Text = levels.IsSilent ? "-90 dB (Sessiz)" : $"{levels.Decibels:F1} dB";
        BassText.Text = $"Bass: {(int)(spectrum.Bass * 100)}%";
        MidText.Text = $"Mid: {(int)(spectrum.Mid * 100)}%";

        StatusLabel.Text = _lumnModule.State == ModuleState.Running
            ? (levels.IsSilent ? "Sessizlik (Boşta / CPU %0)" : "Canlı Çalıyor (60 FPS)")
            : "Durduruldu";

        StatusLabel.Foreground = _lumnModule.State == ModuleState.Running
            ? (levels.IsSilent ? Avalonia.Media.Brushes.Gray : Avalonia.Media.Brushes.LightGreen)
            : Avalonia.Media.Brushes.IndianRed;
    }

    public void ApplyClickThrough(bool enabled, IWindowPlatformAdapter platformAdapter)
    {
        var handle = TryGetPlatformHandle()?.Handle;
        if (handle.HasValue && handle.Value != IntPtr.Zero)
        {
            platformAdapter.SetClickThrough(handle.Value, enabled);
        }
    }

    private bool _isShuttingDown;

    public void ShutdownWindow()
    {
        _isShuttingDown = true;
        _metricsTimer.Stop();
        VisualizerControl.DetachModule();
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isShuttingDown)
        {
            // Hide rather than fully destroying window handle for fast reopen
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
