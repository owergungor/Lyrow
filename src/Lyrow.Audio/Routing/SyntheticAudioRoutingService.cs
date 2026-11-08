using System;
using Lyrow.Core.Contracts;

namespace Lyrow.Audio.Routing;

/// <summary>
/// Synthetic test implementation of <see cref="IAudioRoutingService"/> for deterministic testing.
/// </summary>
public sealed class SyntheticAudioRoutingService : IAudioRoutingService
{
    public bool IsSupported { get; set; } = true;

    public string? CurrentDefaultDeviceId { get; set; }

    public bool ShouldFail { get; set; } = false;

    public bool SetDefaultPlaybackDevice(string deviceId)
    {
        if (ShouldFail || !IsSupported || string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        CurrentDefaultDeviceId = deviceId;
        return true;
    }
}
