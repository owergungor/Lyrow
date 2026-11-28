using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyrowV50FallbackAndShortCodeTests
{
    private sealed class StubSyncProvider : ILyricsSyncProvider
    {
        private readonly Func<string, string, CancellationToken, Task<LyricsTimingData?>> _fn;

        public StubSyncProvider(LyricsProviderId id, Func<string, string, CancellationToken, Task<LyricsTimingData?>> fn)
        {
            ProviderId = id;
            _fn = fn;
        }

        public LyricsProviderId ProviderId { get; }

        public Task<LyricsTimingData?> FetchTimingDataAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
            => _fn(trackName, artistName, cancellationToken);
    }

    private sealed class StubLegacyFallbackClient : ILyricsClient
    {
        private readonly SyncedLyrics? _lyrics;

        public StubLegacyFallbackClient(SyncedLyrics? lyrics) => _lyrics = lyrics;

        public int CallCount { get; private set; }

        public Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_lyrics != null
                ? new LyricsSearchResult(true, _lyrics, "PlaybackFallback")
                : new LyricsSearchResult(false, null, "PlaybackFallback", "Not found"));
        }
    }

    private static LyricsTimingData MakeTiming(LyricsProviderId provider, LyricsTimingGranularity gran)
    {
        var syls = (gran & LyricsTimingGranularity.TimedSyllables) != 0
            ? new[]
            {
                new LyricSyllableTiming("Hel", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), 0, 0, false),
                new LyricSyllableTiming("lo", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.0), 1, 0, true)
            }
            : Array.Empty<LyricSyllableTiming>();

        var words = (gran & LyricsTimingGranularity.TimedWords) != 0
            ? new[] { new LyricWordSegment("Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0, syls) }
            : Array.Empty<LyricWordSegment>();

        var lines = (gran & LyricsTimingGranularity.TimedLines) != 0
            ? new[] { new LyricLineTiming(0, "Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), words, syls) }
            : Array.Empty<LyricLineTiming>();

        return new LyricsTimingData("Hello", "Artist", provider, gran, lines, "Hello");
    }

    [Fact]
    public void ShortCodes_SyllableWordLineAsync_MatchExactSpecification()
    {
        var hData = LyricsTimingGranularity.TimedSyllables | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics;
        var mData = LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics;
        var lData = LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics;
        var nData = LyricsTimingGranularity.PlainLyrics;

        // 4. H-BLY, H-BLU, H-BNL
        Assert.Equal("H-BLY", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Syllable, LyricsProviderId.BetterLyrics, MakeTiming(LyricsProviderId.BetterLyrics, hData)));
        Assert.Equal("H-BLU", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Syllable, LyricsProviderId.BetterLyricsUnison, MakeTiming(LyricsProviderId.BetterLyricsUnison, hData)));
        Assert.Equal("H-BNL", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Syllable, LyricsProviderId.BiniLyrics, MakeTiming(LyricsProviderId.BiniLyrics, hData)));

        // 5. M-BLU, M-BLP
        Assert.Equal("M-BLU", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, LyricsProviderId.BetterLyricsUnison, MakeTiming(LyricsProviderId.BetterLyricsUnison, mData)));
        Assert.Equal("M-BLP", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, LyricsProviderId.BetterLyricsPortato, MakeTiming(LyricsProviderId.BetterLyricsPortato, mData)));

        // 6. L-BLY, L-BLU, L-BNL, L-LRC, L-BLL
        Assert.Equal("L-BLY", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Line, LyricsProviderId.BetterLyrics, MakeTiming(LyricsProviderId.BetterLyrics, lData)));
        Assert.Equal("L-BLU", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Line, LyricsProviderId.BetterLyricsUnison, MakeTiming(LyricsProviderId.BetterLyricsUnison, lData)));
        Assert.Equal("L-BNL", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Line, LyricsProviderId.BiniLyrics, MakeTiming(LyricsProviderId.BiniLyrics, lData)));
        Assert.Equal("L-LRC", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Line, LyricsProviderId.Lrclib, MakeTiming(LyricsProviderId.Lrclib, lData)));
        Assert.Equal("L-BLL", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Line, LyricsProviderId.BetterLyricsLegato, MakeTiming(LyricsProviderId.BetterLyricsLegato, lData)));

        // 7. N-BLU
        Assert.Equal("N-BLU", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Asynchronous, LyricsProviderId.BetterLyricsUnison, MakeTiming(LyricsProviderId.BetterLyricsUnison, nData)));
    }

    [Fact]
    public void ShortCodes_AreLanguageInvariantAcrossTurkishAndEnglish()
    {
        var loc = LocalizationService.Instance;
        var timing = MakeTiming(LyricsProviderId.BetterLyrics, LyricsTimingGranularity.TimedSyllables | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines);

        loc.SetLanguage("tr");
        string? trCode = LyricsSyncCodeFormatter.Format(LyricsSyncMode.Syllable, LyricsProviderId.BetterLyrics, timing);

        loc.SetLanguage("en");
        string? enCode = LyricsSyncCodeFormatter.Format(LyricsSyncMode.Syllable, LyricsProviderId.BetterLyrics, timing);

        Assert.Equal("H-BLY", trCode);
        Assert.Equal("H-BLY", enCode);
        Assert.Equal(trCode, enCode);
    }

    [Fact]
    public void ShortCodes_RejectMismatchedGranularityAndNeverFabricateCode()
    {
        var lineOnly = MakeTiming(LyricsProviderId.Lrclib, LyricsTimingGranularity.TimedLines);
        var wordOnly = MakeTiming(LyricsProviderId.BetterLyricsPortato, LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines);

        // Requesting H for line-only or word-only data MUST NOT produce H-LRC or H-BLP
        Assert.Null(LyricsSyncCodeFormatter.Format(LyricsSyncMode.Syllable, LyricsProviderId.Lrclib, lineOnly));
        Assert.Null(LyricsSyncCodeFormatter.Format(LyricsSyncMode.Syllable, LyricsProviderId.BetterLyricsPortato, wordOnly));
        Assert.Null(LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, LyricsProviderId.Lrclib, lineOnly));
        Assert.Null(LyricsSyncCodeFormatter.Format(LyricsSyncMode.Line, null, lineOnly));
    }

    [Fact]
    public async Task Fallback_ActivatesWhenNoSyncProviderReturnsData_AndProgressesWithPlaybackPosition()
    {
        // All sync providers return null
        var emptyProvider = new StubSyncProvider(LyricsProviderId.BetterLyrics, (_, _, _) => Task.FromResult<LyricsTimingData?>(null));
        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { emptyProvider });

        var fallbackSynced = new SyncedLyrics(
            "Fallback Song", "Fallback Artist", isSynced: true,
            new[]
            {
                new LyricLine(TimeSpan.FromSeconds(2), "First fallback line", 0, EndTime: TimeSpan.FromSeconds(5)),
                new LyricLine(TimeSpan.FromSeconds(6), "Second fallback line", 1, EndTime: TimeSpan.FromSeconds(9))
            },
            "First fallback line\nSecond fallback line");

        var fallbackClient = new StubLegacyFallbackClient(fallbackSynced);

        await using var module = new LyricsModule(
            lyricsClient: orchestrator,
            fallbackLyricsClient: fallbackClient);
        await module.InitializeAsync();
        await module.StartAsync();

        await module.HandleTrackChangedAsync(new MediaTrackInfo("Fallback Song", "Fallback Artist", "Album", TimeSpan.FromMinutes(3)));

        // 1. Fallback activated with zero fabricated provider code
        Assert.Equal(1, fallbackClient.CallCount);
        Assert.True(module.IsUsingPlaybackFallback);
        Assert.Null(module.ActiveSyncBadgeCode);
        Assert.Equal(2, module.CurrentLyrics.Lines.Count);

        // 2. Fallback lyrics progress accurately with playback position
        module.Synchronizer.EvaluatePosition(TimeSpan.FromSeconds(3));
        Assert.Equal(0, module.ActiveLineIndex);

        module.Synchronizer.EvaluatePosition(TimeSpan.FromSeconds(7));
        Assert.Equal(1, module.ActiveLineIndex);
    }

    [Fact]
    public async Task ActiveCode_UpdatesOnSourceUpgrade_AndStaleCancelledResponseNeverOverwritesCode()
    {
        var seenCodes = new List<string?>();

        var fastLrclib = new StubSyncProvider(LyricsProviderId.Lrclib, async (_, _, ct) =>
        {
            await Task.Delay(20, ct);
            return MakeTiming(LyricsProviderId.Lrclib, LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics);
        });
        var slowerBetterLyrics = new StubSyncProvider(LyricsProviderId.BetterLyrics, async (t, _, ct) =>
        {
            if (t == "Song A")
            {
                await Task.Delay(180, ct);
                return MakeTiming(LyricsProviderId.BetterLyrics, LyricsTimingGranularity.TimedSyllables | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines);
            }
            await Task.Delay(80, ct);
            return MakeTiming(LyricsProviderId.BetterLyrics, LyricsTimingGranularity.TimedSyllables | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines);
        });

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { fastLrclib, slowerBetterLyrics });
        await using var module = new LyricsModule(lyricsClient: orchestrator);
        await module.InitializeAsync();
        await module.StartAsync();

        // Track 1: starts with L-LRC then upgrades seamlessly to H-BLY
        var t1 = module.HandleTrackChangedAsync(new MediaTrackInfo("Song B", "Artist", "Album", TimeSpan.FromMinutes(3)));
        await Task.Delay(45);
        seenCodes.Add(module.ActiveSyncBadgeCode);
        await t1;
        seenCodes.Add(module.ActiveSyncBadgeCode);

        Assert.Equal("H-BLY", module.ActiveSyncBadgeCode);
        Assert.Contains("H-BLY", seenCodes);

        // Track switch: Song A slow syllable response must NOT overwrite Song C's active L-LRC code
        var lrcOnlyForC = new StubSyncProvider(LyricsProviderId.Lrclib, async (t, _, ct) =>
        {
            await Task.Delay(15, ct);
            return MakeTiming(LyricsProviderId.Lrclib, LyricsTimingGranularity.TimedLines);
        });
        var slowOnlyForA = new StubSyncProvider(LyricsProviderId.BetterLyrics, async (t, _, ct) =>
        {
            if (t == "Song A")
            {
                await Task.Delay(200, ct);
                return MakeTiming(LyricsProviderId.BetterLyrics, LyricsTimingGranularity.TimedSyllables | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines);
            }
            return null;
        });

        var orch2 = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { slowOnlyForA, lrcOnlyForC });
        await using var mod2 = new LyricsModule(lyricsClient: orch2);
        await mod2.InitializeAsync();
        await mod2.StartAsync();

        var taskA = mod2.HandleTrackChangedAsync(new MediaTrackInfo("Song A", "Artist", "Album", TimeSpan.FromMinutes(3)));
        await Task.Delay(10);
        await mod2.HandleTrackChangedAsync(new MediaTrackInfo("Song C", "Artist", "Album", TimeSpan.FromMinutes(3)));
        await taskA;

        Assert.Equal("Song C", mod2.CurrentTrack.Title);
        Assert.Equal("L-LRC", mod2.ActiveSyncBadgeCode);
    }
}
