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
}
