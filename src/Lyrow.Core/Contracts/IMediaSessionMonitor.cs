using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// Service abstraction for monitoring active media sessions (GSMTC on Windows, mock/synthetic in tests).
/// </summary>
public interface IMediaSessionMonitor : IAsyncDisposable
{
    /// <summary>Fired when current song title, artist, or album changes.</summary>
    event EventHandler<MediaTrackInfo>? TrackChanged;

    /// <summary>Fired when playback timeline position or play/pause state changes.</summary>
    event EventHandler<MediaTimelineInfo>? TimelineChanged;

    /// <summary>Fired when monitor experiences an unexpected hardware/OS error.</summary>
    event EventHandler<Exception>? MonitorFaulted;

    /// <summary>Gets whether media session monitoring is currently active.</summary>
    bool IsMonitoring { get; }

    /// <summary>Gets the most recently observed track metadata.</summary>
    MediaTrackInfo CurrentTrack { get; }

    /// <summary>Gets the most recently observed playback timeline status.</summary>
    MediaTimelineInfo CurrentTimeline { get; }

    /// <summary>Starts listening for media session events.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops listening for media events and releases OS listeners.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
