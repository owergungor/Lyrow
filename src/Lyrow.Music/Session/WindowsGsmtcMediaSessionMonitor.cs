using System.Runtime.Versioning;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Lyrow.Music.Metadata;

namespace Lyrow.Music.Session;

/// <summary>
/// Hardware Windows GSMTC (Global System Media Transport Controls) media session monitor.
/// Listens to active OS-level media sessions (Spotify, Apple Music, YouTube, Edge, Chrome)
/// and dispatches real Play, Pause, TogglePlayPause, Next, and Previous commands.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsGsmtcMediaSessionMonitor : IMediaSessionMonitor
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IMediaMetadataNormalizer _normalizer;
    private readonly HashSet<string> _seekUnsupportedAppIds = new(StringComparer.OrdinalIgnoreCase);
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private CancellationTokenSource? _pollingCts;
    private bool _isMonitoring;
    private bool _isDisposed;
    private long _trackSequence = 0;
    private long _sessionGeneration = 0;
    private DateTime _lastTrackSwitchUtc = DateTime.MinValue;
    private DateTime _lastSeekUtc = DateTime.MinValue;
    private TimeSpan _lastSeekTargetPosition = TimeSpan.Zero;
    private int _lastTrackThumbnailHash = 0;
    private int _lastTrackThumbnailLength = 0;
    private string _lastTrackAlbum = string.Empty;

    public WindowsGsmtcMediaSessionMonitor(IMediaMetadataNormalizer? normalizer = null)
    {
        _normalizer = normalizer ?? new MediaMetadataNormalizer();
        CurrentTrack = MediaTrackInfo.Empty;
        CurrentTimeline = MediaTimelineInfo.Zero;
    }

    public event EventHandler<MediaTrackInfo>? TrackChanged;
    public event EventHandler<MediaTimelineInfo>? TimelineChanged;
    public event EventHandler<Exception>? MonitorFaulted;

    public bool IsMonitoring => _isMonitoring;
    public bool HasActiveSession => _currentSession != null;
    public string? SourceAppId => _currentSession?.SourceAppUserModelId;
    public MediaTrackInfo CurrentTrack { get; private set; }
    public MediaTimelineInfo CurrentTimeline { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException("GSMTC requires Windows 10 build 19041 or higher.");
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isMonitoring) return;

            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                _sessionManager.SessionsChanged += OnSessionsChanged;
                var session = ResolveBestSession(_sessionManager);
                await AttachSessionInternalAsync(session).ConfigureAwait(false);
            }

            _isMonitoring = true;
        }
        catch (Exception ex)
        {
            MonitorFaulted?.Invoke(this, ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;
            StopTimelinePolling();
            DetachCurrentSession();

            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _sessionManager.SessionsChanged -= OnSessionsChanged;
                _sessionManager = null;
            }

            CurrentTrack = MediaTrackInfo.Empty;
            CurrentTimeline = MediaTimelineInfo.Zero;
        }
        finally
        {
            _lock.Release();
        }
    }

    private GlobalSystemMediaTransportControlsSession? ResolveBestSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            var current = manager.GetCurrentSession();
            if (current != null)
            {
                try
                {
                    var curInfo = current.GetPlaybackInfo();
                    var curStatus = curInfo?.PlaybackStatus;
                    // Keep OS current session when Playing, Changing (track transition/seek), or Opened
                    if (curStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ||
                        curStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing ||
                        curStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened)
                    {
                        return current;
                    }
                }
                catch { }
            }

            var all = manager.GetSessions();
            if (all != null)
            {
                int count = all.Count;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var s = all[i];
                        var info = s.GetPlaybackInfo();
                        if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            return s;
                        }
                    }
                    catch { }
                }

                if (current != null) return current;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var s = all[i];
                        var info = s.GetPlaybackInfo();
                        if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                        {
                            return s;
                        }
                    }
                    catch { }
                }

                if (count > 0) return all[0];
            }

            return current;
        }
        catch
        {
            return null;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        if (!_isMonitoring) return;
        _ = RefreshSessionBindingAsync(sender);
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        if (!_isMonitoring) return;
        _ = RefreshSessionBindingAsync(sender);
    }

    private async Task RefreshSessionBindingAsync(GlobalSystemMediaTransportControlsSessionManager sender)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_isMonitoring) return;
            var best = ResolveBestSession(sender);
            DetachCurrentSession();
            await AttachSessionInternalAsync(best).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task AttachSessionInternalAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        Interlocked.Increment(ref _sessionGeneration);
        _currentSession = session;
        if (_currentSession == null)
        {
            StopTimelinePolling();
            CurrentTrack = MediaTrackInfo.Empty;
            CurrentTimeline = MediaTimelineInfo.Zero;
            TrackChanged?.Invoke(this, CurrentTrack);
            TimelineChanged?.Invoke(this, CurrentTimeline);
            return;
        }

        _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        await UpdateAllPropertiesAsync(_currentSession).ConfigureAwait(false);
        StartTimelinePolling();
    }

    private void DetachCurrentSession()
    {
        Interlocked.Increment(ref _sessionGeneration);
        Interlocked.Increment(ref _trackSequence);
        StopTimelinePolling();
        if (_currentSession != null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }
            catch { }
            _currentSession = null;
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ = UpdateMediaPropertiesAsync(sender);
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        _ = UpdatePlaybackAndTimelineAsync(sender);
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        _ = UpdatePlaybackAndTimelineAsync(sender);
    }

    private async Task UpdateAllPropertiesAsync(GlobalSystemMediaTransportControlsSession session)
    {
        await UpdateMediaPropertiesAsync(session).ConfigureAwait(false);
        await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
    }

    public static async Task<byte[]?> ReadThumbnailStreamAsync(IRandomAccessStreamReference? thumbnailRef)
    {
        if (thumbnailRef == null) return null;

        try
        {
            using var stream = await thumbnailRef.OpenReadAsync();
            if (stream != null)
            {
                try
                {
                    if (stream.CanRead && stream.Size > 0)
                    {
                        stream.Seek(0);
                    }
                }
                catch { }

                using var netStream = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                await netStream.CopyToAsync(ms).ConfigureAwait(false);
                var bytes = ms.ToArray();
                if (bytes.Length > 0)
                {
                    return bytes;
                }
            }
        }
        catch (Exception ex)
        {
            Lyrow.Core.Services.LocalDiagnosticLogger.LogWarning($"[GSMTC] Primary thumbnail stream read failed: {ex.Message}");
        }

        try
        {
            using var stream = await thumbnailRef.OpenReadAsync();
            if (stream != null && stream.Size > 0)
            {
                stream.Seek(0);
                var thumbnailBytes = new byte[stream.Size];
                using var dataReader = new DataReader(stream);
                await dataReader.LoadAsync((uint)stream.Size);
                dataReader.ReadBytes(thumbnailBytes);
                if (thumbnailBytes.Length > 0)
                {
                    return thumbnailBytes;
                }
            }
        }
        catch (Exception ex)
        {
            Lyrow.Core.Services.LocalDiagnosticLogger.LogWarning($"[GSMTC] Fallback DataReader thumbnail read failed: {ex.Message}");
        }

        return null;
    }

    public static int ComputeThumbnailHash(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return 0;
        unchecked
        {
            uint hash = 2166136261u;
            int step = Math.Max(1, bytes.Length / 512);
            for (int i = 0; i < bytes.Length; i += step)
            {
                hash ^= bytes[i];
                hash *= 16777619u;
            }
            hash ^= (uint)bytes.Length;
            hash *= 16777619u;
            return (int)hash;
        }
    }

    /// <summary>
    /// Detects when GSMTC emits a new track's Title/Artist while still holding the previous track's exact thumbnail bytes.
    /// Unless both tracks explicitly share the same non-empty album title, identical bytes across a track switch are rejected as stale carryover.
    /// </summary>
    public static bool IsStaleCarryoverThumbnail(
        bool isTrackSwitch,
        string? previousAlbum,
        string? newAlbum,
        byte[]? newThumbnailBytes,
        int previousThumbnailHash,
        int previousThumbnailLength)
    {
        if (!isTrackSwitch || newThumbnailBytes == null || newThumbnailBytes.Length == 0 || previousThumbnailLength <= 0)
        {
            return false;
        }

        if (newThumbnailBytes.Length != previousThumbnailLength)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(newAlbum) &&
            !string.IsNullOrWhiteSpace(previousAlbum) &&
            string.Equals(newAlbum.Trim(), previousAlbum.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ComputeThumbnailHash(newThumbnailBytes) == previousThumbnailHash;
    }

    /// <summary>
    /// Reconciles raw GSMTC timeline position and LastUpdatedTime against track-switch and seek boundaries
    /// so previous-track elapsed time is never added to a newly started track and post-seek positions never jump or snap back.
    /// </summary>
    public static (TimeSpan Position, DateTime LastUpdatedUtc) ReconcileTimelinePosition(
        TimeSpan rawPositionSinceStart,
        TimeSpan trackDuration,
        MediaPlaybackStatus status,
        DateTime? gsmtcReportedUtc,
        DateTime nowUtc,
        DateTime lastTrackSwitchUtc,
        DateTime lastSeekUtc,
        TimeSpan lastSeekTargetPosition)
    {
        var pos = rawPositionSinceStart < TimeSpan.Zero ? TimeSpan.Zero : rawPositionSinceStart;
        bool hasRecentSeek = lastSeekUtc > lastTrackSwitchUtc &&
                             lastSeekUtc != DateTime.MinValue &&
                             (nowUtc - lastSeekUtc).TotalMilliseconds is >= -50 and <= 2400;

        // 1. Protect post-seek window: if GSMTC hasn't updated Position to near the seek target yet, hold/extrapolate from seek target
        if (hasRecentSeek && Math.Abs((pos - lastSeekTargetPosition).TotalSeconds) > 3.5)
        {
            var elapsedSinceSeek = nowUtc > lastSeekUtc ? (nowUtc - lastSeekUtc) : TimeSpan.Zero;
            pos = status == MediaPlaybackStatus.Playing
                ? (lastSeekTargetPosition + elapsedSinceSeek)
                : lastSeekTargetPosition;
            if (trackDuration > TimeSpan.Zero && pos > trackDuration)
            {
                pos = trackDuration;
            }
            return (pos, nowUtc);
        }

        // 2. Protect track session: if track switched and no user seek occurred on the new track,
        // the track's elapsed position can NEVER exceed the elapsed real-time since the track switch.
        // This eliminates cumulative playlist / video compilation / carryover time from previous tracks
        // (e.g. Song A 2 min + Song B 19s = 2:19 clamped cleanly to 00:19).
        if (lastTrackSwitchUtc != DateTime.MinValue && lastSeekUtc <= lastTrackSwitchUtc)
        {
            var maxPossibleElapsed = nowUtc > lastTrackSwitchUtc ? (nowUtc - lastTrackSwitchUtc) : TimeSpan.Zero;
            if (pos > maxPossibleElapsed + TimeSpan.FromSeconds(2.0))
            {
                pos = status == MediaPlaybackStatus.Playing ? maxPossibleElapsed : TimeSpan.Zero;
                if (trackDuration > TimeSpan.Zero && pos > trackDuration)
                {
                    pos = trackDuration;
                }
                return (pos, nowUtc);
            }
        }

        var lastUpdatedUtc = nowUtc;
        if (gsmtcReportedUtc.HasValue && gsmtcReportedUtc.Value.Year > 2020)
        {
            var effectiveReportedUtc = gsmtcReportedUtc.Value;

            // Clamp anchor so delta NEVER includes time before the current track started or before the last seek!
            if (lastTrackSwitchUtc != DateTime.MinValue && effectiveReportedUtc < lastTrackSwitchUtc)
            {
                effectiveReportedUtc = lastTrackSwitchUtc;
            }
            if (lastSeekUtc != DateTime.MinValue && effectiveReportedUtc < lastSeekUtc)
            {
                effectiveReportedUtc = lastSeekUtc;
            }

            var delta = nowUtc - effectiveReportedUtc;
            if (status == MediaPlaybackStatus.Playing && delta.TotalSeconds >= 0 && delta.TotalHours < 24)
            {
                if (delta.TotalSeconds > 0.05)
                {
                    var candidatePos = pos + delta;
                    if (lastTrackSwitchUtc != DateTime.MinValue && lastSeekUtc <= lastTrackSwitchUtc)
                    {
                        var maxPossible = nowUtc > lastTrackSwitchUtc ? (nowUtc - lastTrackSwitchUtc) : TimeSpan.Zero;
                        pos = candidatePos > maxPossible ? maxPossible : candidatePos;
                    }
                    else
                    {
                        pos = candidatePos;
                    }
                }
                if (trackDuration > TimeSpan.Zero && pos > trackDuration)
                {
                    pos = trackDuration;
                }
                lastUpdatedUtc = nowUtc;
            }
            else if (Math.Abs(delta.TotalSeconds) <= 3.0)
            {
                lastUpdatedUtc = effectiveReportedUtc;
            }
        }

        if (trackDuration > TimeSpan.Zero && pos > trackDuration)
        {
            pos = trackDuration;
        }

        return (pos, lastUpdatedUtc);
    }

    private async Task UpdateMediaPropertiesAsync(GlobalSystemMediaTransportControlsSession session)
    {
        if (_currentSession != null && !ReferenceEquals(session, _currentSession)) return;
        long expectedSessionGen = Interlocked.Read(ref _sessionGeneration);
        long currentSeq = Interlocked.Increment(ref _trackSequence);
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null ||
                Interlocked.Read(ref _trackSequence) != currentSeq ||
                Interlocked.Read(ref _sessionGeneration) != expectedSessionGen)
            {
                return;
            }
            if (_currentSession != null && !ReferenceEquals(session, _currentSession)) return;

            byte[]? thumbnailBytes = await ReadThumbnailStreamAsync(props.Thumbnail).ConfigureAwait(false);

            string rawTitle = props.Title ?? string.Empty;
            string rawArtist = props.Artist ?? string.Empty;
            string rawAlbum = props.AlbumTitle ?? string.Empty;

            var norm = _normalizer.Normalize(rawTitle, rawArtist, rawAlbum);
            string newTitle = norm.Title;
            string newArtist = norm.Artist;
            string newAlbum = norm.Album;

            bool isTrackSwitch = !string.Equals(CurrentTrack.Title, newTitle, StringComparison.OrdinalIgnoreCase) ||
                                 !string.Equals(CurrentTrack.Artist, newArtist, StringComparison.OrdinalIgnoreCase);

            int rejectedStaleHash = 0;
            if (isTrackSwitch)
            {
                // If GSMTC still holds the previous song's exact thumbnail bytes during a track switch, discard it so it never leaks into the new track
                if (IsStaleCarryoverThumbnail(isTrackSwitch, _lastTrackAlbum, newAlbum, thumbnailBytes, _lastTrackThumbnailHash, _lastTrackThumbnailLength))
                {
                    rejectedStaleHash = _lastTrackThumbnailHash;
                    Lyrow.Core.Services.LocalDiagnosticLogger.LogInfo(
                        $"[GSMTC] Discarded stale carryover thumbnail ({thumbnailBytes?.Length ?? 0} bytes) from previous track when switching to '{newArtist} - {newTitle}'.");
                    thumbnailBytes = null;
                }

                if (thumbnailBytes != null && thumbnailBytes.Length > 0)
                {
                    _lastTrackThumbnailHash = ComputeThumbnailHash(thumbnailBytes);
                    _lastTrackThumbnailLength = thumbnailBytes.Length;
                }
                _lastTrackAlbum = newAlbum;
            }
            else
            {
                // Preserve already-loaded artwork if browser emits a secondary metadata update with null Thumbnail for the same track
                if ((thumbnailBytes == null || thumbnailBytes.Length == 0) &&
                    CurrentTrack.ThumbnailData != null && CurrentTrack.ThumbnailData.Length > 0)
                {
                    thumbnailBytes = CurrentTrack.ThumbnailData;
                }
                else if (thumbnailBytes != null && thumbnailBytes.Length > 0)
                {
                    _lastTrackThumbnailHash = ComputeThumbnailHash(thumbnailBytes);
                    _lastTrackThumbnailLength = thumbnailBytes.Length;
                }
            }

            var timelineProps = session.GetTimelineProperties();
            var startTime = timelineProps?.StartTime ?? TimeSpan.Zero;
            var endTime = timelineProps?.EndTime ?? TimeSpan.Zero;
            var duration = endTime > startTime ? (endTime - startTime) : endTime;

            var pbInfo = session.GetPlaybackInfo();
            string? appId = session.SourceAppUserModelId;
            bool isBlacklisted = !string.IsNullOrEmpty(appId) && _seekUnsupportedAppIds.Contains(appId);
            bool canSeek = pbInfo?.Controls != null && pbInfo.Controls.IsPlaybackPositionEnabled && !isBlacklisted;

            if (isTrackSwitch)
            {
                _lastTrackSwitchUtc = DateTime.UtcNow;
                _lastSeekUtc = DateTime.MinValue;
                _lastSeekTargetPosition = TimeSpan.Zero;

                // Reset timeline immediately on track switch so old track's position/duration is never extrapolated
                CurrentTimeline = new MediaTimelineInfo(TimeSpan.Zero, duration, MediaPlaybackStatus.Changing, _lastTrackSwitchUtc, canSeek);
                TimelineChanged?.Invoke(this, CurrentTimeline);
            }

            var track = new MediaTrackInfo(
                Title: newTitle,
                Artist: newArtist,
                Album: newAlbum,
                Duration: duration,
                ThumbnailData: thumbnailBytes,
                RawTitle: norm.RawTitle,
                RawArtist: norm.RawArtist,
                RawAlbum: norm.RawAlbum);

            if (Interlocked.Read(ref _trackSequence) != currentSeq ||
                Interlocked.Read(ref _sessionGeneration) != expectedSessionGen)
            {
                return;
            }
            if (_currentSession != null && !ReferenceEquals(session, _currentSession)) return;

            if (isTrackSwitch && !track.IsEmpty)
            {
                Lyrow.Core.Services.LocalDiagnosticLogger.LogInfo(
                    $"[GSMTC] Active session='{appId}' track='{newArtist} - {newTitle}' album='{newAlbum}' thumbnailBytes={thumbnailBytes?.Length ?? 0}");
            }

            CurrentTrack = track;
            TrackChanged?.Invoke(this, track);

            // Synchronize playback status and timeline immediately after track properties change
            await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);

            if (isTrackSwitch)
            {
                // Follow up at 150ms and 450ms so asynchronous GSMTC timeline properties (Spotify, YouTube/Apple Music Web)
                // settle into active Playing state without requiring explicit user interaction
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(150).ConfigureAwait(false);
                        if (_isMonitoring && _currentSession != null && ReferenceEquals(session, _currentSession))
                        {
                            await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
                        }

                        await Task.Delay(300).ConfigureAwait(false);
                        if (_isMonitoring && _currentSession != null && ReferenceEquals(session, _currentSession))
                        {
                            await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
                        }
                    }
                    catch { }
                });
            }

            // Web players (YouTube Music Web / Apple Music Web) often populate GSMTC Thumbnail 300ms-1500ms after MediaPropertiesChanged
            if (!track.IsEmpty && (thumbnailBytes == null || thumbnailBytes.Length == 0))
            {
                Lyrow.Core.Services.LocalDiagnosticLogger.LogInfo($"[GSMTC] Track '{newTitle}' by '{newArtist}' (App={appId}) has no initial GSMTC thumbnail; scheduling delayed thumbnail re-probes.");
                _ = ProbeDelayedThumbnailAsync(session, expectedSessionGen, currentSeq, newTitle, newArtist, rejectedStaleHash);
            }
        }
        catch (Exception ex)
        {
            MonitorFaulted?.Invoke(this, ex);
        }
    }

    private async Task ProbeDelayedThumbnailAsync(
        GlobalSystemMediaTransportControlsSession session,
        long expectedSessionGen,
        long expectedSeq,
        string expectedTitle,
        string expectedArtist,
        int rejectedStaleHash)
    {
        int[] delaysMs = [350, 1200, 2500];
        foreach (int delay in delaysMs)
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                if (!_isMonitoring ||
                    Interlocked.Read(ref _trackSequence) != expectedSeq ||
                    Interlocked.Read(ref _sessionGeneration) != expectedSessionGen)
                {
                    return;
                }
                if (CurrentTrack.ThumbnailData != null && CurrentTrack.ThumbnailData.Length > 0) return;

                var props = await session.TryGetMediaPropertiesAsync();
                if (props == null ||
                    Interlocked.Read(ref _trackSequence) != expectedSeq ||
                    Interlocked.Read(ref _sessionGeneration) != expectedSessionGen)
                {
                    return;
                }

                var norm = _normalizer.Normalize(props.Title, props.Artist, props.AlbumTitle);
                if (!string.Equals(norm.Title, expectedTitle, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(norm.Artist, expectedArtist, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var lateBytes = await ReadThumbnailStreamAsync(props.Thumbnail).ConfigureAwait(false);
                if (lateBytes != null && lateBytes.Length > 0 &&
                    Interlocked.Read(ref _trackSequence) == expectedSeq &&
                    Interlocked.Read(ref _sessionGeneration) == expectedSessionGen)
                {
                    int lateHash = ComputeThumbnailHash(lateBytes);
                    if (rejectedStaleHash != 0 && lateHash == rejectedStaleHash)
                    {
                        continue;
                    }

                    _lastTrackThumbnailHash = lateHash;
                    _lastTrackThumbnailLength = lateBytes.Length;
                    Lyrow.Core.Services.LocalDiagnosticLogger.LogInfo($"[GSMTC] Delayed thumbnail arrived after {delay}ms for '{expectedTitle}' ({lateBytes.Length} bytes).");
                    CurrentTrack = CurrentTrack with { ThumbnailData = lateBytes };
                    TrackChanged?.Invoke(this, CurrentTrack);
                    return;
                }
            }
            catch
            {
                // Ignore transient WinRT errors during delayed probe
            }
        }
    }

    private Task UpdatePlaybackAndTimelineAsync(GlobalSystemMediaTransportControlsSession session)
    {
        if (_currentSession != null && !ReferenceEquals(session, _currentSession)) return Task.CompletedTask;
        try
        {
            var playbackInfo = session.GetPlaybackInfo();
            var timelineProps = session.GetTimelineProperties();

            var status = playbackInfo?.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackStatus.Stopped,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackStatus.Changing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackStatus.Opened,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackStatus.Closed,
                _ => MediaPlaybackStatus.Unknown
            };

            // If session is still reporting Changing or Opened but the settling window (>600ms) has elapsed, promote to Playing
            if ((status == MediaPlaybackStatus.Changing || status == MediaPlaybackStatus.Opened) &&
                _lastTrackSwitchUtc != DateTime.MinValue &&
                (DateTime.UtcNow - _lastTrackSwitchUtc).TotalMilliseconds > 600)
            {
                status = MediaPlaybackStatus.Playing;
            }
            else if (status == MediaPlaybackStatus.Unknown && CurrentTimeline.Status == MediaPlaybackStatus.Playing)
            {
                status = MediaPlaybackStatus.Playing;
            }

            var startTime = timelineProps?.StartTime ?? TimeSpan.Zero;
            var endTime = timelineProps?.EndTime ?? TimeSpan.Zero;
            var rawPos = timelineProps?.Position ?? TimeSpan.Zero;
            var dur = endTime > startTime ? (endTime - startTime) : endTime;
            var pos = rawPos >= startTime ? (rawPos - startTime) : rawPos;
            if (dur <= TimeSpan.Zero && CurrentTrack.Duration > TimeSpan.Zero)
            {
                dur = CurrentTrack.Duration;
            }
            if (CurrentTrack.Duration <= TimeSpan.Zero && dur > TimeSpan.Zero && !CurrentTrack.IsEmpty)
            {
                CurrentTrack = CurrentTrack with { Duration = dur };
                TrackChanged?.Invoke(this, CurrentTrack);
            }

            DateTime? reportedUtc = (timelineProps != null && timelineProps.LastUpdatedTime.Year > 2020)
                ? timelineProps.LastUpdatedTime.UtcDateTime
                : null;

            var (reconciledPos, lastUpdatedUtc) = ReconcileTimelinePosition(
                pos,
                dur,
                status,
                reportedUtc,
                DateTime.UtcNow,
                _lastTrackSwitchUtc,
                _lastSeekUtc,
                _lastSeekTargetPosition);

            var controls = playbackInfo?.Controls;
            string? appId = session.SourceAppUserModelId;
            bool isBlacklisted = !string.IsNullOrEmpty(appId) && _seekUnsupportedAppIds.Contains(appId);
            bool canSeek = controls != null && controls.IsPlaybackPositionEnabled && !isBlacklisted;
            var timeline = new MediaTimelineInfo(reconciledPos, dur, status, lastUpdatedUtc, canSeek);
            CurrentTimeline = timeline;
            TimelineChanged?.Invoke(this, timeline);
        }
        catch (Exception ex)
        {
            MonitorFaulted?.Invoke(this, ex);
        }

        return Task.CompletedTask;
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> EnsureActiveSessionAsync()
    {
        if (_currentSession != null) return _currentSession;

        if (_sessionManager == null)
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            }
            catch { }
        }

        if (_sessionManager != null)
        {
            var best = ResolveBestSession(_sessionManager);
            if (best != null)
            {
                await AttachSessionInternalAsync(best).ConfigureAwait(false);
            }
        }

        return _currentSession;
    }

    public async Task<bool> TryPlayAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            bool ok = await session.TryPlayAsync();
            await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
            return ok;
        }
        catch
        {
            DetachCurrentSession();
            return false;
        }
    }

    public async Task<bool> TryPauseAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            bool ok = await session.TryPauseAsync();
            await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
            return ok;
        }
        catch
        {
            DetachCurrentSession();
            return false;
        }
    }

    public async Task<bool> TryTogglePlayPauseAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            var status = session.GetPlaybackInfo()?.PlaybackStatus;
            bool ok = false;
            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                ok = await session.TryPauseAsync();
                if (!ok) ok = await session.TryTogglePlayPauseAsync();
            }
            else if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused ||
                     status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped)
            {
                ok = await session.TryPlayAsync();
                if (!ok) ok = await session.TryTogglePlayPauseAsync();
            }
            else
            {
                ok = await session.TryTogglePlayPauseAsync();
            }

            await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
            return ok;
        }
        catch
        {
            DetachCurrentSession();
            return false;
        }
    }

    public async Task<bool> TrySkipNextAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            bool ok = await session.TrySkipNextAsync();
            _ = Task.Run(async () =>
            {
                await Task.Delay(200).ConfigureAwait(false);
                await UpdateAllPropertiesAsync(session).ConfigureAwait(false);
            });
            return ok;
        }
        catch
        {
            DetachCurrentSession();
            return false;
        }
    }

    public async Task<bool> TrySkipPreviousAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            bool ok = await session.TrySkipPreviousAsync();
            _ = Task.Run(async () =>
            {
                await Task.Delay(200).ConfigureAwait(false);
                await UpdateAllPropertiesAsync(session).ConfigureAwait(false);
            });
            return ok;
        }
        catch
        {
            DetachCurrentSession();
            return false;
        }
    }

    public async Task<bool> TrySeekAsync(TimeSpan position)
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        string? appId = session.SourceAppUserModelId;
        if (!string.IsNullOrEmpty(appId) && _seekUnsupportedAppIds.Contains(appId))
        {
            return false;
        }

        var controls = session.GetPlaybackInfo()?.Controls;
        if (controls == null || !controls.IsPlaybackPositionEnabled)
        {
            if (!string.IsNullOrEmpty(appId)) _seekUnsupportedAppIds.Add(appId);
            CurrentTimeline = CurrentTimeline with { CanSeek = false };
            TimelineChanged?.Invoke(this, CurrentTimeline);
            return false;
        }

        try
        {
            var timelineProps = session.GetTimelineProperties();
            var startTime = timelineProps?.StartTime ?? TimeSpan.Zero;
            long targetTicks = (startTime + position).Ticks;

            // Windows GSMTC takes requested playback position in 100-nanosecond units (ticks)
            bool ok = await session.TryChangePlaybackPositionAsync(targetTicks);
            if (ok)
            {
                var seekNowUtc = DateTime.UtcNow;
                _lastSeekUtc = seekNowUtc;
                _lastSeekTargetPosition = position;

                // Immediately anchor current timeline to sought position so UI scrubber does not snap back
                CurrentTimeline = CurrentTimeline with
                {
                    Position = position,
                    LastUpdatedUtc = seekNowUtc,
                    CanSeek = true
                };
                TimelineChanged?.Invoke(this, CurrentTimeline);

                _ = Task.Run(async () =>
                {
                    // Delay allow desktop/web media players (Apple Music Web / Spotify / Edge)
                    // time to buffer, jump, and update their internal GSMTC timeline properties
                    // before querying, preventing stale position overwrite snaps.
                    await Task.Delay(400).ConfigureAwait(false);
                    await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
                });
                return true;
            }
            else
            {
                // Active session explicitly reported seek failed (e.g. streaming web session with unsupported seeking)
                if (!string.IsNullOrEmpty(appId)) _seekUnsupportedAppIds.Add(appId);
                CurrentTimeline = CurrentTimeline with { CanSeek = false };
                TimelineChanged?.Invoke(this, CurrentTimeline);
                return false;
            }
        }
        catch
        {
            DetachCurrentSession();
            return false;
        }
    }

    private void StartTimelinePolling()
    {
        StopTimelinePolling();
        var cts = new CancellationTokenSource();
        _pollingCts = cts;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false)) break;
                    var session = _currentSession;
                    if (session != null && _isMonitoring &&
                        CurrentTimeline.Status != MediaPlaybackStatus.Paused &&
                        CurrentTimeline.Status != MediaPlaybackStatus.Stopped &&
                        CurrentTimeline.Status != MediaPlaybackStatus.Closed)
                    {
                        await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore transient exceptions in background polling loop
                }
            }
        }, cts.Token);
    }

    private void StopTimelinePolling()
    {
        try
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
        }
        catch { }
        finally
        {
            _pollingCts = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopTimelinePolling();
        await StopAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
