using System.Diagnostics;
using Lyrow.Core.Lyrics;
using Xunit;

namespace Lyrow.Core.Tests;

public class LyrowV50FlowGlowTests
{
    private static LyricLineTiming CreateSyllableLine()
    {
        var syls = new[]
        {
            new LyricSyllableTiming("May", TimeSpan.FromSeconds(10.0), TimeSpan.FromSeconds(10.6), 0, 0, false),
            new LyricSyllableTiming("be", TimeSpan.FromSeconds(10.6), TimeSpan.FromSeconds(11.2), 1, 0, true),
            new LyricSyllableTiming("to", TimeSpan.FromSeconds(11.2), TimeSpan.FromSeconds(11.6), 2, 1, false),
            new LyricSyllableTiming("night", TimeSpan.FromSeconds(11.6), TimeSpan.FromSeconds(12.4), 3, 1, true)
        };
        var words = new[]
        {
            new LyricWordSegment("Maybe", TimeSpan.FromSeconds(10.0), TimeSpan.FromSeconds(11.2), 0, new[] { syls[0], syls[1] }),
            new LyricWordSegment("tonight", TimeSpan.FromSeconds(11.2), TimeSpan.FromSeconds(12.4), 1, new[] { syls[2], syls[3] })
        };
        return new LyricLineTiming(0, "Maybe tonight", TimeSpan.FromSeconds(10.0), TimeSpan.FromSeconds(12.4), words, syls);
    }

    private static LyricLineTiming CreateWordOnlyLine()
    {
        var words = new[]
        {
            new LyricWordSegment("Hello", TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(6.5), 0, Array.Empty<LyricSyllableTiming>()),
            new LyricWordSegment("world", TimeSpan.FromSeconds(6.5), TimeSpan.FromSeconds(8.0), 1, Array.Empty<LyricSyllableTiming>())
        };
        return new LyricLineTiming(0, "Hello world", TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(8.0), words, Array.Empty<LyricSyllableTiming>());
    }

    private static LyricLineTiming CreateLineOnlyLine() =>
        new(0, "Just a timed line", TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(6.0), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>());

    [Fact]
    public void FlowHighlight_SyllableMode_DominantSyllableAndSofterContainingWord()
    {
        var line = CreateSyllableLine();
        var seg = new ActiveLyricSegment(LyricsSyncMode.Syllable, LineIndex: 0, WordIndex: 0, SyllableIndex: 0, LineProgress: 0.15, WordProgress: 0.25, SyllableProgress: 0.5, IsPaused: false);

        var dark = LyricsHighlightRenderer.ComputeFlowHighlightSnapshot(line, seg, isLightTheme: false, glowEnabled: true);
        var light = LyricsHighlightRenderer.ComputeFlowHighlightSnapshot(line, seg, isLightTheme: true, glowEnabled: true);

        Assert.True(dark.HasTimedHighlight);
        Assert.Equal(LyricsSyncMode.Syllable, dark.EffectiveMode);
        // Active syllable must be strictly more dominant than containing word
        Assert.True(dark.SyllableHighlightIntensity > dark.WordHighlightIntensity);
        Assert.True(dark.WordHighlightIntensity > 0.0);
        // Containing word span ("Maybe") must cover active syllable span ("May")
        Assert.True(dark.ActiveWordStartRatio <= dark.ActiveSyllableStartRatio);
        Assert.True(dark.ActiveWordEndRatio >= dark.ActiveSyllableEndRatio);
        Assert.True(dark.LineFillRatio > 0.0 && dark.LineFillRatio < 0.5);
        Assert.True(dark.FlowGlowIntensity > 0.0);

        // Light theme preserves readability with softer controlled glow
        Assert.True(light.HasTimedHighlight);
        Assert.True(light.SyllableHighlightIntensity > light.WordHighlightIntensity);
        Assert.True(light.FlowGlowIntensity > 0.0 && light.FlowGlowIntensity < dark.FlowGlowIntensity);
    }

    [Fact]
    public void FlowHighlight_WordMode_HighlightsWordFlowWithoutFakingSyllables()
    {
        var line = CreateWordOnlyLine();
        var seg = new ActiveLyricSegment(LyricsSyncMode.Word, LineIndex: 0, WordIndex: 1, SyllableIndex: -1, LineProgress: 0.75, WordProgress: 0.5, SyllableProgress: 0.0, IsPaused: false);

        var snap = LyricsHighlightRenderer.ComputeFlowHighlightSnapshot(line, seg, isLightTheme: false, glowEnabled: true);

        Assert.True(snap.HasTimedHighlight);
        Assert.Equal(LyricsSyncMode.Word, snap.EffectiveMode);
        Assert.True(snap.WordHighlightIntensity > 0.0);
        Assert.Equal(0.0, snap.SyllableHighlightIntensity);
        Assert.True(snap.LineFillRatio > 0.5 && snap.LineFillRatio < 1.0);
    }

    [Fact]
    public void FlowHighlight_LineMode_SweepsLeftToRightWithoutFakeWordOrSyllableTiming()
    {
        var line = CreateLineOnlyLine();
        var segStart = new ActiveLyricSegment(LyricsSyncMode.Line, 0, -1, -1, LineProgress: 0.20, 0, 0, false);
        var segEnd = new ActiveLyricSegment(LyricsSyncMode.Line, 0, -1, -1, LineProgress: 0.85, 0, 0, false);

        var snapStart = LyricsHighlightRenderer.ComputeFlowHighlightSnapshot(line, segStart, isLightTheme: false, glowEnabled: true);
        var snapEnd = LyricsHighlightRenderer.ComputeFlowHighlightSnapshot(line, segEnd, isLightTheme: false, glowEnabled: true);

        Assert.True(snapStart.HasTimedHighlight);
        Assert.Equal(0.20, snapStart.LineFillRatio, precision: 3);
        Assert.Equal(0.85, snapEnd.LineFillRatio, precision: 3);
        Assert.Equal(0.0, snapStart.WordHighlightIntensity);
        Assert.Equal(0.0, snapStart.SyllableHighlightIntensity);
        Assert.Equal(0.20, LyricsHighlightRenderer.ComputeExactLineFillRatio(line, segStart), precision: 3);
    }

    [Fact]
    public void FlowHighlight_AsynchronousMode_ProducesZeroFakeHighlight()
    {
        var line = CreateLineOnlyLine();
        var seg = new ActiveLyricSegment(LyricsSyncMode.Asynchronous, -1, -1, -1, 0, 0, 0, false);

        var snap = LyricsHighlightRenderer.ComputeFlowHighlightSnapshot(line, seg, isLightTheme: false, glowEnabled: true);

        Assert.False(snap.HasTimedHighlight);
        Assert.Equal(0.0, snap.LineFillRatio);
        Assert.Equal(0.0, snap.FlowGlowIntensity);
        Assert.Equal(0.0, snap.WordHighlightIntensity);
        Assert.Equal(0.0, snap.SyllableHighlightIntensity);
    }

    [Fact]
    public void SmoothFlowRatio_PauseFreezes_ResumeEases_SeekSnapsImmediately()
    {
        // Normal playback eases smoothly forward
        double r1 = LyricsHighlightRenderer.AdvanceSmoothFlowRatio(0.20, 0.50, isPaused: false, isSeekOrLineChange: false, easingFactor: 0.32);
        Assert.True(r1 > 0.20 && r1 < 0.50);

        // Pause freezes progress completely even if target changes
        double frozen = LyricsHighlightRenderer.AdvanceSmoothFlowRatio(r1, 0.80, isPaused: true, isSeekOrLineChange: false);
        Assert.Equal(r1, frozen);

        // Resume continues smoothly from frozen ratio
        double resumed = LyricsHighlightRenderer.AdvanceSmoothFlowRatio(frozen, 0.80, isPaused: false, isSeekOrLineChange: false);
        Assert.True(resumed > frozen && resumed < 0.80);

        // Seek snaps immediately to exact target position
        double seeked = LyricsHighlightRenderer.AdvanceSmoothFlowRatio(resumed, 0.12, isPaused: false, isSeekOrLineChange: true);
        Assert.Equal(0.12, seeked);
    }

    [Fact]
    public void FlowHighlight_ZeroAllocationPerFrameOver10000Iterations()
    {
        var line = CreateSyllableLine();
        var seg = new ActiveLyricSegment(LyricsSyncMode.Syllable, 0, 0, 1, 0.4, 0.7, 0.5, false);

        // Warmup
        for (int i = 0; i < 50; i++)
        {
            _ = LyricsHighlightRenderer.ComputeFlowHighlightSnapshot(line, seg, false, true);
            _ = LyricsHighlightRenderer.AdvanceSmoothFlowRatio(0.3, 0.6, false, false);
        }

        var sw = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        double ratio = 0.0;
        for (int i = 0; i < 10000; i++)
        {
            var snap = LyricsHighlightRenderer.ComputeFlowHighlightSnapshot(line, seg, (i & 1) == 0, true);
            ratio = LyricsHighlightRenderer.AdvanceSmoothFlowRatio(ratio, snap.LineFillRatio, false, false);
        }
        sw.Stop();
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(sw.ElapsedMilliseconds < 50);
        Assert.Equal(0, after - before);
    }
}
