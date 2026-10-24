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
    private SyncedLyrics _currentLyrics = SyncedLyrics.Empty;
    private MediaTrackInfo _currentTrack = MediaTrackInfo.Empty;
    private MediaTimelineInfo _currentTimeline = MediaTimelineInfo.Zero;
    private int _activeLineIndex = -1;
    private bool _isDisposed;
    private DateTime _rateLimitUntilUtc = DateTime.MinValue;

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
        _currentTimeline = timeline;
        _synchronizer.SyncOffsetMs = _settingsManager?.Current.Lyrics.SyncOffsetMs ?? 0;
        _synchronizer.UpdateTimeline(timeline);
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
        _currentTimeline = timeline;

        _synchronizer.SyncOffsetMs = _settingsManager?.Current.Lyrics.SyncOffsetMs ?? 0;
        _synchronizer.UpdateTimeline(timeline);

        if (_eventBus != null)
        {
            _ = _eventBus.PublishAsync(new MediaPlaybackStateChangedEvent(timeline));
        }
    }

    private void OnMonitorFaulted(object? sender, Exception ex)
    {
        LastFault = ex;
    }

    public async Task HandleTrackChangedAsync(MediaTrackInfo track, CancellationToken ct = default)
    {
        _currentTrack = track;
        _activeLineIndex = -1;
        _synchronizer.Reset();

        if (_eventBus != null)
        {
            await _eventBus.PublishAsync(new MediaTrackChangedEvent(track), ct).ConfigureAwait(false);
        }

        if (track.IsEmpty)
        {
            _currentLyrics = SyncedLyrics.Empty;
            _synchronizer.SetLyrics(SyncedLyrics.Empty);
            return;
        }

        // 1. Try local cache first
        if (_lyricsCache != null)
        {
            var cached = await _lyricsCache.GetAsync(track.Title, track.Artist, ct).ConfigureAwait(false);
            if (cached != null)
            {
                _currentLyrics = cached;
                _synchronizer.SetLyrics(cached);
                _synchronizer.UpdateTimeline(_currentTimeline);

                if (_eventBus != null)
                {
                    await _eventBus.PublishAsync(new LyricsLoadedEvent(cached, "Cache"), ct).ConfigureAwait(false);
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
                        RetryAfterSeconds: remaining), ct).ConfigureAwait(false);
                }
                _currentLyrics = SyncedLyrics.Empty;
                _synchronizer.SetLyrics(SyncedLyrics.Empty);
                return;
            }

            var result = await _lyricsClient.FetchLyricsAsync(track.Title, track.Artist, track.Duration, ct).ConfigureAwait(false);
            if (result.Found && result.Lyrics != null)
            {
                _currentLyrics = result.Lyrics;
                _synchronizer.SetLyrics(result.Lyrics);
                _synchronizer.UpdateTimeline(_currentTimeline);

                if (_lyricsCache != null && (_settingsManager?.Current.Lyrics.CacheLyricsLocally ?? true))
                {
                    await _lyricsCache.SaveAsync(track.Title, track.Artist, result.Lyrics, ct).ConfigureAwait(false);
                }

                if (_eventBus != null)
                {
                    await _eventBus.PublishAsync(new LyricsLoadedEvent(result.Lyrics, result.Source), ct).ConfigureAwait(false);
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
                    result.RetryAfterSeconds), ct).ConfigureAwait(false);
            }
        }

        // If not found or client missing
        _currentLyrics = SyncedLyrics.Empty;
        _synchronizer.SetLyrics(SyncedLyrics.Empty);
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
