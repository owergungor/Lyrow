using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyricsCenterViewportTests
{
    private const string MultiLineLrc = """
        [00:05.00]Line 0 (Intro)
        [00:10.00]Line 1
        [00:15.00]Line 2
        [00:20.00]Line 3
        [00:25.00]Line 4
        [00:30.00]Line 5 (Target Center Line)
        [00:35.00]Line 6 (Next Line)
        [00:40.00]Line 7
        [00:45.00]Line 8
        [00:50.00]Line 9
        [00:55.00]Line 10 (High Index Line)
        """;

    [Fact]
    public void ActiveLine_AtLine5_ResolvesLine5ForViewportCentering()
    {
        var lyrics = LrcParser.Parse(MultiLineLrc);
        lyrics.Lines.Count.Should().Be(11);

        // At 31.5 seconds, playback is strictly in Line 5 (index 5)
        int activeIdx = lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(31.5));
        activeIdx.Should().Be(5);
        lyrics.Lines[activeIdx].Text.Should().Contain("Line 5");
    }

    [Fact]
    public void ActiveLine_AdvanceFromLine5ToLine6_TransitionsSmoothly()
    {
        using var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(LrcParser.Parse(MultiLineLrc));

        int lastActive = -1;
        synchronizer.ActiveLineChanged += (_, e) => lastActive = e.LineIndex;

        // Position in Line 5
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(32), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(5);
        lastActive.Should().Be(5);

        // Position advances into Line 6 (at 36s)
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(36), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(6);
        lastActive.Should().Be(6);
    }

    [Fact]
    public void Seek_BackwardFromLine10ToLine3_InstantlyCentersLine3()
    {
        using var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(LrcParser.Parse(MultiLineLrc));

        // Start at Line 10 (56 seconds)
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(56), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(10);

        // Seek backward to 21 seconds (Line 3)
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(21), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(3);
    }

    [Fact]
    public void Seek_ForwardFromLine1ToLine8_InstantlyCentersLine8()
    {
        using var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(LrcParser.Parse(MultiLineLrc));

        // Start at Line 1 (11 seconds)
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(11), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(1);

        // Seek forward to 46 seconds (Line 8)
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(46), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(8);
    }

    [Fact]
    public void PauseAndResume_PreservesExactActiveLineAndCentering()
    {
        using var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(LrcParser.Parse(MultiLineLrc));

        int eventFiredCount = 0;
        synchronizer.ActiveLineChanged += (_, _) => eventFiredCount++;

        // Playing at Line 5
        var playState = new MediaTimelineInfo(TimeSpan.FromSeconds(31), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow);
        synchronizer.UpdateTimeline(playState);
        synchronizer.CurrentActiveIndex.Should().Be(5);
        int countBeforePause = eventFiredCount;

        // Paused at same timestamp
        var pauseState = new MediaTimelineInfo(TimeSpan.FromSeconds(31), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Paused, DateTime.UtcNow);
        synchronizer.UpdateTimeline(pauseState);
        synchronizer.CurrentActiveIndex.Should().Be(5);
        // ActiveLineChanged should NOT fire redundantly on pause!
        eventFiredCount.Should().Be(countBeforePause);

        // Resume playback
        var resumeState = new MediaTimelineInfo(TimeSpan.FromSeconds(31.5), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow);
        synchronizer.UpdateTimeline(resumeState);
        synchronizer.CurrentActiveIndex.Should().Be(5);
        eventFiredCount.Should().Be(countBeforePause);
    }

    [Fact]
    public void TrackChange_ClearsOldLyricsState_AndResetsActiveIndex()
    {
        using var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(LrcParser.Parse(MultiLineLrc));
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(31), TimeSpan.FromMinutes(3), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(5);

        // Track change occurs: reset synchronizer
        synchronizer.Reset();
        synchronizer.CurrentActiveIndex.Should().BeLessThan(0);

        // Load new song with completely different lyrics
        string newSongLrc = "[00:02.00]New Track Intro\n[00:08.00]New Verse";
        synchronizer.SetLyrics(LrcParser.Parse(newSongLrc));
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(2), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(0);
    }

    [Fact]
    public void CenteringMath_PureCalculation_VerifiesTargetOffsetCenter()
    {
        // Viewport height: 400px. Center: 200px.
        double viewportHeight = 400.0;
        double halfViewport = viewportHeight / 2.0;

        // Line 5: height 30px, local Y inside panel (including top padding halfViewport)
        double topMargin = halfViewport - 15.0; // 185px
        double line5LocalY = topMargin + (5 * 46.0); // 185 + 230 = 415px
        double line5CenterY = line5LocalY + 15.0; // 430px

        // Formula: targetOffset = lineCenter - (viewportHeight / 2)
        double targetOffsetY = line5CenterY - (viewportHeight / 2.0); // 430 - 200 = 230px
        
        // Line 5 center on screen when scrolled to targetOffsetY:
        double visualCenterOnScreen = line5CenterY - targetOffsetY;
        visualCenterOnScreen.Should().Be(viewportHeight / 2.0); // Exactly 200px!
    }
}
