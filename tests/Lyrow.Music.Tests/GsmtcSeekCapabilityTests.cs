using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class GsmtcSeekCapabilityTests
{
    [Fact]
    public async Task TrySeekAsync_WhenSeekSupported_ShouldReturnTrueAndAdvancePosition()
    {
        var monitor = new SyntheticMediaSessionMonitor
        {
            CanSeek = true
        };
        await monitor.StartAsync();

        monitor.SimulateTrack("Track 1", "Artist", "Album", TimeSpan.FromMinutes(3));
        monitor.CanSeek.Should().BeTrue();
        monitor.CurrentTimeline.CanSeek.Should().BeTrue();

        bool result = await monitor.TrySeekAsync(TimeSpan.FromSeconds(45));

        result.Should().BeTrue();
        monitor.CurrentTimeline.Position.Should().Be(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public async Task TrySeekAsync_WhenSeekUnsupported_ShouldReturnFalseAndNotChangePosition()
    {
        // Apple Music on Windows reports IsPlaybackPositionEnabled = false
        var monitor = new SyntheticMediaSessionMonitor
        {
            CanSeek = false,
            SourceAppId = "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App"
        };
        await monitor.StartAsync();

        monitor.SimulateTrack("Beni Sen İnandır", "Pinhani", "Kediköy", TimeSpan.FromMinutes(3));
        monitor.CanSeek.Should().BeFalse();
        monitor.CurrentTimeline.CanSeek.Should().BeFalse();

        bool result = await monitor.TrySeekAsync(TimeSpan.FromSeconds(45));

        result.Should().BeFalse();
        monitor.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CapabilityChange_ShouldFireTimelineChangedEvent_WithUpdatedCanSeek()
    {
        var monitor = new SyntheticMediaSessionMonitor { CanSeek = true };
        monitor.SimulateTrack("Song", "Artist", "Album", TimeSpan.FromMinutes(4));

        MediaTimelineInfo? observed = null;
        monitor.TimelineChanged += (_, tl) => observed = tl;

        // Change capability dynamically (e.g. player changed or state updated)
        monitor.CanSeek = false;
        monitor.SimulatePlaybackState(MediaPlaybackStatus.Playing);

        observed.Should().NotBeNull();
        observed!.CanSeek.Should().BeFalse();
    }

    [Fact]
    public void SessionChange_ShouldTransitionCanSeekStateCorrectly()
    {
        var monitor = new SyntheticMediaSessionMonitor();

        // 1. Initially Spotify (seek supported)
        monitor.CanSeek = true;
        monitor.SourceAppId = "Spotify.exe";
        monitor.SimulateTrack("Song 1", "Artist 1", "Album 1", TimeSpan.FromMinutes(3));
        monitor.CurrentTimeline.CanSeek.Should().BeTrue();

        // 2. Switch to Apple Music (seek unsupported)
        monitor.CanSeek = false;
        monitor.SourceAppId = "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App";
        monitor.SimulateTrack("Lucky", "Britney Spears", "Oops", TimeSpan.FromMinutes(3));
        monitor.CurrentTimeline.CanSeek.Should().BeFalse();

        // 3. Switch back to Spotify
        monitor.CanSeek = true;
        monitor.SourceAppId = "Spotify.exe";
        monitor.SimulateTrack("Song 2", "Artist 2", "Album 2", TimeSpan.FromMinutes(3));
        monitor.CurrentTimeline.CanSeek.Should().BeTrue();
    }

    [Fact]
    public async Task TrySeekAsync_WhenNoActiveSession_ShouldReturnFalse()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        monitor.SimulateNoSession();

        bool result = await monitor.TrySeekAsync(TimeSpan.FromSeconds(30));

        result.Should().BeFalse();
    }

    [Fact]
    public void PlaybackTimelineInfo_ShouldPreserveCanSeekFlagAcrossInterpolation()
    {
        var timeline = new MediaTimelineInfo(
            Position: TimeSpan.FromSeconds(10),
            Duration: TimeSpan.FromMinutes(3),
            Status: MediaPlaybackStatus.Playing,
            LastUpdatedUtc: DateTime.UtcNow,
            CanSeek: false);

        timeline.CanSeek.Should().BeFalse();
        timeline.GetInterpolatedPosition().Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(10));
    }
}
