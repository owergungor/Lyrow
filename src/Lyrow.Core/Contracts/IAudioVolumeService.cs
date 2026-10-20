namespace Lyrow.Core.Contracts;

/// <summary>
/// Service abstraction for controlling real Windows playback volume (0% to 100%),
/// active media session volume, mute state, and automatic session reconnection when media players switch.
/// </summary>
public interface IAudioVolumeService : IDisposable
{
    /// <summary>Fired when volume percentage (0-100) or mute state changes.</summary>
    event EventHandler<int>? VolumeChanged;

    /// <summary>Gets the current effective playback volume in range [0, 100].</summary>
    int GetVolumePercent();

    /// <summary>Sets the playback volume in range [0, 100] and applies it to the active media session / master output.</summary>
    bool SetVolumePercent(int percent);

    /// <summary>Gets the playback volume percentage in range [0, 100].</summary>
    int GetPlaybackVolumePercent();

    /// <summary>Sets the playback volume percentage in range [0, 100].</summary>
    bool SetPlaybackVolumePercent(int percent);

    /// <summary>Gets the system master endpoint volume percentage in range [0, 100].</summary>
    int GetSystemVolumePercent();

    /// <summary>Sets the system master endpoint volume percentage in range [0, 100].</summary>
    bool SetSystemVolumePercent(int percent);

    /// <summary>Gets whether the playback session is muted.</summary>
    bool IsPlaybackMuted { get; }

    /// <summary>Gets whether the system master endpoint is muted.</summary>
    bool IsSystemMuted { get; }

    /// <summary>Sets mute state on the playback session.</summary>
    bool SetPlaybackMute(bool muted);

    /// <summary>Sets mute state on the system master endpoint.</summary>
    bool SetSystemMute(bool muted);

    /// <summary>Toggles mute state on the playback session.</summary>
    bool TogglePlaybackMute();

    /// <summary>Toggles mute state on the system master endpoint.</summary>
    bool ToggleSystemMute();

    /// <summary>Gets the current system master endpoint volume in range [0, 100].</summary>
    int MasterVolumePercent { get; }

    /// <summary>Gets the current active media session volume in range [0, 100].</summary>
    int SessionVolumePercent { get; }

    /// <summary>Gets whether the active session or master output is currently muted.</summary>
    bool IsMuted { get; }

    /// <summary>Sets mute state on the active session / master output.</summary>
    bool SetMute(bool muted);

    /// <summary>Toggles mute state and returns the new mute state.</summary>
    bool ToggleMute();

    /// <summary>Gets whether an active per-application media audio session is currently bound.</summary>
    bool HasActiveMediaSession { get; }

    /// <summary>Gets a human-readable label for the currently controlled audio target (e.g., "Spotify", "Chrome", "System Master").</summary>
    string ActiveTargetName { get; }

    /// <summary>Gets a human-readable label for the playback session target.</summary>
    string PlaybackTargetName { get; }

    /// <summary>Gets a human-readable label for the system master endpoint target.</summary>
    string SystemTargetName { get; }

    /// <summary>Updates the preferred media player identifier (e.g., GSMTC SourceAppUserModelId) and reconnects to its audio session.</summary>
    void SetPreferredAppId(string? appUserModelId);

    /// <summary>Forces a refresh of audio endpoints and sessions, reconnecting if the active session or output device changed.</summary>
    bool RefreshAndReconnect();
}
