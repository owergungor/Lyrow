using System;
using Lyrow.Core.Models;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyricsDurationResetTests
{
    [Fact]
    public void TrackSwitch_ResetsTimelinePositionToZero_AndUpdatesDuration()
    {
        var monitor = new SyntheticMediaSessionMonitor();

        // Track A (3 min, 150 sec progress)
        monitor.SimulateTrack("Track A", "Artist A", "Album A", TimeSpan.FromMinutes(3));
        monitor.SimulatePlaybackState(MediaPlaybackStatus.Playing, TimeSpan.FromSeconds(150));

        Assert.Equal(TimeSpan.FromSeconds(150), monitor.CurrentTimeline.Position);
        Assert.Equal(TimeSpan.FromMinutes(3), monitor.CurrentTimeline.Duration);

        // Switch to Track B (5 min)
        monitor.SimulateTrack("Track B", "Artist B", "Album B", TimeSpan.FromMinutes(5));

        // Position MUST reset to 0, duration MUST be Track B's duration (5 min), NOT accumulated or carried over from Track A
        Assert.Equal(TimeSpan.Zero, monitor.CurrentTimeline.Position);
        Assert.Equal(TimeSpan.FromMinutes(5), monitor.CurrentTimeline.Duration);
    }
}
