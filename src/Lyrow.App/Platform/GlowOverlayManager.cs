using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;

namespace Lyrow.App.Platform;

/// <summary>
/// Multi-monitor coordinator for Glow ambient edge lighting overlay windows.
/// Manages per-monitor overlays, PerMonitorV2 DPI scaling detection, dynamic display resolution changes,
/// and smooth album artwork palette cross-fading.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlowOverlayManager : IDisposable
{
    private readonly LumnModule _lumnModule;
    private readonly ISettingsManager _settingsManager;
    private readonly List<GlowOverlayWindow> _activeOverlays = new();
    private readonly object _lock = new();

    private GlowPalette _currentPalette = GlowPalette.Default;
    private bool _isRunning = false;
    private bool _isDisposed = false;
    private CancellationTokenSource? _shutdownCts;

    public GlowPalette CurrentPalette
    {
        get
        {
            lock (_lock)
            {
                return _activeOverlays.Count > 0 ? _activeOverlays[0].CurrentPalette : _currentPalette;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _isRunning;
            }
        }
    }

    public IReadOnlyList<GlowOverlayWindow> ActiveOverlays
    {
        get
        {
            lock (_lock)
            {
                return _activeOverlays.ToList();
            }
        }
    }

    public GlowOverlayManager(LumnModule lumnModule, ISettingsManager settingsManager)
    {
        _lumnModule = lumnModule;
        _settingsManager = settingsManager;
    }

    public void StartOverlays()
    {
        lock (_lock)
        {
            if (_isRunning) return;
            _isRunning = true;

            RecreateOverlays();
        }
    }

    public void StopOverlays()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;

            CloseAllOverlays();
        }
    }

    public void UpdateArtworkPalette(byte[]? artworkBytes)
    {
        var palette = ArtworkColorExtractor.ExtractPalette(artworkBytes);
        SetCustomPalette(palette);
    }

    public void SetCustomPalette(GlowPalette palette)
    {
        lock (_lock)
        {
            _currentPalette = palette;
            foreach (var overlay in _activeOverlays)
            {
                overlay.SetPalette(_currentPalette, immediate: false);
            }
        }
    }

    public void UpdatePlaybackState(MediaPlaybackStatus status, bool hasSession)
    {
        lock (_lock)
        {
            if (status == MediaPlaybackStatus.Playing && hasSession)
            {
                // Resume cancels pending shutdown delay
                _shutdownCts?.Cancel();
                _shutdownCts = null;

                foreach (var overlay in _activeOverlays)
                {
                    overlay.SetPlaybackState(MediaPlaybackStatus.Playing);
                    overlay.SetFadeTarget(1.0f);
                }
            }
            else if (status == MediaPlaybackStatus.Paused && hasSession)
            {
                // Pause freezes beat animation without instant shutdown
                _shutdownCts?.Cancel();
                _shutdownCts = null;

                foreach (var overlay in _activeOverlays)
                {
                    overlay.SetPlaybackState(MediaPlaybackStatus.Paused);
                }
            }
            else
            {
                // Stopped / inactive: countdown begins according to GlowShutdownDelaySeconds
                foreach (var overlay in _activeOverlays)
                {
                    overlay.SetPlaybackState(MediaPlaybackStatus.Stopped);
                }

                _shutdownCts?.Cancel();
                _shutdownCts = new CancellationTokenSource();
                var token = _shutdownCts.Token;
                int delaySec = _settingsManager?.Current.GlowShutdownDelaySeconds ?? 5;

                if (delaySec <= 0)
                {
                    foreach (var overlay in _activeOverlays)
                    {
                        overlay.SetFadeTarget(0.0f);
                    }
                }
                else
                {
                    Task.Delay(TimeSpan.FromSeconds(delaySec), token).ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            lock (_lock)
                            {
                                foreach (var overlay in _activeOverlays)
                                {
                                    overlay.SetFadeTarget(0.0f);
                                }
                            }
                        }
                    }, TaskScheduler.Default);
                }
            }
        }
    }

    public void HandleDisplayConfigurationChanged()
    {
        lock (_lock)
        {
            if (!_isRunning) return;
            RecreateOverlays();
        }
    }

    private void RecreateOverlays()
    {
        CloseAllOverlays();

        var screens = EnumerateScreens();
        if (screens.Count == 0)
        {
            var overlay = new GlowOverlayWindow(_lumnModule, _settingsManager);
            overlay.SetPalette(_currentPalette, immediate: true);
            overlay.Create(0, 0, 1920, 1080);
            _activeOverlays.Add(overlay);
            return;
        }

        var settings = _settingsManager.Current;
        var selectedIndices = settings.SelectedDisplayIndices ?? new List<int> { 0 };

        for (int i = 0; i < screens.Count; i++)
        {
            bool shouldInclude = settings.MultiMonitorGlow || selectedIndices.Contains(i) || (selectedIndices.Count == 0 && i == 0);
            if (!shouldInclude) continue;

            var screen = screens[i];
            var overlay = new GlowOverlayWindow(_lumnModule, _settingsManager);
            overlay.SetPalette(_currentPalette, immediate: true);
            overlay.Create(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
            _activeOverlays.Add(overlay);
        }
    }

    private void CloseAllOverlays()
    {
        foreach (var overlay in _activeOverlays)
        {
            overlay.CloseOverlay();
            overlay.Dispose();
        }
        _activeOverlays.Clear();
    }

    public static List<DisplayScreenInfo> EnumerateScreens()
    {
        var result = new List<DisplayScreenInfo>();
        if (!OperatingSystem.IsWindows()) return result;

        int index = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();

            if (GetMonitorInfoW(hMonitor, ref mi))
            {
                int width = mi.rcMonitor.right - mi.rcMonitor.left;
                int height = mi.rcMonitor.bottom - mi.rcMonitor.top;
                int workW = mi.rcWork.right - mi.rcWork.left;
                int workH = mi.rcWork.bottom - mi.rcWork.top;

                double scale = 1.0;
                try
                {
                    if (GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0 && dpiX > 0)
                    {
                        scale = Math.Round(dpiX / 96.0, 2);
                    }
                }
                catch { }

                result.Add(new DisplayScreenInfo
                {
                    Index = index++,
                    Name = new string(mi.szDevice).TrimEnd('\0'),
                    Bounds = new PixelRect(mi.rcMonitor.left, mi.rcMonitor.top, width, height),
                    WorkingArea = new PixelRect(mi.rcWork.left, mi.rcWork.top, workW, workH),
                    IsPrimary = (mi.dwFlags & 1) != 0,
                    Scaling = scale
                });
            }
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopOverlays();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public char[] szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
