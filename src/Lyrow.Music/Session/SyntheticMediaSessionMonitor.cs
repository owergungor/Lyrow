using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Music.Session;

/// <summary>
/// Synthetic mock media session monitor for automated testing and CI environments.
/// Simulates Spotify/Browser track switches, timeline updates, seek operations, play/pause/next/prev commands, and no-session states.
/// </summary>
public sealed class SyntheticMediaSessionMonitor : IMediaSessionMonitor
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<MediaTrackInfo> _playlist = new();
    private int _playlistIndex = 0;
    private bool _hasActiveSession = true;
    private bool _isDisposed;

    public SyntheticMediaSessionMonitor()
    {
        CurrentTrack = MediaTrackInfo.Empty;
        CurrentTimeline = MediaTimelineInfo.Zero;
    }

    public event EventHandler<MediaTrackInfo>? TrackChanged;
    public event EventHandler<MediaTimelineInfo>? TimelineChanged;
    public event EventHandler<Exception>? MonitorFaulted;

    public bool IsMonitoring { get; private set; }
    public bool HasActiveSession
    {
        get => _hasActiveSession;
        set => _hasActiveSession = value;
    }

    public string? SourceAppId { get; set; } = "Spotify.exe";
    public MediaTrackInfo CurrentTrack { get; private set; }
    public MediaTimelineInfo CurrentTimeline { get; private set; }

    public int PlayCallCount { get; private set; }
    public int PauseCallCount { get; private set; }
    public int TogglePlayPauseCallCount { get; private set; }
    public int SkipNextCallCount { get; private set; }
    public int SkipPreviousCallCount { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        IsMonitoring = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        IsMonitoring = false;
        return Task.CompletedTask;
    }

    public Task<bool> TryPlayAsync()
    {
        if (!_hasActiveSession || _isDisposed) return Task.FromResult(false);
        PlayCallCount++;
        SimulatePlaybackState(MediaPlaybackStatus.Playing, CurrentTimeline.GetInterpolatedPosition());
        return Task.FromResult(true);
    }

    public Task<bool> TryPauseAsync()
    {
        if (!_hasActiveSession || _isDisposed) return Task.FromResult(false);
        PauseCallCount++;
        SimulatePlaybackState(MediaPlaybackStatus.Paused, CurrentTimeline.GetInterpolatedPosition());
        return Task.FromResult(true);
    }

    public Task<bool> TryTogglePlayPauseAsync()
    {
        if (!_hasActiveSession || _isDisposed) return Task.FromResult(false);
        TogglePlayPauseCallCount++;
        var nextStatus = CurrentTimeline.Status == MediaPlaybackStatus.Playing
            ? MediaPlaybackStatus.Paused
            : MediaPlaybackStatus.Playing;
        SimulatePlaybackState(nextStatus, CurrentTimeline.GetInterpolatedPosition());
        return Task.FromResult(true);
    }

    public Task<bool> TrySkipNextAsync()
    {
        if (!_hasActiveSession || _isDisposed) return Task.FromResult(false);
        SkipNextCallCount++;
        if (_playlist.Count > 0)
        {
            _playlistIndex = (_playlistIndex + 1) % _playlist.Count;
            var next = _playlist[_playlistIndex];
            SimulateTrack(next.Title, next.Artist, next.Album, next.Duration, next.AlbumArtUri, next.ThumbnailData);
        }
        else if (!CurrentTrack.IsEmpty)
        {
            SimulateSeek(TimeSpan.Zero);
        }
        return Task.FromResult(true);
    }

    public Task<bool> TrySeekAsync(TimeSpan position)
    {
        if (!_hasActiveSession || _isDisposed) return Task.FromResult(false);
        SimulateSeek(position);
        return Task.FromResult(true);
    }

    public Task<bool> TrySkipPreviousAsync()
    {
        if (!_hasActiveSession || _isDisposed) return Task.FromResult(false);
        SkipPreviousCallCount++;
        if (_playlist.Count > 0)
        {
            _playlistIndex = (_playlistIndex - 1 + _playlist.Count) % _playlist.Count;
            var prev = _playlist[_playlistIndex];
            SimulateTrack(prev.Title, prev.Artist, prev.Album, prev.Duration, prev.AlbumArtUri, prev.ThumbnailData);
        }
        else if (!CurrentTrack.IsEmpty)
        {
            SimulateSeek(TimeSpan.Zero);
        }
        return Task.FromResult(true);
    }

    public void SetPlaylist(IEnumerable<MediaTrackInfo> tracks)
    {
        _playlist.Clear();
        _playlist.AddRange(tracks);
        _playlistIndex = 0;
        if (_playlist.Count > 0)
        {
            var first = _playlist[0];
            SimulateTrack(first.Title, first.Artist, first.Album, first.Duration, first.AlbumArtUri, first.ThumbnailData);
        }
    }

    /// <summary>Simulates no active media session in OS.</summary>
    public void SimulateNoSession()
    {
        _hasActiveSession = false;
        SourceAppId = null;
        CurrentTrack = MediaTrackInfo.Empty;
        CurrentTimeline = MediaTimelineInfo.Zero;
        TrackChanged?.Invoke(this, CurrentTrack);
        TimelineChanged?.Invoke(this, CurrentTimeline);
    }

    /// <summary>Simulates changing the currently playing song in media player.</summary>
    public void SimulateTrack(
        string title,
        string artist,
        string album,
        TimeSpan duration,
        string? albumArtUri = null,
        byte[]? thumbnailData = null)
    {
        _hasActiveSession = true;
        var track = new MediaTrackInfo(title, artist, album, duration, AlbumArtUri: albumArtUri, ThumbnailData: thumbnailData);
        CurrentTrack = track;
        CurrentTimeline = new MediaTimelineInfo(TimeSpan.Zero, duration, MediaPlaybackStatus.Playing, DateTime.UtcNow);

        TrackChanged?.Invoke(this, track);
        TimelineChanged?.Invoke(this, CurrentTimeline);
    }

    /// <summary>Simulates a playback state change (e.g. Play, Pause, Stop).</summary>
    public void SimulatePlaybackState(MediaPlaybackStatus status, TimeSpan? position = null)
    {
        var pos = position ?? CurrentTimeline.GetInterpolatedPosition();
        CurrentTimeline = new MediaTimelineInfo(pos, CurrentTrack.Duration, status, DateTime.UtcNow);
        TimelineChanged?.Invoke(this, CurrentTimeline);
    }

    /// <summary>Simulates user seeking forward or backward in media player.</summary>
    public void SimulateSeek(TimeSpan targetPosition)
    {
        CurrentTimeline = new MediaTimelineInfo(targetPosition, CurrentTrack.Duration, CurrentTimeline.Status, DateTime.UtcNow);
        TimelineChanged?.Invoke(this, CurrentTimeline);
    }

    /// <summary>Simulates an unexpected hardware or session fault.</summary>
    public void SimulateFault(Exception ex)
    {
        MonitorFaulted?.Invoke(this, ex);
    }

    public ValueTask DisposeAsync()
    {
        if (_isDisposed) return ValueTask.CompletedTask;
        _isDisposed = true;
        IsMonitoring = false;
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}
