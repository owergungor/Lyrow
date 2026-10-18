using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Music.Session;

/// <summary>
/// Synthetic mock media session monitor for automated testing and CI environments.
/// Simulates Spotify/Browser track switches, timeline updates, seek operations, and play/pause states.
/// </summary>
public sealed class SyntheticMediaSessionMonitor : IMediaSessionMonitor
{
    private readonly SemaphoreSlim _lock = new(1, 1);
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
    public MediaTrackInfo CurrentTrack { get; private set; }
    public MediaTimelineInfo CurrentTimeline { get; private set; }

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

    public Task<bool> TryTogglePlayPauseAsync() => Task.FromResult(true);
    public Task<bool> TrySkipNextAsync() => Task.FromResult(true);
    public Task<bool> TrySkipPreviousAsync() => Task.FromResult(true);

    /// <summary>Simulates changing the currently playing song in media player.</summary>
    public void SimulateTrack(string title, string artist, string album, TimeSpan duration, string? albumArtUri = null)
    {
        var track = new MediaTrackInfo(title, artist, album, duration, AlbumArtUri: albumArtUri);
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
