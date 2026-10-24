using System;
using Lyrow.Core.Lyrics;
using Xunit;

namespace Lyrow.App.Tests;

public class LyricsCenteringAnimationTests
{
    [Fact]
    public void CalculateTargetCenterOffset_PlacesActiveLineAtVerticalCenter()
    {
        double viewportHeight = 600;
        double lineTopInContent = 400;
        double lineHeight = 50;

        // Target center offset = lineTopInContent + (lineHeight / 2.0) - (viewportHeight / 2.0)
        // = 400 + 25 - 300 = 125
        double offset = LyricsViewportCalculator.CalculateTargetCenterOffset(
            viewportHeight: viewportHeight,
            lineTopInContent: lineTopInContent,
            lineHeight: lineHeight);

        Assert.Equal(125.0, offset);
    }
}
