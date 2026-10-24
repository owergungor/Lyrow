using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
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
/// Apple Music-inspired 60 FPS fluid lyrics experience for Windows 11.
/// Keeps the active lyric line vertically centered at all times with smooth interpolated
/// scrolling, hierarchical opacity/scale transitions, interactive seek-on-click, and media volume control.
/// </summary>
public sealed partial class LyricsWindow : Window
{
    private readonly LyricsModule _lyricsModule;
    private readonly ISettingsManager _settingsManager;
    private readonly ArtworkBitmapManager? _artworkBitmapManager;
    private readonly IMediaSessionMonitor? _mediaMonitor;
    private readonly IAudioVolumeService? _volumeService;
    private readonly ToastNotificationManager? _toastManager;
    private readonly DispatcherTimer _uiTimer;
    private bool _isUpdatingVolumeUi = false;

    private readonly List<Border> _lineContainers = new();
    private readonly List<TextBlock> _lineBlocks = new();
    private readonly List<double> _currentOpacities = new();
    private readonly List<double> _targetOpacities = new();
    private readonly List<double> _currentScales = new();
    private readonly List<double> _targetScales = new();

    private int _currentActiveIndex = -99;
    private int _lastFontScaleIndex = -1;
    private double _lastFocusedSize = -1;
    private double _lastUnfocusedSize = -1;
    private string _lastSongKey = string.Empty;
    private SyncedLyrics? _lastLyrics;
    private double _currentScrollOffsetY = 0.0;
    private double _targetScrollOffsetY = 0.0;
    private bool _snapToCenterOnNextLayout = false;
    private bool _userIsInteracting = false;
    private DateTime _lastUserInteractionUtc = DateTime.MinValue;
    private bool _isShuttingDown = false;

    public LyricsWindow(
        LyricsModule lyricsModule,
        ISettingsManager settingsManager,
        ArtworkBitmapManager? artworkBitmapManager = null,
        IMediaSessionMonitor? mediaMonitor = null,
        IAudioVolumeService? volumeService = null,
        ToastNotificationManager? toastManager = null)
    {
        InitializeComponent();
        _lyricsModule = lyricsModule;
        _settingsManager = settingsManager;
        _artworkBitmapManager = artworkBitmapManager;
        _mediaMonitor = mediaMonitor;
        _volumeService = volumeService;
        _toastManager = toastManager;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(LyricsTitleBar);
        ResizeForCurrentDpi(500, 720);

        TxtOffsetMs.Text = $"{_settingsManager.Current.Lyrics.SyncOffsetMs:+0;-0;0} ms";
        BtnOffsetMinus.Click += (_, _) => AdjustOffset(-250);
        BtnOffsetPlus.Click += (_, _) => AdjustOffset(250);

        // Media Playback Volume slider & mute
        if (_volumeService != null)
        {
            SyncVolumeUi();
            SliderLyricsVolume.ValueChanged += (_, e) =>
            {
                if (_isUpdatingVolumeUi) return;
                _isUpdatingVolumeUi = true;
                try
                {
                    int val = Math.Clamp((int)Math.Round(e.NewValue), 0, 100);
                    _volumeService.SetPlaybackVolumePercent(val);
                    TxtLyricsVolumePercent.Text = $"{val}%";
                    IconLyricsVolumeMute.Glyph = (val == 0 || _volumeService.IsPlaybackMuted) ? "\uE74F" : "\uE767";
                }
                finally
                {
                    _isUpdatingVolumeUi = false;
                }
            };
            BtnLyricsVolumeMute.Click += (_, _) =>
            {
                _volumeService.TogglePlaybackMute();
                SyncVolumeUi();
            };
            _volumeService.VolumeChanged += (_, _) =>
            {
                DispatcherQueue?.TryEnqueue(SyncVolumeUi);
            };
        }

        BtnLyricsPrev.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TrySkipPreviousAsync();
        };
        BtnLyricsPlayPause.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TryTogglePlayPauseAsync();
        };
        BtnLyricsNext.Click += async (_, _) =>
        {
            if (_mediaMonitor != null) await _mediaMonitor.TrySkipNextAsync();
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

        Closed += (_, _) =>
        {
            if (!_isShuttingDown)
            {
                _uiTimer?.Stop();
            }
        };

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // 60 FPS smooth Apple Music animation & centering loop
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();
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

    private void UpdateViewportPadding()
    {
        double viewportH = LyricsScrollViewer.ActualHeight;
        if (viewportH <= 0) return;

        // Top and bottom padding equal to half the viewport height so even line 0 and the last line
        // can sit at the exact vertical center of the viewport!
        double halfPadding = Math.Max(60.0, (viewportH / 2.0) - 24.0);
        LyricsPanel.Padding = new Thickness(28, halfPadding, 28, halfPadding);
    }

    private async void OnUiTimerTick(object? sender, object e)
    {
        var track = !_lyricsModule.CurrentTrack.IsEmpty
            ? _lyricsModule.CurrentTrack
            : (_mediaMonitor?.CurrentTrack ?? MediaTrackInfo.Empty);

        var timeline = _lyricsModule.CurrentTimeline.Duration > TimeSpan.Zero || _lyricsModule.CurrentTimeline.Status != MediaPlaybackStatus.Stopped
            ? _lyricsModule.CurrentTimeline
            : (_mediaMonitor?.CurrentTimeline ?? MediaTimelineInfo.Zero);

        var lyrics = _lyricsModule.CurrentLyrics;

        if (track.IsEmpty)
        {
            TxtSongTitle.Text = "No active media session";
            TxtArtistName.Text = "Play music in Spotify, Apple Music, or your browser";
            ProgressTimeline.Value = 0;
            TxtTimestamp.Text = "00:00 / 00:00";
            TxtEmptyState.Text = "Play a song in Spotify, Apple Music, or your browser";
            PanelEmptyState.Visibility = Visibility.Visible;
            LyricsScrollViewer.Visibility = Visibility.Collapsed;
            IconArtworkPlaceholder.Visibility = Visibility.Visible;
            ImgTrackArtwork.Source = null;
            return;
        }

        TxtSongTitle.Text = track.Title;
        TxtArtistName.Text = string.IsNullOrWhiteSpace(track.Album)
            ? track.Artist
            : $"{track.Artist} — {track.Album}";

        var pos = timeline.GetInterpolatedPosition();
        var dur = timeline.Duration > TimeSpan.Zero ? timeline.Duration : track.Duration;
        TxtTimestamp.Text = $"{pos:mm\\:ss} / {dur:mm\\:ss}";

        if (dur.TotalSeconds > 0)
        {
            ProgressTimeline.Value = Math.Clamp((pos.TotalSeconds / dur.TotalSeconds) * 100.0, 0, 100);
        }
        else
        {
            ProgressTimeline.Value = 0;
        }

        bool hasTrack = !track.IsEmpty;
        BtnLyricsPrev.IsEnabled = hasTrack;
        BtnLyricsPlayPause.IsEnabled = hasTrack;
        BtnLyricsNext.IsEnabled = hasTrack;
        IconLyricsPlayPause.Glyph = (timeline.Status == MediaPlaybackStatus.Playing) ? "\uE769" : "\uE768";

        // Ensure synchronizer evaluates latest interpolated position
        _lyricsModule.Synchronizer.EvaluatePosition(pos);

        double focusedSize = _settingsManager.Current.FocusedLyricFontSize > 0 ? _settingsManager.Current.FocusedLyricFontSize : 28.0;
        double unfocusedSize = _settingsManager.Current.UnfocusedLyricFontSize > 0 ? _settingsManager.Current.UnfocusedLyricFontSize : 20.0;
        int fontScaleIndex = _settingsManager.Current.LyricsFontScaleIndex;
        string songKey = $"{track.Artist}_{track.Title}";

        if (songKey != _lastSongKey || !ReferenceEquals(lyrics, _lastLyrics) || fontScaleIndex != _lastFontScaleIndex || Math.Abs(focusedSize - _lastFocusedSize) > 0.5 || Math.Abs(unfocusedSize - _lastUnfocusedSize) > 0.5)
        {
            bool trackChanged = songKey != _lastSongKey;
            _lastSongKey = songKey;
            _lastLyrics = lyrics;
            _lastFontScaleIndex = fontScaleIndex;
            _lastFocusedSize = focusedSize;
            _lastUnfocusedSize = unfocusedSize;
            RebuildLyricsUi(lyrics);

            if (trackChanged)
            {
                await UpdateArtworkAsync(track);
            }
        }

        // Active line synchronization
        int activeIdx = _lyricsModule.ActiveLineIndex;
        if (activeIdx != _currentActiveIndex)
        {
            UpdateActiveLineTargets(activeIdx, immediate: _currentActiveIndex == -99);
        }

        // 60 FPS smooth visual interpolation (opacity, scale, and vertical centering)
        StepSmoothAnimations();
    }

    private async Task UpdateArtworkAsync(MediaTrackInfo track)
    {
        if (_artworkBitmapManager == null) return;
        try
        {
            var bitmap = await _artworkBitmapManager.GetArtworkBitmapAsync(track);
            if (bitmap != null)
            {
                ImgTrackArtwork.Source = bitmap;
                IconArtworkPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                ImgTrackArtwork.Source = null;
                IconArtworkPlaceholder.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            ImgTrackArtwork.Source = null;
            IconArtworkPlaceholder.Visibility = Visibility.Visible;
        }
    }

    private void RebuildLyricsUi(SyncedLyrics lyrics)
    {
        LyricsPanel.Children.Clear();
        _lineContainers.Clear();
        _lineBlocks.Clear();
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
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                LyricsPanel.Children.Add(plainBlock);
            }
            else
            {
                TxtEmptyState.Text = "Searching for synchronized lyrics...";
                PanelEmptyState.Visibility = Visibility.Visible;
                LyricsScrollViewer.Visibility = Visibility.Collapsed;
            }
            return;
        }

        PanelEmptyState.Visibility = Visibility.Collapsed;
        LyricsScrollViewer.Visibility = Visibility.Visible;

        double focusedSize = _settingsManager.Current.FocusedLyricFontSize;
        double unfocusedSize = _settingsManager.Current.UnfocusedLyricFontSize;
        _lastFocusedSize = focusedSize;
        _lastUnfocusedSize = unfocusedSize;

        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var target = LyricsViewportCalculator.GetLineVisualTarget(i, -1, focusedSize, unfocusedSize);

            var block = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text,
                FontSize = target.FontSize,
                FontWeight = FontWeights.Medium,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                LineHeight = target.FontSize * 1.32
            };

            var container = new Border
            {
                Child = block,
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Opacity = target.TargetOpacity,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform { ScaleX = target.TargetScale, ScaleY = target.TargetScale },
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
            };

            // Interactive seek-to-timestamp on lyric line click
            var lineTimestamp = line.Timestamp;
            container.PointerPressed += async (_, _) =>
            {
                await SeekToLyricLineAsync(lineTimestamp);
            };

            _lineBlocks.Add(block);
            _lineContainers.Add(container);
            _currentOpacities.Add(target.TargetOpacity);
            _targetOpacities.Add(target.TargetOpacity);
            _currentScales.Add(target.TargetScale);
            _targetScales.Add(target.TargetScale);

            LyricsPanel.Children.Add(container);
        }

        _snapToCenterOnNextLayout = true;
    }

    private void UpdateActiveLineTargets(int activeIdx, bool immediate)
    {
        if (_lineContainers.Count == 0) return;

        _currentActiveIndex = activeIdx;
        double focusedSize = _settingsManager.Current.FocusedLyricFontSize;
        double unfocusedSize = _settingsManager.Current.UnfocusedLyricFontSize;

        for (int i = 0; i < _lineContainers.Count; i++)
        {
            var target = LyricsViewportCalculator.GetLineVisualTarget(i, activeIdx, focusedSize, unfocusedSize);
            _targetOpacities[i] = target.TargetOpacity;
            _targetScales[i] = target.TargetScale;

            var block = _lineBlocks[i];
            block.FontSize = target.FontSize;
            block.LineHeight = target.FontSize * 1.32;
            block.FontWeight = target.IsActive
                ? FontWeights.Bold
                : (target.Distance == 1 ? FontWeights.SemiBold : FontWeights.Normal);

            if (immediate)
            {
                _currentOpacities[i] = target.TargetOpacity;
                _currentScales[i] = target.TargetScale;
                _lineContainers[i].Opacity = target.TargetOpacity;
                if (_lineContainers[i].RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = target.TargetScale;
                    st.ScaleY = target.TargetScale;
                }
            }
        }

        LyricsPanel.UpdateLayout();
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
            var transform = activeContainer.TransformToVisual(LyricsScrollViewer);
            var pointInViewer = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            _targetScrollOffsetY = LyricsViewportCalculator.CalculateTargetCenterOffsetFromViewportPoint(
                currentVerticalOffset: LyricsScrollViewer.VerticalOffset,
                viewportHeight: viewportH,
                lineTopInViewport: pointInViewer.Y,
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
            double tgtSc = _targetScales[i];
            if (Math.Abs(tgtSc - curSc) > 0.001)
            {
                double nextSc = LyricsViewportCalculator.InterpolateValue(curSc, tgtSc, 0.20);
                _currentScales[i] = nextSc;
                if (_lineContainers[i].RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = nextSc;
                    st.ScaleY = nextSc;
                }
            }
        }

        // 2. Continuously keep active line centered in viewport with smooth 60 FPS easing (always enabled)
        if (_userIsInteracting)
        {
            if ((DateTime.UtcNow - _lastUserInteractionUtc).TotalSeconds < 2.8)
            {
                return;
            }
            _userIsInteracting = false;
        }

        RecalculateCenterScrollTarget(immediate: false);

        double actualOffset = LyricsScrollViewer.VerticalOffset;
        if (Math.Abs(_currentScrollOffsetY - actualOffset) > 40.0)
        {
            _currentScrollOffsetY = actualOffset;
        }

        if (Math.Abs(_targetScrollOffsetY - _currentScrollOffsetY) > 0.25)
        {
            _currentScrollOffsetY = LyricsViewportCalculator.InterpolateScrollOffset(
                _currentScrollOffsetY,
                _targetScrollOffsetY,
                smoothingFactor: 0.18,
                snapEpsilon: 0.30);

            LyricsScrollViewer.ChangeView(null, _currentScrollOffsetY, null, disableAnimation: true);
        }
    }

    private void SyncVolumeUi()
    {
        if (_volumeService == null || _isUpdatingVolumeUi) return;
        _isUpdatingVolumeUi = true;
        try
        {
            int vol = _volumeService.GetPlaybackVolumePercent();
            bool muted = _volumeService.IsPlaybackMuted;
            SliderLyricsVolume.Value = vol;
            TxtLyricsVolumePercent.Text = muted ? "Muted" : $"{vol}%";
            IconLyricsVolumeMute.Glyph = (muted || vol == 0) ? "\uE74F" : "\uE767";
        }
        finally
        {
            _isUpdatingVolumeUi = false;
        }
    }

    private async Task SeekToLyricLineAsync(TimeSpan timestamp)
    {
        if (_mediaMonitor == null) return;
        bool ok = await _mediaMonitor.TrySeekAsync(timestamp);
        if (!ok)
        {
            _toastManager?.ShowToast(
                LocalizationService.Instance["Lyrics_SeekFailed"],
                title: "Seek",
                severity: InfoBarSeverity.Warning);
        }
        else
        {
            _lyricsModule.Synchronizer.EvaluatePosition(timestamp);
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
}
