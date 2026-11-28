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
    private readonly LyricsSyncEngine _syncEngine;
    private LyricsResolutionResult? _lastResolution;
    private string? _activeSyncBadgeCode;
    private bool _isUsingPlaybackFallback;
    private readonly object _sourcesLock = new();
    private List<LyricsTrackSourceEntry> _verifiedSourcesForCurrentTrack = new();
    private LyricsProviderId? _activeSourceProviderId;
    private LyricsSyncMode _activeSourceQuality = LyricsSyncMode.Asynchronous;
    private bool _hasManualSourceSelection;
    private IMediaSessionMonitor? _mediaSessionMonitor;
    private ILyricsClient? _lyricsClient;
    private ILyricsClient? _fallbackLyricsClient;
    private ILyricsCache? _lyricsCache;
    private readonly IEventBus? _eventBus;
    private readonly ISettingsManager? _settingsManager;

    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _currentTrackCts;
    private long _trackSequence = 0;
    private SyncedLyrics _currentLyrics = SyncedLyrics.Empty;
    private string _currentLyricsSongKey = string.Empty;
    private MediaTrackInfo _currentTrack = MediaTrackInfo.Empty;
    private MediaTimelineInfo _currentTimeline = MediaTimelineInfo.Zero;
    private int _activeLineIndex = -1;
    private bool _isDisposed;
    private readonly IPlaybackTimelineEngine _timelineEngine;
    private DateTime _rateLimitUntilUtc = DateTime.MinValue;
    private DateTime _lastTrackSwitchUtc = DateTime.MinValue;
    private TimeSpan _previousTrackDuration = TimeSpan.Zero;
    private string? _activeSourceBadgeCode;
    private string? _activeSourceDisplayName;

    public LyricsModule(
        IMediaSessionMonitor? mediaSessionMonitor = null,
        ILyricsClient? lyricsClient = null,
        ILyricsCache? lyricsCache = null,
        IEventBus? eventBus = null,
        ISettingsManager? settingsManager = null,
        IPlaybackTimelineEngine? timelineEngine = null,
        ILyricsClient? fallbackLyricsClient = null)
    {
        _mediaSessionMonitor = mediaSessionMonitor;
        _lyricsClient = lyricsClient;
        _fallbackLyricsClient = fallbackLyricsClient;
        _lyricsCache = lyricsCache;
        _eventBus = eventBus;
        _settingsManager = settingsManager;
        _timelineEngine = timelineEngine ?? new Lyrow.Core.Timeline.PlaybackTimelineEngine();

        _synchronizer = new LyricsTimelineSynchronizer(eventBus);
        _synchronizer.ActiveLineChanged += OnSynchronizerActiveLineChanged;
        _syncEngine = new LyricsSyncEngine();
        _syncEngine.SetMode(_settingsManager?.Current.Lyrics.SyncMode ?? LyricsSyncMode.Automatic);
    }

    public string ModuleId => "lyrics";
    public string DisplayName => "Lyrics (Synced Lyrics & Metadata)";
    public ModuleState State { get; private set; } = ModuleState.Uninitialized;
    public Exception? LastFault { get; private set; }

    public SyncedLyrics CurrentLyrics => _currentLyrics;
    public string CurrentLyricsSongKey => _currentLyricsSongKey;
    public MediaTrackInfo CurrentTrack => _currentTrack;
    public MediaTimelineInfo CurrentTimeline => _currentTimeline;
    public IPlaybackTimelineEngine TimelineEngine => _timelineEngine;
    public int ActiveLineIndex => _synchronizer.CurrentActiveIndex;
    public LyricsTimelineSynchronizer Synchronizer => _synchronizer;
    public LyricsSyncEngine SyncEngine => _syncEngine;
    public LyricsResolutionResult? LastResolution => _lastResolution;
    public string? ActiveSyncBadgeCode => _activeSyncBadgeCode;
    public LyricsProviderId? ActiveSourceProviderId { get { lock (_sourcesLock) return _activeSourceProviderId; } }
    public string? ActiveSourceBadgeCode => _activeSourceBadgeCode;
    public string? ActiveSourceDisplayName => _activeSourceDisplayName;
    public bool IsUsingPlaybackFallback => _isUsingPlaybackFallback;
    public bool HasManualSourceSelection => _hasManualSourceSelection;

    /// <summary>
    /// Returns all verified, deduplicated lyrics sources available for the active synchronization quality
    /// on the current track (used by the &lt; and &gt; source switcher controls).
    /// </summary>
    public IReadOnlyList<LyricsTrackSourceEntry> AvailableSourcesForCurrentQuality
    {
        get
        {
            lock (_sourcesLock)
            {
                if (_verifiedSourcesForCurrentTrack.Count == 0) return Array.Empty<LyricsTrackSourceEntry>();
                return _verifiedSourcesForCurrentTrack
                    .Where(s => s.ResolvedMode == _activeSourceQuality)
                    .ToArray();
            }
        }
    }

    public bool CanCycleSources => AvailableSourcesForCurrentQuality.Count > 1;

    /// <summary>
    /// Cycles to the previous (-1) or next (+1) verified lyrics source within the active synchronization quality.
    /// Wraps around circularly, preserves playback position / pause / seek / flow glow, keeps the previous source
    /// untouched if the candidate is invalid, and does not alter global synchronization mode settings.
    /// </summary>
    public bool TryCycleSource(int delta)
    {
        if (delta == 0) return false;

        LyricsTrackSourceEntry? target = null;
        lock (_sourcesLock)
        {
            var pool = _verifiedSourcesForCurrentTrack
                .Where(s => s.ResolvedMode == _activeSourceQuality)
                .ToList();
            if (pool.Count <= 1) return false;

            int currentIdx = _activeSourceProviderId.HasValue
                ? pool.FindIndex(s => s.ProviderId == _activeSourceProviderId.Value)
                : 0;
            if (currentIdx < 0) currentIdx = 0;

            int nextIdx = ((currentIdx + delta) % pool.Count + pool.Count) % pool.Count;
            var candidate = pool[nextIdx];

            // Validate candidate before switching so a broken/empty candidate never overwrites active lyrics
            if (candidate.TimingData == null ||
                candidate.TimingData.ActualGranularity == LyricsTimingGranularity.None ||
                candidate.SyncedLyrics == null ||
                string.IsNullOrWhiteSpace(candidate.BadgeCode))
            {
                return false;
            }

            target = candidate;
            _activeSourceProviderId = candidate.ProviderId;
            _hasManualSourceSelection = true;
        }

        if (target == null) return false;

        var requestedMode = _settingsManager?.Current.Lyrics.SyncMode ?? LyricsSyncMode.Automatic;
        _currentLyrics = target.SyncedLyrics;
        _activeSyncBadgeCode = target.BadgeCode;
        _activeSourceBadgeCode = LyricsSourceBadgeFormatter.GetSourceCode(target.ProviderId);
        _activeSourceDisplayName = LyricsSourceBadgeFormatter.GetSourceDisplayName(target.ProviderId);
        _isUsingPlaybackFallback = false;
        _synchronizer.SetLyrics(target.SyncedLyrics);
        _synchronizer.UpdateTimeline(_currentTimeline);
        _syncEngine.Load(target.TimingData, requestedMode == LyricsSyncMode.Automatic ? target.ResolvedMode : requestedMode);
        _syncEngine.Evaluate(_currentTimeline.Position, _currentTimeline.Status != MediaPlaybackStatus.Playing);

        if (_eventBus != null)
        {
            _ = _eventBus.PublishAsync(new LyricsLoadedEvent(target.SyncedLyrics, target.DisplayName));
        }

        return true;
    }

    private void ClearPerTrackSourceState()
    {
        lock (_sourcesLock)
        {
            _verifiedSourcesForCurrentTrack.Clear();
            _activeSourceProviderId = null;
            _activeSourceQuality = LyricsSyncMode.Asynchronous;
            _hasManualSourceSelection = false;
        }
        _activeSourceBadgeCode = null;
        _activeSourceDisplayName = null;
    }

    private void MergeVerifiedSources(IReadOnlyList<LyricsTrackSourceEntry>? incoming)
    {
        if (incoming == null || incoming.Count == 0) return;
        lock (_sourcesLock)
        {
            foreach (var entry in incoming)
            {
                if (entry.TimingData == null || entry.TimingData.ActualGranularity == LyricsTimingGranularity.None) continue;
                int existingIdx = _verifiedSourcesForCurrentTrack.FindIndex(s => s.ProviderId == entry.ProviderId);
                if (existingIdx >= 0)
                {
                    if (LyricsProviderPriorityResolver.GetPrecisionRank(entry.ResolvedMode) >
                        LyricsProviderPriorityResolver.GetPrecisionRank(_verifiedSourcesForCurrentTrack[existingIdx].ResolvedMode))
                    {
                        _verifiedSourcesForCurrentTrack[existingIdx] = entry;
                    }
                }
                else
                {
                    _verifiedSourcesForCurrentTrack.Add(entry);
                }
            }
        }
    }

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

            _synchronizer.SyncOffsetMs = 0;
            _syncEngine.SyncOffsetMs = 0;

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
                _syncEngine.Reset();
                _lastResolution = null;
                _activeSyncBadgeCode = null;
                _isUsingPlaybackFallback = false;
                ClearPerTrackSourceState();
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
        _timelineEngine.HandleTimelineProperties(timeline.Position, timeline.Duration, timeline.Status, timeline.LastUpdatedUtc, timeline.CanSeek);
        _currentTimeline = _timelineEngine.ToMediaTimelineInfo();
        _synchronizer.SyncOffsetMs = 0;
        _syncEngine.SyncOffsetMs = 0;
        _synchronizer.UpdateTimeline(_currentTimeline);
    }

    public async Task SetSyncModeAsync(LyricsSyncMode mode, CancellationToken ct = default)
    {
        _syncEngine.SetMode(mode);

        if (_lyricsClient is IMultiProviderLyricsService multiService &&
            !_currentTrack.IsEmpty &&
            !string.IsNullOrWhiteSpace(_currentTrack.Title) &&
            !string.IsNullOrWhiteSpace(_currentTrack.Artist))
        {
            try
            {
                long seq = Interlocked.Read(ref _trackSequence);
                var resolution = await multiService.ResolveLyricsAsync(
                    _currentTrack.Title.Trim(),
                    _currentTrack.Artist.Trim(),
                    mode,
                    _currentTrack.Duration,
                    ct).ConfigureAwait(false);

                if (ct.IsCancellationRequested || Interlocked.Read(ref _trackSequence) != seq)
                {
                    return;
                }

                _lastResolution = resolution;
                if (resolution.Success && resolution.TimingData != null)
                {
                    var synced = resolution.TimingData.ToSyncedLyrics();
                    _currentLyrics = synced;
                    _currentLyricsSongKey = $"{_currentTrack.Artist.Trim()}_{_currentTrack.Title.Trim()}";
                    _synchronizer.SetLyrics(synced);
                    _synchronizer.UpdateTimeline(_currentTimeline);
                    _syncEngine.Load(resolution.TimingData, mode);
                    _syncEngine.Evaluate(_currentTimeline.Position, _currentTimeline.Status != MediaPlaybackStatus.Playing);
                    _activeSyncBadgeCode = LyricsSyncCodeFormatter.Format(resolution);
                    _activeSourceBadgeCode = LyricsSourceBadgeFormatter.GetSourceCode(resolution.ProviderId);
                    _activeSourceDisplayName = LyricsSourceBadgeFormatter.GetSourceDisplayName(resolution.ProviderId);
                    _isUsingPlaybackFallback = false;
                }
            }
            catch (OperationCanceledException)
            {
                // Ignored on track switch / cancellation
            }
        }
    }

    private void OnSynchronizerActiveLineChanged(object? sender, ActiveLyricLineChangedEventArgs e)
    {
        _activeLineIndex = e.LineIndex;
    }

    private void OnTrackChanged(object? sender, MediaTrackInfo track)
    {
        if (State != ModuleState.Running) return;

        string incomingTitle = (track.Title ?? string.Empty).Trim();
        string incomingArtist = (track.Artist ?? string.Empty).Trim();
        string currentTitle = (_currentTrack.Title ?? string.Empty).Trim();
        string currentArtist = (_currentTrack.Artist ?? string.Empty).Trim();

        bool isSameSong = !track.IsEmpty &&
                          string.Equals(incomingTitle, currentTitle, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(incomingArtist, currentArtist, StringComparison.OrdinalIgnoreCase);

        // Invalidate previous track lyrics immediately on event dispatch only if transitioning to a different song
        if (!isSameSong)
        { 
            _currentLyrics = SyncedLyrics.Empty;
            _currentLyricsSongKey = string.Empty;
            _activeLineIndex = -1;
            _lastResolution = null;
            _activeSyncBadgeCode = null;
            _isUsingPlaybackFallback = false;
            ClearPerTrackSourceState();
            _synchronizer.Reset();
            _synchronizer.SetLyrics(SyncedLyrics.Empty);
            _syncEngine.Reset();
        }

        _ = HandleTrackChangedAsync(track, _activeCts?.Token ?? CancellationToken.None);
    }

    private void OnTimelineChanged(object? sender, MediaTimelineInfo timeline)
    {
        if (State != ModuleState.Running) return;

        _timelineEngine.HandleTimelineProperties(timeline.Position, timeline.Duration, timeline.Status, timeline.LastUpdatedUtc, timeline.CanSeek);
        _currentTimeline = _timelineEngine.ToMediaTimelineInfo();

        _synchronizer.SyncOffsetMs = 0;
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

            if (updatedDur > TimeSpan.Zero)
            {
                _timelineEngine.HandleTrackChanged(_currentTrack);
                if (_currentTimeline.Duration <= TimeSpan.Zero)
                {
                    _currentTimeline = _currentTimeline with { Duration = updatedDur };
                    _synchronizer.UpdateTimeline(_currentTimeline);
                }
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
        _timelineEngine.HandleTrackChanged(track);
        _currentTimeline = _timelineEngine.ToMediaTimelineInfo();
        _synchronizer.Reset();
        _synchronizer.SetLyrics(SyncedLyrics.Empty);
        _synchronizer.UpdateTimeline(_currentTimeline);
        _syncEngine.Reset();
        _lastResolution = null;
        _activeSyncBadgeCode = null;
        _isUsingPlaybackFallback = false;
        ClearPerTrackSourceState();
        _currentLyrics = SyncedLyrics.Empty;
        _currentLyricsSongKey = string.Empty;

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
            var requestedMode = LyricsSyncMode.Automatic;
            _syncEngine.SyncOffsetMs = 0;

            // 1. Multi-provider v5.0 fast parallel + progressive upgrade path
            if (_lyricsClient is IMultiProviderLyricsService multiService)
            {
                int currentPrecisionRank = -1;
                string targetSongKey = $"{incomingArtist}_{incomingTitle}";

                // 1a. Instant local disk cache check: show cached lyrics immediately (<5ms) so user never waits!
                if (_lyricsCache != null)
                {
                    var cachedImmediate = await _lyricsCache.GetAsync(incomingTitle, incomingArtist, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested || Interlocked.Read(ref _trackSequence) != currentSeq)
                    {
                        return;
                    }

                    if (cachedImmediate != null)
                    {
                        var cachedTiming = LyricsTimingData.FromSyncedLyrics(cachedImmediate, LyricsProviderId.Lrclib);
                        var cachedMode = requestedMode == LyricsSyncMode.Asynchronous
                            ? LyricsSyncMode.Asynchronous
                            : cachedTiming.BestSupportedMode;
                        currentPrecisionRank = LyricsProviderPriorityResolver.GetPrecisionRank(cachedMode);

                        _currentLyrics = cachedImmediate;
                        _currentLyricsSongKey = targetSongKey;
                        _activeSourceBadgeCode = "CACHE";
                        _activeSourceDisplayName = "Local Cache";
                        _synchronizer.SetLyrics(cachedImmediate);
                        _synchronizer.UpdateTimeline(_currentTimeline);
                        _syncEngine.Load(cachedTiming, requestedMode);
                        _syncEngine.Evaluate(_currentTimeline.Position, _currentTimeline.Status != MediaPlaybackStatus.Playing);

                        if (_eventBus != null)
                        {
                            await _eventBus.PublishAsync(new LyricsLoadedEvent(cachedImmediate, "Cache"), token).ConfigureAwait(false);
                        }

                        // If cached lyrics already satisfy the requested precision (e.g. Line/Async or cached Syllables), skip network completely!
                        bool cacheAlreadySatisfies = requestedMode switch
                        {
                            LyricsSyncMode.Asynchronous => true,
                            LyricsSyncMode.Line => cachedTiming.HasTimedLines,
                            LyricsSyncMode.Word => cachedTiming.HasTimedWords,
                            LyricsSyncMode.Syllable => cachedTiming.HasTimedSyllables,
                            LyricsSyncMode.Automatic => cachedTiming.HasTimedSyllables,
                            _ => false
                        };

                        if (cacheAlreadySatisfies)
                        {
                            return;
                        }
                    }
                }

                if (DateTime.UtcNow < _rateLimitUntilUtc)
                {
                    if (currentPrecisionRank >= 0) return; // Already showing cached lyrics
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
                    _syncEngine.Reset();
                    return;
                }

                // 1b. Controlled parallel multi-provider resolution with progressive seamless upgrade
                async Task ApplyProgressiveUpgradeAsync(LyricsResolutionResult interim)
                {
                    if (token.IsCancellationRequested || Interlocked.Read(ref _trackSequence) != currentSeq)
                    {
                        return;
                    }
                    if (!interim.Success || interim.TimingData == null) return;

                    // Always merge newly arrived verified sources for the < / > switcher (deduplicated by upstream ProviderId)
                    MergeVerifiedSources(interim.AvailableSources);

                    // Respect user's manual source choice for the duration of the current track
                    if (_hasManualSourceSelection && _currentLyricsSongKey == targetSongKey)
                    {
                        return;
                    }

                    int incomingRank = LyricsProviderPriorityResolver.GetPrecisionRank(interim.ResolvedMode);
                    // Never downgrade precision once a higher-precision timing payload is already active!
                    if (incomingRank <= currentPrecisionRank && _currentLyricsSongKey == targetSongKey)
                    {
                        return;
                    }

                    currentPrecisionRank = incomingRank;
                    _lastResolution = interim;
                    _activeSyncBadgeCode = LyricsSyncCodeFormatter.Format(interim);
                    _activeSourceBadgeCode = LyricsSourceBadgeFormatter.GetSourceCode(interim.ProviderId);
                    _activeSourceDisplayName = LyricsSourceBadgeFormatter.GetSourceDisplayName(interim.ProviderId);
                    _isUsingPlaybackFallback = false;
                    lock (_sourcesLock)
                    {
                        _activeSourceProviderId = interim.ProviderId;
                        _activeSourceQuality = interim.ResolvedMode;
                    }
                    var synced = interim.SyncedLyrics ?? interim.TimingData.ToSyncedLyrics();
                    _currentLyrics = synced;
                    _currentLyricsSongKey = targetSongKey;
                    _synchronizer.SetLyrics(synced);
                    _synchronizer.UpdateTimeline(_currentTimeline);
                    _syncEngine.Load(interim.TimingData, requestedMode);
                    _syncEngine.Evaluate(_currentTimeline.Position, _currentTimeline.Status != MediaPlaybackStatus.Playing);

                    if (_eventBus != null)
                    {
                        await _eventBus.PublishAsync(new LyricsLoadedEvent(synced, interim.ProviderId?.ToString() ?? "MultiProvider"), token).ConfigureAwait(false);
                    }
                }

                var resolution = await multiService.ResolveLyricsProgressiveAsync(
                    incomingTitle,
                    incomingArtist,
                    requestedMode,
                    onInterimResult: ApplyProgressiveUpgradeAsync,
                    duration: track.Duration,
                    cancellationToken: token).ConfigureAwait(false);

                if (token.IsCancellationRequested || Interlocked.Read(ref _trackSequence) != currentSeq)
                {
                    return;
                }

                if (resolution.Success && resolution.TimingData != null)
                {
                    await ApplyProgressiveUpgradeAsync(resolution).ConfigureAwait(false);

                    if (_lyricsCache != null && (_settingsManager?.Current.Lyrics.CacheLyricsLocally ?? true))
                    {
                        await _lyricsCache.SaveAsync(incomingTitle, incomingArtist, _currentLyrics, token).ConfigureAwait(false);
                    }
                    return;
                }

                // If we already displayed cached lyrics earlier, keep them (playback fallback, no fabricated provider code)!
                if (currentPrecisionRank >= 0)
                {
                    _isUsingPlaybackFallback = true;
                    _activeSyncBadgeCode = null;
                    return;
                }

                // Previous-version playback-source fallback: if no sync provider returned lyrics, fall back to the legacy ILyricsClient
                // so lyrics still load and progress with playback position without disabling Lyrics!
                if (_fallbackLyricsClient != null)
                {
                    var fbResult = await _fallbackLyricsClient.FetchLyricsAsync(incomingTitle, incomingArtist, track.Duration, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested || Interlocked.Read(ref _trackSequence) != currentSeq)
                    {
                        return;
                    }

                    if (fbResult.Found && fbResult.Lyrics != null)
                    {
                        _currentLyrics = fbResult.Lyrics;
                        _currentLyricsSongKey = targetSongKey;
                        _isUsingPlaybackFallback = true;
                        _activeSyncBadgeCode = null; // Never fabricate a sync badge code on fallback
                        var (fbCode, fbName) = LyricsSourceBadgeFormatter.FormatFallbackOrCustomSource(fbResult.Source);
                        _activeSourceBadgeCode = fbCode;
                        _activeSourceDisplayName = fbName;
                        _synchronizer.SetLyrics(fbResult.Lyrics);
                        _synchronizer.UpdateTimeline(_currentTimeline);
                        _syncEngine.Reset(); // Let LyricsTimelineSynchronizer + LyricsFillProgressCalculator drive playback-position progression

                        if (_lyricsCache != null && (_settingsManager?.Current.Lyrics.CacheLyricsLocally ?? true))
                        {
                            await _lyricsCache.SaveAsync(incomingTitle, incomingArtist, fbResult.Lyrics, token).ConfigureAwait(false);
                        }

                        if (_eventBus != null)
                        {
                            await _eventBus.PublishAsync(new LyricsLoadedEvent(fbResult.Lyrics, fbResult.Source), token).ConfigureAwait(false);
                        }
                        return;
                    }
                }

                if (_eventBus != null)
                {
                    await _eventBus.PublishAsync(new LyricsFetchFailedEvent(
                        resolution.StatusMessage ?? "Lyrics not found.",
                        IsRateLimited: false,
                        RetryAfterSeconds: 0), token).ConfigureAwait(false);
                }

                if (Interlocked.Read(ref _trackSequence) == currentSeq)
                {
                    _currentLyrics = SyncedLyrics.Empty;
                    _activeSyncBadgeCode = null;
                    _isUsingPlaybackFallback = false;
                    _synchronizer.SetLyrics(SyncedLyrics.Empty);
                    _syncEngine.Reset();
                }
                return;
            }

            // 2. Legacy single-client path (local cache first, then ILyricsClient)
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
                    _currentLyricsSongKey = $"{incomingArtist}_{incomingTitle}";
                    _synchronizer.SetLyrics(cached);
                    _synchronizer.UpdateTimeline(_currentTimeline);
                    _syncEngine.Load(LyricsTimingData.FromSyncedLyrics(cached, LyricsProviderId.Lrclib), requestedMode);

                    if (_eventBus != null)
                    {
                        await _eventBus.PublishAsync(new LyricsLoadedEvent(cached, "Cache"), token).ConfigureAwait(false);
                    }
                    return;
                }
            }

            // 3. Fetch from legacy ILyricsClient
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
                    _syncEngine.Reset();
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
                    _currentLyricsSongKey = $"{incomingArtist}_{incomingTitle}";
                    _synchronizer.SetLyrics(result.Lyrics);
                    _synchronizer.UpdateTimeline(_currentTimeline);
                    _syncEngine.Load(LyricsTimingData.FromSyncedLyrics(result.Lyrics, LyricsProviderId.Lrclib), requestedMode);

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
                _syncEngine.Reset();
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
