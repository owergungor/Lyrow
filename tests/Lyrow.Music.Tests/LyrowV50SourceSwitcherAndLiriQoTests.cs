using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyrowV50SourceSwitcherAndLiriQoTests
{
    private const string LiriQoMultiProviderSampleJson = """
{
  "count": 5,
  "tracks": [
    {
      "provider": "apple_music_liriqo",
      "upstreamProvider": "Apple Music",
      "syncLevel": "word",
      "nativeWordTiming": true,
      "title": "Shape of You",
      "artist": "Ed Sheeran",
      "timed": [
        {
          "id": 0,
          "text": "The club isn't the best place",
          "start": 9731,
          "end": 12105,
          "words": [
            { "text": "The", "start": 9731, "end": 9927 },
            { "text": "club", "start": 9927, "end": 10284 }
          ]
        }
      ]
    },
    {
      "provider": "qq",
      "upstreamProvider": "QQ Music",
      "syncLevel": "word",
      "nativeWordTiming": true,
      "title": "Shape of You",
      "artist": "Ed Sheeran",
      "timed": [
        {
          "id": 0,
          "text": "The club isn't the best place",
          "start": 9750,
          "end": 12100,
          "words": [
            { "text": "The", "start": 9750, "end": 9950 },
            { "text": "club", "start": 9950, "end": 10300 }
          ]
        }
      ]
    },
    {
      "provider": "kuwo",
      "upstreamProvider": "Kuwo",
      "syncLevel": "character",
      "nativeWordTiming": true,
      "title": "Shape of You",
      "artist": "Ed Sheeran",
      "timed": [
        {
          "id": 0,
          "text": "Hello",
          "start": 1000,
          "end": 2000,
          "words": [
            { "text": "H", "start": 1000, "end": 1200 },
            { "text": "e", "start": 1200, "end": 1400 },
            { "text": "l", "start": 1400, "end": 1600 },
            { "text": "l", "start": 1600, "end": 1800 },
            { "text": "o", "start": 1800, "end": 2000 }
          ]
        }
      ]
    },
    {
      "provider": "netease",
      "upstreamProvider": "NetEase",
      "syncLevel": "word",
      "nativeWordTiming": false,
      "title": "Shape of You",
      "artist": "Ed Sheeran",
      "timed": [
        {
          "id": 0,
          "text": "Synthetic words rejected",
          "start": 1000,
          "end": 3000,
          "words": [
            { "text": "Synthetic", "start": 1000, "end": 1666 },
            { "text": "words", "start": 1666, "end": 2333 },
            { "text": "rejected", "start": 2333, "end": 3000 }
          ]
        }
      ]
    },
    {
      "provider": "lrclib",
      "upstreamProvider": "LRCLIB",
      "syncLevel": "line",
      "title": "Shape of You",
      "artist": "Ed Sheeran",
      "timed": [
        { "id": 0, "text": "The club isn't the best place", "start": 9731, "end": 12105 }
      ]
    }
  ]
}
""";

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

    private sealed class StubMultiTrackAggregator : IMultiTrackLyricsProvider
    {
        private readonly Func<string, string, IReadOnlyList<LyricsTimingData>> _fn;
        public StubMultiTrackAggregator(Func<string, string, IReadOnlyList<LyricsTimingData>> fn) => _fn = fn;
        public string AggregatorName => "LiriQo";
        public Task<IReadOnlyList<LyricsTimingData>> FetchAllUpstreamTracksAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_fn(trackName, artistName));
    }

    private static LyricsTimingData MakeSyllableTiming(LyricsProviderId id, string lineText)
    {
        var syls = new[]
        {
            new LyricSyllableTiming("May", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), 0, 0, false),
            new LyricSyllableTiming("be", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.0), 1, 0, true)
        };
        var words = new[] { new LyricWordSegment("Maybe", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 0, syls) };
        return new LyricsTimingData("Track", "Artist", id,
            LyricsTimingGranularity.TimedSyllables | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics,
            new[] { new LyricLineTiming(0, lineText, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), words, syls) },
            lineText);
    }

    [Fact]
    public void LiriQoParser_ValidatesGranularity_SeparatesCharacterFromSyllable_AndRejectsSyntheticWords()
    {
        var parsed = LiriQoClient.ParseResponseJson(LiriQoMultiProviderSampleJson, "Shape of You", "Ed Sheeran");
        Assert.Equal(5, parsed.Count);

        var apple = parsed.Single(t => t.SourceProvider == LyricsProviderId.AppleMusic);
        Assert.True(apple.HasTimedWords);
        Assert.False(apple.HasTimedSyllables);
        Assert.Equal("M-APL", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, apple.SourceProvider, apple));

        var qq = parsed.Single(t => t.SourceProvider == LyricsProviderId.QQMusic);
        Assert.True(qq.HasTimedWords);
        Assert.Equal("M-QQ", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, qq.SourceProvider, qq));

        // Character-level Latin split from Kuwo MUST NOT be classified as Syllable (H) or Word (M)!
        var kuwo = parsed.Single(t => t.SourceProvider == LyricsProviderId.Kuwo);
        Assert.False(kuwo.HasTimedSyllables);
        Assert.False(kuwo.HasTimedWords);
        Assert.True(kuwo.HasTimedLines);
        Assert.Null(LyricsSyncCodeFormatter.Format(LyricsSyncMode.Syllable, kuwo.SourceProvider, kuwo));
        Assert.Equal("L-KWO", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Line, kuwo.SourceProvider, kuwo));

        // nativeWordTiming: false MUST NOT be classified as Word (M)
        var netease = parsed.Single(t => t.SourceProvider == LyricsProviderId.NetEase);
        Assert.False(netease.HasTimedWords);
        Assert.True(netease.HasTimedLines);
        Assert.Equal("L-NET", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Line, netease.SourceProvider, netease));
    }

    [Fact]
    public void NewProviderShortCodes_AreInvariantAcrossEnglishAndTurkish()
    {
        var wordGran = LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines;
        var loc = LocalizationService.Instance;

        foreach (var lang in new[] { "en", "tr" })
        {
            loc.SetLanguage(lang);
            Assert.Equal("H-APL", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Syllable, LyricsProviderId.AppleMusic, MakeSyllableTiming(LyricsProviderId.AppleMusic, "Maybe")));
            Assert.Equal("M-QQ", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, LyricsProviderId.QQMusic, new LyricsTimingData("t", "a", LyricsProviderId.QQMusic, wordGran, new[] { new LyricLineTiming(0, "Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), new[] { new LyricWordSegment("Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, Array.Empty<LyricSyllableTiming>()) }, Array.Empty<LyricSyllableTiming>()) }, "Hi")));
            Assert.Equal("M-NET", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, LyricsProviderId.NetEase, new LyricsTimingData("t", "a", LyricsProviderId.NetEase, wordGran, new[] { new LyricLineTiming(0, "Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), new[] { new LyricWordSegment("Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, Array.Empty<LyricSyllableTiming>()) }, Array.Empty<LyricSyllableTiming>()) }, "Hi")));
            Assert.Equal("M-KUG", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, LyricsProviderId.KuGou, new LyricsTimingData("t", "a", LyricsProviderId.KuGou, wordGran, new[] { new LyricLineTiming(0, "Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), new[] { new LyricWordSegment("Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, Array.Empty<LyricSyllableTiming>()) }, Array.Empty<LyricSyllableTiming>()) }, "Hi")));
            Assert.Equal("M-MIG", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, LyricsProviderId.Migu, new LyricsTimingData("t", "a", LyricsProviderId.Migu, wordGran, new[] { new LyricLineTiming(0, "Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), new[] { new LyricWordSegment("Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, Array.Empty<LyricSyllableTiming>()) }, Array.Empty<LyricSyllableTiming>()) }, "Hi")));
            Assert.Equal("M-PET", LyricsSyncCodeFormatter.Format(LyricsSyncMode.Word, LyricsProviderId.PetitLyrics, new LyricsTimingData("t", "a", LyricsProviderId.PetitLyrics, wordGran, new[] { new LyricLineTiming(0, "Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), new[] { new LyricWordSegment("Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), 0, Array.Empty<LyricSyllableTiming>()) }, Array.Empty<LyricSyllableTiming>()) }, "Hi")));
            // Unknown upstream must never receive a fabricated code
            Assert.Null(LyricsSyncCodeFormatter.Format(LyricsSyncMode.Line, LyricsProviderId.LiriQoUnknown, new LyricsTimingData("t", "a", LyricsProviderId.LiriQoUnknown, LyricsTimingGranularity.TimedLines, new[] { new LyricLineTiming(0, "Hi", TimeSpan.Zero, TimeSpan.FromSeconds(2), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>()) }, "Hi")));
        }

        loc.SetLanguage("en");
    }

    [Fact]
    public async Task Orchestrator_DeduplicatesSameUpstreamAcrossDirectAndAggregatorAdapters()
    {
        // Direct LRCLIB AND LiriQo both return LRCLIB line lyrics -> AvailableSources must contain ONLY ONE L-LRC!
        var directLrc = new StubSyncProvider(LyricsProviderId.Lrclib, (_, _, _) =>
            Task.FromResult<LyricsTimingData?>(new LyricsTimingData("Song", "Artist", LyricsProviderId.Lrclib,
                LyricsTimingGranularity.TimedLines,
                new[] { new LyricLineTiming(0, "Direct LRC", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>()) },
                "Direct LRC")));

        var liriqoDup = new StubMultiTrackAggregator((_, _) => new[]
        {
            new LyricsTimingData("Song", "Artist", LyricsProviderId.Lrclib,
                LyricsTimingGranularity.TimedLines,
                new[] { new LyricLineTiming(0, "LiriQo LRC", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>()) },
                "LiriQo LRC")
        });

        var orchestrator = new MultiProviderLyricsOrchestrator(
            new ILyricsSyncProvider[] { directLrc },
            multiTrackProviders: new IMultiTrackLyricsProvider[] { liriqoDup });

        var res = await orchestrator.ResolveLyricsProgressiveAsync("Song", "Artist", LyricsSyncMode.Automatic);

        Assert.True(res.Success);
        Assert.NotNull(res.AvailableSources);
        Assert.Single(res.AvailableSources!);
        Assert.Equal("L-LRC", res.AvailableSources![0].BadgeCode);
    }

    [Fact]
    public async Task SourceSwitcher_CyclesCircularlyWithinSameQuality_PersistsForTrack_AndResetsOnNewTrack()
    {
        var bly = new StubSyncProvider(LyricsProviderId.BetterLyrics, (_, _, _) =>
            Task.FromResult<LyricsTimingData?>(MakeSyllableTiming(LyricsProviderId.BetterLyrics, "BLY Syllable Lyrics")));

        var liriqo = new StubMultiTrackAggregator((_, _) => new[]
        {
            MakeSyllableTiming(LyricsProviderId.AppleMusic, "APL Syllable Lyrics")
        });

        var orchestrator = new MultiProviderLyricsOrchestrator(
            new ILyricsSyncProvider[] { bly },
            multiTrackProviders: new IMultiTrackLyricsProvider[] { liriqo });

        await using var module = new LyricsModule(lyricsClient: orchestrator);
        await module.InitializeAsync();
        await module.StartAsync();

        await module.HandleTrackChangedAsync(new MediaTrackInfo("Track 1", "Artist", "Album", TimeSpan.FromMinutes(3)));

        // Automatic selects highest priority: H-BLY; two H sources available (H-BLY, H-APL)
        Assert.Equal("H-BLY", module.ActiveSyncBadgeCode);
        Assert.True(module.CanCycleSources);
        Assert.Equal(2, module.AvailableSourcesForCurrentQuality.Count);
        Assert.False(module.HasManualSourceSelection);

        // Press > (next): switches to H-APL and updates lyrics + timing
        Assert.True(module.TryCycleSource(+1));
        Assert.Equal("H-APL", module.ActiveSyncBadgeCode);
        Assert.Equal("APL Syllable Lyrics", module.CurrentLyrics.Lines[0].Text);
        Assert.True(module.HasManualSourceSelection);

        // Press > again: wraps around circularly back to H-BLY
        Assert.True(module.TryCycleSource(+1));
        Assert.Equal("H-BLY", module.ActiveSyncBadgeCode);
        Assert.Equal("BLY Syllable Lyrics", module.CurrentLyrics.Lines[0].Text);

        // Press < (previous): wraps backward to H-APL
        Assert.True(module.TryCycleSource(-1));
        Assert.Equal("H-APL", module.ActiveSyncBadgeCode);

        // Seek / Pause / Resume continue working cleanly after manual source switch
        var seekSeg = module.SyncEngine.Seek(TimeSpan.FromSeconds(1.2));
        Assert.Equal(0, seekSeg.LineIndex);
        Assert.Equal(0, seekSeg.SyllableIndex);
        var pauseSeg = module.SyncEngine.Pause(TimeSpan.FromSeconds(1.2));
        Assert.True(pauseSeg.IsPaused);
        var resumeSeg = module.SyncEngine.Resume(TimeSpan.FromSeconds(1.7));
        Assert.Equal(1, resumeSeg.SyllableIndex);

        // New track resets manual selection back to Automatic priority (H-BLY)
        await module.HandleTrackChangedAsync(new MediaTrackInfo("Track 2", "Artist", "Album", TimeSpan.FromMinutes(3)));
        Assert.False(module.HasManualSourceSelection);
        Assert.Equal("H-BLY", module.ActiveSyncBadgeCode);
    }

    [Fact]
    public async Task SourceSwitcher_SingleSourceDisablesArrows()
    {
        var onlyBly = new StubSyncProvider(LyricsProviderId.BetterLyrics, (_, _, _) =>
            Task.FromResult<LyricsTimingData?>(MakeSyllableTiming(LyricsProviderId.BetterLyrics, "Only BLY")));

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { onlyBly });
        await using var module = new LyricsModule(lyricsClient: orchestrator);
        await module.InitializeAsync();
        await module.StartAsync();

        await module.HandleTrackChangedAsync(new MediaTrackInfo("Solo Track", "Artist", "Album", TimeSpan.FromMinutes(3)));

        Assert.Equal("H-BLY", module.ActiveSyncBadgeCode);
        Assert.False(module.CanCycleSources);
        Assert.False(module.TryCycleSource(+1));
        Assert.Equal("H-BLY", module.ActiveSyncBadgeCode);
    }
}
