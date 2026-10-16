using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// Service abstraction responsible for system loopback or input audio capture.
/// </summary>
public interface IAudioCaptureService : IAsyncDisposable
{
    /// <summary>
    /// Fired when a fresh block of audio samples is captured and ready for analysis.
    /// </summary>
    event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;

    /// <summary>
    /// Fired when an audio endpoint device is connected, removed, or changed by the OS.
    /// </summary>
    event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

    /// <summary>
    /// Fired when a hardware or driver fault occurs during capture.
    /// </summary>
    event EventHandler<Exception>? CaptureFaulted;

    /// <summary>
    /// Gets whether audio capture is currently active.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Gets the descriptor of the active capture device, or null if inactive.
    /// </summary>
    AudioDeviceDescriptor? CurrentDevice { get; }

    /// <summary>
    /// Enumerates currently available audio endpoints suitable for capture.
    /// </summary>
    IReadOnlyList<AudioDeviceDescriptor> GetAvailableDevices();

    /// <summary>
    /// Starts audio capture on the specified device, or the default render endpoint if null.
    /// </summary>
    Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops audio capture and immediately releases hardware capture handles.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
