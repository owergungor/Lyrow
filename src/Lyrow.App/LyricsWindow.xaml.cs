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
    private readonly IAudioVolumeService? _volumeService;
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

    private long _windowTrackSequence = 0;
    private int _lastArtworkSignature = 0;
    private int _currentActiveIndex = -99;
    private int _lastFontScaleIndex = -1;
    private double _lastFocusedSize = -1;
    private double _lastUnfocusedSize = -1;
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
        ToastNotificationManager? toastManager = null)
    {
        InitializeComponent();
        _lyricsModule = lyricsModule;
        _settingsManager = settingsManager;
        ApplyTheme(_settingsManager.Current.Theme);
        _artworkBitmapManager = artworkBitmapManager;
        _mediaMonitor = mediaMonitor;
        _volumeService = volumeService;
        _toastManager = toastManager;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(LyricsTitleBar);
        ResizeForCurrentDpi(960, 640);

        TxtOffsetMs.Text = $"{_settingsManager.Current.Lyrics.SyncOffsetMs:+0;-0;0} ms";
        BtnOffsetMinus.Click += (_, _) => AdjustOffset(-250);
        BtnOffsetPlus.Click += (_, _) => AdjustOffset(250);

        // Mode Switching: Dar Mod <-> Horizontal
        BtnDarMod.Click += (_, _) => SetWindowMode(LyricsWindowMode.Compact);
        BtnNormalModeCompact.Click += (_, _) => SetWindowMode(LyricsWindowMode.Horizontal);
        BtnExpandFromCompact.Click += (_, _) => SetWindowMode(LyricsWindowMode.Horizontal);

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
                    IconVolumeMute.Glyph = (val == 0 || _volumeService.IsPlaybackMuted) ? "\uE74F" : "\uE767";
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

            _volumeService.VolumeChanged += (_, _) =>
            {
                DispatcherQueue?.TryEnqueue(SyncVolumeUi);
            };
        }

        // Layout and Keyboard Listeners
        RootLayout.SizeChanged += (_, _) => UpdateLayoutForCurrentSize();
        RootLayout.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.F11)
            {
                ToggleFullscreen();
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

        Closed += (_, _) =>
        {
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
            LyricsTitleBar.Title = "Lyrow — Mini Player";
            if (resizeWindow)
            {
                ResizeForCurrentDpi(500, 232);
            }
        }
        else
        {
            CompactPlayerView.Visibility = Visibility.Collapsed;
            HorizontalView.Visibility = Visibility.Visible;
            LyricsTitleBar.Title = "Lyrow — Synchronized Lyrics";
            if (resizeWindow)
            {
                ResizeForCurrentDpi(960, 640);
            }
            ApplyResponsiveScaling(RootLayout.ActualWidth > 0 ? RootLayout.ActualWidth : 960, RootLayout.ActualHeight > 0 ? RootLayout.ActualHeight : 640);
            UpdateViewportPadding();
            _snapToCenterOnNextLayout = true;
        }
    }

    /// <summary>
    /// Toggles lyrics visibility. When inactive, the player centers horizontally in the entire window.
    /// When active, the 50/50 layout is smoothly restored.
    /// </summary>
    public void ToggleLyricsPanel()
    {
        _isLyricsEnabled = !_isLyricsEnabled;
        if (_isLyricsEnabled)
        {
            ColLyrics.Width = new GridLength(1, GridUnitType.Star);
            LyricsColumnContainer.Visibility = Visibility.Visible;
            IconToggleLyrics.Glyph = "\uEC4F";
            ToolTipService.SetToolTip(BtnToggleLyrics, "Hide Lyrics (Center Player)");
            UpdateViewportPadding();
            _snapToCenterOnNextLayout = true;
        }
        else
        {
            ColLyrics.Width = new GridLength(0);
            LyricsColumnContainer.Visibility = Visibility.Collapsed;
            IconToggleLyrics.Glyph = "\uE8D6";
            ToolTipService.SetToolTip(BtnToggleLyrics, "Show Lyrics (50/50 Split)");
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
            else if (artSig != _lastArtworkSignature && !track.IsEmpty)
            {
                _lastArtworkSignature = artSig;
                _artworkBitmapManager?.Invalidate(track.Artist, track.Title);
                _ = UpdateArtworkAsync(track, Interlocked.Read(ref _windowTrackSequence));
            }
        });
    }

    private void WireInteractiveProgressScrubber(Grid hitBox, ProgressBar progressBar)
    {
        bool isPointerCaptured = false;

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
            TxtRemaining.Text = dur > targetTime ? $"-{(dur - targetTime):m\\:ss}" : "-0:00";
            TxtRemainingCompact.Text = dur > targetTime ? $"-{(dur - targetTime):m\\:ss}" : "-0:00";
            _lyricsModule.Synchronizer.EvaluatePosition(targetTime);
        }

        hitBox.PointerPressed += (s, e) =>
        {
            if (_mediaMonitor != null && !_mediaMonitor.CanSeek)
            {
                _toastManager?.ShowToast(
                    "Seek işlemi aktif medya oynatıcısı (Apple Music) tarafından desteklenmiyor.",
                    title: "Seek Desteklenmiyor",
                    severity: InfoBarSeverity.Informational);
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
                await SeekToLyricLineAsync(targetTime);
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
        long currentSeq = Interlocked.Increment(ref _windowTrackSequence);
        _lastSongKey = $"{newTrack.Artist}_{newTrack.Title}";
        _lastArtworkSignature = ArtworkBitmapManager.ComputeArtworkSignature(newTrack);
        _lastLyrics = SyncedLyrics.Empty;
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
        TxtEmptyState.Text = newTrack.IsEmpty
            ? "Waiting for playback..."
            : "Searching for synchronized lyrics...";
        TxtEmptyStateSub.Text = newTrack.IsEmpty
            ? "Play a song in Spotify, Apple Music, or your browser"
            : $"Looking up lyrics for \"{newTrack.Title}\"...";
        PanelEmptyState.Visibility = Visibility.Visible;

        // 3. Reset progress bars and time labels strictly to 0:00 / clean state
        ProgressTimeline.Value = 0;
        ProgressTimelineCompact.Value = 0;
        TxtElapsed.Text = "0:00";
        TxtElapsedCompact.Text = "0:00";
        TxtRemaining.Text = newTrack.Duration > TimeSpan.Zero ? $"-{newTrack.Duration:mm\\:ss}" : "-0:00";
        TxtRemainingCompact.Text = newTrack.Duration > TimeSpan.Zero ? $"-{newTrack.Duration:mm\\:ss}" : "-0:00";

        // 4. Invalidate artwork and reset ambient target
        if (!newTrack.IsEmpty)
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
            TxtArtistAlbum.Text = "Play music in Spotify, Apple Music, or your browser";
            TxtSongTitleCompact.Text = "No active media session";
            TxtArtistAlbumCompact.Text = "Play music in Spotify, Apple Music, or your browser";
            ProgressTimeline.Value = 0;
            ProgressTimelineCompact.Value = 0;
            TxtElapsed.Text = "0:00";
            TxtRemaining.Text = "-0:00";
            TxtElapsedCompact.Text = "0:00";
            TxtRemainingCompact.Text = "-0:00";
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

        string songKey = $"{track.Artist}_{track.Title}";
        if (songKey != _lastSongKey)
        {
            ResetTrackState(track);
            return;
        }

        int currentArtSig = ArtworkBitmapManager.ComputeArtworkSignature(track);
        if (currentArtSig != _lastArtworkSignature)
        {
            _lastArtworkSignature = currentArtSig;
            _artworkBitmapManager?.Invalidate(track.Artist, track.Title);
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

        // Position and Durations
        var pos = timeline.GetInterpolatedPosition();
        var dur = track.Duration > TimeSpan.Zero ? track.Duration : timeline.Duration;

        if (dur > TimeSpan.Zero && pos > dur) pos = dur;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;

        if (!_isDraggingProgressBar)
        {
            TxtElapsed.Text = $"{pos:m\\:ss}";
            TxtElapsedCompact.Text = $"{pos:m\\:ss}";
            TxtRemaining.Text = dur > pos ? $"-{(dur - pos):m\\:ss}" : "-0:00";
            TxtRemainingCompact.Text = dur > pos ? $"-{(dur - pos):m\\:ss}" : "-0:00";

            if (dur.TotalSeconds > 0)
            {
                double progressPct = Math.Clamp((pos.TotalSeconds / dur.TotalSeconds) * 100.0, 0, 100);
                ProgressTimeline.Value = progressPct;
                ProgressTimelineCompact.Value = progressPct;
            }
            else
            {
                ProgressTimeline.Value = 0;
                ProgressTimelineCompact.Value = 0;
            }
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

        var playGlyph = (timeline.Status == MediaPlaybackStatus.Playing) ? "\uE769" : "\uE768";
        IconPlayPause.Glyph = playGlyph;
        IconPlayPauseCompact.Glyph = playGlyph;

        // Synchronizer position evaluation
        _lyricsModule.Synchronizer.EvaluatePosition(pos);

        // Font scaling and dynamic lyrics rebuilding
        double focusedSize = _settingsManager.Current.FocusedLyricFontSize > 0 ? _settingsManager.Current.FocusedLyricFontSize : 28.0;
        double unfocusedSize = _settingsManager.Current.UnfocusedLyricFontSize > 0 ? _settingsManager.Current.UnfocusedLyricFontSize : 20.0;
        int fontScaleIndex = _settingsManager.Current.LyricsFontScaleIndex;

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

        // 60 FPS smooth visual interpolation
        StepSmoothAnimations();
    }

    private void UpdateOutputDeviceUi()
    {
        string deviceName = _volumeService?.OutputDeviceFriendlyName ?? "Speakers";
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
        try
        {
            var bitmap = await _artworkBitmapManager.GetArtworkBitmapAsync(track);
            if (sequenceId > 0 && Interlocked.Read(ref _windowTrackSequence) != sequenceId) return;

            if (bitmap != null)
            {
                ImgTrackArtwork.Source = bitmap;
                IconArtworkPlaceholder.Visibility = Visibility.Collapsed;
                ImgTrackArtworkCompact.Source = bitmap;
                IconArtworkPlaceholderCompact.Visibility = Visibility.Collapsed;
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
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
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

        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var target = LyricsViewportCalculator.GetLineVisualTarget(i, -1, focusedSize, unfocusedSize);
            string lineText = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;

            // Split into sequential sub-lines (either explicit \n or long multi-clause lyric lines > 46 chars)
            var subLines = LyricsFillProgressCalculator.SplitIntoSequentialSubLines(lineText, maxCharsPerSubLine: 46);
            var subBaseList = new List<TextBlock>(subLines.Count);
            var subOverlayList = new List<TextBlock>(subLines.Count);
            var subClipList = new List<RectangleGeometry>(subLines.Count);

            var lineStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            foreach (var subText in subLines)
            {
                // Base TextBlock (dim/secondary inactive color)
                var baseBlock = new TextBlock
                {
                    Text = subText,
                    FontSize = target.FontSize,
                    FontWeight = FontWeights.Medium,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    LineHeight = target.FontSize * 1.32,
                    Foreground = InactiveLyricBrush
                };

                // Overlay TextBlock: Active highlight clipped by RectangleGeometry sequentially per sub-line
                var overlayClip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 0, 0) };
                var overlayBlock = new TextBlock
                {
                    Text = subText,
                    FontSize = target.FontSize,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    LineHeight = target.FontSize * 1.32,
                    Foreground = ActiveLyricBrush,
                    Clip = overlayClip,
                    Visibility = Visibility.Collapsed
                };

                var subGrid = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Center
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
        var tline = _lyricsModule.CurrentTimeline;
        _lyricsModule.Synchronizer.EvaluatePosition(tline.GetInterpolatedPosition());
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
            var target = LyricsViewportCalculator.GetLineVisualTarget(i, activeIdx, focusedSize, unfocusedSize);
            _targetOpacities[i] = target.TargetOpacity;
            _targetScales[i] = target.TargetScale;

            var baseList = _lineSubBaseBlocks[i];
            for (int s = 0; s < baseList.Count; s++)
            {
                var block = baseList[s];
                block.FontSize = target.FontSize;
                block.LineHeight = target.FontSize * 1.32;
                block.FontWeight = target.IsActive
                    ? FontWeights.Bold
                    : (target.Distance == 1 ? FontWeights.SemiBold : FontWeights.Normal);
            }

            if (i < _lineSubOverlayBlocks.Count)
            {
                var overlayList = _lineSubOverlayBlocks[i];
                var clipList = _lineSubClips[i];
                var subStrings = _lineSubStrings[i];

                if (target.IsActive && i < _currentRenderedLines.Count)
                {
                    var renderLine = _currentRenderedLines[i];
                    var tline = _lyricsModule.CurrentTimeline;
                    var cPos = tline.GetInterpolatedPosition();
                    TimeSpan? nTs = (i + 1 < _currentRenderedLines.Count) ? _currentRenderedLines[i + 1].Timestamp : null;
                    var subProgresses = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(renderLine, subStrings, cPos, nTs);

                    for (int s = 0; s < overlayList.Count; s++)
                    {
                        var overlay = overlayList[s];
                        overlay.FontSize = target.FontSize;
                        overlay.LineHeight = target.FontSize * 1.32;
                        overlay.FontWeight = FontWeights.Bold;
                        overlay.Visibility = Visibility.Visible;

                        double sp = (s < subProgresses.Count) ? subProgresses[s] : 0.0;
                        double clipW = sp * (overlay.ActualWidth > 0 ? overlay.ActualWidth : 2000.0);
                        double clipH = overlay.ActualHeight > 0 ? overlay.ActualHeight : 200.0;
                        clipList[s].Rect = new Windows.Foundation.Rect(0, 0, clipW, clipH);
                    }
                }
                else
                {
                    // Single canonical visual text: overlay collapsed for all inactive surrounding lines
                    for (int s = 0; s < overlayList.Count; s++)
                    {
                        var overlay = overlayList[s];
                        overlay.FontSize = target.FontSize;
                        overlay.LineHeight = target.FontSize * 1.32;
                        overlay.FontWeight = FontWeights.Bold;
                        overlay.Visibility = Visibility.Collapsed;
                        clipList[s].Rect = new Windows.Foundation.Rect(0, 0, 0, 0);
                    }
                }
            }

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

        // 2. Real-time Apple Music-style karaoke fill clipping on the active line
        var lyrics = _lyricsModule.CurrentLyrics;
        var timeline = _lyricsModule.CurrentTimeline.Duration > TimeSpan.Zero || _lyricsModule.CurrentTimeline.Status != MediaPlaybackStatus.Stopped
            ? _lyricsModule.CurrentTimeline
            : (_mediaMonitor?.CurrentTimeline ?? MediaTimelineInfo.Zero);

        var currentPos = timeline.GetInterpolatedPosition();
        var trackDur = _lyricsModule.CurrentTrack.Duration > TimeSpan.Zero
            ? _lyricsModule.CurrentTrack.Duration
            : (timeline.Duration > TimeSpan.Zero ? timeline.Duration : (TimeSpan?)null);

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
                        if (overlayList[s].Visibility != Visibility.Collapsed)
                        {
                            overlayList[s].Visibility = Visibility.Collapsed;
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

                if (overlayList.Count == 1 && line.Words != null && line.Words.Count > 0)
                {
                    var overlay = overlayList[0];
                    if (overlay.Visibility != Visibility.Visible) overlay.Visibility = Visibility.Visible;
                    double actualW = overlay.ActualWidth;
                    double actualH = overlay.ActualHeight > 0 ? overlay.ActualHeight : 200.0;
                    if (actualW > 0)
                    {
                        double progress = LyricsFillProgressCalculator.CalculateWordTimingProgress(
                            currentPos,
                            line.StartTime,
                            nextTimestamp,
                            line.Words);
                        clipList[0].Rect = new Windows.Foundation.Rect(0, 0, progress * actualW, actualH);
                    }
                    continue;
                }

                var subProgresses = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
                    line,
                    subStrings,
                    currentPos,
                    nextTimestamp,
                    trackDur);

                for (int s = 0; s < overlayList.Count; s++)
                {
                    var overlay = overlayList[s];
                    if (overlay.Visibility != Visibility.Visible)
                    {
                        overlay.Visibility = Visibility.Visible;
                    }

                    double actualW = overlay.ActualWidth;
                    double actualH = overlay.ActualHeight > 0 ? overlay.ActualHeight : 200.0;
                    if (actualW <= 0) continue;

                    double sp = (s < subProgresses.Count) ? subProgresses[s] : 0.0;
                    clipList[s].Rect = new Windows.Foundation.Rect(0, 0, sp * actualW, actualH);
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

        // 4. Preserved ambient dynamic artwork background 60 FPS cross-fade
        _currentAmbientPalette = AmbientBackgroundPalette.Lerp(_currentAmbientPalette, _targetAmbientPalette, 0.065f);
        StopTopLeft.Color = Windows.UI.Color.FromArgb(255, _currentAmbientPalette.Dominant.R, _currentAmbientPalette.Dominant.G, _currentAmbientPalette.Dominant.B);
        StopCenter.Color = Windows.UI.Color.FromArgb(255, _currentAmbientPalette.Secondary.R, _currentAmbientPalette.Secondary.G, _currentAmbientPalette.Secondary.B);
        StopBottomRight.Color = Windows.UI.Color.FromArgb(255, _currentAmbientPalette.Tertiary.R, _currentAmbientPalette.Tertiary.G, _currentAmbientPalette.Tertiary.B);
    }

    private void UpdateLayoutForCurrentSize()
    {
        double w = RootLayout.ActualWidth;
        double h = RootLayout.ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Hysteresis transition: When in Compact mode and user widens window >= CompactToNormalWidthThreshold,
        // automatically switch back to Horizontal normal mode while preserving user's resized window dimensions.
        if (_currentMode == LyricsWindowMode.Compact)
        {
            if (w >= LyricsViewportCalculator.CompactToNormalWidthThreshold && h >= 320.0)
            {
                SetWindowMode(LyricsWindowMode.Horizontal, resizeWindow: false);
            }
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

        if (Math.Abs(metrics.FocusedLyricFontSize - _lastResponsiveFocusedSize) >= 0.5 ||
            Math.Abs(metrics.UnfocusedLyricFontSize - _lastResponsiveUnfocusedSize) >= 0.5)
        {
            _lastResponsiveFocusedSize = metrics.FocusedLyricFontSize;
            _lastResponsiveUnfocusedSize = metrics.UnfocusedLyricFontSize;
            if (_lineContainers.Count > 0)
            {
                UpdateActiveLineTargets(_currentActiveIndex, immediate: false);
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
            TxtVolumePercent.Text = muted ? "Muted" : $"{vol}%";
            string muteGlyph = (muted || vol == 0) ? "\uE74F" : "\uE767";
            IconVolumeMute.Glyph = muteGlyph;
        }
        finally
        {
            _isUpdatingVolumeUi = false;
        }
    }

    private async Task SeekToLyricLineAsync(TimeSpan timestamp)
    {
        if (_mediaMonitor == null) return;
        if (!_mediaMonitor.CanSeek)
        {
            _toastManager?.ShowToast(
                "Seek işlemi aktif medya oynatıcısı tarafından desteklenmiyor.",
                title: "Seek Desteklenmiyor",
                severity: InfoBarSeverity.Informational);
            return;
        }
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
        if (AmbientBackgroundLayer != null)
        {
            AmbientBackgroundLayer.Opacity = mode == ThemeMode.Light ? 0.15 : 1.0;
        }
    }

    private void UpdateScrubberToolTips()
    {
        bool canSeek = _mediaMonitor?.CanSeek ?? false;
        string toolTip = canSeek
            ? "Click or drag to seek"
            : "Seek işlemi aktif medya oynatıcısı (Apple Music) tarafından desteklenmiyor.";
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
            TxtEmptyState.Text = "Sözler bulunamadı";
            TxtEmptyStateSub.Text = !string.IsNullOrWhiteSpace(message) ? message : "Bu parça için senkronize söz bulunamadı.";
            PanelEmptyState.Visibility = Visibility.Visible;
            LyricsScrollViewer.Visibility = Visibility.Collapsed;
        }
    }
}
