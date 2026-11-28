using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Timeline;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowSourceBadgeAndRemainingTimeTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void RemainingTime_3m30sAt1m00s_Returns2m30sRemaining()
    {
        var engine = new PlaybackTimelineEngine();
        var track = new MediaTrackInfo("Track 1", "Artist 1", "Album", TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(30));
        engine.HandleTrackChanged(track);
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromMinutes(1),
            rawDuration: track.Duration,
            rawStatus: MediaPlaybackStatus.Paused);

        engine.CurrentTrackRemaining.Should().Be(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30));
        engine.CurrentTrackRemainingFormatted.Should().Be("-2:30");
    }

    [Fact]
    public void RemainingTime_4m00sAt3m45s_Returns0m15sRemaining_AndZeroAtEnd()
    {
        var engine = new PlaybackTimelineEngine();
        var track = new MediaTrackInfo("Track 2", "Artist 2", "Album", TimeSpan.FromMinutes(4));
        engine.HandleTrackChanged(track);
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(45),
            rawDuration: track.Duration,
            rawStatus: MediaPlaybackStatus.Paused);

        engine.CurrentTrackRemaining.Should().Be(TimeSpan.FromSeconds(15));
        engine.CurrentTrackRemainingFormatted.Should().Be("-0:15");

        // At or past end of song -> never negative, displays "0:00"
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromMinutes(4),
            rawDuration: track.Duration,
            rawStatus: MediaPlaybackStatus.Paused);
        engine.CurrentTrackRemaining.Should().Be(TimeSpan.Zero);
        engine.CurrentTrackRemainingFormatted.Should().Be("0:00");
    }

    [Fact]
    public void GsmtcDurationResolver_NonZeroStartTime_ComputesExactDurationPositionAndRemaining()
    {
        // StartTime = 1:00, EndTime = 4:30 -> true duration = 3:30; RawPosition = 2:00 -> relative pos = 1:00 -> remaining = 2:30
        var (pos, dur) = GsmtcDurationResolver.Resolve(
            startTime: TimeSpan.FromMinutes(1),
            endTime: TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(30),
            minSeekTime: TimeSpan.FromMinutes(1),
            maxSeekTime: TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(30),
            rawPosition: TimeSpan.FromMinutes(2));

        dur.Should().Be(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(30));
        pos.Should().Be(TimeSpan.FromMinutes(1));

        var engine = new PlaybackTimelineEngine();
        engine.HandleTrackChanged(new MediaTrackInfo("Offset Media", "Artist", "Album", dur));
        engine.HandleTimelineProperties(pos, dur, MediaPlaybackStatus.Paused);
        engine.CurrentTrackRemainingFormatted.Should().Be("-2:30");
    }

    [Fact]
    public void GsmtcDurationResolver_InvalidUnknownOrLiveStreamEndTime_ReturnsZeroAndDisplaysPlaceholder()
    {
        // 1. Live stream / degenerate seek range (EndTime = 0, MinSeekTime == MaxSeekTime)
        var (_, liveDur) = GsmtcDurationResolver.Resolve(
            startTime: TimeSpan.Zero,
            endTime: TimeSpan.Zero,
            minSeekTime: TimeSpan.FromMinutes(10),
            maxSeekTime: TimeSpan.FromMinutes(10),
            rawPosition: TimeSpan.FromMinutes(10));
        liveDur.Should().Be(TimeSpan.Zero);

        // 2. Sentinel / absurdly huge EndTime (e.g. TimeSpan.MaxValue)
        var (_, hugeDur) = GsmtcDurationResolver.Resolve(
            startTime: TimeSpan.Zero,
            endTime: TimeSpan.MaxValue,
            minSeekTime: TimeSpan.Zero,
            maxSeekTime: TimeSpan.MaxValue,
            rawPosition: TimeSpan.FromSeconds(30));
        hugeDur.Should().Be(TimeSpan.Zero);

        // 3. Negative EndTime
        var (_, negDur) = GsmtcDurationResolver.Resolve(
            startTime: TimeSpan.Zero,
            endTime: TimeSpan.FromSeconds(-20),
            minSeekTime: TimeSpan.Zero,
            maxSeekTime: TimeSpan.Zero,
            rawPosition: TimeSpan.FromSeconds(5));
        negDur.Should().Be(TimeSpan.Zero);

        // Engine displays "--:--" when duration is unknown
        var engine = new PlaybackTimelineEngine();
        engine.HandleTrackChanged(new MediaTrackInfo("Live Radio", "Station", "", liveDur));
        engine.CurrentTrackRemainingFormatted.Should().Be("--:--");
    }

    [Fact]
    public void TrackSwitchAndLateDurationArrival_NeverCarriesOverOldDurationAndUpdatesCleanly()
    {
        var engine = new PlaybackTimelineEngine();

        // Track A: 3:30 at 2:00 -> -1:30
        var trackA = new MediaTrackInfo("Old Track", "Artist A", "Album", TimeSpan.FromSeconds(210));
        engine.HandleTrackChanged(trackA);
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(120), trackA.Duration, MediaPlaybackStatus.Paused);
        engine.CurrentTrackRemainingFormatted.Should().Be("-1:30");

        // Switch to Track B with initially unknown duration -> old duration MUST NOT linger
        var trackBInitial = new MediaTrackInfo("New Track", "Artist B", "Album", TimeSpan.Zero);
        engine.HandleTrackChanged(trackBInitial);
        engine.CurrentTrackDuration.Should().Be(TimeSpan.Zero);
        engine.CurrentTrackPosition.Should().Be(TimeSpan.Zero);
        engine.CurrentTrackRemainingFormatted.Should().Be("--:--");

        // Real duration (4:00) arrives later -> updates immediately
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(4), MediaPlaybackStatus.Paused);
        engine.CurrentTrackDuration.Should().Be(TimeSpan.FromMinutes(4));
        engine.CurrentTrackRemainingFormatted.Should().Be("-3:45");
    }

    [Fact]
    public void SeekPauseResumeAndStaleReportedUtc_KeepRemainingTimeAccurate()
    {
        var engine = new PlaybackTimelineEngine();
        var track = new MediaTrackInfo("Seek Track", "Artist", "Album", TimeSpan.FromMinutes(4));
        engine.HandleTrackChanged(track);

        // Pause freezes remaining time
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromMinutes(1),
            rawDuration: track.Duration,
            rawStatus: MediaPlaybackStatus.Paused);
        engine.CurrentTrackRemainingFormatted.Should().Be("-3:00");

        // Seek while paused -> stays paused at 2:30 (does not march forward or snap back)
        engine.HandleSeek(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30));
        engine.PlaybackStatus.Should().Be(MediaPlaybackStatus.Paused);
        engine.CurrentTrackRemainingFormatted.Should().Be("-1:30");

        // Seek forward and backward while playing -> remaining updates immediately
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30),
            rawDuration: track.Duration,
            rawStatus: MediaPlaybackStatus.Playing);
        engine.HandleSeek(TimeSpan.FromMinutes(3));
        engine.CurrentTrackRemainingFormatted.Should().Be("-1:00");
        engine.HandleSeek(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(30));
        engine.CurrentTrackRemainingFormatted.Should().Be("-2:30");
    }

    [Fact]
    public void LyricsSourceBadgeFormatter_MapsAllSupportedProvidersToExactCodes()
    {
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.BetterLyrics).Should().Be("WEB");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.AppleMusic).Should().Be("APL");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.Spotify).Should().Be("SPT");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.BetterLyricsUnison).Should().Be("BLU");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.Lrclib).Should().Be("LRC");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.QQMusic).Should().Be("QQ");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.NetEase).Should().Be("NTE");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.KuGou).Should().Be("KG");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.Kuwo).Should().Be("KW");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.Migu).Should().Be("MG");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.PetitLyrics).Should().Be("PL");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.BiniLyrics).Should().Be("BNL");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.BetterLyricsPortato).Should().Be("BLP");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.BetterLyricsLegato).Should().Be("BLL");
        LyricsSourceBadgeFormatter.GetSourceCode(LyricsProviderId.LiriQoUnknown).Should().Be("LRQ");
        LyricsSourceBadgeFormatter.GetSourceCode(null).Should().BeNull();

        LyricsSourceBadgeFormatter.FormatFallbackOrCustomSource("User Custom").Code.Should().Be("USR");
        LyricsSourceBadgeFormatter.FormatFallbackOrCustomSource("Cache").Code.Should().Be("CACHE");
        LyricsSourceBadgeFormatter.FormatFallbackOrCustomSource("LRCLIB").Code.Should().Be("LRC");
    }

    private sealed class StubSyncProvider : ILyricsSyncProvider
    {
        private readonly Func<Task<LyricsTimingData?>> _factory;
        public LyricsProviderId ProviderId { get; }
        public StubSyncProvider(LyricsProviderId id, Func<Task<LyricsTimingData?>> factory)
        {
            ProviderId = id;
            _factory = factory;
        }
        public Task<LyricsTimingData?> FetchTimingDataAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
            => _factory();
    }

    private sealed class StubMultiTrackProvider : IMultiTrackLyricsProvider
    {
        private readonly IReadOnlyList<LyricsTimingData> _tracks;
        public string AggregatorName => "StubLiriQo";
        public StubMultiTrackProvider(IReadOnlyList<LyricsTimingData> tracks) => _tracks = tracks;
        public Task<IReadOnlyList<LyricsTimingData>> FetchAllUpstreamTracksAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_tracks);
    }

    [Fact]
    public async Task SourceBadge_UpdatesOnProgressiveUpgradeAndSourceCycling_AndExistsInNormalAndCompactXaml()
    {
        // 1. Progressive upgrade: LRCLIB (Line -> "LRC") arrives first, then BetterLyrics (Syllable -> "WEB") and AppleMusic (Syllable -> "APL") arrive
        var fastLrc = new StubSyncProvider(LyricsProviderId.Lrclib, () => Task.FromResult<LyricsTimingData?>(
            new LyricsTimingData("Test Song", "Test Artist", LyricsProviderId.Lrclib,
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines,
                new[] { new LyricLineTiming(0, "Hello world", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>()) },
                "Hello world")));

        var sylBetterLyrics = new StubSyncProvider(LyricsProviderId.BetterLyrics, () =>
        {
            var syls = new[]
            {
                new LyricSyllableTiming("Hel", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.4), 0, 0),
                new LyricSyllableTiming("lo", TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(2.0), 1, 0)
            };
            var words = new[] { new LyricWordSegment("Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.0), 0, syls) };
            return Task.FromResult<LyricsTimingData?>(new LyricsTimingData("Test Song", "Test Artist", LyricsProviderId.BetterLyrics,
                LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedSyllables,
                new[] { new LyricLineTiming(0, "Hello world", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), words, syls) },
                "Hello world"));
        });

        var appleSyls = new[]
        {
            new LyricSyllableTiming("Hel", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), 0, 0),
            new LyricSyllableTiming("lo", TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2.1), 1, 0)
        };
        var appleWords = new[] { new LyricWordSegment("Hello", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.1), 0, appleSyls) };
        var appleTrack = new LyricsTimingData("Test Song", "Test Artist", LyricsProviderId.AppleMusic,
            LyricsTimingGranularity.PlainLyrics | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedSyllables,
            new[] { new LyricLineTiming(0, "Hello world", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), appleWords, appleSyls) },
            "Hello world");

        var orchestrator = new MultiProviderLyricsOrchestrator(
            new ILyricsSyncProvider[] { fastLrc, sylBetterLyrics },
            multiTrackProviders: new IMultiTrackLyricsProvider[] { new StubMultiTrackProvider(new[] { appleTrack }) });
        var module = new LyricsModule(mediaSessionMonitor: null, lyricsClient: orchestrator, lyricsCache: null);
        await module.HandleTrackChangedAsync(new MediaTrackInfo("Test Song", "Test Artist", "Album", TimeSpan.FromSeconds(180)));

        // Upgraded from LRCLIB (Line) to highest-quality Syllable source -> source badge is "WEB" or "APL" (never mixed with sync quality letter!)
        string initialCode = module.ActiveSourceBadgeCode!;
        initialCode.Should().BeOneOf("WEB", "APL");
        string expectedOtherCode = initialCode == "WEB" ? "APL" : "WEB";

        // Cycling source switches immediately to the other verified Syllable source
        module.CanCycleSources.Should().BeTrue();
        module.TryCycleSource(1).Should().BeTrue();
        module.ActiveSourceBadgeCode.Should().Be(expectedOtherCode);

        // 2. Verify both Normal and Compact lyrics views declare the source badge controls in XAML
        string lyricsXaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        lyricsXaml.Should().Contain("x:Name=\"BorderLyricsSourceBadge\"");
        lyricsXaml.Should().Contain("x:Name=\"TxtLyricsSourceBadge\"");
        lyricsXaml.Should().Contain("x:Name=\"BorderLyricsSourceBadgeCompact\"");
        lyricsXaml.Should().Contain("x:Name=\"TxtLyricsSourceBadgeCompact\"");
    }
}
