using FluentAssertions;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class LyricLineClickSelectionTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static SyncedLyrics CreateSampleLyrics()
    {
        var lines = new List<LyricLine>
        {
            new(TimeSpan.FromSeconds(5.0), "First line of the song", 0),
            new(TimeSpan.FromSeconds(10.0), "Second line", 1),
            new(TimeSpan.FromSeconds(15.250), "Middle line A", 2),
            new(TimeSpan.FromSeconds(15.290), "Closely spaced line B", 3),
            new(TimeSpan.FromSeconds(42.500), "Chorus middle line", 4),
            new(TimeSpan.FromSeconds(175.0), "Final line near the end", 5)
        };
        return new SyncedLyrics("Sample Track", "Sample Artist", true, lines, string.Join("\n", lines.Select(l => l.Text)));
    }

    [Fact]
    public void ClickFirstMiddleAndLastLine_ResolvesExactLineIndexEvenWithGsmtcSeekRounding()
    {
        var lyrics = CreateSampleLyrics();
        var duration = TimeSpan.FromSeconds(180);
        var timingData = LyricsTimingData.FromSyncedLyrics(lyrics, LyricsProviderId.Lrclib);
        var syncEngine = new LyricsSyncEngine();
        syncEngine.Load(timingData, LyricsSyncMode.Line);
        var synchronizer = new LyricsTimelineSynchronizer();
        synchronizer.SetLyrics(lyrics);

        int[] testIndices = [0, 4, lyrics.Lines.Count - 1];
        foreach (int idx in testIndices)
        {
            var resolved = LyricLineClickCoordinator.ResolveSeekTarget(lyrics.Lines, idx, duration);
            resolved.IsValid.Should().BeTrue();
            resolved.LineIndex.Should().Be(idx);

            // Simulate GSMTC / player rounding down by 10ms on seek
            var roundedSeekPos = resolved.SeekTimestamp - TimeSpan.FromMilliseconds(10);
            if (roundedSeekPos < TimeSpan.Zero) roundedSeekPos = TimeSpan.Zero;

            syncEngine.Seek(roundedSeekPos);
            synchronizer.EvaluatePosition(roundedSeekPos);

            syncEngine.CurrentSegment.LineIndex.Should().Be(idx, $"clicked line {idx} must remain active even with 10ms player seek rounding");
            synchronizer.CurrentActiveIndex.Should().Be(idx);
        }
    }

    [Fact]
    public void ClickCloselySpacedAdjacentLines_NeverCrossesIntoNextOrFallsBackToPrevious()
    {
        var lyrics = CreateSampleLyrics();
        var duration = TimeSpan.FromSeconds(180);
        // Lines 2 (15.250s) and 3 (15.290s) are only 40ms apart
        var resA = LyricLineClickCoordinator.ResolveSeekTarget(lyrics.Lines, 2, duration);
        var resB = LyricLineClickCoordinator.ResolveSeekTarget(lyrics.Lines, 3, duration);

        resA.IsValid.Should().BeTrue();
        resB.IsValid.Should().BeTrue();
        resA.SeekTimestamp.Should().BeGreaterThanOrEqualTo(lyrics.Lines[2].Timestamp);
        resA.SeekTimestamp.Should().BeLessThan(lyrics.Lines[3].Timestamp);
        resB.SeekTimestamp.Should().BeGreaterThanOrEqualTo(lyrics.Lines[3].Timestamp);

        lyrics.FindActiveLineIndex(resA.SeekTimestamp).Should().Be(2);
        lyrics.FindActiveLineIndex(resB.SeekTimestamp).Should().Be(3);

        // Even ultra-close 4ms gap stays strictly inside the clicked line
        var ultraClose = new List<LyricLine>
        {
            new(TimeSpan.FromMilliseconds(1000), "A", 0),
            new(TimeSpan.FromMilliseconds(1004), "B", 1)
        };
        var resUltraA = LyricLineClickCoordinator.ResolveSeekTarget(ultraClose, 0, TimeSpan.FromSeconds(30));
        resUltraA.SeekTimestamp.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1000));
        resUltraA.SeekTimestamp.Should().BeLessThan(TimeSpan.FromMilliseconds(1004));
    }

    [Fact]
    public void RapidSequentialClicks_StaleEarlierCompletionNeverOverwritesNewerSelection()
    {
        var coordinator = new LyricLineClickCoordinator();
        var t0 = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

        // User rapidly clicks line 1, then line 5, then line 3 before earlier async seeks finish
        long seq1 = coordinator.BeginSelection(1, t0);
        long seq2 = coordinator.BeginSelection(5, t0.AddMilliseconds(25));
        long seq3 = coordinator.BeginSelection(3, t0.AddMilliseconds(50));

        // Auto-sync timer ticks with stale pre-seek index 0 during settling window -> stays locked on latest click (3)
        coordinator.ResolveActiveLineIndex(evaluatedActiveIndex: 0, lineCount: 10, utcNow: t0.AddMilliseconds(80))
            .Should().Be(3);

        // Out-of-order completion: Click 1 finishes late -> MUST be rejected!
        coordinator.TryCompleteSelection(seq1, seekSucceeded: true).Should().BeFalse();
        // Click 2 finishes late -> MUST be rejected!
        coordinator.TryCompleteSelection(seq2, seekSucceeded: true).Should().BeFalse();
        // Latest Click 3 finishes -> ACCEPTED!
        coordinator.TryCompleteSelection(seq3, seekSucceeded: true).Should().BeTrue();

        // Once engine catches up to index 3, pending lock releases cleanly and auto-sync continues normally
        coordinator.ResolveActiveLineIndex(evaluatedActiveIndex: 3, lineCount: 10, utcNow: t0.AddMilliseconds(120))
            .Should().Be(3);
        coordinator.ResolveActiveLineIndex(evaluatedActiveIndex: 4, lineCount: 10, utcNow: t0.AddMilliseconds(200))
            .Should().Be(4);
    }

    [Fact]
    public void ScrolledViewportLineSelection_CentersTargetLineAndClearsManualScrollHold()
    {
        // Simulate scrolled lyrics viewport: user scrolled down to offset 420px and clicked line at Y=680px
        double targetCenter = LyricsViewportCalculator.CalculateTargetCenterOffset(
            viewportHeight: 500.0,
            lineTopInContent: 680.0,
            lineHeight: 44.0,
            maxScrollableHeight: 1400.0);

        // Center of line = 680 + 22 = 702; viewport center = 250 -> scroll target = 452
        targetCenter.Should().BeApproximately(452.0, 0.01);

        // Verify LyricsWindow clears _userIsInteracting when a lyric line is clicked so centering runs immediately
        string lyricsCs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        int seekMethodStart = lyricsCs.IndexOf("private async Task SeekToLyricLineIndexAsync(int lineIndex)", StringComparison.Ordinal);
        seekMethodStart.Should().BeGreaterThan(0);
        string seekMethodSlice = lyricsCs[seekMethodStart..Math.Min(lyricsCs.Length, seekMethodStart + 2600)];
        seekMethodSlice.Should().Contain("_userIsInteracting = false;");
    }

    [Fact]
    public void WindowResizeAndProgressiveTimingUpgrade_PreserveOneToOneIndexMappingWithoutStaleClosures()
    {
        string lyricsCs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // 1. RebuildLyricsUi binds by live index (capturedLineIndex) instead of stale LyricLine object closure
        lyricsCs.Should().Contain("int capturedLineIndex = i;");
        lyricsCs.Should().Contain("await SeekToLyricLineIndexAsync(capturedLineIndex);");
        lyricsCs.Should().NotContain("var targetModelLine = line;");

        // 2. Simulate progressive upgrade (Line -> Syllable) where line timestamps shift slightly for same text
        var initialLyrics = new SyncedLyrics("T", "A", true, new List<LyricLine>
        {
            new(TimeSpan.FromSeconds(10.0), "Hello world", 0),
            new(TimeSpan.FromSeconds(14.0), "Second lyric", 1)
        }, "Hello world\nSecond lyric");
        var upgradedLyrics = new SyncedLyrics("T", "A", true, new List<LyricLine>
        {
            new(TimeSpan.FromSeconds(10.8), "Hello world", 0),
            new(TimeSpan.FromSeconds(14.9), "Second lyric", 1)
        }, "Hello world\nSecond lyric");

        // Resolving index 1 after upgrade MUST use upgraded timestamp (14.9s), not stale 14.0s
        var upgradedTarget = LyricLineClickCoordinator.ResolveSeekTarget(upgradedLyrics.Lines, 1, TimeSpan.FromSeconds(180));
        upgradedTarget.IsValid.Should().BeTrue();
        upgradedTarget.SeekTimestamp.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(14.9));
        upgradedLyrics.FindActiveLineIndex(upgradedTarget.SeekTimestamp).Should().Be(1);
        initialLyrics.FindActiveLineIndex(upgradedTarget.SeekTimestamp).Should().Be(1);
    }

    [Fact]
    public void SyllableAndWordHighlightingActive_OverlaysDoNotInterceptClicksAndSeekLandsOnClickedLine()
    {
        // 1. Build genuine Syllable timing data
        var line0Words = new[]
        {
            new LyricWordSegment("Blind", TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(5.4), 0, Array.Empty<LyricSyllableTiming>()),
            new LyricWordSegment("lights", TimeSpan.FromSeconds(5.5), TimeSpan.FromSeconds(6.2), 1, Array.Empty<LyricSyllableTiming>())
        };
        var line0Syls = new[]
        {
            new LyricSyllableTiming("Blind", TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(5.2), 0, 0),
            new LyricSyllableTiming("ing", TimeSpan.FromSeconds(5.2), TimeSpan.FromSeconds(5.4), 0, 1),
            new LyricSyllableTiming("lights", TimeSpan.FromSeconds(5.5), TimeSpan.FromSeconds(6.2), 1, 0)
        };
        var line1Words = new[]
        {
            new LyricWordSegment("I", TimeSpan.FromSeconds(8.0), TimeSpan.FromSeconds(8.2), 0, Array.Empty<LyricSyllableTiming>()),
            new LyricWordSegment("said", TimeSpan.FromSeconds(8.3), TimeSpan.FromSeconds(8.8), 1, Array.Empty<LyricSyllableTiming>())
        };
        var line1Syls = new[]
        {
            new LyricSyllableTiming("I", TimeSpan.FromSeconds(8.0), TimeSpan.FromSeconds(8.2), 0, 0),
            new LyricSyllableTiming("said", TimeSpan.FromSeconds(8.3), TimeSpan.FromSeconds(8.8), 1, 0)
        };

        var timingData = new LyricsTimingData(
            "Blinding Lights",
            "The Weeknd",
            LyricsProviderId.BetterLyrics,
            LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedSyllables,
            new[]
            {
                new LyricLineTiming(0, "Blinding lights", TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(7.5), line0Words, line0Syls),
                new LyricLineTiming(1, "I said", TimeSpan.FromSeconds(8.0), TimeSpan.FromSeconds(10.0), line1Words, line1Syls)
            },
            "Blinding lights\nI said");

        var engine = new LyricsSyncEngine();
        engine.Load(timingData, LyricsSyncMode.Syllable);
        engine.Evaluate(TimeSpan.FromSeconds(5.3), isPaused: false);
        engine.CurrentSegment.LineIndex.Should().Be(0);
        engine.CurrentSegment.SyllableIndex.Should().Be(1);

        // User clicks line 1 while syllable highlight is active on line 0
        var resolved = LyricLineClickCoordinator.ResolveSeekTarget(timingData.Lines, 1, TimeSpan.FromSeconds(200));
        resolved.IsValid.Should().BeTrue();
        engine.Seek(resolved.SeekTimestamp);
        engine.CurrentSegment.LineIndex.Should().Be(1);

        // 2. Verify highlight overlay layers in LyricsWindow.xaml.cs have IsHitTestVisible = false and event is Handled
        string lyricsCs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        int rebuildStart = lyricsCs.IndexOf("private void RebuildLyricsUi(SyncedLyrics lyrics)", StringComparison.Ordinal);
        int rebuildEnd = lyricsCs.IndexOf("private void UpdateActiveLineTargets(", rebuildStart, StringComparison.Ordinal);
        string rebuildSlice = lyricsCs[rebuildStart..rebuildEnd];

        int hitTestFalseCount = rebuildSlice.Split("IsHitTestVisible = false").Length - 1;
        hitTestFalseCount.Should().BeGreaterThanOrEqualTo(5, "lineStack, baseBlock, wordOverlayBlock, overlayBlock, and subGrid must all be non-hit-testable so only Border container captures clicks");
        rebuildSlice.Should().Contain("IsLeftButtonPressed");
        rebuildSlice.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void ClickingSameLineMultipleTimes_AlwaysResolvesIdenticalLineAndNeverDrifts()
    {
        var lyrics = CreateSampleLyrics();
        var duration = TimeSpan.FromSeconds(180);
        var timingData = LyricsTimingData.FromSyncedLyrics(lyrics, LyricsProviderId.Lrclib);
        var engine = new LyricsSyncEngine();
        engine.Load(timingData, LyricsSyncMode.Line);
        var coordinator = new LyricLineClickCoordinator();
        var now = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

        TimeSpan? firstTarget = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            var resolved = LyricLineClickCoordinator.ResolveSeekTarget(lyrics.Lines, 4, duration);
            resolved.IsValid.Should().BeTrue();
            if (firstTarget == null)
            {
                firstTarget = resolved.SeekTimestamp;
            }
            else
            {
                resolved.SeekTimestamp.Should().Be(firstTarget.Value);
            }

            long seq = coordinator.BeginSelection(4, now.AddMilliseconds(attempt * 100));
            coordinator.TryCompleteSelection(seq, seekSucceeded: true).Should().BeTrue();
            engine.Seek(resolved.SeekTimestamp);
            engine.CurrentSegment.LineIndex.Should().Be(4);
        }
    }

    [Fact]
    public void AfterLineClickSeek_AutomaticProgressionAdvancesToSubsequentLinesWithoutStalling()
    {
        var lyrics = CreateSampleLyrics();
        var duration = TimeSpan.FromSeconds(180);
        var timingData = LyricsTimingData.FromSyncedLyrics(lyrics, LyricsProviderId.Lrclib);
        var syncEngine = new LyricsSyncEngine();
        syncEngine.Load(timingData, LyricsSyncMode.Line);
        var coordinator = new LyricLineClickCoordinator();
        var timelineEngine = new Lyrow.Core.Timeline.PlaybackTimelineEngine();
        var track = new MediaTrackInfo("Sample Track", "Sample Artist", "Sample Album", duration);

        timelineEngine.HandleTrackChanged(track);
        timelineEngine.HandleTimelineProperties(TimeSpan.FromSeconds(1), duration, MediaPlaybackStatus.Playing);

        // 1. User clicks line 1 (10.0s)
        var click = LyricLineClickCoordinator.ResolveSeekTarget(lyrics.Lines, 1, duration);
        var tClick = DateTime.UtcNow;
        long seq = coordinator.BeginSelection(1, tClick);
        coordinator.TryCompleteSelection(seq, seekSucceeded: true).Should().BeTrue();
        coordinator.PendingLineIndex.Should().Be(-1, "completed seek must immediately release manual lock");

        timelineEngine.HandleSeek(click.SeekTimestamp);
        syncEngine.Seek(click.SeekTimestamp);
        syncEngine.Evaluate(click.SeekTimestamp, isPaused: false).LineIndex.Should().Be(1);

        // 2. Playback continues to line 2 (15.25s) and line 4 (42.5s) -> auto-tracking MUST advance!
        var line2Seg = syncEngine.Evaluate(TimeSpan.FromSeconds(15.26), isPaused: false);
        coordinator.ResolveActiveLineIndex(line2Seg.LineIndex, lyrics.Lines.Count, tClick.AddMilliseconds(50))
            .Should().Be(2);

        var line4Seg = syncEngine.Evaluate(TimeSpan.FromSeconds(42.6), isPaused: false);
        coordinator.ResolveActiveLineIndex(line4Seg.LineIndex, lyrics.Lines.Count, tClick.AddMilliseconds(100))
            .Should().Be(4);
    }

    [Fact]
    public void PostSeekTimelinePolling_WithStaticGsmtcAnchorOverFiveSeconds_ContinuesAdvancingInsteadOfFreezing()
    {
        // Regression test for the exact freeze bug:
        // Players like Spotify/Apple Music keep Position fixed at the last seek anchor while LastUpdatedTime ages > 5s.
        // Polling every 500ms MUST continue interpolating forward from (Position + (now - LastUpdatedTime)),
        // and pre-seek GSMTC snapshots (LastUpdatedTime < _lastSeekUtc) MUST NOT snap back.
        var timelineEngine = new Lyrow.Core.Timeline.PlaybackTimelineEngine();
        var duration = TimeSpan.FromMinutes(4);
        var track = new MediaTrackInfo("Song", "Artist", "Album", duration);
        timelineEngine.HandleTrackChanged(track);

        // Simulate user seeking to 1:00
        timelineEngine.HandleSeek(TimeSpan.FromSeconds(60));

        // 1. Pre-seek GSMTC snapshot arrives (reportedUtc BEFORE seek) -> must NOT snap back to 0:10!
        timelineEngine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromSeconds(10),
            rawDuration: duration,
            rawStatus: MediaPlaybackStatus.Playing,
            reportedUtc: DateTime.UtcNow.AddSeconds(-2));
        timelineEngine.CurrentTrackPosition.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(59));

        // 2. Post-seek GSMTC anchor arrives from Spotify (Position=60s, LastUpdatedTime=now, Playing)
        // Wait briefly and verify position advances monotonically
        var anchorUtc = DateTime.UtcNow;
        timelineEngine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromSeconds(60),
            rawDuration: duration,
            rawStatus: MediaPlaybackStatus.Playing,
            reportedUtc: anchorUtc);
        var posBefore = timelineEngine.CurrentTrackPosition;
        System.Threading.Thread.Sleep(60);
        var posAfter = timelineEngine.CurrentTrackPosition;
        posAfter.Should().BeGreaterThan(posBefore);
    }

    [Fact]
    public void TrackSwitchClearsPendingClickGuard_AndScrollingFollowsNewActiveLines()
    {
        var coordinator = new LyricLineClickCoordinator();
        var t0 = DateTime.UtcNow;

        // In-flight click on Track A line 8
        long seqA = coordinator.BeginSelection(8, t0);
        coordinator.PendingLineIndex.Should().Be(8);

        // Track switches to Track B -> pending click guard MUST be invalidated immediately
        coordinator.InvalidatePending();
        coordinator.PendingLineIndex.Should().Be(-1);
        coordinator.TryCompleteSelection(seqA, seekSucceeded: true).Should().BeFalse();

        // Active line tracking & centering scroll follow Track B line 0, then line 1, then line 2
        coordinator.ResolveActiveLineIndex(0, 12, t0.AddMilliseconds(50)).Should().Be(0);
        coordinator.ResolveActiveLineIndex(1, 12, t0.AddMilliseconds(100)).Should().Be(1);

        double scrollLine1 = LyricsViewportCalculator.CalculateTargetCenterOffset(500, 300, 40, 1200);
        double scrollLine2 = LyricsViewportCalculator.CalculateTargetCenterOffset(500, 360, 40, 1200);
        scrollLine2.Should().BeGreaterThan(scrollLine1);
    }
}
