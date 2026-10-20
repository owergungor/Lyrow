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

    /// <summary>Gets whether an active OS media session is currently attached.</summary>
    bool HasActiveSession { get; }

    /// <summary>Gets the identifier of the active media player application (e.g., Spotify.exe, chrome.exe).</summary>
    string? SourceAppId { get; }

    /// <summary>Gets the most recently observed track metadata.</summary>
    MediaTrackInfo CurrentTrack { get; }

    /// <summary>Gets the most recently observed playback timeline status.</summary>
    MediaTimelineInfo CurrentTimeline { get; }

    /// <summary>Starts listening for media session events.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops listening for media events and releases OS listeners.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends Play command to the active OS media session.</summary>
    Task<bool> TryPlayAsync();

    /// <summary>Sends Pause command to the active OS media session.</summary>
    Task<bool> TryPauseAsync();

    /// <summary>Toggles play/pause on the active OS media session.</summary>
    Task<bool> TryTogglePlayPauseAsync();

    /// <summary>Skips to next track on the active OS media session.</summary>
    Task<bool> TrySkipNextAsync();

    /// <summary>Skips to previous track on the active OS media session.</summary>
    Task<bool> TrySkipPreviousAsync();

    /// <summary>Seeks to specific playback position in active media session.</summary>
    Task<bool> TrySeekAsync(TimeSpan position);
}
