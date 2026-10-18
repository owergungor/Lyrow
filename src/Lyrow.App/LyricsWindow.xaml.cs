using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;

namespace Lyrow.App;

/// <summary>
/// Apple Music-inspired fluid lyrics experience for Windows 11.
/// Keeps the active lyric line vertically centered with smooth hardware-accelerated
/// animated scrolling and bold typography transitions.
/// </summary>
public sealed partial class LyricsWindow : Window
{
    private readonly VerciModule _verciModule;
    private readonly ISettingsManager _settingsManager;
    private readonly ArtworkBitmapManager? _artworkBitmapManager;
    private readonly DispatcherTimer _uiTimer;

    private readonly List<TextBlock> _lineBlocks = new();
    private int _currentActiveIndex = -99;
    private string _lastSongKey = string.Empty;
    private SyncedLyrics? _lastLyrics;
    private bool _userIsInteracting = false;
    private DateTime _lastUserInteractionUtc = DateTime.MinValue;
    private bool _isShuttingDown = false;

    public LyricsWindow(VerciModule verciModule, ISettingsManager settingsManager, ArtworkBitmapManager? artworkBitmapManager = null)
    {
        InitializeComponent();
        _verciModule = verciModule;
        _settingsManager = settingsManager;
        _artworkBitmapManager = artworkBitmapManager;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(LyricsTitleBar);
        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 700));
        }
        catch { }

        TxtOffsetMs.Text = $"{_settingsManager.Current.Verci.SyncOffsetMs:+0;-0;0} ms";
        BtnOffsetMinus.Click += (_, _) => AdjustOffset(-500);
        BtnOffsetPlus.Click += (_, _) => AdjustOffset(500);

        SwitchAutoScroll.IsOn = _settingsManager.Current.Verci.AutoScroll;
        SwitchAutoScroll.Toggled += (_, _) =>
        {
            _settingsManager.Current.Verci.AutoScroll = SwitchAutoScroll.IsOn;
        };

        LyricsScrollViewer.SizeChanged += (_, _) => UpdateViewportPadding();
        LyricsScrollViewer.ViewChanged += (_, e) =>
        {
            if (e.IsIntermediate)
            {
                _userIsInteracting = true;
                _lastUserInteractionUtc = DateTime.UtcNow;
            }
        };

        Closed += (_, _) =>
        {
            if (!_isShuttingDown)
            {
                _uiTimer?.Stop();
            }
        };

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50) // 20 FPS smooth timeline tracking
        };
        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();
    }

    private void AdjustOffset(int deltaMs)
    {
        int newOffset = _settingsManager.Current.Verci.SyncOffsetMs + deltaMs;
        _settingsManager.Current.Verci.SyncOffsetMs = newOffset;
        _verciModule.SetSyncOffset(newOffset);
        TxtOffsetMs.Text = $"{newOffset:+0;-0;0} ms";
    }

    private void UpdateViewportPadding()
    {
        double viewportH = LyricsScrollViewer.ActualHeight;
        if (viewportH <= 0) return;

        double halfPadding = Math.Max(40.0, (viewportH / 2.0) - 24.0);
        LyricsPanel.Margin = new Thickness(24, halfPadding, 24, halfPadding);

        if (_currentActiveIndex >= 0 && _currentActiveIndex < _lineBlocks.Count)
        {
            ScrollToActiveLineCentered(_lineBlocks[_currentActiveIndex], immediate: true);
        }
    }

    private async void OnUiTimerTick(object? sender, object e)
    {
        var track = _verciModule.CurrentTrack;
        var lyrics = _verciModule.CurrentLyrics;

        if (track.IsEmpty)
        {
            TxtSongTitle.Text = "No active media session";
            TxtArtistName.Text = "Waiting for Spotify, Apple Music, or Chrome...";
            ProgressTimeline.Value = 0;
            TxtTimestamp.Text = "00:00 / 00:00";
            PanelEmptyState.Visibility = Visibility.Visible;
            LyricsScrollViewer.Visibility = Visibility.Collapsed;
            IconArtworkPlaceholder.Visibility = Visibility.Visible;
            ImgTrackArtwork.Source = null;
            return;
        }

        TxtSongTitle.Text = track.Title;
        TxtArtistName.Text = track.Artist;

        var pos = _verciModule.CurrentTimeline.GetInterpolatedPosition();
        var dur = _verciModule.CurrentTimeline.Duration > TimeSpan.Zero ? _verciModule.CurrentTimeline.Duration : track.Duration;
        TxtTimestamp.Text = $"{pos:mm\\:ss} / {dur:mm\\:ss}";

        if (dur.TotalSeconds > 0)
        {
            ProgressTimeline.Value = Math.Clamp((pos.TotalSeconds / dur.TotalSeconds) * 100.0, 0, 100);
        }
        else
        {
            ProgressTimeline.Value = 0;
        }

        string songKey = $"{track.Artist}_{track.Title}";
        if (songKey != _lastSongKey || !ReferenceEquals(lyrics, _lastLyrics))
        {
            _lastSongKey = songKey;
            _lastLyrics = lyrics;
            RebuildLyricsUi(lyrics);
            await UpdateArtworkAsync(track);
        }

        // Active line synchronization
        int activeIdx = _verciModule.ActiveLineIndex;
        if (activeIdx != _currentActiveIndex)
        {
            UpdateActiveLineHighlight(activeIdx);
        }
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
        _lineBlocks.Clear();
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
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                LyricsPanel.Children.Add(plainBlock);
            }
            else
            {
                PanelEmptyState.Visibility = Visibility.Visible;
                LyricsScrollViewer.Visibility = Visibility.Collapsed;
            }
            return;
        }

        PanelEmptyState.Visibility = Visibility.Collapsed;
        LyricsScrollViewer.Visibility = Visibility.Visible;

        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var block = new TextBlock
            {
                Text = line.Text,
                FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.Normal,
                Opacity = 0.35,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            _lineBlocks.Add(block);
            LyricsPanel.Children.Add(block);
        }
    }

    private void UpdateActiveLineHighlight(int activeIdx)
    {
        if (_lineBlocks.Count == 0) return;

        // Reset previous line
        if (_currentActiveIndex >= 0 && _currentActiveIndex < _lineBlocks.Count)
        {
            var prevBlock = _lineBlocks[_currentActiveIndex];
            prevBlock.FontSize = 17;
            prevBlock.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            prevBlock.Opacity = 0.35;
        }

        _currentActiveIndex = activeIdx;

        // Highlight new active line
        if (activeIdx >= 0 && activeIdx < _lineBlocks.Count)
        {
            var activeBlock = _lineBlocks[activeIdx];
            activeBlock.FontSize = 25;
            activeBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            activeBlock.Opacity = 1.0;

            // Check if user is currently scrolling manually
            if (_userIsInteracting && (DateTime.UtcNow - _lastUserInteractionUtc).TotalSeconds < 3.0)
            {
                return; // Allow user to browse lyrics freely
            }
            _userIsInteracting = false;

            if (SwitchAutoScroll.IsOn)
            {
                ScrollToActiveLineCentered(activeBlock, immediate: false);
            }
        }
    }

    private void ScrollToActiveLineCentered(TextBlock activeBlock, bool immediate)
    {
        double viewportH = LyricsScrollViewer.ActualHeight;
        if (viewportH <= 0) return;

        try
        {
            var transform = activeBlock.TransformToVisual(LyricsPanel);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            double lineCenter = point.Y + (activeBlock.ActualHeight / 2.0);
            double targetOffsetY = lineCenter - (viewportH / 2.0);

            LyricsScrollViewer.ChangeView(null, Math.Max(0, targetOffsetY), null, disableAnimation: immediate);
        }
        catch
        {
            // Safeguard against layout phase transitions
        }
    }

    public void Shutdown()
    {
        _isShuttingDown = true;
        _uiTimer.Stop();
        Close();
    }
}
