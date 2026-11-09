using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;

namespace Lyrow.Core.Modules;

/// <summary>
/// Core implementation of the Lyrics module managing synchronized lyrics and music metadata overlay.
/// Operates completely independently from Glow/Glow and coordinates the media monitor, lyrics cache and synchronizer.
/// </summary>
public sealed class LyricsModule : ILyrowModule
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly LyricsTimelineSynchronizer _synchronizer;
    private IMediaSessionMonitor? _mediaSessionMonitor;
    private ILyricsClient? _lyricsClient;
    private ILyricsCache? _lyricsCache;
    private readonly IEventBus? _eventBus;
    private readonly ISettingsManager? _settingsManager;

    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _currentTrackCts;
    private long _trackSequence = 0;
    private SyncedLyrics _currentLyrics = SyncedLyrics.Empty;
    private MediaTrackInfo _currentTrack = MediaTrackInfo.Empty;
    private MediaTimelineInfo _currentTimeline = MediaTimelineInfo.Zero;
    private int _activeLineIndex = -1;
    private bool _isDisposed;
    private DateTime _rateLimitUntilUtc = DateTime.MinValue;
    private DateTime _lastTrackSwitchUtc = DateTime.MinValue;
    private TimeSpan _previousTrackDuration = TimeSpan.Zero;

    public LyricsModule(
        IMediaSessionMonitor? mediaSessionMonitor = null,
        ILyricsClient? lyricsClient = null,
        ILyricsCache? lyricsCache = null,
        IEventBus? eventBus = null,
        ISettingsManager? settingsManager = null)
    {
        _mediaSessionMonitor = mediaSessionMonitor;
        _lyricsClient = lyricsClient;
        _lyricsCache = lyricsCache;
        _eventBus = eventBus;
        _settingsManager = settingsManager;

        _synchronizer = new LyricsTimelineSynchronizer(eventBus);
        _synchronizer.ActiveLineChanged += OnSynchronizerActiveLineChanged;
    }

    public string ModuleId => "lyrics";
    public string DisplayName => "Lyrics (Synced Lyrics & Metadata)";
    public ModuleState State { get; private set; } = ModuleState.Uninitialized;
    public Exception? LastFault { get; private set; }

    public SyncedLyrics CurrentLyrics => _currentLyrics;
    public MediaTrackInfo CurrentTrack => _currentTrack;
    public MediaTimelineInfo CurrentTimeline => _currentTimeline;
    public int ActiveLineIndex => _synchronizer.CurrentActiveIndex;
    public LyricsTimelineSynchronizer Synchronizer => _synchronizer;
    public long TrackSequence => Interlocked.Read(ref _trackSequence);

    /// <summary>
    /// When true, stopping the Lyrics overlay module detaches event handlers without shutting down the shared OS media session monitor.
    /// Defaults to false for isolated unit tests.
    /// </summary>
    public bool KeepMediaMonitorAliveWhenStopped { get; set; } = false;

    /// <summary>
    /// Attaches or updates the media monitor, lyrics client and cache services.
    /// </summary>
    public void AttachServices(
        IMediaSessionMonitor mediaSessionMonitor,
        ILyricsClient lyricsClient,
        ILyricsCache lyricsCache)
    {
        _mediaSessionMonitor = mediaSessionMonitor;
        _lyricsClient = lyricsClient;
        _lyricsCache = lyricsCache;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != ModuleState.Uninitialized) return;

            State = ModuleState.Initialized;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == ModuleState.Running) return;

            State = ModuleState.Starting;
            _activeCts = new CancellationTokenSource();
            var ct = _activeCts.Token;

            _synchronizer.SyncOffsetMs = _settingsManager?.Current.Lyrics.SyncOffsetMs ?? 0;

            if (_mediaSessionMonitor != null)
            {
                _mediaSessionMonitor.TrackChanged -= OnTrackChanged;
                _mediaSessionMonitor.TimelineChanged -= OnTimelineChanged;
                _mediaSessionMonitor.MonitorFaulted -= OnMonitorFaulted;

                _mediaSessionMonitor.TrackChanged += OnTrackChanged;
                _mediaSessionMonitor.TimelineChanged += OnTimelineChanged;
                _mediaSessionMonitor.MonitorFaulted += OnMonitorFaulted;

                if (!_mediaSessionMonitor.IsMonitoring)
                {
                    await _mediaSessionMonitor.StartAsync(ct).ConfigureAwait(false);
                }

                // If a track is already active on startup, query its lyrics
                if (!_mediaSessionMonitor.CurrentTrack.IsEmpty)
                {
                    _currentTimeline = _mediaSessionMonitor.CurrentTimeline;
                    _ = HandleTrackChangedAsync(_mediaSessionMonitor.CurrentTrack, ct);
                }
            }

            State = ModuleState.Running;
        }
        catch (Exception ex)
        {
            LastFault = ex;
            State = ModuleState.Faulted;
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != ModuleState.Running && State != ModuleState.Starting) return;

            State = ModuleState.Stopping;

            _activeCts?.Cancel();
            _currentTrackCts?.Cancel();
            _currentTrackCts?.Dispose();
            _currentTrackCts = null;

            if (_mediaSessionMonitor != null)
            {
                _mediaSessionMonitor.TrackChanged -= OnTrackChanged;
                _mediaSessionMonitor.TimelineChanged -= OnTimelineChanged;
                _mediaSessionMonitor.MonitorFaulted -= OnMonitorFaulted;

                if (!KeepMediaMonitorAliveWhenStopped)
                {
                    await _mediaSessionMonitor.StopAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            if (!KeepMediaMonitorAliveWhenStopped)
            {
                _synchronizer.Reset();
                _currentLyrics = SyncedLyrics.Empty;
                _currentTrack = MediaTrackInfo.Empty;
                _currentTimeline = MediaTimelineInfo.Zero;
                _activeLineIndex = -1;
            }

            _activeCts?.Dispose();
            _activeCts = null;

            State = ModuleState.Stopped;
        }
        catch (Exception ex)
        {
            LastFault = ex;
            State = ModuleState.Faulted;
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public void UpdateTimelineExternal(MediaTimelineInfo timeline)
    {
        var dur = _currentTrack.Duration > TimeSpan.Zero ? _currentTrack.Duration : timeline.Duration;
        var pos = timeline.Position;
        if (dur > TimeSpan.Zero && pos > dur) pos = dur;
        _currentTimeline = new MediaTimelineInfo(pos, dur, timeline.Status, timeline.LastUpdatedUtc, timeline.CanSeek);
        _synchronizer.SyncOffsetMs = _settingsManager?.Current.Lyrics.SyncOffsetMs ?? 0;
        _synchronizer.UpdateTimeline(_currentTimeline);
    }

    public void SetSyncOffset(int offsetMs)
    {
        _synchronizer.SyncOffsetMs = offsetMs;
    }

    private void OnSynchronizerActiveLineChanged(object? sender, ActiveLyricLineChangedEventArgs e)
    {
        _activeLineIndex = e.LineIndex;
    }

    private void OnTrackChanged(object? sender, MediaTrackInfo track)
    {
        if (State != ModuleState.Running) return;
        _ = HandleTrackChangedAsync(track, _activeCts?.Token ?? CancellationToken.None);
    }

    private void OnTimelineChanged(object? sender, MediaTimelineInfo timeline)
    {
        if (State != ModuleState.Running) return;

        // Guard against stale timeline events from previous track during automatic track transition
        if (DateTime.UtcNow - _lastTrackSwitchUtc < TimeSpan.FromMilliseconds(1500) && _previousTrackDuration > TimeSpan.Zero)
        {
            // If incoming timeline carries the previous track's duration or position near previous track's end
            bool isPreviousDuration = Math.Abs((timeline.Duration - _previousTrackDuration).TotalMilliseconds) < 500;
            bool isDifferentFromCurrent = _currentTrack.Duration > TimeSpan.Zero && Math.Abs((timeline.Duration - _currentTrack.Duration).TotalMilliseconds) > 500;
            if (isPreviousDuration && isDifferentFromCurrent)
            {
                return; // Discard stale timeline belonging to previous track
            }
        }

        var dur = _currentTrack.Duration > TimeSpan.Zero ? _currentTrack.Duration : timeline.Duration;
        var pos = timeline.Position;
        if (dur > TimeSpan.Zero && pos > dur) pos = dur;
        _currentTimeline = new MediaTimelineInfo(pos, dur, timeline.Status, timeline.LastUpdatedUtc, timeline.CanSeek);

        _synchronizer.SyncOffsetMs = _settingsManager?.Current.Lyrics.SyncOffsetMs ?? 0;
        _synchronizer.UpdateTimeline(_currentTimeline);

        if (_eventBus != null)
        {
            _ = _eventBus.PublishAsync(new MediaPlaybackStateChangedEvent(_currentTimeline));
        }
    }

    private void OnMonitorFaulted(object? sender, Exception ex)
    {
        LastFault = ex;
    }

    public async Task HandleTrackChangedAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        long currentSeq;
        CancellationTokenSource trackCts;

        string incomingTitle = (track.Title ?? string.Empty).Trim();
        string incomingArtist = (track.Artist ?? string.Empty).Trim();
        string currentTitle = (_currentTrack.Title ?? string.Empty).Trim();
        string currentArtist = (_currentTrack.Artist ?? string.Empty).Trim();

        bool isSameTitleAndArtist = !track.IsEmpty &&
                                    string.Equals(incomingTitle, currentTitle, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(incomingArtist, currentArtist, StringComparison.OrdinalIgnoreCase);

        // If GSMTC emits a secondary property update for the exact same track (e.g. ThumbnailData or Album or Duration arrived 50ms later)
        // and we are already fetching or have already loaded lyrics for this track, update metadata in-place without aborting the active fetch!
        bool isMetadataEnrichment = (track.ThumbnailData != null && track.ThumbnailData.Length > 0 && (_currentTrack.ThumbnailData == null || _currentTrack.ThumbnailData.Length != track.ThumbnailData.Length)) ||
                                    (track.Duration > TimeSpan.Zero && _currentTrack.Duration <= TimeSpan.Zero);
        if (isSameTitleAndArtist && (isMetadataEnrichment || _currentLyrics.Lines.Count > 0 || !string.IsNullOrWhiteSpace(_currentLyrics.PlainLyrics) || (_currentTrackCts != null && !_currentTrackCts.IsCancellationRequested)))
        {
            var updatedDur = track.Duration > TimeSpan.Zero ? track.Duration : _currentTrack.Duration;
            var updatedThumb = (track.ThumbnailData != null && track.ThumbnailData.Length > 0) ? track.ThumbnailData : _currentTrack.ThumbnailData;
            var updatedAlbum = !string.IsNullOrWhiteSpace(track.Album) ? track.Album : _currentTrack.Album;
            _currentTrack = track with
            {
                Duration = updatedDur,
                ThumbnailData = updatedThumb,
                Album = updatedAlbum
            };

            if (updatedDur > TimeSpan.Zero && _currentTimeline.Duration <= TimeSpan.Zero)
            {
                _currentTimeline = _currentTimeline with { Duration = updatedDur };
                _synchronizer.UpdateTimeline(_currentTimeline);
            }

            if (_eventBus != null)
            {
                await _eventBus.PublishAsync(new MediaTrackChangedEvent(_currentTrack), ct).ConfigureAwait(false);
            }
            return;
        }

        _currentTrackCts?.Cancel();
        _currentTrackCts?.Dispose();

        currentSeq = Interlocked.Increment(ref _trackSequence);

        _lastTrackSwitchUtc = DateTime.UtcNow;
        _previousTrackDuration = _currentTrack.Duration;
        _currentTrack = track;
        _activeLineIndex = -1;
        _currentTimeline = new MediaTimelineInfo(TimeSpan.Zero, track.Duration, MediaPlaybackStatus.Changing, DateTime.UtcNow);
        _synchronizer.Reset();
        _synchronizer.SetLyrics(SyncedLyrics.Empty);
        _synchronizer.UpdateTimeline(_currentTimeline);
        _currentLyrics = SyncedLyrics.Empty;

        trackCts = _activeCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct, _activeCts.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(ct);
        _currentTrackCts = trackCts;

        var token = trackCts.Token;

        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new MediaPlaybackStateChangedEvent(_currentTimeline), token).ConfigureAwait(false);
            await _eventBus.PublishAsync(new MediaTrackChangedEvent(track), token).ConfigureAwait(false);
        }

        if (track.IsEmpty || string.IsNullOrWhiteSpace(incomingTitle) || string.IsNullOrWhiteSpace(incomingArtist))
        {
            _currentLyrics = SyncedLyrics.Empty;
            _synchronizer.SetLyrics(SyncedLyrics.Empty);
            return;
        }

        try
        {
            // 1. Try local cache first
            if (_lyricsCache != null)
        {
            var cached = await _lyricsCache.GetAsync(incomingTitle, incomingArtist, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || Interlocked.Read(ref _trackSequence) != currentSeq)
            {
                return;
            }

            if (cached != null)
            {
                _currentLyrics = cached;
                _synchronizer.SetLyrics(cached);
                _synchronizer.UpdateTimeline(_currentTimeline);

                if (_eventBus != null)
                {
                    await _eventBus.PublishAsync(new LyricsLoadedEvent(cached, "Cache"), token).ConfigureAwait(false);
                }
                return;
            }
        }

        // 2. Fetch from LRCLIB API
        if (_lyricsClient != null)
        {
            if (DateTime.UtcNow < _rateLimitUntilUtc)
            {
                int remaining = (int)Math.Max(1, (_rateLimitUntilUtc - DateTime.UtcNow).TotalSeconds);
                if (_eventBus != null)
                {
                    await _eventBus.PublishAsync(new LyricsFetchFailedEvent(
                        $"İstek limiti aşıldı. Lütfen {remaining} saniye bekleyin.",
                        IsRateLimited: true,
                        RetryAfterSeconds: remaining), token).ConfigureAwait(false);
                }
                _currentLyrics = SyncedLyrics.Empty;
                _synchronizer.SetLyrics(SyncedLyrics.Empty);
                return;
            }

            var result = await _lyricsClient.FetchLyricsAsync(incomingTitle, incomingArtist, track.Duration, token).ConfigureAwait(false);
            if (token.IsCancellationRequested || Interlocked.Read(ref _trackSequence) != currentSeq)
            {
                return;
            }

            if (result.Found && result.Lyrics != null)
            {
                _currentLyrics = result.Lyrics;
                _synchronizer.SetLyrics(result.Lyrics);
                _synchronizer.UpdateTimeline(_currentTimeline);

                if (_lyricsCache != null && (_settingsManager?.Current.Lyrics.CacheLyricsLocally ?? true))
                {
                    await _lyricsCache.SaveAsync(incomingTitle, incomingArtist, result.Lyrics, token).ConfigureAwait(false);
                }

                if (_eventBus != null)
                {
                    await _eventBus.PublishAsync(new LyricsLoadedEvent(result.Lyrics, result.Source), token).ConfigureAwait(false);
                }
                return;
            }

            if (result.IsRateLimited)
            {
                _rateLimitUntilUtc = DateTime.UtcNow.AddSeconds(result.RetryAfterSeconds);
            }

            if (_eventBus != null)
            {
                await _eventBus.PublishAsync(new LyricsFetchFailedEvent(
                    result.Error ?? "Lyrics not found.",
                    result.IsRateLimited,
                    result.RetryAfterSeconds), token).ConfigureAwait(false);
            }
        }

        // If not found or client missing
        if (Interlocked.Read(ref _trackSequence) == currentSeq)
        {
            _currentLyrics = SyncedLyrics.Empty;
            _synchronizer.SetLyrics(SyncedLyrics.Empty);
        }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (State == ModuleState.Running)
        {
            await StopAsync().ConfigureAwait(false);
        }

        _synchronizer.Dispose();

        if (_mediaSessionMonitor != null)
        {
            await _mediaSessionMonitor.DisposeAsync().ConfigureAwait(false);
        }

        _stateLock.Dispose();
        _activeCts?.Dispose();
    }
}
