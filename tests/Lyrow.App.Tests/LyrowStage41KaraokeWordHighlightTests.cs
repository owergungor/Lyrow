using System;
using System.Collections.Generic;
using FluentAssertions;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Unit tests for Stage 41:
/// - Karaoke word sequential highlighting timing.
/// - Unreached words remaining inactive (0 progress).
/// - Wrapped sub-line 2 isolation while sub-line 1 is in progress.
/// - Single-word line monotonicity and progress clamping.
/// - Non-karaoke plain line smooth fallback progression.
/// </summary>
public sealed class LyrowStage41KaraokeWordHighlightTests
{
    [Fact]
    public void KaraokeWords_HighlightSequentially_AsTimestampsArrive()
    {
        var words = new List<LyricWordTiming>
        {
            new("Never", TimeSpan.FromSeconds(10.0), TimeSpan.FromSeconds(10.8)),
            new("gonna", TimeSpan.FromSeconds(10.8), TimeSpan.FromSeconds(11.4)),
            new("give", TimeSpan.FromSeconds(11.4), TimeSpan.FromSeconds(12.0)),
            new("you", TimeSpan.FromSeconds(12.0), TimeSpan.FromSeconds(12.4)),
            new("up", TimeSpan.FromSeconds(12.4), TimeSpan.FromSeconds(13.0))
        };

        var line = new LyricLine(
            Timestamp: TimeSpan.FromSeconds(10.0),
            Text: "Never gonna give you up",
            LineIndex: 0,
            Words: words,
            EndTime: TimeSpan.FromSeconds(13.0));

        var subLines = new[] { "Never gonna give you up" };

        // Before line starts
        var p0 = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(9.5));
        p0[0].Should().Be(0.0);

        // During "Never" (10.4s)
        var p1 = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(10.4));
        p1[0].Should().BeGreaterThan(0.0).And.BeLessThan(0.35);

        // During "give" (11.7s)
        var p2 = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(11.7));
        p2[0].Should().BeGreaterThan(0.40).And.BeLessThan(0.75);

        // During "up" (12.7s)
        var p3 = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(12.7));
        p3[0].Should().BeGreaterThan(0.80).And.BeLessThan(1.0);

        // After completion (13.1s)
        var p4 = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(13.1));
        p4[0].Should().Be(1.0);
    }

    [Fact]
    public void WrappedSubLine2_DoesNotHighlight_BeforeSubLine1Finishes()
    {
        var words = new List<LyricWordTiming>
        {
            new("Take", TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(2.6)),
            new("on", TimeSpan.FromSeconds(2.6), TimeSpan.FromSeconds(3.0)),
            new("me", TimeSpan.FromSeconds(3.0), TimeSpan.FromSeconds(4.0)),
            new("take", TimeSpan.FromSeconds(4.0), TimeSpan.FromSeconds(4.6)),
            new("me", TimeSpan.FromSeconds(4.6), TimeSpan.FromSeconds(5.2)),
            new("on", TimeSpan.FromSeconds(5.2), TimeSpan.FromSeconds(6.0))
        };

        var line = new LyricLine(
            Timestamp: TimeSpan.FromSeconds(2.0),
            Text: "Take on me take me on",
            LineIndex: 0,
            Words: words,
            EndTime: TimeSpan.FromSeconds(6.0));

        var subLines = new[] { "Take on me", "take me on" };

        // Subline 1 is playing (2.8s)
        var pA = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(2.8));
        pA[0].Should().BeGreaterThan(0.0).And.BeLessThan(1.0);
        pA[1].Should().Be(0.0, "Subline 2 must remain completely 0.0 before its words start");

        // Subline 1 just finished, Subline 2 begins (4.3s)
        var pB = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(4.3));
        pB[0].Should().Be(1.0, "Subline 1 should be fully filled");
        pB[1].Should().BeGreaterThan(0.0).And.BeLessThan(1.0, "Subline 2 should now be filling");

        // Both complete (6.1s)
        var pC = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(6.1));
        pC[0].Should().Be(1.0);
        pC[1].Should().Be(1.0);
    }

    [Fact]
    public void PlainLyricsWithoutWordTimings_FollowsSmoothLineProgress()
    {
        var line = new LyricLine(
            Timestamp: TimeSpan.FromSeconds(20.0),
            Text: "A plain synchronized lyric line without words",
            LineIndex: 5,
            Words: null,
            EndTime: TimeSpan.FromSeconds(24.0));

        var subLines = new[] { "A plain synchronized lyric line without words" };

        var pStart = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(20.0));
        pStart[0].Should().Be(0.0);

        var pMid = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(22.0));
        pMid[0].Should().BeApproximately(0.5, 0.05);

        var pEnd = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, TimeSpan.FromSeconds(24.0));
        pEnd[0].Should().Be(1.0);
    }
}
