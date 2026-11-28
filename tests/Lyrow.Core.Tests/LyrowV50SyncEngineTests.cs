using System.Diagnostics;
using Lyrow.Core.Lyrics;
using Xunit;

namespace Lyrow.Core.Tests;

public class LyrowV50SyncEngineTests
{
    private static LyricsTimingData CreateSyllableTimingFixture()
    {
        // Mirrors real Better Lyrics TTML timing for "The Weeknd - Blinding Lights"
        // including intra-word syllables ("e"+"nough", "May"+"be")
        var line0Syls = new[]
        {
            new LyricSyllableTiming("I've", TimeSpan.FromSeconds(27.000), TimeSpan.FromSeconds(27.500), 0, 0, true),
            new LyricSyllableTiming("been", TimeSpan.FromSeconds(27.500), TimeSpan.FromSeconds(28.000), 1, 1, true),
            new LyricSyllableTiming("try", TimeSpan.FromSeconds(28.000), TimeSpan.FromSeconds(28.450), 2, 2, false),
            new LyricSyllableTiming("na", TimeSpan.FromSeconds(28.450), TimeSpan.FromSeconds(28.900), 3, 2, true),
            new LyricSyllableTiming("call", TimeSpan.FromSeconds(28.900), TimeSpan.FromSeconds(29.800), 4, 3, true)
        };
        var line0Words = new[]
        {
            new LyricWordSegment("I've", TimeSpan.FromSeconds(27.000), TimeSpan.FromSeconds(27.500), 0, new[] { line0Syls[0] }),
            new LyricWordSegment("been", TimeSpan.FromSeconds(27.500), TimeSpan.FromSeconds(28.000), 1, new[] { line0Syls[1] }),
            new LyricWordSegment("tryna", TimeSpan.FromSeconds(28.000), TimeSpan.FromSeconds(28.900), 2, new[] { line0Syls[2], line0Syls[3] }),
            new LyricWordSegment("call", TimeSpan.FromSeconds(28.900), TimeSpan.FromSeconds(29.800), 3, new[] { line0Syls[4] })
        };
        var line0 = new LyricLineTiming(0, "I've been tryna call", TimeSpan.FromSeconds(27.000), TimeSpan.FromSeconds(29.800), line0Words, line0Syls);

        var line1Syls = new[]
        {
            new LyricSyllableTiming("I've", TimeSpan.FromSeconds(30.000), TimeSpan.FromSeconds(30.400), 0, 0, true),
            new LyricSyllableTiming("been", TimeSpan.FromSeconds(30.400), TimeSpan.FromSeconds(30.800), 1, 1, true),
            new LyricSyllableTiming("e", TimeSpan.FromSeconds(31.839), TimeSpan.FromSeconds(31.996), 2, 2, false),
            new LyricSyllableTiming("nough", TimeSpan.FromSeconds(31.996), TimeSpan.FromSeconds(32.529), 3, 2, true)
        };
        var line1Words = new[]
        {
            new LyricWordSegment("I've", TimeSpan.FromSeconds(30.000), TimeSpan.FromSeconds(30.400), 0, new[] { line1Syls[0] }),
            new LyricWordSegment("been", TimeSpan.FromSeconds(30.400), TimeSpan.FromSeconds(30.800), 1, new[] { line1Syls[1] }),
            new LyricWordSegment("enough", TimeSpan.FromSeconds(31.839), TimeSpan.FromSeconds(32.529), 2, new[] { line1Syls[2], line1Syls[3] })
        };
        var line1 = new LyricLineTiming(1, "I've been enough", TimeSpan.FromSeconds(30.000), TimeSpan.FromSeconds(32.529), line1Words, line1Syls);

        var line2Syls = new[]
        {
            new LyricSyllableTiming("May", TimeSpan.FromSeconds(35.729), TimeSpan.FromSeconds(36.489), 0, 0, false),
            new LyricSyllableTiming("be", TimeSpan.FromSeconds(36.489), TimeSpan.FromSeconds(37.394), 1, 0, true)
        };
        var line2Words = new[]
        {
            new LyricWordSegment("Maybe", TimeSpan.FromSeconds(35.729), TimeSpan.FromSeconds(37.394), 0, line2Syls)
        };
        var line2 = new LyricLineTiming(2, "Maybe", TimeSpan.FromSeconds(35.729), TimeSpan.FromSeconds(39.000), line2Words, line2Syls);

        return new LyricsTimingData(
            "Blinding Lights",
            "The Weeknd",
            LyricsProviderId.BetterLyrics,
            LyricsTimingGranularity.TimedSyllables | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics,
            new[] { line0, line1, line2 },
            plainLyrics: null);
    }

    private static LyricsTimingData CreateWordOnlyTimingFixture()
    {
        var words = new[]
        {
            new LyricWordSegment("Hello", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), 0, Array.Empty<LyricSyllableTiming>()),
            new LyricWordSegment("world", TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(14), 1, Array.Empty<LyricSyllableTiming>())
        };
        var line0 = new LyricLineTiming(0, "Hello world", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(14), words, Array.Empty<LyricSyllableTiming>());

        return new LyricsTimingData(
            "Hello",
            "Artist",
            LyricsProviderId.BetterLyricsUnison,
            LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics,
            new[] { line0 },
            plainLyrics: null);
    }

    private static LyricsTimingData CreateLineOnlyTimingFixture()
    {
        var line0 = new LyricLineTiming(0, "First line", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>());
        var line1 = new LyricLineTiming(1, "Second line", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>());
        return new LyricsTimingData(
            "Track",
            "Artist",
            LyricsProviderId.Lrclib,
            LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics,
            new[] { line0, line1 },
            plainLyrics: null);
    }

    [Fact]
    public void PriorityResolver_MatchesExactPriorityOrdersForAllFourModes()
    {
        var resolver = new LyricsProviderPriorityResolver();

        Assert.Equal(
            new[]
            {
                LyricsProviderId.BetterLyrics,
                LyricsProviderId.AppleMusic,
                LyricsProviderId.BetterLyricsUnison,
                LyricsProviderId.BiniLyrics
            },
            resolver.GetPriorityOrder(LyricsSyncMode.Syllable));

        Assert.Equal(
            new[]
            {
                LyricsProviderId.AppleMusic,
                LyricsProviderId.BetterLyricsUnison,
                LyricsProviderId.QQMusic,
                LyricsProviderId.BetterLyricsPortato,
                LyricsProviderId.NetEase,
                LyricsProviderId.KuGou,
                LyricsProviderId.Kuwo,
                LyricsProviderId.Migu,
                LyricsProviderId.PetitLyrics
            },
            resolver.GetPriorityOrder(LyricsSyncMode.Word));

        Assert.Equal(
            new[]
            {
                LyricsProviderId.BetterLyrics,
                LyricsProviderId.AppleMusic,
                LyricsProviderId.BetterLyricsUnison,
                LyricsProviderId.BiniLyrics,
                LyricsProviderId.Lrclib,
                LyricsProviderId.BetterLyricsLegato,
                LyricsProviderId.QQMusic,
                LyricsProviderId.NetEase,
                LyricsProviderId.KuGou,
                LyricsProviderId.Kuwo,
                LyricsProviderId.Migu
            },
            resolver.GetPriorityOrder(LyricsSyncMode.Line));

        Assert.Equal(
            new[] { LyricsProviderId.BetterLyricsUnison },
            resolver.GetPriorityOrder(LyricsSyncMode.Asynchronous));
    }

    [Fact]
    public void PriorityResolver_StrictModeValidation_RejectsLowerGranularity()
    {
        var syllableData = CreateSyllableTimingFixture();
        var wordData = CreateWordOnlyTimingFixture();
        var lineData = CreateLineOnlyTimingFixture();
        var plainData = new LyricsTimingData(
            "Plain", "Artist", LyricsProviderId.BetterLyricsUnison,
            LyricsTimingGranularity.PlainLyrics, Array.Empty<LyricLineTiming>(), "Just plain text");

        Assert.False(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Syllable, wordData));
        Assert.False(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Syllable, lineData));
        Assert.False(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Syllable, plainData));
        Assert.True(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Syllable, syllableData));

        Assert.False(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Word, lineData));
        Assert.False(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Word, plainData));
        Assert.True(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Word, wordData));

        Assert.False(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Line, plainData));
        Assert.True(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Line, lineData));

        Assert.True(LyricsProviderPriorityResolver.SatisfiesStrictMode(LyricsSyncMode.Asynchronous, plainData));
    }

    [Fact]
    public void SyncEngine_SyllableMode_EvaluatesExactStartEndAndIntraWordSyllables()
    {
        var engine = new LyricsSyncEngine();
        var data = CreateSyllableTimingFixture();
        Assert.True(data.HasTimedSyllables);

        engine.Load(data, LyricsSyncMode.Syllable);
        Assert.Equal(LyricsSyncMode.Syllable, engine.EffectiveMode);

        // First syllable "e" of "enough" (31.839 -> 31.996, index 2 in line 1)
        var seg1 = engine.Evaluate(TimeSpan.FromSeconds(31.900));
        Assert.Equal(1, seg1.LineIndex);
        Assert.Equal(2, seg1.WordIndex);
        Assert.Equal(2, seg1.SyllableIndex);
        Assert.InRange(seg1.SyllableProgress, 0.30, 0.50);

        // Second syllable "nough" of "enough" (31.996 -> 32.529, index 3 in line 1)
        var seg2 = engine.Evaluate(TimeSpan.FromSeconds(32.262));
        Assert.Equal(1, seg2.LineIndex);
        Assert.Equal(2, seg2.WordIndex);
        Assert.Equal(3, seg2.SyllableIndex);
        Assert.InRange(seg2.SyllableProgress, 0.45, 0.55);

        // "May" vs "be" on line 2
        var segMay = engine.Evaluate(TimeSpan.FromSeconds(36.000));
        Assert.Equal(2, segMay.LineIndex);
        Assert.Equal(0, segMay.WordIndex);
        Assert.Equal(0, segMay.SyllableIndex);

        var segBe = engine.Evaluate(TimeSpan.FromSeconds(37.000));
        Assert.Equal(2, segBe.LineIndex);
        Assert.Equal(0, segBe.WordIndex);
        Assert.Equal(1, segBe.SyllableIndex);
    }

    [Fact]
    public void SyncEngine_WordMode_EvaluatesExactWordBoundariesWithoutFakingSyllables()
    {
        var engine = new LyricsSyncEngine();
        var wordData = CreateWordOnlyTimingFixture();
        Assert.True(wordData.HasTimedWords);
        Assert.False(wordData.HasTimedSyllables);

        engine.Load(wordData, LyricsSyncMode.Word);
        Assert.Equal(LyricsSyncMode.Word, engine.EffectiveMode);

        var w0 = engine.Evaluate(TimeSpan.FromSeconds(11.0));
        Assert.Equal(0, w0.LineIndex);
        Assert.Equal(0, w0.WordIndex);
        Assert.Equal(-1, w0.SyllableIndex);
        Assert.InRange(w0.WordProgress, 0.49, 0.51);

        var w1 = engine.Evaluate(TimeSpan.FromSeconds(13.0));
        Assert.Equal(0, w1.LineIndex);
        Assert.Equal(1, w1.WordIndex);
        Assert.Equal(-1, w1.SyllableIndex);
        Assert.InRange(w1.WordProgress, 0.49, 0.51);
    }

    [Fact]
    public void SyncEngine_LineAndAsyncModes_HandleTransitionsAndZeroFakeHighlight()
    {
        var engine = new LyricsSyncEngine();
        var lineData = CreateLineOnlyTimingFixture();
        Assert.True(lineData.HasTimedLines);
        Assert.False(lineData.HasTimedWords);

        engine.Load(lineData, LyricsSyncMode.Line);
        var beforeFirst = engine.Evaluate(TimeSpan.FromSeconds(2.0));
        Assert.Equal(-1, beforeFirst.LineIndex);

        var first = engine.Evaluate(TimeSpan.FromSeconds(6.0));
        Assert.Equal(0, first.LineIndex);
        Assert.Equal(-1, first.WordIndex);
        Assert.Equal(-1, first.SyllableIndex);

        var second = engine.Evaluate(TimeSpan.FromSeconds(12.0));
        Assert.Equal(1, second.LineIndex);

        // Asynchronous mode: no active segment, no fake highlighting
        engine.SetMode(LyricsSyncMode.Asynchronous);
        var asyncSeg = engine.Evaluate(TimeSpan.FromSeconds(12.0));
        Assert.Equal(LyricsSyncMode.Asynchronous, engine.EffectiveMode);
        Assert.Equal(-1, asyncSeg.LineIndex);
        Assert.Equal(-1, asyncSeg.WordIndex);
        Assert.Equal(-1, asyncSeg.SyllableIndex);
        Assert.Equal(0.0, asyncSeg.LineProgress);
    }

    [Fact]
    public void SyncEngine_AutomaticMode_SelectsHighestGenuineGranularity()
    {
        var engine = new LyricsSyncEngine();

        engine.Load(CreateSyllableTimingFixture(), LyricsSyncMode.Automatic);
        Assert.Equal(LyricsSyncMode.Syllable, engine.EffectiveMode);

        engine.Load(CreateWordOnlyTimingFixture(), LyricsSyncMode.Automatic);
        Assert.Equal(LyricsSyncMode.Word, engine.EffectiveMode);

        engine.Load(CreateLineOnlyTimingFixture(), LyricsSyncMode.Automatic);
        Assert.Equal(LyricsSyncMode.Line, engine.EffectiveMode);

        var plainOnly = new LyricsTimingData(
            "Plain", "Artist", LyricsProviderId.BetterLyricsUnison,
            LyricsTimingGranularity.PlainLyrics,
            Array.Empty<LyricLineTiming>(), plainLyrics: "Just plain text");
        engine.Load(plainOnly, LyricsSyncMode.Automatic);
        Assert.Equal(LyricsSyncMode.Asynchronous, engine.EffectiveMode);
    }

    [Fact]
    public void SyncEngine_SeekPauseResumeAndTrackReset_AreInstantAndDeterministic()
    {
        var engine = new LyricsSyncEngine();
        engine.Load(CreateSyllableTimingFixture(), LyricsSyncMode.Syllable);

        // Forward seek
        var s1 = engine.Seek(TimeSpan.FromSeconds(36.0));
        Assert.Equal(2, s1.LineIndex);
        Assert.Equal(0, s1.SyllableIndex);

        // Pause freezes active segment even if position jitters
        engine.Pause(TimeSpan.FromSeconds(36.0));
        var frozen = engine.Evaluate(TimeSpan.FromSeconds(28.5), isPaused: true);
        Assert.Equal(2, frozen.LineIndex);
        Assert.True(frozen.IsPaused);

        // Resume picks up cleanly at new position
        var resumed = engine.Resume(TimeSpan.FromSeconds(28.5));
        Assert.Equal(0, resumed.LineIndex);
        Assert.Equal(2, resumed.WordIndex);
        Assert.Equal(3, resumed.SyllableIndex); // "na" in "tryna"

        // Backward seek
        var back = engine.Seek(TimeSpan.FromSeconds(27.1));
        Assert.Equal(0, back.LineIndex);
        Assert.Equal(0, back.WordIndex);

        // Track change reset clears state atomically
        engine.Reset();
        Assert.False(engine.HasLyrics);
        Assert.Equal(-1, engine.CurrentSegment.LineIndex);
    }

    [Fact]
    public void TimingData_NormalizesOutOfOrderOverlappingAndNegativeTimestamps()
    {
        var dirtyLines = new[]
        {
            new LyricLineTiming(1, "Second", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>()),
            new LyricLineTiming(0, "First overlapping", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(12), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>()),
            new LyricLineTiming(2, "Negative", TimeSpan.FromSeconds(-3), TimeSpan.FromSeconds(-1), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>())
        };

        var normalized = new LyricsTimingData(
            "Test", "Artist", LyricsProviderId.Lrclib,
            LyricsTimingGranularity.TimedLines,
            dirtyLines, plainLyrics: null);

        Assert.Equal(3, normalized.Lines.Count);
        Assert.Equal(TimeSpan.Zero, normalized.Lines[0].StartTime);
        Assert.True(normalized.Lines[1].StartTime >= normalized.Lines[0].EndTime);
        Assert.True(normalized.Lines[2].StartTime >= normalized.Lines[1].EndTime);
        Assert.Equal("First overlapping", normalized.Lines[1].Text);
        Assert.Equal("Second", normalized.Lines[2].Text);
    }

    [Fact]
    public void HighlightRenderer_ProducesAccessibleDistinctStatesInDarkAndLightThemes()
    {
        var darkActive = LyricsHighlightRenderer.ComputeSegmentState(1, 1, 0.5, isLightTheme: false, glowEnabled: true);
        var darkUpcoming = LyricsHighlightRenderer.ComputeSegmentState(2, 1, 0.5, isLightTheme: false, glowEnabled: true);
        var darkPassed = LyricsHighlightRenderer.ComputeSegmentState(0, 1, 0.5, isLightTheme: false, glowEnabled: true);

        var lightActive = LyricsHighlightRenderer.ComputeSegmentState(1, 1, 0.5, isLightTheme: true, glowEnabled: true);
        var lightUpcoming = LyricsHighlightRenderer.ComputeSegmentState(2, 1, 0.5, isLightTheme: true, glowEnabled: true);

        Assert.Equal(1.0, darkActive.Opacity);
        Assert.True(darkActive.Scale > 1.0);
        Assert.True(darkActive.GlowIntensity > 0.4);
        Assert.True(darkPassed.IsPassed);
        Assert.InRange(darkUpcoming.Opacity, 0.45, 0.65);

        Assert.Equal(1.0, lightActive.Opacity);
        Assert.True(lightActive.Scale > 1.0);
        Assert.True(lightActive.GlowIntensity > 0.25);
        Assert.True(lightUpcoming.Opacity > darkUpcoming.Opacity); // Light theme preserves extra readability
    }

    [Fact]
    public void SyncEngine_Performance_10000EvaluationsUnder50msAndZeroAllocPerFrame()
    {
        var engine = new LyricsSyncEngine();
        engine.Load(CreateSyllableTimingFixture(), LyricsSyncMode.Syllable);

        // Warmup
        for (int i = 0; i < 100; i++)
        {
            engine.Evaluate(TimeSpan.FromMilliseconds(27000 + i * 10));
        }

        var sw = new Stopwatch();
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        for (int i = 0; i < 10000; i++)
        {
            double ms = 27000 + (i % 12000);
            engine.Evaluate(TimeSpan.FromMilliseconds(ms));
        }
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(sw.ElapsedMilliseconds < 50, $"10,000 evaluations took {sw.ElapsedMilliseconds}ms");
        Assert.Equal(0, allocAfter - allocBefore);
    }
}
