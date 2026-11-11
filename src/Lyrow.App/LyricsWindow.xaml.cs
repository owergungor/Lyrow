using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Lyrow.App.Controls;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;

namespace Lyrow.App;

/// <summary>
/// Operational display modes for the Lyrics and Compact Player Window.
/// </summary>
public enum LyricsWindowMode
{
    Horizontal,
    Compact
}

/// <summary>
/// Lyrow v3.3 Apple Music-inspired fluid lyrics and compact player experience for Windows 11.
/// Features a 50/50 horizontal split (player on left, lyrics on right), dynamic artwork-derived background,
/// Apple Music-style compact Dar Mod player, instant track synchronization, and zero duplicate text rendering.
/// </summary>
public sealed partial class LyricsWindow : Window
{
    private readonly LyricsModule _lyricsModule;
    private readonly ISettingsManager _settingsManager;
    private readonly ArtworkBitmapManager? _artworkBitmapManager;
    private readonly IMediaSessionMonitor? _mediaMonitor;
    private readonly IPlaybackTimelineEngine _timelineEngine;
    private readonly IAudioVolumeService? _volumeService;
    private readonly IAudioCaptureService? _audioCaptureService;
    private readonly IAudioRoutingService? _audioRoutingService;
    private readonly ToastNotificationManager? _toastManager;
    private readonly DispatcherTimer _uiTimer;
    private bool _isUpdatingVolumeUi = false;

    private readonly List<Border> _lineContainers = new();
    private readonly List<List<TextBlock>> _lineSubBaseBlocks = new();
    private readonly List<List<TextBlock>> _lineSubOverlayBlocks = new();
    private readonly List<List<RectangleGeometry>> _lineSubClips = new();
    private readonly List<IReadOnlyList<string>> _lineSubStrings = new();
    private readonly List<LyricLine> _currentRenderedLines = new();
    private readonly List<double> _currentOpacities = new();
    private readonly List<double> _targetOpacities = new();
    private readonly List<double> _currentScales = new();
    private readonly List<double> _targetScales = new();

    private bool _isCompactLyricsExpanded = false;
    private SyncedLyrics? _lastCompactLyrics = null;
    private int _currentCompactActiveIndex = -99;
    private readonly List<TextBlock> _compactLineBlocks = new();
    private readonly int?[] _compactSlotLineIndices = new int?[3];

    private long _windowTrackSequence = 0;
    private int _lastArtworkSignature = 0;
    private int _currentActiveIndex = -99;
    private int _lastFontScaleIndex = -1;
    private double _lastFocusedSize = -1;
    private double _lastUnfocusedSize = -1;
    private DateTime _trackResetUtc = DateTime.MinValue;
    private double _lastAppliedResponsiveArtworkSize = -1;
    private double _lastResponsiveFocusedSize = 28.0;
    private double _lastResponsiveUnfocusedSize = 20.0;
    private string _lastSongKey = string.Empty;
    private SyncedLyrics? _lastLyrics;
    private double _currentScrollOffsetY = 0.0;
    private double _targetScrollOffsetY = 0.0;
    private bool _snapToCenterOnNextLayout = false;
    private bool _userIsInteracting = false;
    private bool _isDraggingProgressBar = false;
    private bool _isResizingProgrammatically = false;
    private DateTime _lastUserInteractionUtc = DateTime.MinValue;
    private bool _isShuttingDown = false;

    // View state
    private LyricsWindowMode _currentMode = LyricsWindowMode.Horizontal;
    private bool _isLyricsEnabled = true;

    // Preserved Ambient Dynamic Artwork Background Engine
    private static readonly SolidColorBrush InactiveLyricBrush = new(Windows.UI.Color.FromArgb(200, 190, 190, 205));
    private static readonly SolidColorBrush ActiveLyricBrush = new(Windows.UI.Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush TransparentBrush = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    private AmbientBackgroundPalette _currentAmbientPalette = AmbientBackgroundPalette.Default;
    private AmbientBackgroundPalette _targetAmbientPalette = AmbientBackgroundPalette.Default;

    public LyricsWindowMode CurrentMode => _currentMode;
    public bool IsLyricsEnabled => _isLyricsEnabled;

    public LyricsWindow(
        LyricsModule lyricsModule,
        ISettingsManager settingsManager,
        ArtworkBitmapManager? artworkBitmapManager = null,
        IMediaSessionMonitor? mediaMonitor = null,
        IAudioVolumeService? volumeService = null,
        ToastNotificationManager? toastManager = null,
        IAudioCaptureService? audioCaptureService = null,
        IAudioRoutingService? audioRoutingService = null,
        IPlaybackTimelineEngine? timelineEngine = null)
    {
        InitializeComponent();
        _lyricsModule = lyricsModule;
        _settingsManager = settingsManager;
        ApplyTheme(_settingsManager.Current.Theme);
        UpdateBackdropOpacity(_settingsManager.Current.LyricsBackdropOpacity);
        _artworkBitmapManager = artworkBitmapManager;
        _mediaMonitor = mediaMonitor;
        _volumeService = volumeService;
        _timelineEngine = timelineEngine ?? lyricsModule.TimelineEngine ?? (App.Services?.GetService(typeof(Lyrow.Core.Contracts.IPlaybackTimelineEngine)) as Lyrow.Core.Contracts.IPlaybackTimelineEngine) ?? new Lyrow.Core.Timeline.PlaybackTimelineEngine();
        // Window-scoped notification ownership: LyricsWindow owns its own ToastNotificationManager bound to LyricsToastContainer
        _toastManager = LyricsToastContainer != null
            ? new ToastNotificationManager(LyricsToastContainer, DispatcherQueue)
            : toastManager;
        _audioCaptureService = audioCaptureService ?? (App.Services?.GetService(typeof(IAudioCaptureService)) as IAudioCaptureService);
        _audioRoutingService = audioRoutingService ?? (App.Services?.GetService(typeof(IAudioRoutingService)) as IAudioRoutingService);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(LyricsTitleBar);
        LyricsTitleBar.Title = string.Empty;
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
        {
            overlapped.IsMinimizable = true;
            overlapped.IsMaximizable = true;
            overlapped.IsResizable = true;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            double scale = dpi > 0 ? (dpi / 96.0) : 1.0;
            overlapped.PreferredMinimumWidth = (int)Math.Round(640 * scale);
            overlapped.PreferredMinimumHeight = (int)Math.Round(520 * scale);
        }
        ResizeForCurrentDpi(960, 720);

        TxtOffsetMs.Text = $"{_settingsManager.Current.Lyrics.SyncOffsetMs:+0;-0;0} ms";
        BtnOffsetMinus.Click += (_, _) => AdjustOffset(-250);
        BtnOffsetPlus.Click += (_, _) => AdjustOffset(250);

        // Mode Switching: Dar Mod <-> Horizontal
        BtnDarMod.Click += (_, _) => SetWindowMode(LyricsWindowMode.Compact);
        BtnNormalModeCompact.Click += (_, _) => SetWindowMode(LyricsWindowMode.Horizontal);
        BtnExpandFromCompact.Click += (_, _) => ToggleCompactLyrics();
        SetupDeviceSelectors();

        // Interactive Progress Bar Click & Drag Seeking
        WireInteractiveProgressScrubber(GridProgressHitBox, ProgressTimeline);
        WireInteractiveProgressScrubber(GridProgressCompactHitBox, ProgressTimelineCompact);
        UpdateScrubberToolTips();

        // Lyrics Toggle (Active -> 50/50, Inactive -> Player Centered)
        BtnToggleLyrics.Click += (_, _) => ToggleLyricsPanel();

        // Fullscreen Toggle
        BtnFullscreen.Click += (_, _) => ToggleFullscreen();

        // Media Transport Controls: Horizontal View
        BtnPrev.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TrySkipPreviousAsync();
        };
        BtnPlayPause.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TryTogglePlayPauseAsync();
        };
        BtnNext.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TrySkipNextAsync();
        };

        // Media Transport Controls: Compact Dar Mod View
        BtnPrevCompact.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TrySkipPreviousAsync();
        };
        BtnPlayPauseCompact.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TryTogglePlayPauseAsync();
        };
        BtnNextCompact.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TrySkipNextAsync();
        };

        // Volume Controls
        if (_volumeService != null)
        {
            SyncVolumeUi();
            SliderVolume.ValueChanged += (_, e) =>
            {
                if (_isUpdatingVolumeUi) return;
                _isUpdatingVolumeUi = true;
                try
                {
                    int val = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
                    _volumeService.SetPlaybackVolumePercent(val);
                    TxtVolumePercent.Text = $"{val}%";
                    string glyph = (val == 0 || _volumeService.IsPlaybackMuted) ? "\uE74F" : "\uE767";
                    IconVolumeMute.Glyph = glyph;
                    if (IconVolumeMuteCompact != null) IconVolumeMuteCompact.Glyph = glyph;
                }
                finally
                {
                    _isUpdatingVolumeUi = false;
                }
            };

            BtnVolumeMute.Click += (_, _) =>
            {
                _volumeService.TogglePlaybackMute();
                SyncVolumeUi();
            };

            if (FlyoutVolumeCompact != null)
            {
                FlyoutVolumeCompact.Opening += (_, _) => SyncVolumeUi();
            }

            if (SliderVolumeCompact != null)
            {
                SliderVolumeCompact.ValueChanged += (_, e) =>
                {
                    if (_isUpdatingVolumeUi) return;
                    _isUpdatingVolumeUi = true;
                    try
                    {
                        int val = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
                        _volumeService.SetPlaybackVolumePercent(val);
                        SliderVolume.Value = val;
                        TxtVolumePercent.Text = $"{val}%";
                        string glyph = (val == 0 || _volumeService.IsPlaybackMuted) ? "\uE74F" : "\uE767";
                        IconVolumeMute.Glyph = glyph;
                        if (IconVolumeMuteCompact != null) IconVolumeMuteCompact.Glyph = glyph;
                        if (IconPopupMuteCompact != null) IconPopupMuteCompact.Glyph = (val == 0 || _volumeService.IsPlaybackMuted) ? "\uE74F" : "\uE992";
                    }
                    finally
                    {
                        _isUpdatingVolumeUi = false;
                    }
                };
            }

            if (BtnPopupMuteCompact != null)
            {
                BtnPopupMuteCompact.Click += (_, _) =>
                {
                    _volumeService.TogglePlaybackMute();
                    SyncVolumeUi();
                };
            }

            if (BtnPopupVolumeMaxCompact != null)
            {
                BtnPopupVolumeMaxCompact.Click += (_, _) =>
                {
                    if (_volumeService.IsPlaybackMuted)
                    {
                        _volumeService.TogglePlaybackMute();
                    }
                    else
                    {
                        _volumeService.SetPlaybackVolumePercent(100);
                    }
                    SyncVolumeUi();
                };
            }

            _volumeService.VolumeChanged += (_, _) =>
            {
                DispatcherQueue?.TryEnqueue(SyncVolumeUi);
            };
        }

        // Layout and Keyboard Listeners
        RootLayout.SizeChanged += (_, _) => UpdateLayoutForCurrentSize();
        RootLayout.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                }
            }
            else if (e.Key == Windows.System.VirtualKey.Space)
            {
                if (_mediaMonitor != null) await _mediaMonitor.TryTogglePlayPauseAsync();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Left)
            {
                if (_mediaMonitor != null) await _mediaMonitor.TrySkipPreviousAsync();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Right)
            {
                if (_mediaMonitor != null) await _mediaMonitor.TrySkipNextAsync();
                e.Handled = true;
            }
        };

        LyricsScrollViewer.SizeChanged += (_, _) =>
        {
            UpdateViewportPadding();
            RecalculateCenterScrollTarget(immediate: true);
        };

        LyricsScrollViewer.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnLyricsPointerWheelChanged),
            handledEventsToo: true);

        if (_mediaMonitor != null)
        {
            _mediaMonitor.TrackChanged += OnMediaMonitorTrackChanged;
        }

        ApplyLocalization();
        LocalizationService.Instance.LanguageChanged += OnLocalizationLanguageChanged;

        Closed += (_, _) =>
        {
            LocalizationService.Instance.LanguageChanged -= OnLocalizationLanguageChanged;
            if (_mediaMonitor != null)
            {
                _mediaMonitor.TrackChanged -= OnMediaMonitorTrackChanged;
            }
            if (!_isShuttingDown)
            {
                _uiTimer?.Stop();
            }
        };

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // 60 FPS smooth animation and centering loop
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();
    }

    private void OnLocalizationLanguageChanged(object? sender, string lang)
    {
        DispatcherQueue?.TryEnqueue(ApplyLocalization);
    }

    public void ApplyLocalization()
    {
        var loc = LocalizationService.Instance;

        if (BtnToggleLyrics != null)
        {
            string lyricsTip = _isLyricsEnabled ? loc["Lyrics_HideLyrics"] : loc["Lyrics_ShowLyrics"];
            ToolTipService.SetToolTip(BtnToggleLyrics, lyricsTip);
            AutomationProperties.SetName(BtnToggleLyrics, lyricsTip);
        }

        if (BtnDarMod != null)
        {
            ToolTipService.SetToolTip(BtnDarMod, loc["Compact_Player"]);
            AutomationProperties.SetName(BtnDarMod, loc["Compact_Player"]);
        }

        if (BtnNormalModeCompact != null)
        {
            ToolTipService.SetToolTip(BtnNormalModeCompact, loc["Compact_NormalMode"]);
            AutomationProperties.SetName(BtnNormalModeCompact, loc["Compact_NormalMode"]);
        }

        if (BtnExpandFromCompact != null)
        {
            ToolTipService.SetToolTip(BtnExpandFromCompact, loc["Compact_ToggleLyrics"]);
            AutomationProperties.SetName(BtnExpandFromCompact, loc["Compact_ToggleLyrics"]);
        }

        if (BtnFullscreen != null)
        {
            ToolTipService.SetToolTip(BtnFullscreen, loc["Fullscreen_Toggle"]);
            AutomationProperties.SetName(BtnFullscreen, loc["Fullscreen_Toggle"]);
        }

        if (BtnPrev != null)
        {
            ToolTipService.SetToolTip(BtnPrev, loc["Playback_Prev"]);
            AutomationProperties.SetName(BtnPrev, loc["Playback_Prev"]);
        }

        if (BtnPlayPause != null)
        {
            ToolTipService.SetToolTip(BtnPlayPause, loc["Playback_PlayPause"]);
            AutomationProperties.SetName(BtnPlayPause, loc["Playback_PlayPause"]);
        }

        if (BtnNext != null)
        {
            ToolTipService.SetToolTip(BtnNext, loc["Playback_Next"]);
            AutomationProperties.SetName(BtnNext, loc["Playback_Next"]);
        }

        if (BtnVolumeMute != null)
        {
            ToolTipService.SetToolTip(BtnVolumeMute, loc["Playback_MuteUnmute"]);
            AutomationProperties.SetName(BtnVolumeMute, loc["Playback_MuteUnmute"]);
        }

        if (BtnOffsetMinus != null)
        {
            ToolTipService.SetToolTip(BtnOffsetMinus, "-250ms " + loc["Offset_Delay"]);
            AutomationProperties.SetName(BtnOffsetMinus, loc["Offset_Decrease"]);
        }

        if (BtnOffsetPlus != null)
        {
            ToolTipService.SetToolTip(BtnOffsetPlus, "+250ms " + loc["Offset_Advance"]);
            AutomationProperties.SetName(BtnOffsetPlus, loc["Offset_Increase"]);
        }

        if (TxtEmptyState != null) TxtEmptyState.Text = loc["Status_WaitingMedia"];
        if (TxtEmptyStateSub != null) TxtEmptyStateSub.Text = loc["Status_PlayInApp"];
    }

    /// <summary>
    /// Switches between Dar Mod (compact player) and Horizontal 50/50 lyrics window.
    /// </summary>
    public void SetWindowMode(LyricsWindowMode mode, bool resizeWindow = true)
    {
        _currentMode = mode;
        if (mode == LyricsWindowMode.Compact)
        {
            HorizontalView.Visibility = Visibility.Collapsed;
            CompactPlayerView.Visibility = Visibility.Visible;
            Grid.SetRow(MainViewportArea, 0);
            Grid.SetRowSpan(MainViewportArea, 2);
            LyricsTitleBar.Height = 26;
            LyricsTitleBar.MinHeight = 24;
            LyricsTitleBar.Title = string.Empty;
            if (resizeWindow)
            {
                ResizeCompactWindow(_isCompactLyricsExpanded);
            }
        }
        else
        {
            if (_isCompactLyricsExpanded)
            {
                _isCompactLyricsExpanded = false;
                GridCompactLyrics.Visibility = Visibility.Collapsed;
                IconExpandFromCompact.Glyph = "\uEC4F";
            }
            CompactPlayerView.Visibility = Visibility.Collapsed;
            HorizontalView.Visibility = Visibility.Visible;
            Grid.SetRow(MainViewportArea, 1);
            Grid.SetRowSpan(MainViewportArea, 1);
            LyricsTitleBar.Height = double.NaN;
            LyricsTitleBar.MinHeight = 32;
            LyricsTitleBar.Title = string.Empty;
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                uint dpi = GetDpiForWindow(hwnd);
                double scale = dpi > 0 ? (dpi / 96.0) : 1.0;
                overlapped.PreferredMinimumWidth = (int)Math.Round(640 * scale);
                overlapped.PreferredMinimumHeight = (int)Math.Round(520 * scale);
            }
            if (resizeWindow)
            {
                ResizeForCurrentDpi(960, 720);
            }
            ApplyResponsiveScaling(RootLayout.ActualWidth > 0 ? RootLayout.ActualWidth : 960, RootLayout.ActualHeight > 0 ? RootLayout.ActualHeight : 720);
            UpdateViewportPadding();
            _snapToCenterOnNextLayout = true;
        }
    }

    /// <summary>
    /// Toggles lyrics visibility. When inactive, the player centers horizontally in the entire window.
    /// When active, the 50/50 layout is smoothly restored.
    /// </summary>
    public void UpdateBackdropOpacity(int opacityPercent)
    {
        if (AmbientBackgroundLayer != null)
        {
            AmbientBackgroundLayer.Opacity = Math.Clamp(opacityPercent, 40, 100) / 100.0;
        }
    }

    public void ApplyLyricsPersonalization()
    {
        UpdateBackdropOpacity(_settingsManager.Current.LyricsBackdropOpacity);
        LyricsPanel.Spacing = _settingsManager.Current.LyricsLineSpacing > 0 ? _settingsManager.Current.LyricsLineSpacing : 20;
        if (_lastLyrics != null)
        {
            RebuildLyricsUi(_lastLyrics);
        }
    }

    public void ToggleLyricsPanel()
    {
        _isLyricsEnabled = !_isLyricsEnabled;
        if (_isLyricsEnabled)
        {
            ColLyrics.Width = new GridLength(1, GridUnitType.Star);
            LyricsColumnContainer.Visibility = Visibility.Visible;
            IconToggleLyrics.Glyph = "\uEC4F";
            string tip = LocalizationService.Instance["Lyrics_HideLyrics"];
            ToolTipService.SetToolTip(BtnToggleLyrics, tip);
            AutomationProperties.SetName(BtnToggleLyrics, tip);
            UpdateViewportPadding();
            _snapToCenterOnNextLayout = true;
        }
        else
        {
            ColLyrics.Width = new GridLength(0);
            LyricsColumnContainer.Visibility = Visibility.Collapsed;
            IconToggleLyrics.Glyph = "\uE8D6";
            string tip = LocalizationService.Instance["Lyrics_ShowLyrics"];
            ToolTipService.SetToolTip(BtnToggleLyrics, tip);
            AutomationProperties.SetName(BtnToggleLyrics, tip);
        }
    }

    private void ResizeForCurrentDpi(int logicalWidth, int logicalHeight)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            double scale = dpi > 0 ? (dpi / 96.0) : 1.0;
            int physW = (int)Math.Round(logicalWidth * scale);
            int physH = (int)Math.Round(logicalHeight * scale);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(physW, physH));
        }
        catch
        {
            try { AppWindow.Resize(new Windows.Graphics.SizeInt32(logicalWidth, logicalHeight)); } catch { }
        }
    }

    private void OnLyricsPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(LyricsScrollViewer).Properties;
        if (!props.IsHorizontalMouseWheel && props.MouseWheelDelta != 0)
        {
            _userIsInteracting = true;
            _lastUserInteractionUtc = DateTime.UtcNow;
            double nextOffset = Math.Clamp(
                LyricsScrollViewer.VerticalOffset - (props.MouseWheelDelta * 0.75),
                0.0,
                LyricsScrollViewer.ScrollableHeight);
            _currentScrollOffsetY = nextOffset;
            _targetScrollOffsetY = nextOffset;
            LyricsScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: true);
            e.Handled = true;
        }
    }

    private void AdjustOffset(int deltaMs)
    {
        int newOffset = _settingsManager.Current.Lyrics.SyncOffsetMs + deltaMs;
        _settingsManager.Current.Lyrics.SyncOffsetMs = newOffset;
        _lyricsModule.SetSyncOffset(newOffset);
        TxtOffsetMs.Text = $"{newOffset:+0;-0;0} ms";
    }

    private void OnMediaMonitorTrackChanged(object? sender, MediaTrackInfo track)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            string songKey = $"{track.Artist}_{track.Title}";
            int artSig = ArtworkBitmapManager.ComputeArtworkSignature(track);
            if (!string.Equals(songKey, _lastSongKey, StringComparison.OrdinalIgnoreCase))
            {
                ResetTrackState(track);
            }
            else if (!track.IsEmpty && track.ThumbnailData is { Length: > 0 } && artSig != _lastArtworkSignature)
            {
                _lastArtworkSignature = artSig;
                _artworkBitmapManager?.Invalidate(track.Artist, track.Title);
                _ = UpdateArtworkAsync(track, Interlocked.Read(ref _windowTrackSequence));
            }
        });
    }

    private MediaTrackInfo ResolveCurrentTrack()
    {
        var moduleTrack = _lyricsModule.CurrentTrack;
        var monitorTrack = _mediaMonitor?.CurrentTrack ?? MediaTrackInfo.Empty;

        if (moduleTrack.IsEmpty) return monitorTrack;
        if (monitorTrack.IsEmpty) return moduleTrack;

        bool sameSong = string.Equals(moduleTrack.Title, monitorTrack.Title, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(moduleTrack.Artist, monitorTrack.Artist, StringComparison.OrdinalIgnoreCase);

        if (sameSong)
        {
            byte[]? bestThumb = (moduleTrack.ThumbnailData is { Length: > 0 })
                ? moduleTrack.ThumbnailData
                : monitorTrack.ThumbnailData;
            TimeSpan bestDur = moduleTrack.Duration > TimeSpan.Zero ? moduleTrack.Duration : monitorTrack.Duration;
            string? bestUri = !string.IsNullOrWhiteSpace(moduleTrack.AlbumArtUri) ? moduleTrack.AlbumArtUri : monitorTrack.AlbumArtUri;
            return moduleTrack with { ThumbnailData = bestThumb, Duration = bestDur, AlbumArtUri = bestUri };
        }

        return monitorTrack;
    }

    private void WireInteractiveProgressScrubber(Grid hitBox, ProgressBar progressBar)
    {
        bool isPointerCaptured = false;

        if (_mediaMonitor != null)
        {
            _mediaMonitor.TimelineChanged += (_, _) =>
            {
                DispatcherQueue.TryEnqueue(UpdateScrubberToolTips);
            };
        }

        TimeSpan ResolveActiveTrackDuration()
        {
            var track = !_lyricsModule.CurrentTrack.IsEmpty
                ? _lyricsModule.CurrentTrack
                : (_mediaMonitor?.CurrentTrack ?? MediaTrackInfo.Empty);
            var timeline = _lyricsModule.CurrentTimeline.Duration > TimeSpan.Zero
                ? _lyricsModule.CurrentTimeline
                : (_mediaMonitor?.CurrentTimeline ?? MediaTimelineInfo.Zero);
            return track.Duration > TimeSpan.Zero ? track.Duration : timeline.Duration;
        }

        void UpdatePreviewFromPoint(Windows.Foundation.Point pt)
        {
            double w = hitBox.ActualWidth > 0 ? hitBox.ActualWidth : progressBar.ActualWidth;
            var dur = ResolveActiveTrackDuration();
            if (w <= 0 || dur <= TimeSpan.Zero) return;

            var targetTime = LyricsFillProgressCalculator.CalculateSeekTimeFromPointer(pt.X, w, dur);
            double pct = Math.Clamp((targetTime.TotalMilliseconds / dur.TotalMilliseconds) * 100.0, 0.0, 100.0);
            ProgressTimeline.Value = pct;
            ProgressTimelineCompact.Value = pct;
            TxtElapsed.Text = $"{targetTime:m\\:ss}";
            TxtElapsedCompact.Text = $"{targetTime:m\\:ss}";
            TxtRemaining.Text = dur > targetTime ? $"-{(dur - targetTime):m\\:ss}" : (dur > TimeSpan.Zero ? "-0:00" : "--:--");
            TxtRemainingCompact.Text = dur > targetTime ? $"-{(dur - targetTime):m\\:ss}" : (dur > TimeSpan.Zero ? "-0:00" : "--:--");
            _lyricsModule.Synchronizer.EvaluatePosition(targetTime);
        }

        hitBox.PointerPressed += (s, e) =>
        {
            if (_mediaMonitor != null && !_mediaMonitor.CanSeek)
            {
                ShowSeekUnsupportedWarning();
                e.Handled = true;
                return;
            }

            var dur = ResolveActiveTrackDuration();
            if (dur <= TimeSpan.Zero) return;

            isPointerCaptured = hitBox.CapturePointer(e.Pointer);
            _isDraggingProgressBar = true;
            var pt = e.GetCurrentPoint(hitBox).Position;
            UpdatePreviewFromPoint(pt);
            e.Handled = true;
        };

        hitBox.PointerMoved += (s, e) =>
        {
            if (!_isDraggingProgressBar || !isPointerCaptured) return;
            var pt = e.GetCurrentPoint(hitBox).Position;
            UpdatePreviewFromPoint(pt);
            e.Handled = true;
        };

        hitBox.PointerReleased += async (s, e) =>
        {
            if (!_isDraggingProgressBar) return;
            _isDraggingProgressBar = false;
            if (isPointerCaptured)
            {
                hitBox.ReleasePointerCapture(e.Pointer);
                isPointerCaptured = false;
            }

            double w = hitBox.ActualWidth > 0 ? hitBox.ActualWidth : progressBar.ActualWidth;
            var dur = ResolveActiveTrackDuration();
            if (w > 0 && dur > TimeSpan.Zero)
            {
                var pt = e.GetCurrentPoint(hitBox).Position;
                var targetTime = LyricsFillProgressCalculator.CalculateSeekTimeFromPointer(pt.X, w, dur);
                await SeekToMediaPositionAsync(targetTime);
            }
            e.Handled = true;
        };

        hitBox.PointerCanceled += (s, e) =>
        {
            _isDraggingProgressBar = false;
            if (isPointerCaptured)
            {
                hitBox.ReleasePointerCapture(e.Pointer);
                isPointerCaptured = false;
            }
        };
    }

    /// <summary>
    /// Atomically wipes previous track state, lyrics, timestamps, and animation targets.
    /// Prevents cross-track leakage when automatically transitioning from Track A to Track B.
    /// </summary>
    public void ResetTrackState(MediaTrackInfo newTrack)
    {
        UpdateScrubberToolTips();
        _trackResetUtc = DateTime.UtcNow;
        long currentSeq = Interlocked.Increment(ref _windowTrackSequence);
        _lastSongKey = $"{newTrack.Artist?.Trim()}_{newTrack.Title?.Trim()}";
        _lastArtworkSignature = ArtworkBitmapManager.ComputeArtworkSignature(newTrack);
        _lastLyrics = SyncedLyrics.Empty;
        _lastCompactLyrics = null;
        _currentCompactActiveIndex = -99;
        if (CompactLyricsPanel != null) CompactLyricsPanel.Children.Clear();
        _compactLineBlocks.Clear();
        _currentActiveIndex = -1;
        _currentScrollOffsetY = 0;
        _targetScrollOffsetY = 0;
        _snapToCenterOnNextLayout = true;

        // 1. Clear rendered lyric line visual representations immediately
        LyricsPanel.Children.Clear();
        _lineContainers.Clear();
        _lineSubBaseBlocks.Clear();
        _lineSubOverlayBlocks.Clear();
        _lineSubClips.Clear();
        _lineSubStrings.Clear();
        _currentRenderedLines.Clear();
        _currentOpacities.Clear();
        _targetOpacities.Clear();
        _currentScales.Clear();
        _targetScales.Clear();

        // 2. Reset scroll view
        LyricsScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
        LyricsScrollViewer.Visibility = Visibility.Collapsed;
        var loc = LocalizationService.Instance;
        TxtEmptyState.Text = newTrack.IsEmpty ? loc["Lyrics_WaitingPlayback"] : loc["Lyrics_Searching"];
        TxtEmptyStateSub.Text = newTrack.IsEmpty ? loc["Lyrics_PlayToSeeLyrics"] : $"{loc["Lyrics_Searching"]} ({newTrack.Title})";
        PanelEmptyState.Visibility = Visibility.Visible;

        // 3. Reset progress bars and time labels strictly to 0:00 / clean state
        _timelineEngine.HandleTrackChanged(newTrack);
        ProgressTimeline.Value = 0;
        ProgressTimelineCompact.Value = 0;
        TxtElapsed.Text = "0:00";
        TxtElapsedCompact.Text = "0:00";
        TxtRemaining.Text = newTrack.Duration > TimeSpan.Zero ? Lyrow.Core.Timeline.PlaybackTimelineEngine.FormatRemaining(newTrack.Duration) : "--:--";
        TxtRemainingCompact.Text = newTrack.Duration > TimeSpan.Zero ? Lyrow.Core.Timeline.PlaybackTimelineEngine.FormatRemaining(newTrack.Duration) : "--:--";

        // 4. Invalidate artwork only when native high-res GSMTC thumbnail bytes are provided; preserve online/transcoded cache otherwise
        if (!newTrack.IsEmpty &&
            newTrack.ThumbnailData is { Length: > 0 } &&
            !ArtworkService.IsLowResolutionThumbnail(newTrack.ThumbnailData) &&
            !ArtworkService.IsWebpImageData(newTrack.ThumbnailData))
        {
            _artworkBitmapManager?.Invalidate(newTrack.Artist, newTrack.Title);
        }
        ImgTrackArtwork.Source = null;
        IconArtworkPlaceholder.Visibility = Visibility.Visible;
        ImgTrackArtworkCompact.Source = null;
        IconArtworkPlaceholderCompact.Visibility = Visibility.Visible;
        _targetAmbientPalette = AmbientBackgroundPalette.Default;

        // 5. Update song titles and artist metadata
        if (newTrack.IsEmpty)
        {
            TxtSongTitle.Text = "No active media session";
            TxtArtistAlbum.Text = "Play music in Spotify, Apple Music, or your browser";
            TxtSongTitleCompact.Text = "No active media session";
            TxtArtistAlbumCompact.Text = "Play music in Spotify, Apple Music, or your browser";
        }
        else
        {
            TxtSongTitle.Text = newTrack.Title;
            string artistAlbum = string.IsNullOrWhiteSpace(newTrack.Album)
                ? newTrack.Artist
                : $"{newTrack.Artist} — {newTrack.Album}";
            TxtArtistAlbum.Text = artistAlbum;
            TxtSongTitleCompact.Text = newTrack.Title;
            TxtArtistAlbumCompact.Text = artistAlbum;
            var metaAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { From = 0.4, To = 1.0, Duration = TimeSpan.FromMilliseconds(200) };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(metaAnim, TxtSongTitle);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(metaAnim, "Opacity");
            var sbMeta = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            sbMeta.Children.Add(metaAnim);
            sbMeta.Begin();

            _ = UpdateArtworkAsync(newTrack, currentSeq);
        }
    }

    private void UpdateViewportPadding()
    {
        double viewportH = LyricsScrollViewer.ActualHeight;
        double viewportW = LyricsScrollViewer.ActualWidth;
        if (viewportH <= 0) return;

        double halfPadding = Math.Max(60.0, (viewportH / 2.0) - 24.0);
        double horizPadding = Math.Clamp(viewportW * 0.05, 24.0, 56.0);
        LyricsPanel.Padding = new Thickness(horizPadding, halfPadding, horizPadding, halfPadding);
    }

    private void OnUiTimerTick(object? sender, object e)
    {
        var track = ResolveCurrentTrack();
        var lyrics = _lyricsModule.CurrentLyrics;

        if (track.IsEmpty)
        {
            _timelineEngine.Reset();
            TxtSongTitle.Text = "No active media session";
            TxtArtistAlbum.Text = "Play music in Spotify, Apple Music, or your browser";
            TxtSongTitleCompact.Text = "No active media session";
            TxtArtistAlbumCompact.Text = "Play music in Spotify, Apple Music, or your browser";
            ProgressTimeline.Value = 0;
            ProgressTimelineCompact.Value = 0;
            TxtElapsed.Text = "0:00";
            TxtRemaining.Text = "--:--";
            TxtElapsedCompact.Text = "0:00";
            TxtRemainingCompact.Text = "--:--";
            TxtEmptyState.Text = "Waiting for playback...";
            TxtEmptyStateSub.Text = "Play a song in Spotify, Apple Music, or your browser";
            PanelEmptyState.Visibility = Visibility.Visible;
            LyricsScrollViewer.Visibility = Visibility.Collapsed;
            IconArtworkPlaceholder.Visibility = Visibility.Visible;
            ImgTrackArtwork.Source = null;
            IconArtworkPlaceholderCompact.Visibility = Visibility.Visible;
            ImgTrackArtworkCompact.Source = null;
            _targetAmbientPalette = AmbientBackgroundPalette.Default;
            return;
        }

        string songKey = $"{track.Artist?.Trim()}_{track.Title?.Trim()}";
        if (songKey != _lastSongKey)
        {
            ResetTrackState(track);
            return;
        }

        int currentArtSig = ArtworkBitmapManager.ComputeArtworkSignature(track);
        if (track.ThumbnailData is { Length: > 0 } && currentArtSig != _lastArtworkSignature)
        {
            _lastArtworkSignature = currentArtSig;
            if (!ArtworkService.IsLowResolutionThumbnail(track.ThumbnailData) &&
                !ArtworkService.IsWebpImageData(track.ThumbnailData))
            {
                _artworkBitmapManager?.Invalidate(track.Artist, track.Title);
            }
            _ = UpdateArtworkAsync(track, Interlocked.Read(ref _windowTrackSequence));
        }

        // Title and Metadata
        TxtSongTitle.Text = track.Title;
        string artistAlbum = string.IsNullOrWhiteSpace(track.Album)
            ? track.Artist
            : $"{track.Artist} — {track.Album}";
        TxtArtistAlbum.Text = artistAlbum;
        TxtSongTitleCompact.Text = track.Title;
        TxtArtistAlbumCompact.Text = artistAlbum;

        // Position and Durations from the Central Playback Timeline Engine
        var pos = _timelineEngine.CurrentTrackPosition;
        var dur = _timelineEngine.CurrentTrackDuration > TimeSpan.Zero
            ? _timelineEngine.CurrentTrackDuration
            : track.Duration;

        if (!_isDraggingProgressBar)
        {
            TxtElapsed.Text = _timelineEngine.CurrentTrackPositionFormatted;
            TxtElapsedCompact.Text = _timelineEngine.CurrentTrackPositionFormatted;
            TxtRemaining.Text = _timelineEngine.CurrentTrackRemainingFormatted;
            TxtRemainingCompact.Text = _timelineEngine.CurrentTrackRemainingFormatted;

            double progressPct = _timelineEngine.CurrentTrackProgressPercentage;
            ProgressTimeline.Value = progressPct;
            ProgressTimelineCompact.Value = progressPct;
        }

        // Hardware Output Device Info
        UpdateOutputDeviceUi();

        // Media Transport Controls
        bool hasTrack = !track.IsEmpty;
        BtnPrev.IsEnabled = hasTrack;
        BtnPlayPause.IsEnabled = hasTrack;
        BtnNext.IsEnabled = hasTrack;
        BtnPrevCompact.IsEnabled = hasTrack;
        BtnPlayPauseCompact.IsEnabled = hasTrack;
        BtnNextCompact.IsEnabled = hasTrack;

        var playGlyph = (_timelineEngine.PlaybackStatus == MediaPlaybackStatus.Playing) ? "\uE769" : "\uE768";
        IconPlayPause.Glyph = playGlyph;
        IconPlayPauseCompact.Glyph = playGlyph;

        // Synchronizer position evaluation
        _lyricsModule.Synchronizer.EvaluatePosition(pos);

        // Font scaling and dynamic lyrics rebuilding
        double focusedSize = _settingsManager.Current.FocusedLyricFontSize > 0 ? _settingsManager.Current.FocusedLyricFontSize : 28.0;
        double unfocusedSize = _settingsManager.Current.UnfocusedLyricFontSize > 0 ? _settingsManager.Current.UnfocusedLyricFontSize : 20.0;
        int fontScaleIndex = _settingsManager.Current.LyricsFontScaleIndex;

        // Strictly guard against previous track lyrics remaining on screen
        string expectedSongKey = $"{track.Artist?.Trim()}_{track.Title?.Trim()}";
        bool lyricsBelongsToCurrentTrack = !string.IsNullOrEmpty(_lyricsModule.CurrentLyricsSongKey) &&
                                           (string.Equals(_lyricsModule.CurrentLyricsSongKey, expectedSongKey, StringComparison.OrdinalIgnoreCase) ||
                                            (lyrics != null && string.Equals(lyrics.Title, track.Title, StringComparison.OrdinalIgnoreCase)));

        if (!lyricsBelongsToCurrentTrack || lyrics == null || lyrics.Lines.Count == 0)
        {
            if (_currentRenderedLines.Count > 0)
            {
                LyricsPanel.Children.Clear();
                _lineContainers.Clear();
                _currentRenderedLines.Clear();
                LyricsScrollViewer.Visibility = Visibility.Collapsed;
                PanelEmptyState.Visibility = Visibility.Visible;
            }
            if (_compactLineBlocks.Count > 0)
            {
                CompactLyricsPanel.Children.Clear();
                _compactLineBlocks.Clear();
            }
        }
        else
        {
            if (!ReferenceEquals(lyrics, _lastLyrics) || fontScaleIndex != _lastFontScaleIndex ||
                Math.Abs(focusedSize - _lastFocusedSize) > 0.5 || Math.Abs(unfocusedSize - _lastUnfocusedSize) > 0.5)
            {
                _lastLyrics = lyrics;
                _lastFontScaleIndex = fontScaleIndex;
                _lastFocusedSize = focusedSize;
                _lastUnfocusedSize = unfocusedSize;
                RebuildLyricsUi(lyrics);
            }

            // Active line synchronization
            int activeIdx = _lyricsModule.ActiveLineIndex;
            if (activeIdx != _currentActiveIndex)
            {
                UpdateActiveLineTargets(activeIdx, immediate: _currentActiveIndex == -99);
            }

            if (_currentMode == LyricsWindowMode.Compact && _isCompactLyricsExpanded)
            {
                if (!ReferenceEquals(lyrics, _lastCompactLyrics))
                {
                    RebuildCompactLyricsUi(lyrics);
                }
                UpdateCompactActiveLine(activeIdx);
            }
        }

        // 60 FPS smooth visual interpolation
        StepSmoothAnimations();
    }

    private void UpdateOutputDeviceUi()
    {
        string deviceName = _audioCaptureService?.CurrentDevice?.Name
                            ?? _volumeService?.OutputDeviceFriendlyName
                            ?? "Speakers";
        var category = _volumeService?.OutputDeviceCategory ?? AudioDeviceCategory.Speakers;

        string glyph = category switch
        {
            AudioDeviceCategory.Headphones => "\uE7F6",
            AudioDeviceCategory.Bluetooth => "\uE702",
            _ => "\uE767"
        };

        IconDevice.Glyph = glyph;
        TxtDevice.Text = deviceName;
        IconDeviceCompact.Glyph = glyph;
        TxtDeviceCompact.Text = deviceName;
    }

    private async Task UpdateArtworkAsync(MediaTrackInfo track, long sequenceId = 0)
    {
        if (_artworkBitmapManager == null) return;
        string expectedSongKey = $"{track.Artist}_{track.Title}";
        try
        {
            var bitmap = await _artworkBitmapManager.GetArtworkBitmapAsync(track);
            if (sequenceId > 0 && Interlocked.Read(ref _windowTrackSequence) != sequenceId) return;
            if (!string.Equals(_lastSongKey, expectedSongKey, StringComparison.OrdinalIgnoreCase)) return;

            if (bitmap != null)
            {
                bool allowAnim = ArtworkBitmapManager.AreSystemAnimationsEnabled() && _settingsManager.Current.EnableAnimations;
                bitmap.AutoPlay = allowAnim;
                if (!allowAnim)
                {
                    try { bitmap.Stop(); } catch { }
                }

                ImgTrackArtwork.Opacity = 1.0;
                ImgTrackArtworkCompact.Opacity = 1.0;
                ImgTrackArtwork.Source = bitmap;
                IconArtworkPlaceholder.Visibility = Visibility.Collapsed;
                ImgTrackArtworkCompact.Source = bitmap;
                IconArtworkPlaceholderCompact.Visibility = Visibility.Collapsed;
                LocalDiagnosticLogger.LogInfo($"[LyricsWindow] Bound artwork bitmap for '{track.Artist} - {track.Title}' (Mode={_currentMode}).");
            }
            else
            {
                ImgTrackArtwork.Source = null;
                IconArtworkPlaceholder.Visibility = Visibility.Visible;
                ImgTrackArtworkCompact.Source = null;
                IconArtworkPlaceholderCompact.Visibility = Visibility.Visible;
            }

            var palette = await _artworkBitmapManager.ExtractArtworkPaletteAsync(track);
            if (sequenceId > 0 && Interlocked.Read(ref _windowTrackSequence) != sequenceId) return;
            _targetAmbientPalette = ArtworkPaletteExtractor.ExtractAmbientPalette(palette);
        }
        catch
        {
            if (sequenceId > 0 && Interlocked.Read(ref _windowTrackSequence) != sequenceId) return;
            ImgTrackArtwork.Source = null;
            IconArtworkPlaceholder.Visibility = Visibility.Visible;
            ImgTrackArtworkCompact.Source = null;
            IconArtworkPlaceholderCompact.Visibility = Visibility.Visible;
            _targetAmbientPalette = AmbientBackgroundPalette.Default;
        }
    }

    private void RebuildLyricsUi(SyncedLyrics lyrics)
    {
        LyricsPanel.Children.Clear();
        LyricsPanel.Spacing = _settingsManager.Current.LyricsLineSpacing > 0 ? _settingsManager.Current.LyricsLineSpacing : 20;
        bool isCenter = string.Equals(_settingsManager.Current.LyricsAlignment, "Center", StringComparison.OrdinalIgnoreCase);
        var horizAlign = isCenter ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        var textAlign = isCenter ? TextAlignment.Center : TextAlignment.Left;
        _lineContainers.Clear();
        _lineSubBaseBlocks.Clear();
        _lineSubOverlayBlocks.Clear();
        _lineSubClips.Clear();
        _lineSubStrings.Clear();
        _currentRenderedLines.Clear();
        _currentOpacities.Clear();
        _targetOpacities.Clear();
        _currentScales.Clear();
        _targetScales.Clear();
        _currentActiveIndex = -99;

        UpdateViewportPadding();

        if (lyrics.Lines.Count == 0)
        {
            if (!string.IsNullOrEmpty(lyrics.PlainLyrics))
            {
                PanelEmptyState.Visibility = Visibility.Collapsed;
                LyricsScrollViewer.Visibility = Visibility.Visible;

                var plainBlock = new TextBlock
                {
                    Text = lyrics.PlainLyrics,
                    FontSize = 17,
                    LineHeight = 28,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.88,
                    HorizontalAlignment = horizAlign,
                    TextAlignment = textAlign
                };
                LyricsPanel.Children.Add(plainBlock);
            }
            else
            {
                TxtEmptyState.Text = "Searching for synchronized lyrics...";
                TxtEmptyStateSub.Text = "Looking up synchronized lyrics from LRCLIB...";
                PanelEmptyState.Visibility = Visibility.Visible;
                LyricsScrollViewer.Visibility = Visibility.Collapsed;
            }
            return;
        }

        PanelEmptyState.Visibility = Visibility.Collapsed;
        LyricsScrollViewer.Visibility = Visibility.Visible;

        double focusedSize = _lastResponsiveFocusedSize > 0 ? _lastResponsiveFocusedSize : (_settingsManager.Current.FocusedLyricFontSize > 0 ? _settingsManager.Current.FocusedLyricFontSize : 28.0);
        double unfocusedSize = _lastResponsiveUnfocusedSize > 0 ? _lastResponsiveUnfocusedSize : (_settingsManager.Current.UnfocusedLyricFontSize > 0 ? _settingsManager.Current.UnfocusedLyricFontSize : 20.0);
        _lastFocusedSize = _settingsManager.Current.FocusedLyricFontSize > 0 ? _settingsManager.Current.FocusedLyricFontSize : 28.0;
        _lastUnfocusedSize = _settingsManager.Current.UnfocusedLyricFontSize > 0 ? _settingsManager.Current.UnfocusedLyricFontSize : 20.0;

        // Constant layout typography per line guarantees zero line-height/rewrap jitter when active line transitions!
        double layoutFontSize = Math.Round(Math.Clamp((focusedSize * 0.45) + (unfocusedSize * 0.55), 14.0, 42.0), 1);
        double layoutLineHeight = Math.Round(layoutFontSize * 1.34, 1);

        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var target = LyricsViewportCalculator.GetLineVisualTarget(i, -1, focusedSize, unfocusedSize);
            string lineText = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;

            // Split into sequential sub-lines (either explicit \n or wrapped sub-lines matching viewport width)
            double availLyricsW = LyricsScrollViewer.ActualWidth > 80.0
                ? LyricsScrollViewer.ActualWidth - 48.0
                : (RootLayout.ActualWidth > 200.0 ? (RootLayout.ActualWidth * 0.50) - 72.0 : 380.0);
            int maxChars = Math.Clamp((int)Math.Floor(availLyricsW / Math.Max(10.0, layoutFontSize * 0.58)), 18, 50);
            var subLines = maxChars > 0
                ? LyricsFillProgressCalculator.SplitIntoSequentialSubLines(lineText, maxCharsPerSubLine: maxChars)
                : LyricsFillProgressCalculator.SplitIntoSequentialSubLines(lineText, maxCharsPerSubLine: 46);
            var subBaseList = new List<TextBlock>(subLines.Count);
            var subOverlayList = new List<TextBlock>(subLines.Count);
            var subClipList = new List<RectangleGeometry>(subLines.Count);

            var lineStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2,
                HorizontalAlignment = horizAlign
            };

            foreach (var subText in subLines)
            {
                // Base TextBlock (dim/secondary inactive color) — constant FontSize/LineHeight/FontWeight prevents layout shift
                var baseBlock = new TextBlock
                {
                    Text = subText,
                    FontSize = layoutFontSize,
                    FontWeight = FontWeights.Normal,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = horizAlign,
                    TextAlignment = textAlign,
                    LineHeight = layoutLineHeight,
                    Foreground = InactiveLyricBrush
                };

                // Overlay TextBlock: Active highlight clipped by RectangleGeometry sequentially per sub-line
                // Must remain Visible so WinUI measures ActualWidth/ActualHeight immediately during layout.
                // Clipping geometry Rect(0,0,0,0) ensures completely transparent rendering until progress occurs.
                var overlayClip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 0, 0) };
                var overlayBlock = new TextBlock
                {
                    Text = subText,
                    FontSize = layoutFontSize,
                    FontWeight = FontWeights.Normal,
                    TextWrapping = TextWrapping.NoWrap,
                    HorizontalAlignment = horizAlign,
                    TextAlignment = textAlign,
                    LineHeight = layoutLineHeight,
                    Foreground = ActiveLyricBrush,
                    Clip = overlayClip,
                    Visibility = Visibility.Visible
                };

                var subGrid = new Grid
                {
                    HorizontalAlignment = horizAlign
                };
                subGrid.Children.Add(baseBlock);
                subGrid.Children.Add(overlayBlock);
                lineStack.Children.Add(subGrid);

                subBaseList.Add(baseBlock);
                subOverlayList.Add(overlayBlock);
                subClipList.Add(overlayClip);
            }

            var container = new Border
            {
                Child = lineStack,
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Opacity = target.TargetOpacity,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 },
                Background = TransparentBrush
            };

            var targetModelLine = line;
            container.PointerPressed += async (_, _) =>
            {
                await SeekToLyricLineAsync(targetModelLine.Timestamp);
            };

            _lineSubBaseBlocks.Add(subBaseList);
            _lineSubOverlayBlocks.Add(subOverlayList);
            _lineSubClips.Add(subClipList);
            _lineSubStrings.Add(subLines);
            _currentRenderedLines.Add(line);
            _lineContainers.Add(container);
            _currentOpacities.Add(target.TargetOpacity);
            _targetOpacities.Add(target.TargetOpacity);
            _currentScales.Add(target.TargetScale);
            _targetScales.Add(target.TargetScale);

            LyricsPanel.Children.Add(container);
        }

        _snapToCenterOnNextLayout = true;

        // Immediately evaluate current playback position so if lyrics loaded mid-track, active line resolves immediately
        _lyricsModule.Synchronizer.EvaluatePosition(_timelineEngine.CurrentTrackPosition);
        UpdateActiveLineTargets(_lyricsModule.ActiveLineIndex, immediate: true);
    }

    private void UpdateActiveLineTargets(int activeIdx, bool immediate)
    {
        if (_lineContainers.Count == 0) return;

        _currentActiveIndex = activeIdx;
        double focusedSize = _lastResponsiveFocusedSize > 0 ? _lastResponsiveFocusedSize : (_settingsManager.Current.FocusedLyricFontSize > 0 ? _settingsManager.Current.FocusedLyricFontSize : 28.0);
        double unfocusedSize = _lastResponsiveUnfocusedSize > 0 ? _lastResponsiveUnfocusedSize : (_settingsManager.Current.UnfocusedLyricFontSize > 0 ? _settingsManager.Current.UnfocusedLyricFontSize : 20.0);

        for (int i = 0; i < _lineContainers.Count; i++)
        {
            bool isThisLineActive = (i == activeIdx);
            var target = LyricsViewportCalculator.GetLineVisualTarget(i, activeIdx, focusedSize, unfocusedSize);
            _targetOpacities[i] = target.TargetOpacity;
            _targetScales[i] = isThisLineActive ? target.TargetScale : 1.0;

            if (!isThisLineActive && _currentScales[i] > 1.0)
            {
                _currentScales[i] = 1.0;
                if (_lineContainers[i].RenderTransform is ScaleTransform stPrev)
                {
                    stPrev.ScaleX = 1.0;
                    stPrev.ScaleY = 1.0;
                }
            }

            var baseList = _lineSubBaseBlocks[i];
            var overlayList = i < _lineSubOverlayBlocks.Count ? _lineSubOverlayBlocks[i] : null;
            for (int s = 0; s < baseList.Count; s++)
            {
                var block = baseList[s];
                block.FontWeight = isThisLineActive ? FontWeights.Bold : FontWeights.Normal;
                if (overlayList != null && s < overlayList.Count)
                {
                    overlayList[s].FontWeight = isThisLineActive ? FontWeights.Bold : FontWeights.Normal;
                }
            }

            if (i < _lineSubOverlayBlocks.Count)
            {
                overlayList = _lineSubOverlayBlocks[i];
                var clipList = _lineSubClips[i];
                var subStrings = _lineSubStrings[i];

                if (target.IsActive && i < _currentRenderedLines.Count)
                {
                    var renderLine = _currentRenderedLines[i];
                    var cPos = _timelineEngine.CurrentTrackPosition + TimeSpan.FromMilliseconds(_lyricsModule.Synchronizer.SyncOffsetMs);
                    if (cPos < TimeSpan.Zero) cPos = TimeSpan.Zero;
                    TimeSpan? nTs = (i + 1 < _currentRenderedLines.Count) ? _currentRenderedLines[i + 1].Timestamp : null;
                    var subProgresses = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(renderLine, subStrings, cPos, nTs);

                    for (int s = 0; s < overlayList.Count; s++)
                    {
                        var overlay = overlayList[s];
                        var baseB = (s < baseList.Count) ? baseList[s] : null;
                        double actualW = overlay.ActualWidth > 0 ? overlay.ActualWidth : (baseB != null && baseB.ActualWidth > 0 ? baseB.ActualWidth : 0.0);
                        double actualH = overlay.ActualHeight > 0 ? overlay.ActualHeight : (baseB != null && baseB.ActualHeight > 0 ? baseB.ActualHeight : 200.0);
                        double sp = (s < subProgresses.Count) ? subProgresses[s] : 0.0;

                        if (overlay.Visibility != Visibility.Visible)
                        {
                            overlay.Visibility = Visibility.Visible;
                        }

                        if (actualW > 0 && sp > 0.001)
                        {
                            clipList[s].Rect = new Windows.Foundation.Rect(0, 0, sp * actualW, actualH);
                        }
                        else
                        {
                            clipList[s].Rect = new Windows.Foundation.Rect(0, 0, 0, 0);
                        }
                    }
                }
                else
                {
                    for (int s = 0; s < overlayList.Count; s++)
                    {
                        var overlay = overlayList[s];
                        overlay.Visibility = Visibility.Collapsed;
                        clipList[s].Rect = new Windows.Foundation.Rect(0, 0, 0, 0);
                    }
                }
            }

            if (immediate)
            {
                _currentOpacities[i] = target.TargetOpacity;
                _currentScales[i] = isThisLineActive ? target.TargetScale : 1.0;
                _lineContainers[i].Opacity = target.TargetOpacity;
                if (_lineContainers[i].RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = isThisLineActive ? target.TargetScale : 1.0;
                    st.ScaleY = isThisLineActive ? target.TargetScale : 1.0;
                }
            }
        }

        RecalculateCenterScrollTarget(immediate);
    }

    private void RecalculateCenterScrollTarget(bool immediate)
    {
        if (_lineContainers.Count == 0) return;

        int targetIdx = _currentActiveIndex >= 0 && _currentActiveIndex < _lineContainers.Count
            ? _currentActiveIndex
            : 0;

        var activeContainer = _lineContainers[targetIdx];
        double viewportH = LyricsScrollViewer.ActualHeight;
        if (viewportH <= 0 || activeContainer.ActualHeight <= 0) return;

        try
        {
            var transform = activeContainer.TransformToVisual(LyricsPanel);
            var pointInPanel = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            _targetScrollOffsetY = LyricsViewportCalculator.CalculateTargetCenterOffset(
                viewportHeight: viewportH,
                lineTopInContent: pointInPanel.Y,
                lineHeight: activeContainer.ActualHeight,
                maxScrollableHeight: LyricsScrollViewer.ScrollableHeight);

            if (immediate || _snapToCenterOnNextLayout)
            {
                _snapToCenterOnNextLayout = false;
                _currentScrollOffsetY = _targetScrollOffsetY;
                LyricsScrollViewer.ChangeView(null, _targetScrollOffsetY, null, disableAnimation: true);
            }
        }
        catch
        {
            // Safeguard during layout transitions
        }
    }

    private void StepSmoothAnimations()
    {
        if (_lineContainers.Count == 0) return;

        // Auto-center snap check as soon as layout measures first container
        if (_snapToCenterOnNextLayout && _lineContainers[0].ActualHeight > 0)
        {
            RecalculateCenterScrollTarget(immediate: true);
        }

        // 1. Smoothly interpolate each lyric line's opacity and scale at 60 FPS
        for (int i = 0; i < _lineContainers.Count; i++)
        {
            double curOp = _currentOpacities[i];
            double tgtOp = _targetOpacities[i];
            if (Math.Abs(tgtOp - curOp) > 0.002)
            {
                double nextOp = LyricsViewportCalculator.InterpolateValue(curOp, tgtOp, 0.22);
                _currentOpacities[i] = nextOp;
                _lineContainers[i].Opacity = nextOp;
            }

            double curSc = _currentScales[i];
            double tgtSc = (i == _currentActiveIndex) ? _targetScales[i] : 1.0;
            if (Math.Abs(tgtSc - curSc) > 0.001)
            {
                double nextSc = LyricsViewportCalculator.InterpolateValue(curSc, tgtSc, 0.20);
                if (i != _currentActiveIndex && nextSc > 1.0)
                {
                    nextSc = 1.0;
                }
                _currentScales[i] = nextSc;
                if (_lineContainers[i].RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = nextSc;
                    st.ScaleY = nextSc;
                }
            }
        }

        // 2. Real-time Apple Music-style karaoke fill clipping on the active line
        var lyrics = _lyricsModule.CurrentLyrics;
        var currentPos = _timelineEngine.CurrentTrackPosition + TimeSpan.FromMilliseconds(_lyricsModule.Synchronizer.SyncOffsetMs);
        if (currentPos < TimeSpan.Zero) currentPos = TimeSpan.Zero;
        var trackDur = _timelineEngine.CurrentTrackDuration > TimeSpan.Zero
            ? _timelineEngine.CurrentTrackDuration
            : (_lyricsModule.CurrentTrack.Duration > TimeSpan.Zero ? _lyricsModule.CurrentTrack.Duration : (TimeSpan?)null);

        if (lyrics != null && lyrics.Lines.Count > 0)
        {
            int lineLimit = Math.Min(_lineSubClips.Count, Math.Min(_lineSubOverlayBlocks.Count, lyrics.Lines.Count));
            for (int i = 0; i < lineLimit; i++)
            {
                var overlayList = _lineSubOverlayBlocks[i];
                var clipList = _lineSubClips[i];
                var subStrings = _lineSubStrings[i];

                if (i != _currentActiveIndex)
                {
                    for (int s = 0; s < overlayList.Count; s++)
                    {
                        var overlay = overlayList[s];
                        if (overlay.Visibility != Visibility.Visible)
                        {
                            overlay.Visibility = Visibility.Visible;
                        }
                        if (clipList[s].Rect.Width > 0 || clipList[s].Rect.Height > 0)
                        {
                            clipList[s].Rect = new Windows.Foundation.Rect(0, 0, 0, 0);
                        }
                    }
                    continue;
                }

                var line = lyrics.Lines[i];
                TimeSpan? nextTimestamp = (i + 1 < lyrics.Lines.Count) ? lyrics.Lines[i + 1].Timestamp : null;

                var subProgresses = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
                    line,
                    subStrings,
                    currentPos,
                    nextTimestamp,
                    trackDur);

                var baseList = i < _lineSubBaseBlocks.Count ? _lineSubBaseBlocks[i] : null;

                for (int s = 0; s < overlayList.Count; s++)
                {
                    var overlay = overlayList[s];
                    var baseB = (baseList != null && s < baseList.Count) ? baseList[s] : null;
                    double actualW = overlay.ActualWidth > 0 ? overlay.ActualWidth : (baseB != null && baseB.ActualWidth > 0 ? baseB.ActualWidth : 0.0);
                    double actualH = overlay.ActualHeight > 0 ? overlay.ActualHeight : (baseB != null && baseB.ActualHeight > 0 ? baseB.ActualHeight : 200.0);
                    double sp = (s < subProgresses.Count) ? subProgresses[s] : 0.0;

                    if (overlay.Visibility != Visibility.Visible)
                    {
                        overlay.Visibility = Visibility.Visible;
                    }

                    if (actualW > 0 && sp > 0.001)
                    {
                        clipList[s].Rect = new Windows.Foundation.Rect(0, 0, sp * actualW, actualH);
                    }
                    else
                    {
                        clipList[s].Rect = new Windows.Foundation.Rect(0, 0, 0, 0);
                    }
                }
            }
        }

        // 3. Keep active line centered with smooth 60 FPS easing
        if (_userIsInteracting)
        {
            if ((DateTime.UtcNow - _lastUserInteractionUtc).TotalSeconds < 2.8)
            {
                return;
            }
            _userIsInteracting = false;
        }

        if (Math.Abs(_targetScrollOffsetY - _currentScrollOffsetY) > 0.25)
        {
            _currentScrollOffsetY = LyricsViewportCalculator.InterpolateScrollOffset(
                _currentScrollOffsetY,
                _targetScrollOffsetY,
                smoothingFactor: 0.16,
                snapEpsilon: 0.35);

            LyricsScrollViewer.ChangeView(null, _currentScrollOffsetY, null, disableAnimation: true);
        }

        // 4. Preserved ambient dynamic artwork background 60 FPS cross-fade
        _currentAmbientPalette = AmbientBackgroundPalette.Lerp(_currentAmbientPalette, _targetAmbientPalette, 0.065f);
        StopTopLeft.Color = Windows.UI.Color.FromArgb(255, _currentAmbientPalette.Dominant.R, _currentAmbientPalette.Dominant.G, _currentAmbientPalette.Dominant.B);
        StopCenter.Color = Windows.UI.Color.FromArgb(255, _currentAmbientPalette.Secondary.R, _currentAmbientPalette.Secondary.G, _currentAmbientPalette.Secondary.B);
        StopBottomRight.Color = Windows.UI.Color.FromArgb(255, _currentAmbientPalette.Tertiary.R, _currentAmbientPalette.Tertiary.G, _currentAmbientPalette.Tertiary.B);
    }

    private void UpdateLayoutForCurrentSize()
    {
        if (_isResizingProgrammatically) return;
        double w = RootLayout.ActualWidth;
        double h = RootLayout.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Hysteresis transition: When in Compact mode and user widens window >= CompactToNormalWidthThreshold,
        // automatically switch back to Horizontal normal mode while preserving user's resized window dimensions.
        if (_currentMode == LyricsWindowMode.Compact)
        {
            if (!_isCompactLyricsExpanded && w >= LyricsViewportCalculator.CompactToNormalWidthThreshold && h >= 320.0)
            {
                SetWindowMode(LyricsWindowMode.Horizontal, resizeWindow: false);
            }
            return;
        }

        if (w <= LyricsViewportCalculator.NormalToCompactWidthThreshold || h <= LyricsViewportCalculator.NormalToCompactHeightThreshold)
        {
            SetWindowMode(LyricsWindowMode.Compact, resizeWindow: true);
            return;
        }

        // Responsive proportional scaling for Horizontal mode
        ApplyResponsiveScaling(w, h);
        UpdateViewportPadding();
    }

    private void ApplyResponsiveScaling(double windowWidth, double windowHeight)
    {
        double baseFocused = _settingsManager.Current.FocusedLyricFontSize > 0 ? _settingsManager.Current.FocusedLyricFontSize : 28.0;
        double baseUnfocused = _settingsManager.Current.UnfocusedLyricFontSize > 0 ? _settingsManager.Current.UnfocusedLyricFontSize : 20.0;

        var metrics = LyricsViewportCalculator.CalculateResponsiveMetrics(
            windowWidth,
            windowHeight,
            _isLyricsEnabled,
            baseFocused,
            baseUnfocused);

        if (Math.Abs(metrics.ArtworkSize - _lastAppliedResponsiveArtworkSize) >= 2.0)
        {
            _lastAppliedResponsiveArtworkSize = metrics.ArtworkSize;

            PlayerInnerPanel.Width = metrics.PlayerColumnWidth;
            BorderTrackArtwork.Width = metrics.ArtworkSize;
            BorderTrackArtwork.Height = metrics.ArtworkSize;

            TxtSongTitle.Width = metrics.PlayerColumnWidth;
            TxtSongTitle.FontSize = metrics.SongTitleFontSize;

            TxtArtistAlbum.Width = metrics.PlayerColumnWidth;
            TxtArtistAlbum.FontSize = metrics.ArtistAlbumFontSize;

            GridProgressHitBox.Width = metrics.ProgressBarWidth;
            ProgressTimeline.Width = metrics.ProgressBarWidth;

            GridTimestamps.Width = metrics.ProgressBarWidth;
            TxtElapsed.FontSize = metrics.TimestampFontSize;
            TxtRemaining.FontSize = metrics.TimestampFontSize;

            TxtDevice.FontSize = metrics.DeviceFontSize;
            TxtDevice.MaxWidth = Math.Max(160.0, metrics.PlayerColumnWidth - 36.0);

            GridControlsRow.Width = metrics.PlayerColumnWidth;
            BtnPlayPause.Width = metrics.PlayPauseButtonSize;
            BtnPlayPause.Height = metrics.PlayPauseButtonSize;
            BtnPlayPause.CornerRadius = new CornerRadius(metrics.PlayPauseButtonSize / 2.0);

            BtnPrev.Width = metrics.SkipButtonSize;
            BtnPrev.Height = metrics.SkipButtonSize;
            BtnPrev.CornerRadius = new CornerRadius(metrics.SkipButtonSize / 2.0);

            BtnNext.Width = metrics.SkipButtonSize;
            BtnNext.Height = metrics.SkipButtonSize;
            BtnNext.CornerRadius = new CornerRadius(metrics.SkipButtonSize / 2.0);

            BtnDarMod.Width = metrics.UtilityButtonSize;
            BtnDarMod.Height = metrics.UtilityButtonSize;
            BtnFullscreen.Width = metrics.UtilityButtonSize;
            BtnFullscreen.Height = metrics.UtilityButtonSize;
            BtnToggleLyrics.Width = metrics.UtilityButtonSize;
            BtnToggleLyrics.Height = metrics.UtilityButtonSize;

            GridVolumeRow.Width = metrics.VolumeBarWidth;
            GridOffsetRow.Width = metrics.PlayerColumnWidth;

            LyricsPanel.Spacing = metrics.LyricLineSpacing;
        }

        if (Math.Abs(metrics.FocusedLyricFontSize - _lastResponsiveFocusedSize) >= 1.0 ||
            Math.Abs(metrics.UnfocusedLyricFontSize - _lastResponsiveUnfocusedSize) >= 1.0)
        {
            _lastResponsiveFocusedSize = metrics.FocusedLyricFontSize;
            _lastResponsiveUnfocusedSize = metrics.UnfocusedLyricFontSize;
            if (_lastLyrics != null && _lineContainers.Count > 0)
            {
                RebuildLyricsUi(_lastLyrics);
            }
        }
    }

    private void ToggleFullscreen()
    {
        try
        {
            if (AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
            {
                AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
                IconFullscreen.Glyph = "\uE740";
            }
            else
            {
                AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                IconFullscreen.Glyph = "\uE73F";
            }
            UpdateLayoutForCurrentSize();
        }
        catch { }
    }

    private void SyncVolumeUi()
    {
        if (_volumeService == null || _isUpdatingVolumeUi) return;
        _isUpdatingVolumeUi = true;
        try
        {
            int vol = _volumeService.GetPlaybackVolumePercent();
            bool muted = _volumeService.IsPlaybackMuted;
            SliderVolume.Value = vol;
            if (SliderVolumeCompact != null) SliderVolumeCompact.Value = vol;
            TxtVolumePercent.Text = muted ? "Muted" : $"{vol}%";
            string muteGlyph = (muted || vol == 0) ? "\uE74F" : "\uE767";
            IconVolumeMute.Glyph = muteGlyph;
            if (IconVolumeMuteCompact != null) IconVolumeMuteCompact.Glyph = muteGlyph;
            if (IconPopupMuteCompact != null) IconPopupMuteCompact.Glyph = (muted || vol == 0) ? "\uE74F" : "\uE992";
        }
        finally
        {
            _isUpdatingVolumeUi = false;
        }
    }

    private void ShowSeekUnsupportedWarning()
    {
        var loc = LocalizationService.Instance;
        string? sourceAppId = _mediaMonitor?.SourceAppId;
        if (MediaSessionProfileHelper.IsAppleMusicDesktop(sourceAppId))
        {
            _toastManager?.ShowToast(
                loc["Lyrics_AppleMusicDesktopSeekWarning"],
                title: "Apple Music",
                severity: InfoBarSeverity.Warning,
                actionLabel: loc["Lyrics_OpenAppleMusicWeb"],
                onAction: OpenAppleMusicWebInBrowser);
        }
        else
        {
            _toastManager?.ShowToast(
                loc["Lyrics_SeekFailed"],
                title: loc["Lyrics_Title"],
                severity: InfoBarSeverity.Informational);
        }
    }

    private void OpenAppleMusicWebInBrowser()
    {
        if (!DefaultWebBrowserLauncher.TryOpenUrlInDefaultBrowser(MediaSessionProfileHelper.AppleMusicWebUrl, out _, out var error))
        {
            LocalDiagnosticLogger.LogWarning($"[LyricsWindow] {error ?? "Failed to open Apple Music Web in default browser."}");
            _toastManager?.ShowToast(
                error ?? "Could not launch default web browser.",
                title: "Apple Music Web",
                severity: InfoBarSeverity.Warning);
        }
    }

    private async Task SeekToMediaPositionAsync(TimeSpan timestamp)
    {
        if (_mediaMonitor == null) return;
        if (!_mediaMonitor.CanSeek)
        {
            ShowSeekUnsupportedWarning();
            return;
        }

        if (timestamp < TimeSpan.Zero) timestamp = TimeSpan.Zero;

        bool ok = await _mediaMonitor.TrySeekAsync(timestamp);
        if (!ok)
        {
            ShowSeekUnsupportedWarning();
        }
        else
        {
            _timelineEngine.HandleSeek(timestamp);
            _lyricsModule.UpdateTimelineExternal(_timelineEngine.ToMediaTimelineInfo());
            _lyricsModule.Synchronizer.EvaluatePosition(timestamp);
            UpdateActiveLineTargets(_lyricsModule.ActiveLineIndex, immediate: false);
            if (_currentMode == LyricsWindowMode.Compact && _isCompactLyricsExpanded)
            {
                UpdateCompactActiveLine(_lyricsModule.ActiveLineIndex);
            }
            RecalculateCenterScrollTarget(immediate: false);
        }
    }

    private async Task SeekToLyricLineAsync(TimeSpan timestamp)
    {
        if (_mediaMonitor == null) return;
        if (!_mediaMonitor.CanSeek)
        {
            ShowSeekUnsupportedWarning();
            return;
        }

        var dur = _lyricsModule.CurrentTrack.Duration > TimeSpan.Zero
            ? _lyricsModule.CurrentTrack.Duration
            : _lyricsModule.CurrentTimeline.Duration;
        int offsetMs = _lyricsModule.Synchronizer.SyncOffsetMs;
        timestamp -= TimeSpan.FromMilliseconds(offsetMs);
        timestamp = LyricsViewportCalculator.CalculateTargetMediaSeekPosition(timestamp, 0, dur);

        bool ok = await _mediaMonitor.TrySeekAsync(timestamp);
        if (!ok)
        {
            ShowSeekUnsupportedWarning();
        }
        else
        {
            _timelineEngine.HandleSeek(timestamp);
            _lyricsModule.UpdateTimelineExternal(_timelineEngine.ToMediaTimelineInfo());
            _lyricsModule.Synchronizer.EvaluatePosition(timestamp);
            UpdateActiveLineTargets(_lyricsModule.ActiveLineIndex, immediate: false);
            if (_currentMode == LyricsWindowMode.Compact && _isCompactLyricsExpanded)
            {
                UpdateCompactActiveLine(_lyricsModule.ActiveLineIndex);
            }
            RecalculateCenterScrollTarget(immediate: false);
        }
    }

    public void Shutdown()
    {
        _isShuttingDown = true;
        _uiTimer.Stop();
        Close();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Applies Light, Dark, or SystemDefault theme dynamically across the Lyrics Window.
    /// </summary>
    public void ApplyTheme(ThemeMode mode)
    {
        if (RootLayout == null) return;
        RootLayout.RequestedTheme = mode switch
        {
            ThemeMode.Dark => ElementTheme.Dark,
            ThemeMode.Light => ElementTheme.Light,
            _ => ElementTheme.Default
        };
        UpdateBackdropOpacity(_settingsManager.Current.LyricsBackdropOpacity);
    }

    private void UpdateScrubberToolTips()
    {
        bool canSeek = _mediaMonitor?.CanSeek ?? false;
        string toolTip = canSeek
            ? "Click or drag to seek"
            : LocalizationService.Instance["Lyrics_SeekFailed"];
        ToolTipService.SetToolTip(GridProgressHitBox, toolTip);
        ToolTipService.SetToolTip(GridProgressCompactHitBox, toolTip);
    }

    /// <summary>
    /// Displays an aesthetic empty state when synchronized lyrics are unavailable for the current track.
    /// </summary>
    public void SetLyricsNotFound(string? message = null)
    {
        if (_currentRenderedLines.Count == 0 && string.IsNullOrEmpty(_lastLyrics?.PlainLyrics))
        {
            var loc = LocalizationService.Instance;
            TxtEmptyState.Text = loc["Lyrics_NotFound"];
            TxtEmptyStateSub.Text = !string.IsNullOrWhiteSpace(message) ? message : loc["Lyrics_NotFound_Sub"];
            PanelEmptyState.Visibility = Visibility.Visible;
            LyricsScrollViewer.Visibility = Visibility.Collapsed;
        }
    }

    private void SetupDeviceSelectors()
    {
        BtnDeviceSelector.PointerEntered += (_, _) => BtnDeviceSelector.Opacity = 1.0;
        BtnDeviceSelector.PointerExited += (_, _) => BtnDeviceSelector.Opacity = 0.75;
        BtnDeviceSelectorCompact.PointerEntered += (_, _) => BtnDeviceSelectorCompact.Opacity = 1.0;
        BtnDeviceSelectorCompact.PointerExited += (_, _) => BtnDeviceSelectorCompact.Opacity = 0.75;

        FlyoutAudioDevices.Opening += (_, _) => PopulateAudioDeviceFlyout(FlyoutAudioDevices);
        FlyoutAudioDevicesCompact.Opening += (_, _) => PopulateAudioDeviceFlyout(FlyoutAudioDevicesCompact);

        if (_audioCaptureService != null)
        {
            _audioCaptureService.DeviceChanged += (_, dev) =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    _volumeService?.RefreshAndReconnect();
                    SyncVolumeUi();
                    UpdateOutputDeviceUi();
                });
            };

            _audioCaptureService.CaptureFaulted += (_, err) =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    _volumeService?.RefreshAndReconnect();
                    SyncVolumeUi();
                    UpdateOutputDeviceUi();
                    _toastManager?.ShowToast(
                        err.Message,
                        title: "Audio Output",
                        severity: InfoBarSeverity.Warning);
                });
            };
        }
    }

    private void PopulateAudioDeviceFlyout(MenuFlyout flyout)
    {
        flyout.Items.Clear();

        var header = new MenuFlyoutItem
        {
            Text = "Audio Output & Glow Endpoint",
            IsEnabled = false
        };
        flyout.Items.Add(header);
        flyout.Items.Add(new MenuFlyoutSeparator());

        var devices = _audioCaptureService?.GetAvailableDevices() ?? Array.Empty<AudioDeviceDescriptor>();
        var activeId = _audioCaptureService?.CurrentDevice?.Id;

        if (devices.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = "Default System Output",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var dev in devices)
            {
                bool isCurrent = (!string.IsNullOrEmpty(activeId) && string.Equals(dev.Id, activeId, StringComparison.OrdinalIgnoreCase))
                                 || (string.IsNullOrEmpty(activeId) && dev.IsDefault);

                var item = new ToggleMenuFlyoutItem
                {
                    Text = dev.IsDefault ? $"{dev.Name} (Default)" : dev.Name,
                    IsChecked = isCurrent
                };

                item.Click += async (_, _) =>
                {
                    try
                    {
                        // 1. Immediately update UI labels & glyph for immediate tactile feedback
                        TxtDevice.Text = dev.Name;
                        TxtDeviceCompact.Text = dev.Name;
                        string immediateGlyph = dev.Name.ToLowerInvariant().Contains("headphone") || dev.Name.ToLowerInvariant().Contains("headset")
                            ? "\uE7F6"
                            : (dev.Name.ToLowerInvariant().Contains("bluetooth") ? "\uE702" : "\uE767");
                        IconDevice.Glyph = immediateGlyph;
                        IconDeviceCompact.Glyph = immediateGlyph;

                        // 2. Set default Windows audio playback device endpoint
                        bool switchedSystemOutput = _audioRoutingService?.SetDefaultPlaybackDevice(dev.Id) ?? false;
                        _settingsManager.Current.Glow.PreferredDeviceId = dev.Id;
                        _ = _settingsManager.SaveAsync(_settingsManager.Current);

                        if (_audioCaptureService != null)
                        {
                            if (_audioCaptureService.IsCapturing)
                            {
                                await _audioCaptureService.StopAsync();
                            }
                            await _audioCaptureService.StartAsync(dev.Id);
                        }

                        // Allow CoreAudio endpoint change to settle before polling new default
                        await Task.Delay(100);

                        _volumeService?.RefreshAndReconnect();
                        SyncVolumeUi();
                        UpdateOutputDeviceUi();

                        _toastManager?.ShowToast(
                            switchedSystemOutput
                                ? $"Switched audio output to {dev.Name}"
                                : $"Bound audio analysis to {dev.Name}",
                            title: "Audio Output",
                            severity: InfoBarSeverity.Success);
                    }
                    catch (Exception ex)
                    {
                        _toastManager?.ShowToast(
                            $"Could not switch audio device: {ex.Message}",
                            title: "Audio Output",
                            severity: InfoBarSeverity.Warning);
                    }
                };

                flyout.Items.Add(item);
            }
        }

        flyout.Items.Add(new MenuFlyoutSeparator());
        var note = new MenuFlyoutItem
        {
            Text = "Note: Lyrow analyzes audio from selected endpoint. Change default sound in Windows Settings.",
            IsEnabled = false
        };
        flyout.Items.Add(note);
    }

    public (int LogicalWidth, int LogicalHeight, int PhysicalWidth, int PhysicalHeight) GetTargetCompactWindowSize(bool? isLyricsExpanded = null)
    {
        bool expanded = isLyricsExpanded ?? _isCompactLyricsExpanded;
        double scale = 1.0;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi > 0) scale = dpi / 96.0;
        }
        catch { }

        return LyricsViewportCalculator.CalculateCompactWindowSize(expanded, scale);
    }

    public void ToggleCompactLyrics()
    {
        _isCompactLyricsExpanded = !_isCompactLyricsExpanded;
        if (_isCompactLyricsExpanded)
        {
            GridCompactLyrics.Visibility = Visibility.Visible;
            IconExpandFromCompact.Glyph = "\uE73E";
            ResizeCompactWindow(true);
            RebuildCompactLyricsUi(_lyricsModule.CurrentLyrics);
        }
        else
        {
            GridCompactLyrics.Visibility = Visibility.Collapsed;
            IconExpandFromCompact.Glyph = "\uEC4F";
            ResizeCompactWindow(false);
        }
    }

    private void ResizeCompactWindow(bool isLyricsExpanded)
    {
        var (logicalW, logicalH, physW, physH) = GetTargetCompactWindowSize(isLyricsExpanded);
        _isResizingProgrammatically = true;
        try
        {
            if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
            {
                double scale = logicalW > 0 ? (double)physW / logicalW : 1.0;
                var (_, _, minPhysW, minPhysH) = LyricsViewportCalculator.CalculateCompactMinimumWindowSize(scale);
                overlapped.PreferredMinimumWidth = minPhysW;
                overlapped.PreferredMinimumHeight = minPhysH;
            }

            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var workArea = displayArea.WorkArea;
                int curX = AppWindow.Position.X;
                int curY = AppWindow.Position.Y;
                if (curY + physH > workArea.Y + workArea.Height)
                {
                    curY = Math.Max(workArea.Y, workArea.Y + workArea.Height - physH - 12);
                }
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(curX, curY, physW, physH));
                return;
            }
        }
        catch
        {
            // Fallback to ResizeForCurrentDpi
        }
        finally
        {
            _isResizingProgrammatically = false;
        }

        ResizeForCurrentDpi(logicalW, logicalH);
    }

    private void RebuildCompactLyricsUi(SyncedLyrics? lyrics)
    {
        _lastCompactLyrics = lyrics;
        CompactLyricsPanel.Children.Clear();
        _compactLineBlocks.Clear();
        _currentCompactActiveIndex = -99;

        // Strictly 3 lines: [0] Previous Line, [1] Active Line, [2] Next Line
        for (int slot = 0; slot < 3; slot++)
        {
            bool isMiddleActive = slot == 1;
            var block = new TextBlock
            {
                Text = string.Empty,
                FontSize = isMiddleActive ? 13.0 : 11.5,
                FontWeight = isMiddleActive ? FontWeights.Bold : FontWeights.Normal,
                Opacity = isMiddleActive ? 1.0 : 0.48,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            };

            int capturedSlot = slot;
            block.PointerPressed += async (_, _) =>
            {
                var currentLyrics = _lastCompactLyrics ?? _lyricsModule.CurrentLyrics;
                if (currentLyrics == null || currentLyrics.Lines.Count == 0) return;
                int? slotLineIdx = _compactSlotLineIndices[capturedSlot];
                if (slotLineIdx.HasValue && slotLineIdx.Value >= 0 && slotLineIdx.Value < currentLyrics.Lines.Count)
                {
                    await SeekToLyricLineAsync(currentLyrics.Lines[slotLineIdx.Value].Timestamp);
                }
            };

            _compactLineBlocks.Add(block);
            CompactLyricsPanel.Children.Add(block);
        }

        UpdateCompactActiveLine(_lyricsModule.ActiveLineIndex);
    }

    private void UpdateCompactActiveLine(int activeIdx)
    {
        if (_compactLineBlocks.Count < 3)
        {
            RebuildCompactLyricsUi(_lyricsModule.CurrentLyrics);
            if (_compactLineBlocks.Count < 3) return;
        }

        if (activeIdx == _currentCompactActiveIndex && _lastCompactLyrics != null) return;
        _currentCompactActiveIndex = activeIdx;

        var lyrics = _lastCompactLyrics ?? _lyricsModule.CurrentLyrics;
        var (prevText, activeText, nextText, prevIdx, actIdx, nextIdx) = LyricsViewportCalculator.GetCompactThreeLineWindow(
            lyrics?.Lines,
            activeIdx,
            lyrics?.PlainLyrics);

        _compactSlotLineIndices[0] = prevIdx;
        _compactSlotLineIndices[1] = actIdx;
        _compactSlotLineIndices[2] = nextIdx;

        _compactLineBlocks[0].Text = prevText;
        _compactLineBlocks[0].FontWeight = FontWeights.Normal;
        _compactLineBlocks[0].Opacity = 0.48;
        _compactLineBlocks[0].FontSize = 11.5;

        _compactLineBlocks[1].Text = activeText;
        _compactLineBlocks[1].FontWeight = FontWeights.Bold;
        _compactLineBlocks[1].Opacity = 1.0;
        _compactLineBlocks[1].FontSize = 13.0;

        _compactLineBlocks[2].Text = nextText;
        _compactLineBlocks[2].FontWeight = FontWeights.Normal;
        _compactLineBlocks[2].Opacity = 0.48;
        _compactLineBlocks[2].FontSize = 11.5;
    }
}
