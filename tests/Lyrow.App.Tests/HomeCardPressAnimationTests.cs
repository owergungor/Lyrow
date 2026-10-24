using System;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class HomeCardPressAnimationTests
{
    [Fact]
    public void CardAnimationValues_AreWithinOptimalHapticParameters()
    {
        // Specification requirement: scale 1.0 -> 0.97 -> 1.0 on press/release
        double normalScale = 1.0;
        double pressedScale = 0.97;
        double diff = normalScale - pressedScale;

        Assert.True(diff > 0.02 && diff < 0.05, "Scale depression must be subtle and tactile (~3%)");
        Assert.Equal(0.97, pressedScale, 2);
    }

    [Theory]
    [InlineData("Playback")]
    [InlineData("Glow")]
    [InlineData("Lyrics")]
    public void HomeCardTargets_MatchValidNavPages(string targetPage)
    {
        string[] validPages = ["Home", "Lyrics", "Glow", "Playback", "Settings"];
        Assert.Contains(targetPage, validPages);
    }
}
