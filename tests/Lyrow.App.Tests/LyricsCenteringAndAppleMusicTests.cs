using FluentAssertions;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

public class LyricsCenteringAndAppleMusicTests
{
    [Theory]
    [InlineData(600.0, 100.0, 40.0, 0.0)]     // (100 + 20) - 300 = -180 -> clamped to 0
    [InlineData(600.0, 500.0, 40.0, 220.0)]   // (500 + 20) - 300 = 220
    [InlineData(700.0, 800.0, 50.0, 475.0)]   // (800 + 25) - 350 = 475
    public void CenteringMath_CalculatesExactViewportCenterOffset(
        double viewportHeight,
        double lineTop,
        double lineHeight,
        double expectedOffsetY)
    {
        double lineCenter = lineTop + (lineHeight / 2.0);
        double targetOffsetY = Math.Max(0.0, lineCenter - (viewportHeight / 2.0));

        targetOffsetY.Should().Be(expectedOffsetY);
    }

    [Fact]
    public void LyricsSynchronizer_ActiveLineTransition_AdvancesWithTimeline()
    {
        var lyrics = new SyncedLyrics(
            title: "Test Track",
            artist: "Test Artist",
            isSynced: true,
            lines: new List<LyricLine>
            {
                new(TimeSpan.FromSeconds(5), "First line of song", 0),
                new(TimeSpan.FromSeconds(10), "Second line of song", 1),
                new(TimeSpan.FromSeconds(15), "Third line of song", 2)
            });

        var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(lyrics);

        // At 2s: Before first line
        synchronizer.UpdateTimeline(CreateTimeline(2));
        synchronizer.CurrentActiveIndex.Should().Be(-1);

        // At 6s: First line active
        synchronizer.UpdateTimeline(CreateTimeline(6));
        synchronizer.CurrentActiveIndex.Should().Be(0);

        // At 12s: Second line active
        synchronizer.UpdateTimeline(CreateTimeline(12));
        synchronizer.CurrentActiveIndex.Should().Be(1);

        // At 20s: Third line active
        synchronizer.UpdateTimeline(CreateTimeline(20));
        synchronizer.CurrentActiveIndex.Should().Be(2);
    }

    [Fact]
    public void LyricsSynchronizer_SeekOperation_ImmediatelyJumpsToCorrectLine()
    {
        var lyrics = new SyncedLyrics(
            title: "Test Track",
            artist: "Test Artist",
            isSynced: true,
            lines: new List<LyricLine>
            {
                new(TimeSpan.FromSeconds(10), "Line 1", 0),
                new(TimeSpan.FromSeconds(20), "Line 2", 1),
                new(TimeSpan.FromSeconds(30), "Line 3", 2),
                new(TimeSpan.FromSeconds(40), "Line 4", 3)
            });

        var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(lyrics);

        // User seeks directly to 35 seconds
        synchronizer.UpdateTimeline(CreateTimeline(35));
        synchronizer.CurrentActiveIndex.Should().Be(2);

        // User seeks backward to 12 seconds
        synchronizer.UpdateTimeline(CreateTimeline(12));
        synchronizer.CurrentActiveIndex.Should().Be(0);
    }

    [Fact]
    public void LyricsSynchronizer_PauseAndResume_MaintainsStableActiveLine()
    {
        var lyrics = new SyncedLyrics(
            title: "Test Track",
            artist: "Test Artist",
            isSynced: true,
            lines: new List<LyricLine>
            {
                new(TimeSpan.FromSeconds(10), "Line 1", 0),
                new(TimeSpan.FromSeconds(20), "Line 2", 1)
            });

        var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(lyrics);

        // Playing at 15s
        synchronizer.UpdateTimeline(CreateTimeline(15, MediaPlaybackStatus.Playing));
        synchronizer.CurrentActiveIndex.Should().Be(0);

        // Paused at 15s
        synchronizer.UpdateTimeline(CreateTimeline(15, MediaPlaybackStatus.Paused));
        synchronizer.CurrentActiveIndex.Should().Be(0);

        // Resumes and reaches 22s
        synchronizer.UpdateTimeline(CreateTimeline(22, MediaPlaybackStatus.Playing));
        synchronizer.CurrentActiveIndex.Should().Be(1);
    }

    private static MediaTimelineInfo CreateTimeline(double seconds, MediaPlaybackStatus status = MediaPlaybackStatus.Playing)
    {
        return new MediaTimelineInfo(
            Position: TimeSpan.FromSeconds(seconds),
            Duration: TimeSpan.FromMinutes(4),
            Status: status,
            LastUpdatedUtc: DateTime.UtcNow);
    }
}
