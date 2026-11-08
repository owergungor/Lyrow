using System;
using System.Runtime.InteropServices;
using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// Service abstraction for controlling and changing the real Windows audio output endpoint.
/// </summary>
public interface IAudioRoutingService
{
    /// <summary>
    /// Gets whether audio routing change is supported on the current operating system.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Sets the real Windows default audio playback endpoint.
    /// Applies to Console, Multimedia, and Communications roles.
    /// </summary>
    /// <param name="deviceId">The MMDevice endpoint ID.</param>
    /// <returns>True if the device was successfully set as the active endpoint; otherwise false.</returns>
    bool SetDefaultPlaybackDevice(string deviceId);
}
