using System;
using System.Collections.Generic;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.Core.Tests;

public class LyricsKaraokeAndFillEngineTests
{
    [Fact]
    public void CalculateLineProgress_BeforeTimestamp_ReturnsZero()
    {
        var lineTime = TimeSpan.FromSeconds(10);
        var curTime = TimeSpan.FromSeconds(5);
        double progress = LyricsFillProgressCalculator.CalculateLineProgress(curTime, lineTime, TimeSpan.FromSeconds(15));
        Assert.Equal(0.0, progress);
    }

    [Fact]
    public void CalculateLineProgress_AtExactEnd_ReturnsOne()
    {
        var lineTime = TimeSpan.FromSeconds(10);
        var curTime = TimeSpan.FromSeconds(15);
        double progress = LyricsFillProgressCalculator.CalculateLineProgress(curTime, lineTime, TimeSpan.FromSeconds(15));
        Assert.Equal(1.0, progress);
    }

    [Fact]
    public void CalculateLineProgress_PastEnd_ClampsToOne()
    {
        var lineTime = TimeSpan.FromSeconds(10);
        var curTime = TimeSpan.FromSeconds(20);
        double progress = LyricsFillProgressCalculator.CalculateLineProgress(curTime, lineTime, TimeSpan.FromSeconds(15));
        Assert.Equal(1.0, progress);
    }

    [Fact]
    public void CalculateLineProgress_Midway_CalculatesProportionalValue()
    {
        var lineTime = TimeSpan.FromSeconds(10);
        var curTime = TimeSpan.FromSeconds(12.5);
        var nextTime = TimeSpan.FromSeconds(15);
        double progress = LyricsFillProgressCalculator.CalculateLineProgress(curTime, lineTime, nextTime);
        Assert.Equal(0.5, progress, 2);
    }

    [Fact]
    public void CalculateLineProgress_FallbackLineDuration_UsedWhenNextTimestampNull()
    {
        var lineTime = TimeSpan.FromSeconds(10);
        var curTime = TimeSpan.FromSeconds(11.75); // 1.75s into 3.5s fallback
        double progress = LyricsFillProgressCalculator.CalculateLineProgress(curTime, lineTime, null);
        Assert.Equal(0.5, progress, 2);
    }

    [Fact]
    public void CalculateCharacterProgress_DistributesSmoothly()
    {
        string text = "Hello World"; // 11 chars
        double lineProgress = 0.5;
        double charProgress = LyricsFillProgressCalculator.CalculateCharacterProgress(lineProgress, text.Length);
        Assert.Equal(0.5, charProgress, 2);
    }

    [Fact]
    public void CalculateWordTimingProgress_RespectsExactWordTimestamps()
    {
        var words = new List<LyricWordTiming>
        {
            new("Lyrow", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12)),
            new("Music", TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(14))
        };

        var pos1 = TimeSpan.FromSeconds(11); // halfway through word 0
        double prog1 = LyricsFillProgressCalculator.CalculateWordTimingProgress(pos1, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14), words);
        Assert.Equal(0.25, prog1, 2);

        var pos2 = TimeSpan.FromSeconds(13); // halfway through word 1
        double prog2 = LyricsFillProgressCalculator.CalculateWordTimingProgress(pos2, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14), words);
        Assert.Equal(0.75, prog2, 2);
    }

    [Fact]
    public void LyricsPlaybackSynchronization_SeekJumpsInstantly()
    {
        var lineTime = TimeSpan.FromSeconds(10);
        var nextTime = TimeSpan.FromSeconds(20);

        // At 12s -> 20%
        double p1 = LyricsFillProgressCalculator.CalculateLineProgress(TimeSpan.FromSeconds(12), lineTime, nextTime);
        Assert.Equal(0.2, p1, 2);

        // Seek forward to 18s -> jumps to 80%
        double p2 = LyricsFillProgressCalculator.CalculateLineProgress(TimeSpan.FromSeconds(18), lineTime, nextTime);
        Assert.Equal(0.8, p2, 2);

        // Seek backward to 11s -> jumps down to 10%
        double p3 = LyricsFillProgressCalculator.CalculateLineProgress(TimeSpan.FromSeconds(11), lineTime, nextTime);
        Assert.Equal(0.1, p3, 2);
    }

    [Fact]
    public void LyricsPause_RetainsSameProgressOnStaticPosition()
    {
        var lineTime = TimeSpan.FromSeconds(10);
        var nextTime = TimeSpan.FromSeconds(20);
        var pausedPos = TimeSpan.FromSeconds(14);

        double p1 = LyricsFillProgressCalculator.CalculateLineProgress(pausedPos, lineTime, nextTime);
        double p2 = LyricsFillProgressCalculator.CalculateLineProgress(pausedPos, lineTime, nextTime);

        Assert.Equal(p1, p2);
        Assert.Equal(0.4, p1, 2);
    }
}
