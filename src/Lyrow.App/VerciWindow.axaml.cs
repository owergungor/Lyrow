using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Lyrow.App;

public partial class VerciWindow : Window
{
    private readonly VerciModule _verciModule;
    private readonly ISettingsManager _settingsManager;
    private readonly List<TextBlock> _lineTextBlocks = new();
    private readonly DispatcherTimer _uiTimer;

    private int _currentRenderedLineIndex = -99;
    private string _lastRenderedSongKey = string.Empty;
    private string _lastArtworkKey = string.Empty;

    private readonly SolidColorBrush _activeLyricBrush = new(Color.Parse("#89B4FA"));
    private readonly SolidColorBrush _inactiveLyricBrush = new(Color.Parse("#A6ADC8"));
    private readonly SolidColorBrush _playingBadgeBrush = new(Color.Parse("#1A3A2A"));
    private readonly SolidColorBrush _pausedBadgeBrush = new(Color.Parse("#252538"));
    private readonly SolidColorBrush _waitingBadgeBrush = new(Color.Parse("#1E1E2E"));
    private readonly ArtworkBitmapManager? _artworkBitmapManager;

    // Parameterless constructor for XAML loader and previewer
    public VerciWindow() : this(
        App.Services?.GetService<VerciModule>() ?? new VerciModule(),
        App.Services?.GetService<ISettingsManager>() ?? new Lyrow.Core.Services.SettingsManager(),
        App.Services?.GetService<ArtworkBitmapManager>())
    {
    }

    public VerciWindow(VerciModule verciModule, ISettingsManager settingsManager, ArtworkBitmapManager? artworkBitmapManager = null)
    {
        InitializeComponent();
        _verciModule = verciModule;
        _settingsManager = settingsManager;
        _artworkBitmapManager = artworkBitmapManager ?? App.Services?.GetService<ArtworkBitmapManager>();

        // Window drag handle
        PointerPressed += OnWindowPointerPressed;

        CloseButton.Click += (_, _) => Hide();
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;

        // Manual sync offset adjustment buttons
        OffsetLabelText.Text = $"{_settingsManager.Current.Verci.SyncOffsetMs:+0;-0;0} ms";
        BtnOffsetMinus.Click += (_, _) => AdjustOffset(-500);
        BtnOffsetPlus.Click += (_, _) => AdjustOffset(500);

        ChkAutoScroll.IsChecked = _settingsManager.Current.Verci.AutoScroll;
        ChkAutoScroll.IsCheckedChanged += (_, _) =>
        {
            _settingsManager.Current.Verci.AutoScroll = ChkAutoScroll.IsChecked ?? true;
        };

        // Smooth viewport centering on window resize
        LyricsScrollViewer.SizeChanged += (_, _) => UpdateViewportPaddingAndCenter();

        // Smooth UI update timer (20 FPS is smooth for lyric scrolling and low CPU)
        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();

        PropertyChanged += OnWindowPropertyChanged;
    }

    private void UpdateViewportPaddingAndCenter()
    {
        if (LyricsScrollViewer.Bounds.Height <= 0) return;
        double halfH = Math.Max(40.0, (LyricsScrollViewer.Bounds.Height / 2.0) - 16.0);
        LyricsPanel.Margin = new Thickness(0, halfH, 0, halfH);

        if (_currentRenderedLineIndex >= 0 && _currentRenderedLineIndex < _lineTextBlocks.Count)
        {
            ScrollToLineCentered(_lineTextBlocks[_currentRenderedLineIndex]);
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty)
        {
            bool isActive = IsVisible && WindowState != WindowState.Minimized;
            if (isActive)
            {
                if (!_uiTimer.IsEnabled)
                {
                    _uiTimer.Start();
                    OnUiTimerTick(null, EventArgs.Empty);
                }
            }
            else
            {
                if (_uiTimer.IsEnabled) _uiTimer.Stop();
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

    private SyncedLyrics? _lastRenderedLyrics;

    private void AdjustOffset(int deltaMs)
    {
        int newOffset = (_settingsManager.Current.Verci.SyncOffsetMs) + deltaMs;
        _settingsManager.Current.Verci.SyncOffsetMs = newOffset;
        _verciModule.SetSyncOffset(newOffset);
        OffsetLabelText.Text = $"{newOffset:+0;-0;0} ms";
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        var track = _verciModule.CurrentTrack;
        var timeline = _verciModule.CurrentTimeline;
        var lyrics = _verciModule.CurrentLyrics;

        // 1. Update track title & artist & artwork
        if (!track.IsEmpty)
        {
            TrackTitleText.Text = track.Title;
            TrackArtistText.Text = string.IsNullOrEmpty(track.Album) ? track.Artist : $"{track.Artist} — {track.Album}";
            StatusText.Text = timeline.Status == MediaPlaybackStatus.Playing ? "Çalıyor" : "Duraklatıldı";
            StatusBadge.Background = timeline.Status == MediaPlaybackStatus.Playing
                ? _playingBadgeBrush
                : _pausedBadgeBrush;

            string trackKey = $"{track.Artist}_{track.Title}";
            if (trackKey != _lastArtworkKey)
            {
                _lastArtworkKey = trackKey;
                _ = UpdateArtworkAsync(track);
            }
        }
        else
        {
            TrackTitleText.Text = "Şarkı Çalınmıyor";
            TrackArtistText.Text = "Spotify veya Medya Oynatıcıyı Başlatın";
            StatusText.Text = "Müzik Bekleniyor...";
            StatusBadge.Background = _waitingBadgeBrush;

            if (_lastArtworkKey != string.Empty)
            {
                _lastArtworkKey = string.Empty;
                VerciArtworkImage.Source = null;
                VerciArtworkImage.IsVisible = false;
                VerciArtworkPlaceholder.IsVisible = true;
            }
        }

        // 2. Update progress timeline
        var pos = timeline.GetInterpolatedPosition();
        var dur = timeline.Duration > TimeSpan.Zero ? timeline.Duration : track.Duration;

        PositionText.Text = $"{pos:mm\\:ss}";
        DurationText.Text = $"{dur:mm\\:ss}";

        if (dur.TotalSeconds > 0)
        {
            TimelineProgress.Value = Math.Clamp((pos.TotalSeconds / dur.TotalSeconds) * 100.0, 0, 100);
        }
        else
        {
            TimelineProgress.Value = 0;
        }

        // 3. Populate or refresh lyrics list only if song changed or lyrics object updated
        string songKey = $"{track.Artist}_{track.Title}";
        if (songKey != _lastRenderedSongKey || !ReferenceEquals(lyrics, _lastRenderedLyrics))
        {
            _lastRenderedSongKey = songKey;
            _lastRenderedLyrics = lyrics;
            RebuildLyricsUi(lyrics);
        }

        // 4. Update active line highlight and auto-scroll to center
        int activeIdx = _verciModule.ActiveLineIndex;
        if (activeIdx != _currentRenderedLineIndex)
        {
            UpdateActiveLine(activeIdx);
        }
    }

    private async Task UpdateArtworkAsync(MediaTrackInfo track)
    {
        if (_artworkBitmapManager == null) return;
        try
        {
            var bitmap = await _artworkBitmapManager.GetArtworkBitmapAsync(track).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_verciModule.CurrentTrack == track && bitmap != null)
                {
                    VerciArtworkImage.Source = bitmap;
                    VerciArtworkImage.IsVisible = true;
                    VerciArtworkPlaceholder.IsVisible = false;
                }
                else if (_verciModule.CurrentTrack == track)
                {
                    VerciArtworkImage.Source = null;
                    VerciArtworkImage.IsVisible = false;
                    VerciArtworkPlaceholder.IsVisible = true;
                }
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VerciArtworkImage.Source = null;
                VerciArtworkImage.IsVisible = false;
                VerciArtworkPlaceholder.IsVisible = true;
            });
        }
    }

    private void RebuildLyricsUi(SyncedLyrics lyrics)
    {
        LyricsPanel.Children.Clear();
        _lineTextBlocks.Clear();
        _currentRenderedLineIndex = -99;

        UpdateViewportPaddingAndCenter();

        if (lyrics.Lines.Count == 0)
        {
            if (!string.IsNullOrEmpty(lyrics.PlainLyrics))
            {
                // Unsynced plain lyrics
                EmptyStatePanel.IsVisible = false;
                var plainBlock = new TextBlock
                {
                    Text = lyrics.PlainLyrics,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.Parse("#CDD6F4")),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                LyricsPanel.Children.Add(plainBlock);
            }
            else
            {
                EmptyStatePanel.IsVisible = true;
            }
            return;
        }

        EmptyStatePanel.IsVisible = false;

        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var block = new TextBlock
            {
                Text = line.Text,
                FontSize = 14,
                FontWeight = FontWeight.Normal,
                Foreground = _inactiveLyricBrush,
                Opacity = 0.40,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            _lineTextBlocks.Add(block);
            LyricsPanel.Children.Add(block);
        }
    }

    private void UpdateActiveLine(int activeIdx)
    {
        if (_lineTextBlocks.Count == 0) return;

        // Reset previous active line styling
        if (_currentRenderedLineIndex >= 0 && _currentRenderedLineIndex < _lineTextBlocks.Count)
        {
            var prevBlock = _lineTextBlocks[_currentRenderedLineIndex];
            prevBlock.FontSize = 14;
            prevBlock.FontWeight = FontWeight.Normal;
            prevBlock.Foreground = _inactiveLyricBrush;
            prevBlock.Opacity = 0.40;
        }

        _currentRenderedLineIndex = activeIdx;

        // Highlight new active line and center in viewport
        if (activeIdx >= 0 && activeIdx < _lineTextBlocks.Count)
        {
            var activeBlock = _lineTextBlocks[activeIdx];
            activeBlock.FontSize = 17;
            activeBlock.FontWeight = FontWeight.SemiBold;
            activeBlock.Foreground = _activeLyricBrush;
            activeBlock.Opacity = 1.0;

            if (ChkAutoScroll.IsChecked ?? true)
            {
                ScrollToLineCentered(activeBlock);
            }
        }
    }

    private void ScrollToLineCentered(TextBlock activeBlock)
    {
        if (LyricsScrollViewer.Bounds.Height <= 0) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                // Calculate vertical center of active line relative to LyricsPanel
                double lineCenter = activeBlock.Bounds.Y + (activeBlock.Bounds.Height / 2.0);
                double targetOffsetY = lineCenter - (LyricsScrollViewer.Bounds.Height / 2.0);
                double maxOffsetY = Math.Max(0.0, LyricsScrollViewer.Extent.Height - LyricsScrollViewer.Viewport.Height);
                double clampedOffset = Math.Clamp(targetOffsetY, 0.0, maxOffsetY);

                LyricsScrollViewer.Offset = new Vector(0, clampedOffset);
            }
            catch { }
        }, DispatcherPriority.Render);
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
        _uiTimer.Stop();
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
