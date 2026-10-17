using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class LyricsRegressionAndSyncTests
{
    private const string FiveLineSampleLrc = """
        [00:10.00]Line 1 (index 0)
        [00:15.00]Line 2 (index 1)
        [00:20.00]Line 3 (index 2)
        [00:25.00]Line 4 (index 3)
        [00:30.00]Line 5 (index 4)
        [00:40.00]Line 6 (index 5)
        """;

    [Fact]
    public void PlaybackLine5_RegressionScenario_ShouldAccuratelyActivateLine5()
    {
        var lyrics = LrcParser.Parse(FiveLineSampleLrc);
        lyrics.Lines.Count.Should().Be(6);

        // Playback at 00:29.90 -> should be Line 4 (index 3)
        lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(29.9)).Should().Be(3);

        // Playback at exact 00:30.00 -> should be Line 5 (index 4)
        lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(30.0)).Should().Be(4);

        // Playback at 00:30.20 -> should be Line 5 (index 4)
        lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(30.2)).Should().Be(4);

        // Playback at 00:39.99 -> still Line 5 (index 4)
        lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(39.99)).Should().Be(4);

        // Playback at 00:40.00 -> Line 6 (index 5)
        lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(40.0)).Should().Be(5);
    }

    [Fact]
    public void ExactTimestamp_BeforeAndAfter_ShouldResolveBoundaryCorrectly()
    {
        var lyrics = LrcParser.Parse("[00:05.00]Intro line\n[00:10.00]Verse 1");

        // Just before 5.0s -> -1 (no lyric yet)
        lyrics.FindActiveLineIndex(TimeSpan.FromMilliseconds(4990)).Should().Be(-1);

        // Exact 5.0s -> index 0
        lyrics.FindActiveLineIndex(TimeSpan.FromMilliseconds(5000)).Should().Be(0);

        // Just after 5.0s -> index 0
        lyrics.FindActiveLineIndex(TimeSpan.FromMilliseconds(5010)).Should().Be(0);

        // Just before 10.0s -> still index 0
        lyrics.FindActiveLineIndex(TimeSpan.FromMilliseconds(9990)).Should().Be(0);

        // Exact 10.0s -> index 1
        lyrics.FindActiveLineIndex(TimeSpan.FromMilliseconds(10000)).Should().Be(1);
    }

    [Fact]
    public void FirstLine_And_LastLine_BoundaryConditions()
    {
        var lyrics = LrcParser.Parse("[00:01.00]First\n[00:05.00]Last");

        // Before first line
        lyrics.FindActiveLineIndex(TimeSpan.Zero).Should().Be(-1);

        // At first line
        lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(1)).Should().Be(0);

        // Well past last line (e.g. 10 minutes in)
        lyrics.FindActiveLineIndex(TimeSpan.FromMinutes(10)).Should().Be(1);
    }

    [Fact]
    public void NegativeAndPositiveOffset_ShouldApplyCorrectDirection()
    {
        using var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(LrcParser.Parse(FiveLineSampleLrc));

        // Position: 00:14.50 (normal: index 0)
        // With positive offset (+1000ms): effective 15.50s -> index 1
        synchronizer.SyncOffsetMs = 1000;
        synchronizer.EvaluatePosition(TimeSpan.FromSeconds(14.5));
        synchronizer.CurrentActiveIndex.Should().Be(1);

        // With negative offset (-1000ms): position at 15.50s effective 14.50s -> index 0
        synchronizer.SyncOffsetMs = -1000;
        synchronizer.EvaluatePosition(TimeSpan.FromSeconds(15.5));
        synchronizer.CurrentActiveIndex.Should().Be(0);
    }

    [Fact]
    public void DuplicateTimestamps_ShouldGracefullyResolveToCandidate()
    {
        string dupLrc = """
            [00:05.00]Chorus line 1
            [00:05.00]Chorus line 2
            [00:10.00]Next verse
            """;
        var lyrics = LrcParser.Parse(dupLrc);
        lyrics.Lines.Count.Should().Be(3);

        // Binary search with duplicate timestamps should select a valid line
        int idx = lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(5.5));
        idx.Should().BeInRange(0, 1);
    }

    [Fact]
    public void EmptyAndMalformedLyrics_ShouldNeverThrow()
    {
        var empty = LrcParser.Parse("");
        empty.FindActiveLineIndex(TimeSpan.FromSeconds(10)).Should().Be(-1);

        var malformed = LrcParser.Parse("Not an LRC line\n[invalid:timestamp] broken\n[9999] bad format");
        malformed.FindActiveLineIndex(TimeSpan.FromSeconds(10)).Should().Be(-1);
    }

    [Fact]
    public void SeekOperations_And_PauseResume_StateTransitions()
    {
        using var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(LrcParser.Parse(FiveLineSampleLrc));

        // Playing at 12s -> Line 1 (index 0, timestamp 10s..15s)
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(12), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(0);

        // Seek forward to 32s -> Line 5 (index 4, timestamp 30s..40s)
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(32), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(4);

        // Pause at 32s -> still Line 5 (index 4)
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(32), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Paused, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(4);

        // Seek backward to 16s while paused -> Line 2 (index 1, timestamp 15s..20s)
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(16), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Paused, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(1);
    }

    [Fact]
    public void TrackChange_ShouldCleanlyResetState()
    {
        using var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(LrcParser.Parse(FiveLineSampleLrc));
        synchronizer.EvaluatePosition(TimeSpan.FromSeconds(32));
        synchronizer.CurrentActiveIndex.Should().Be(4);

        // Track changes -> reset synchronizer
        synchronizer.Reset();
        synchronizer.CurrentActiveIndex.Should().Be(-2);
        synchronizer.CurrentLyrics.Lines.Should().BeEmpty();
    }
}
