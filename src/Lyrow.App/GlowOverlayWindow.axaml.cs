using Avalonia;
using Avalonia.Controls;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Lyrow.App;

public partial class GlowOverlayWindow : Window
{
    private readonly LumnModule _lumnModule;
    private readonly ISettingsManager _settingsManager;
    private bool _isShuttingDown;

    public GlowOverlayWindow() : this(
        App.Services?.GetService<LumnModule>() ?? new LumnModule(),
        App.Services?.GetService<ISettingsManager>() ?? new Lyrow.Core.Services.SettingsManager())
    {
    }

    public GlowOverlayWindow(LumnModule lumnModule, ISettingsManager settingsManager)
    {
        InitializeComponent();
        _lumnModule = lumnModule;
        _settingsManager = settingsManager;

        GlowEdgeControl.AttachModule(_lumnModule);
    }

    /// <summary>
    /// Fits the overlay window accurately across the designated monitor's bounds,
    /// respecting per-monitor DPI scaling and topology.
    /// </summary>
    public void PositionToTargetMonitor(int targetMonitorIndex = -1)
    {
        int monitorIndex = targetMonitorIndex >= 0
            ? targetMonitorIndex
            : _settingsManager.Current.TargetMonitorIndex;

        var screens = Screens?.All;
        var screen = OverlayPositionManager.ResolveTargetScreen(screens, monitorIndex);
        if (screen == null) return;

        double scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / scaling;
        Height = screen.Bounds.Height / scaling;
    }

    public void ApplyClickThrough(bool enabled, IWindowPlatformAdapter platformAdapter)
    {
        var handle = TryGetPlatformHandle()?.Handle;
        if (handle.HasValue && handle.Value != IntPtr.Zero)
        {
            platformAdapter.SetClickThrough(handle.Value, enabled);
        }
    }

    public void ShutdownWindow()
    {
        _isShuttingDown = true;
        GlowEdgeControl.DetachModule();
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
