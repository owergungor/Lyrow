using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// Authoritative snapshot of the centralized playback timeline.
/// </summary>
public sealed record PlaybackTimelineSnapshot(
    string TrackId,
    TimeSpan Position,
    TimeSpan Duration,
    TimeSpan Remaining,
    MediaPlaybackStatus Status,
    DateTime LastUpdatedUtc,
    bool CanSeek);

/// <summary>
/// Central authoritative engine responsible for tracking, interpolating, and formatting
/// the current track's playback elapsed time and remaining time.
/// Prevents cross-track duration leakage, cumulative session offset errors, and timeline freezes.
/// </summary>
public interface IPlaybackTimelineEngine
{
    /// <summary>Canonical identifier of the active track (e.g. "Artist_Title"), or empty if no track is active.</summary>
    string CurrentTrackId { get; }

    /// <summary>Total duration of the current track, or TimeSpan.Zero if unknown.</summary>
    TimeSpan CurrentTrackDuration { get; }

    /// <summary>Current smoothly interpolated playback position of the current track, strictly bounded to [0, Duration].</summary>
    TimeSpan CurrentTrackPosition { get; }

    /// <summary>Current remaining playback time of the current track (Duration - Position, or 0 if position >= duration).</summary>
    TimeSpan CurrentTrackRemaining { get; }

    /// <summary>Formatted elapsed time of the current track (e.g., "1:01" or "0:00").</summary>
    string CurrentTrackPositionFormatted { get; }

    /// <summary>Formatted remaining time of the current track (e.g., "-2:10" or "-0:00").</summary>
    string CurrentTrackRemainingFormatted { get; }

    /// <summary>Formatted total duration of the current track (e.g., "3:11" or "0:00").</summary>
    string CurrentTrackDurationFormatted { get; }

    /// <summary>Progress percentage of the current track (0.0 to 100.0).</summary>
    double CurrentTrackProgressPercentage { get; }

    /// <summary>Current playback status (Playing, Paused, Changing, Stopped, etc.).</summary>
    MediaPlaybackStatus PlaybackStatus { get; }

    /// <summary>Whether scrubbing/seeking is supported for the current media session.</summary>
    bool CanSeek { get; }

    /// <summary>Fired whenever the authoritative timeline state is updated.</summary>
    event EventHandler<PlaybackTimelineSnapshot>? TimelineUpdated;

    /// <summary>Gets the current authoritative timeline snapshot.</summary>
    PlaybackTimelineSnapshot GetCurrentSnapshot();

    /// <summary>Processes a track change or track metadata update.</summary>
    void HandleTrackChanged(MediaTrackInfo track);

    /// <summary>Processes raw timeline properties reported by the underlying media session.</summary>
    void HandleTimelineProperties(
        TimeSpan rawPosition,
        TimeSpan rawDuration,
        MediaPlaybackStatus rawStatus,
        DateTime? reportedUtc = null,
        bool canSeek = true);

    /// <summary>Records an intentional seek operation to a target position within the current track.</summary>
    void HandleSeek(TimeSpan targetPosition);

    /// <summary>Resets the timeline engine to an empty, stopped state.</summary>
    void Reset();

    /// <summary>Converts the current state to a legacy MediaTimelineInfo record.</summary>
    MediaTimelineInfo ToMediaTimelineInfo();
}
