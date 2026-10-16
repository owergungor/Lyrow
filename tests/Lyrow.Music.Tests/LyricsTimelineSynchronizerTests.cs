using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class LyricsTimelineSynchronizerTests : IDisposable
{
    private readonly LyricsTimelineSynchronizer _synchronizer;

    public LyricsTimelineSynchronizerTests()
    {
        _synchronizer = new LyricsTimelineSynchronizer();
        string rawLrc = """
            [00:05.00]Line 0
            [00:10.00]Line 1
            [00:20.00]Line 2
            """;
        _synchronizer.SetLyrics(LrcParser.Parse(rawLrc));
    }

    [Fact]
    public void UpdateTimeline_ShouldTriggerActiveLineChanged_WhenPositionAdvances()
    {
        int lastFiredIndex = _synchronizer.CurrentActiveIndex;
        _synchronizer.ActiveLineChanged += (_, e) =>
        {
            lastFiredIndex = e.LineIndex;
        };

        // At 2 seconds (before first line at 5s)
        _synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        lastFiredIndex.Should().Be(-1);

        // Advance to 6 seconds -> should be Line 0
        _synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(6), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        lastFiredIndex.Should().Be(0);

        // Advance to 12 seconds -> should be Line 1
        _synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(12), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        lastFiredIndex.Should().Be(1);
    }

    [Fact]
    public void UpdateTimeline_ShouldRespondImmediately_ToSeekOperation()
    {
        int lastFiredIndex = _synchronizer.CurrentActiveIndex;
        _synchronizer.ActiveLineChanged += (_, e) =>
        {
            lastFiredIndex = e.LineIndex;
        };

        // Jump to 25s (Line 2)
        _synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(25), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        lastFiredIndex.Should().Be(2);

        // Seek back to 7s (Line 0)
        _synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(7), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        lastFiredIndex.Should().Be(0);
    }

    [Fact]
    public void SyncOffsetMs_ShouldAdjustLineTimingCorrectly()
    {
        int lastFiredIndex = _synchronizer.CurrentActiveIndex;
        _synchronizer.ActiveLineChanged += (_, e) =>
        {
            lastFiredIndex = e.LineIndex;
        };

        // At 4.5 seconds with 0ms offset -> still -1 (line 0 is at 5.0s)
        _synchronizer.SyncOffsetMs = 0;
        _synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(4.5), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Paused, DateTime.UtcNow));
        lastFiredIndex.Should().Be(-1);

        // Apply +1000ms offset (simulates song being slightly ahead of lyrics)
        _synchronizer.SyncOffsetMs = 1000;
        _synchronizer.EvaluatePosition(TimeSpan.FromSeconds(4.5)); // effective 5.5s -> Line 0
        lastFiredIndex.Should().Be(0);
    }

    public void Dispose()
    {
        _synchronizer.Dispose();
    }
}
