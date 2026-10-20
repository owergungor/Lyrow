using FluentAssertions;
using Lyrow.Audio.Capture;
using Lyrow.Audio.Control;
using Xunit;

namespace Lyrow.Audio.Tests;

public sealed class WasapiHardwareValidationTests
{
    [Fact]
    public async Task WasapiAudioCaptureService_GetAvailableDevices_ShouldEnumerateWithoutExceptions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        await using var wasapiService = new WasapiAudioCaptureService();

        // Act
        var devices = wasapiService.GetAvailableDevices();

        // Assert - On Windows, either endpoints are enumerated or empty list if headless/no audio card
        devices.Should().NotBeNull();
        foreach (var dev in devices)
        {
            dev.Id.Should().NotBeNullOrWhiteSpace();
            dev.Name.Should().NotBeNullOrWhiteSpace();
            dev.SampleRate.Should().BeGreaterThan(0);
            dev.Channels.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task WasapiAudioCaptureService_Lifecycle_ShouldStartAndStopCleanly_WhenEndpointAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var wasapiService = new WasapiAudioCaptureService();
        var devices = wasapiService.GetAvailableDevices();

        if (devices.Count == 0)
        {
            // Headless CI environment without soundcard
            return;
        }

        // Act - Start capture on default device
        await wasapiService.StartAsync();
        wasapiService.IsCapturing.Should().BeTrue();
        wasapiService.CurrentDevice.Should().NotBeNull();

        // Allow loopback capture thread to cycle
        await Task.Delay(250);

        // Act - Stop
        await wasapiService.StopAsync();
        wasapiService.IsCapturing.Should().BeFalse();

        // Clean disposal
        await wasapiService.DisposeAsync();
    }

    [Fact]
    public async Task WindowsAudioVolumeService_HardwareValidation_ControlsRealWindowsEndpointVolume()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var wasapiService = new WasapiAudioCaptureService();
        var devices = wasapiService.GetAvailableDevices();
        if (devices.Count == 0)
        {
            // Headless CI runner without audio hardware
            return;
        }

        using var volumeService = new WindowsAudioVolumeService();
        int initialVol = volumeService.GetVolumePercent();
        string target = volumeService.ActiveTargetName;

        // Verify it can read volume in 0..100 range
        initialVol.Should().BeInRange(0, 100);
        target.Should().NotBeNullOrWhiteSpace();

        // Nudge volume by 1% (or -1% if at 100) to verify real Windows playback volume control
        int targetVol = initialVol >= 100 ? initialVol - 1 : initialVol + 1;
        bool setSuccess = volumeService.SetVolumePercent(targetVol);
        if (!setSuccess)
        {
            // Headless audio driver without master endpoint volume control
            return;
        }

        int updatedVol = volumeService.GetVolumePercent();
        updatedVol.Should().Be(targetVol);

        // Restore original volume
        volumeService.SetVolumePercent(initialVol);
        int restoredVol = volumeService.GetVolumePercent();
        restoredVol.Should().Be(initialVol);
    }
}
