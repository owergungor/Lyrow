using System;
using System.Collections.Generic;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.Core.Tests;

public class LyricsAutoScrollAndClickSeekTests
{
    [Fact]
    public void ClickToSeek_MapsDirectlyToLyricLineTimestamp()
    {
        var lines = new List<LyricLine>
        {
            new(TimeSpan.FromSeconds(12), "Line A", 0),
            new(TimeSpan.FromSeconds(36), "Line B", 1),
            new(TimeSpan.FromSeconds(78), "Line C", 2)
        };

        var lyrics = new SyncedLyrics("Test Song", "Test Artist", true, lines);

        // User clicks on Line B
        var clickedLine = lyrics.Lines[1];
        Assert.Equal("Line B", clickedLine.Text);
        Assert.Equal(TimeSpan.FromSeconds(36), clickedLine.Timestamp);

        // Verify seek target matches Line B's exact start time
        var seekTarget = clickedLine.Timestamp;
        Assert.Equal(TimeSpan.FromSeconds(36), seekTarget);

        // Verify index evaluation at that exact timestamp resolves to Line 1 (Line B)
        int resolvedIndex = lyrics.FindActiveLineIndex(seekTarget);
        Assert.Equal(1, resolvedIndex);
    }

    [Fact]
    public void AutoScroll_ViewportCenterCalculation_PlacesActiveLineAtCenter()
    {
        double viewportH = 600.0;
        double currentOffset = 100.0;
        double lineTop = 450.0;
        double lineHeight = 40.0;

        // Line center in viewport = 450 + 20 = 470
        // Viewport center = 300
        // deltaY = 470 - 300 = +170
        // targetOffset = 100 + 170 = 270
        double target = LyricsViewportCalculator.CalculateTargetCenterOffsetFromViewportPoint(
            currentVerticalOffset: currentOffset,
            viewportHeight: viewportH,
            lineTopInViewport: lineTop,
            lineHeight: lineHeight,
            maxScrollableHeight: 2000.0);

        Assert.Equal(270.0, target, 1);
    }

    [Fact]
    public void AutoScroll_SmoothEasing_ConvergesWithoutOscillation()
    {
        double current = 0.0;
        double target = 500.0;

        for (int frame = 0; frame < 60; frame++)
        {
            current = LyricsViewportCalculator.InterpolateScrollOffset(
                current,
                target,
                smoothingFactor: 0.18,
                snapEpsilon: 0.30);
        }

        Assert.Equal(target, current, 1);
    }

    [Fact]
    public void FindActiveLineIndex_MatchesPlaybackTimeAccurately()
    {
        var lines = new List<LyricLine>
        {
            new(TimeSpan.FromSeconds(10), "Line 1", 0),
            new(TimeSpan.FromSeconds(20), "Line 2", 1),
            new(TimeSpan.FromSeconds(30), "Line 3", 2)
        };
        var lyrics = new SyncedLyrics("Song", "Artist", true, lines);

        Assert.Equal(-1, lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(15)));
        Assert.Equal(1, lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(20)));
        Assert.Equal(1, lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(29.9)));
        Assert.Equal(2, lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(30)));
        Assert.Equal(2, lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(100)));
    }
}
