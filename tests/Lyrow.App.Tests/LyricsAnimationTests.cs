using System.Collections.Generic;
using Lyrow.Core.Lyrics;
using Xunit;

namespace Lyrow.App.Tests;

public class LyricsAnimationTests
{
    [Fact]
    public void ResolveLineMotionState_CalculatesCorrectStateTransitions()
    {
        // When activeIndex is 5:
        // line 5 is Active
        Assert.Equal(LyricLineMotionState.Active, LyricsViewportCalculator.ResolveLineMotionState(5, 5));

        // line 6 is ApproachingActive (imminent next line)
        Assert.Equal(LyricLineMotionState.ApproachingActive, LyricsViewportCalculator.ResolveLineMotionState(6, 5));

        // line 7 is Idle (upcoming)
        Assert.Equal(LyricLineMotionState.Idle, LyricsViewportCalculator.ResolveLineMotionState(7, 5));

        // lines 0..4 are Passed
        Assert.Equal(LyricLineMotionState.Passed, LyricsViewportCalculator.ResolveLineMotionState(4, 5));
        Assert.Equal(LyricLineMotionState.Passed, LyricsViewportCalculator.ResolveLineMotionState(0, 5));
    }

    [Fact]
    public void GetLineVisualTarget_AssignsCorrectMotionStateAndOpacity()
    {
        var targetActive = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 3, activeIndex: 3);
        Assert.Equal(LyricLineMotionState.Active, targetActive.State);
        Assert.Equal(1.0, targetActive.TargetOpacity);

        var targetApproaching = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 4, activeIndex: 3);
        Assert.Equal(LyricLineMotionState.ApproachingActive, targetApproaching.State);
        Assert.True(targetApproaching.TargetOpacity > 0.40);

        var targetPassed = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 2, activeIndex: 3);
        Assert.Equal(LyricLineMotionState.Passed, targetPassed.State);
    }
}
