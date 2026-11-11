using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Core.Timeline;

/// <summary>
/// Authoritative implementation of <see cref="IPlaybackTimelineEngine"/>.
/// Manages centralized playback position, duration, remaining time calculation, and smooth interpolation.
/// Completely isolates consecutive track sessions so that previous track elapsed times or cumulative
/// playlist offsets never leak across automatic song transitions.
/// </summary>
public sealed class PlaybackTimelineEngine : IPlaybackTimelineEngine
{
    private readonly object _lock = new();

    private string _currentTrackId = string.Empty;
    private string _previousTrackId = string.Empty;
    private TimeSpan _previousTrackLastPosition = TimeSpan.Zero;
    private TimeSpan _previousTrackDuration = TimeSpan.Zero;
    private TimeSpan _currentTrackDuration = TimeSpan.Zero;
    private TimeSpan _anchorPosition = TimeSpan.Zero;
    private DateTime _lastAnchorUtc = DateTime.UtcNow;
    private DateTime _trackSwitchUtc = DateTime.MinValue;
    private DateTime _lastSeekUtc = DateTime.MinValue;
    private TimeSpan _lastSeekTarget = TimeSpan.Zero;
    private MediaPlaybackStatus _playbackStatus = MediaPlaybackStatus.Stopped;
    private bool _canSeek;

    public event EventHandler<PlaybackTimelineSnapshot>? TimelineUpdated;

    public string CurrentTrackId
    {
        get { lock (_lock) return _currentTrackId; }
    }

    public TimeSpan CurrentTrackDuration
    {
        get { lock (_lock) return _currentTrackDuration; }
    }

    public TimeSpan CurrentTrackPosition
    {
        get
        {
            lock (_lock)
            {
                return CalculateCurrentPositionLocked(DateTime.UtcNow);
            }
        }
    }

    public TimeSpan CurrentTrackRemaining
    {
        get
        {
            lock (_lock)
            {
                if (_currentTrackDuration <= TimeSpan.Zero)
                {
                    return TimeSpan.Zero;
                }
                var pos = CalculateCurrentPositionLocked(DateTime.UtcNow);
                return _currentTrackDuration > pos ? _currentTrackDuration - pos : TimeSpan.Zero;
            }
        }
    }

    public string CurrentTrackPositionFormatted => FormatElapsed(CurrentTrackPosition);

    public string CurrentTrackRemainingFormatted
    {
        get
        {
            lock (_lock)
            {
                if (_currentTrackDuration <= TimeSpan.Zero)
                {
                    return "--:--";
                }
                var pos = CalculateCurrentPositionLocked(DateTime.UtcNow);
                var rem = _currentTrackDuration > pos ? _currentTrackDuration - pos : TimeSpan.Zero;
                return FormatRemaining(rem);
            }
        }
    }

    public string CurrentTrackDurationFormatted => FormatElapsed(CurrentTrackDuration);

    public double CurrentTrackProgressPercentage
    {
        get
        {
            lock (_lock)
            {
                if (_currentTrackDuration.TotalSeconds <= 0) return 0.0;
                var pos = CalculateCurrentPositionLocked(DateTime.UtcNow);
                return Math.Clamp((pos.TotalSeconds / _currentTrackDuration.TotalSeconds) * 100.0, 0.0, 100.0);
            }
        }
    }

    public MediaPlaybackStatus PlaybackStatus
    {
        get { lock (_lock) return _playbackStatus; }
    }

    public bool CanSeek
    {
        get { lock (_lock) return _canSeek; }
    }

    public static string FormatElapsed(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        var floored = TimeSpan.FromSeconds(Math.Floor(time.TotalSeconds));
        return floored.TotalHours >= 1 ? $"{floored:h\\:mm\\:ss}" : $"{floored:m\\:ss}";
    }

    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "-0:00";
        var rounded = TimeSpan.FromSeconds(Math.Round(remaining.TotalSeconds, MidpointRounding.AwayFromZero));
        return rounded.TotalHours >= 1 ? $"-{rounded:h\\:mm\\:ss}" : $"-{rounded:m\\:ss}";
    }

    public PlaybackTimelineSnapshot GetCurrentSnapshot()
    {
        lock (_lock)
        {
            var nowUtc = DateTime.UtcNow;
            var pos = CalculateCurrentPositionLocked(nowUtc);
            var rem = _currentTrackDuration > pos ? _currentTrackDuration - pos : TimeSpan.Zero;
            return new PlaybackTimelineSnapshot(
                _currentTrackId,
                pos,
                _currentTrackDuration,
                rem,
                _playbackStatus,
                _lastAnchorUtc,
                _canSeek);
        }
    }

    public void HandleTrackChanged(MediaTrackInfo track)
    {
        PlaybackTimelineSnapshot snapshot;
        lock (_lock)
        {
            if (track.IsEmpty)
            {
                ResetInternal();
                snapshot = CreateSnapshotLocked(DateTime.UtcNow);
            }
            else
            {
                string incomingTrackId = $"{track.Artist?.Trim()}_{track.Title?.Trim()}";
                bool isSameTrack = !string.IsNullOrEmpty(_currentTrackId) &&
                                   string.Equals(incomingTrackId, _currentTrackId, StringComparison.OrdinalIgnoreCase);

                if (isSameTrack)
                {
                    // Secondary metadata / thumbnail / duration enrichment for active track:
                    // Preserve current position, anchor, and interpolation state!
                    if (track.Duration > TimeSpan.Zero && _currentTrackDuration <= TimeSpan.Zero)
                    {
                        _currentTrackDuration = track.Duration;
                    }
                    snapshot = CreateSnapshotLocked(DateTime.UtcNow);
                }
                else
                {
                    // Clean track transition: PURGE all previous track timeline state!
                    var nowUtc = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(_currentTrackId))
                    {
                        _previousTrackId = _currentTrackId;
                        _previousTrackLastPosition = CalculateCurrentPositionLocked(nowUtc);
                        _previousTrackDuration = _currentTrackDuration;
                    }
                    else
                    {
                        _previousTrackId = string.Empty;
                        _previousTrackLastPosition = TimeSpan.Zero;
                        _previousTrackDuration = TimeSpan.Zero;
                    }

                    _currentTrackId = incomingTrackId;
                    _currentTrackDuration = track.Duration;
                    _anchorPosition = TimeSpan.Zero;
                    _lastAnchorUtc = nowUtc;
                    _trackSwitchUtc = nowUtc;
                    _lastSeekUtc = DateTime.MinValue;
                    _lastSeekTarget = TimeSpan.Zero;
                    _playbackStatus = MediaPlaybackStatus.Changing;
                    snapshot = CreateSnapshotLocked(nowUtc);
                }
            }
        }

        TimelineUpdated?.Invoke(this, snapshot);
    }

    public void HandleTimelineProperties(
        TimeSpan rawPosition,
        TimeSpan rawDuration,
        MediaPlaybackStatus rawStatus,
        DateTime? reportedUtc = null,
        bool canSeek = true)
    {
        PlaybackTimelineSnapshot snapshot;
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_currentTrackId))
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var elapsedSinceSwitch = _trackSwitchUtc != DateTime.MinValue && nowUtc > _trackSwitchUtc
                ? (nowUtc - _trackSwitchUtc)
                : TimeSpan.Zero;

            // 1. Duration update
            bool isPreviousTrackDurationCarryover = !string.IsNullOrEmpty(_previousTrackId) &&
                                                    elapsedSinceSwitch.TotalSeconds < 3.0 &&
                                                    _previousTrackDuration > TimeSpan.Zero &&
                                                    rawDuration == _previousTrackDuration &&
                                                    _currentTrackDuration > TimeSpan.Zero &&
                                                    _currentTrackDuration != _previousTrackDuration;

            if (rawDuration > TimeSpan.Zero && !isPreviousTrackDurationCarryover)
            {
                if (_currentTrackDuration <= TimeSpan.Zero || _currentTrackDuration != rawDuration)
                {
                    _currentTrackDuration = rawDuration;
                }
            }

            // 2. Playback status
            var oldStatus = _playbackStatus;
            _playbackStatus = rawStatus;
            _canSeek = canSeek;

            // Auto-heal: if reported as Changing or Opened but >1.0s elapsed since track switch, promote to Playing
            if ((_playbackStatus == MediaPlaybackStatus.Changing || _playbackStatus == MediaPlaybackStatus.Opened) &&
                _trackSwitchUtc != DateTime.MinValue &&
                (nowUtc - _trackSwitchUtc).TotalSeconds >= 1.0)
            {
                _playbackStatus = MediaPlaybackStatus.Playing;
            }

            // 3. Reconcile Position
            var pos = rawPosition < TimeSpan.Zero ? TimeSpan.Zero : rawPosition;

            bool hasRecentUserSeek = _lastSeekUtc > _trackSwitchUtc && (nowUtc - _lastSeekUtc).TotalSeconds <= 3.0;

            if (hasRecentUserSeek)
            {
                // User performed an explicit seek within the app: extrapolate from seek target
                var elapsedSinceSeek = nowUtc > _lastSeekUtc ? (nowUtc - _lastSeekUtc) : TimeSpan.Zero;
                _anchorPosition = _playbackStatus == MediaPlaybackStatus.Playing
                    ? ClampToDuration(_lastSeekTarget + elapsedSinceSeek)
                    : _lastSeekTarget;
                _lastAnchorUtc = nowUtc;
            }
            else
            {
                // Detect whether incoming rawPosition is a cumulative playlist offset or stale carryover:
                // Rule A: A track's position can NEVER exceed its duration (+1s tolerance)
                // Rule B: Right after track switch from a previous track, a lingering end-of-track position from the previous track must be rejected
                bool isCumulative = _currentTrackDuration > TimeSpan.Zero && pos > _currentTrackDuration + TimeSpan.FromSeconds(1.0);

                bool isPreviousTrackEndCarryover = !string.IsNullOrEmpty(_previousTrackId) &&
                    elapsedSinceSwitch.TotalSeconds < 3.0 &&
                    _previousTrackDuration > TimeSpan.Zero &&
                    pos >= _previousTrackDuration - TimeSpan.FromSeconds(5.0);

                bool isCumulativeOrStale = isCumulative || isPreviousTrackEndCarryover;

                if (isCumulativeOrStale)
                {
                    // Do not let stale/cumulative rawPosition corrupt the track's timeline.
                    if (isPreviousTrackEndCarryover)
                    {
                        _anchorPosition = TimeSpan.Zero;
                        _lastAnchorUtc = nowUtc;
                    }
                    else if (_playbackStatus == MediaPlaybackStatus.Playing)
                    {
                        _anchorPosition = ClampToDuration(elapsedSinceSwitch);
                        _lastAnchorUtc = nowUtc;
                    }
                }
                else
                {
                    // Valid track-relative position from player (e.g., Spotify reporting 0:01, 0:02...)
                    _anchorPosition = ClampToDuration(pos);
                    _lastAnchorUtc = (reportedUtc.HasValue && reportedUtc.Value.Year > 2020 && reportedUtc.Value <= nowUtc)
                        ? reportedUtc.Value
                        : nowUtc;
                }
            }

            snapshot = CreateSnapshotLocked(nowUtc);
        }

        TimelineUpdated?.Invoke(this, snapshot);
    }

    public void HandleSeek(TimeSpan targetPosition)
    {
        PlaybackTimelineSnapshot snapshot;
        lock (_lock)
        {
            var nowUtc = DateTime.UtcNow;
            var clamped = ClampToDuration(targetPosition);
            _anchorPosition = clamped;
            _lastAnchorUtc = nowUtc;
            _lastSeekUtc = nowUtc;
            _lastSeekTarget = clamped;
            if (_playbackStatus != MediaPlaybackStatus.Stopped && _playbackStatus != MediaPlaybackStatus.Closed)
            {
                _playbackStatus = MediaPlaybackStatus.Playing;
            }
            snapshot = CreateSnapshotLocked(nowUtc);
        }

        TimelineUpdated?.Invoke(this, snapshot);
    }

    public void Reset()
    {
        PlaybackTimelineSnapshot snapshot;
        lock (_lock)
        {
            ResetInternal();
            snapshot = CreateSnapshotLocked(DateTime.UtcNow);
        }

        TimelineUpdated?.Invoke(this, snapshot);
    }

    public MediaTimelineInfo ToMediaTimelineInfo()
    {
        lock (_lock)
        {
            return new MediaTimelineInfo(_anchorPosition, _currentTrackDuration, _playbackStatus, _lastAnchorUtc, _canSeek);
        }
    }

    private void ResetInternal()
    {
        _currentTrackId = string.Empty;
        _previousTrackId = string.Empty;
        _previousTrackLastPosition = TimeSpan.Zero;
        _previousTrackDuration = TimeSpan.Zero;
        _currentTrackDuration = TimeSpan.Zero;
        _anchorPosition = TimeSpan.Zero;
        _lastAnchorUtc = DateTime.UtcNow;
        _trackSwitchUtc = DateTime.MinValue;
        _lastSeekUtc = DateTime.MinValue;
        _lastSeekTarget = TimeSpan.Zero;
        _playbackStatus = MediaPlaybackStatus.Stopped;
        _canSeek = false;
    }

    private TimeSpan CalculateCurrentPositionLocked(DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(_currentTrackId) || _playbackStatus == MediaPlaybackStatus.Stopped || _playbackStatus == MediaPlaybackStatus.Closed)
        {
            return TimeSpan.Zero;
        }

        if (_playbackStatus != MediaPlaybackStatus.Playing)
        {
            return ClampToDuration(_anchorPosition);
        }

        var elapsed = nowUtc - _lastAnchorUtc;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        var estimated = _anchorPosition + elapsed;
        return ClampToDuration(estimated);
    }

    private TimeSpan ClampToDuration(TimeSpan pos)
    {
        if (pos < TimeSpan.Zero) return TimeSpan.Zero;
        if (_currentTrackDuration > TimeSpan.Zero && pos > _currentTrackDuration)
        {
            return _currentTrackDuration;
        }
        return pos;
    }

    private PlaybackTimelineSnapshot CreateSnapshotLocked(DateTime nowUtc)
    {
        var pos = CalculateCurrentPositionLocked(nowUtc);
        var rem = _currentTrackDuration > pos ? _currentTrackDuration - pos : TimeSpan.Zero;
        return new PlaybackTimelineSnapshot(
            _currentTrackId,
            pos,
            _currentTrackDuration,
            rem,
            _playbackStatus,
            _lastAnchorUtc,
            _canSeek);
    }
}
