using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public class MediaControlAndTimelineV20Tests
{
    [Fact]
    public async Task MediaControls_PlayPauseAndToggle_UpdatePlaybackStateImmediately()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        await monitor.StartAsync();
        monitor.SimulateTrack("Midnight City", "M83", "Hurry Up, We're Dreaming", TimeSpan.FromMinutes(4));

        monitor.HasActiveSession.Should().BeTrue();
        monitor.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Playing);

        // Pause
        bool paused = await monitor.TryPauseAsync();
        paused.Should().BeTrue();
        monitor.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Paused);

        // Play
        bool played = await monitor.TryPlayAsync();
        played.Should().BeTrue();
        monitor.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Playing);

        // Toggle -> Paused
        bool toggled1 = await monitor.TryTogglePlayPauseAsync();
        toggled1.Should().BeTrue();
        monitor.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Paused);

        // Toggle -> Playing
        bool toggled2 = await monitor.TryTogglePlayPauseAsync();
        toggled2.Should().BeTrue();
        monitor.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Playing);
    }

    [Fact]
    public async Task MediaControls_NextAndPrevious_ChangeTrackAndResetTimeline()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        await monitor.StartAsync();
        monitor.SetPlaylist(new[]
        {
            new MediaTrackInfo("Track One", "Artist A", "Album A", TimeSpan.FromMinutes(3)),
            new MediaTrackInfo("Track Two", "Artist B", "Album B", TimeSpan.FromMinutes(4))
        });

        string initialTitle = monitor.CurrentTrack.Title;

        // Advance timeline to 45s via SimulateSeek
        monitor.SimulateSeek(TimeSpan.FromSeconds(45));

        // Next Track
        bool nextResult = await monitor.TrySkipNextAsync();
        nextResult.Should().BeTrue();
        monitor.CurrentTrack.Title.Should().Be("Track Two");
        monitor.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);

        // Previous Track
        bool prevResult = await monitor.TrySkipPreviousAsync();
        prevResult.Should().BeTrue();
        monitor.CurrentTrack.Title.Should().Be(initialTitle);
        monitor.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task MediaControls_WhenNoActiveSession_ReturnFalseWithoutThrowing()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        monitor.SimulateNoSession();
        monitor.HasActiveSession.Should().BeFalse();

        (await monitor.TryPlayAsync()).Should().BeFalse();
        (await monitor.TryPauseAsync()).Should().BeFalse();
        (await monitor.TryTogglePlayPauseAsync()).Should().BeFalse();
        (await monitor.TrySkipNextAsync()).Should().BeFalse();
        (await monitor.TrySkipPreviousAsync()).Should().BeFalse();
    }

    [Fact]
    public void MediaTimelineInfo_GetInterpolatedPosition_AdvancesFromZeroWhenPlaying()
    {
        var startUtc = DateTime.UtcNow.AddSeconds(-8);
        var timelineAtZero = new MediaTimelineInfo(
            Position: TimeSpan.Zero,
            Duration: TimeSpan.FromMinutes(3),
            Status: MediaPlaybackStatus.Playing,
            LastUpdatedUtc: startUtc);

        var interpolatedAt8Seconds = timelineAtZero.GetInterpolatedPosition();

        interpolatedAt8Seconds.TotalSeconds.Should().BeApproximately(8.0, 0.25);
    }
}
