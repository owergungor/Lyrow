using FluentAssertions;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class GsmtcHardwareValidationTests
{
    [Fact]
    public async Task WindowsGsmtcMediaSessionMonitor_Lifecycle_ShouldStartAndStopCleanly_OnWindows()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return;
        }

        var monitor = new WindowsGsmtcMediaSessionMonitor();

        // Act - Start monitoring
        await monitor.StartAsync();
        monitor.IsMonitoring.Should().BeTrue();

        // Inspect current state (may be empty if no media players are running, which is completely valid)
        monitor.CurrentTrack.Should().NotBeNull();
        monitor.CurrentTimeline.Should().NotBeNull();

        // Act - Stop
        await monitor.StopAsync();
        monitor.IsMonitoring.Should().BeFalse();
        monitor.CurrentTrack.Should().BeSameAs(Lyrow.Core.Models.MediaTrackInfo.Empty);

        // Act - Dispose
        await monitor.DisposeAsync();
    }

    [Fact]
    public async Task WindowsGsmtcMediaSessionMonitor_TransportControls_ExecuteGracefullyWithoutThrowing()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return;
        }

        var monitor = new WindowsGsmtcMediaSessionMonitor();
        await monitor.StartAsync();

        // When no media player is playing, commands return false safely without throwing.
        // When a media player is active, commands return true/false based on GSMTC session state.
        bool playResult = await monitor.TryPlayAsync();
        bool pauseResult = await monitor.TryPauseAsync();
        bool toggleResult = await monitor.TryTogglePlayPauseAsync();
        bool nextResult = await monitor.TrySkipNextAsync();
        bool prevResult = await monitor.TrySkipPreviousAsync();

        if (monitor.HasActiveSession)
        {
            // With active session, transport commands interact with real GSMTC session
            monitor.CurrentTrack.Should().NotBeNull();
        }
        else
        {
            playResult.Should().BeFalse();
            pauseResult.Should().BeFalse();
            toggleResult.Should().BeFalse();
            nextResult.Should().BeFalse();
            prevResult.Should().BeFalse();
        }

        await monitor.StopAsync();
        await monitor.DisposeAsync();
    }
}
