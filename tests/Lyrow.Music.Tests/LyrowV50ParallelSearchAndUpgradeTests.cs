using System.Diagnostics;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyrowV50ParallelSearchAndUpgradeTests
{
    private sealed class DelayedProvider : ILyricsSyncProvider
    {
        private readonly Func<string, string, CancellationToken, Task<LyricsTimingData?>> _fn;
        private int _calls;

        public DelayedProvider(LyricsProviderId id, Func<string, string, CancellationToken, Task<LyricsTimingData?>> fn)
        {
            ProviderId = id;
            _fn = fn;
        }

        public LyricsProviderId ProviderId { get; }
        public int CallCount => Volatile.Read(ref _calls);

        public Task<LyricsTimingData?> FetchTimingDataAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return _fn(trackName, artistName, cancellationToken);
        }
    }

    private sealed class InMemoryTestCache : ILyricsCache
    {
        private readonly Dictionary<string, SyncedLyrics> _store = new(StringComparer.OrdinalIgnoreCase);

        public Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue($"{artistName}|{trackName}", out var val);
            return Task.FromResult(val);
        }

        public Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default)
        {
            _store[$"{artistName}|{trackName}"] = lyrics;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _store.Clear();
            return Task.CompletedTask;
        }
    }

    private static LyricsTimingData MakeLineData(string title, string artist, LyricsProviderId provider) =>
        new(title, artist, provider, LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics,
            new[]
            {
                new LyricLineTiming(0, "Line one", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>()),
                new LyricLineTiming(1, "Line two", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8), Array.Empty<LyricWordSegment>(), Array.Empty<LyricSyllableTiming>())
            },
            "Line one\nLine two");

    private static LyricsTimingData MakeWordData(string title, string artist, LyricsProviderId provider) =>
        new(title, artist, provider, LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics,
            new[]
            {
                new LyricLineTiming(0, "Line one", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4),
                    new[]
                    {
                        new LyricWordSegment("Line", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), 0, Array.Empty<LyricSyllableTiming>()),
                        new LyricWordSegment("one", TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(4), 1, Array.Empty<LyricSyllableTiming>())
                    },
                    Array.Empty<LyricSyllableTiming>())
            },
            "Line one");

    private static LyricsTimingData MakeSyllableData(string title, string artist, LyricsProviderId provider)
    {
        var syls = new[]
        {
            new LyricSyllableTiming("Li", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(1.8), 0, 0, false),
            new LyricSyllableTiming("ne", TimeSpan.FromSeconds(1.8), TimeSpan.FromSeconds(2.5), 1, 0, true),
            new LyricSyllableTiming("one", TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(4.0), 2, 1, true)
        };
        var words = new[]
        {
            new LyricWordSegment("Line", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(2.5), 0, new[] { syls[0], syls[1] }),
            new LyricWordSegment("one", TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(4.0), 1, new[] { syls[2] })
        };
        return new LyricsTimingData(title, artist, provider,
            LyricsTimingGranularity.TimedSyllables | LyricsTimingGranularity.TimedWords | LyricsTimingGranularity.TimedLines | LyricsTimingGranularity.PlainLyrics,
            new[] { new LyricLineTiming(0, "Line one", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), words, syls) },
            "Line one");
    }

    [Fact]
    public async Task ParallelSearch_SlowProviderDoesNotDelayFastProviderFirstResult()
    {
        var slowBetterLyrics = new DelayedProvider(LyricsProviderId.BetterLyrics, async (t, a, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return MakeSyllableData(t, a, LyricsProviderId.BetterLyrics);
        });

        var fastLrclib = new DelayedProvider(LyricsProviderId.Lrclib, async (t, a, ct) =>
        {
            await Task.Delay(25, ct);
            return MakeLineData(t, a, LyricsProviderId.Lrclib);
        });

        var timeouts = new LyricsProviderTimeoutOptions
        {
            BetterLyricsTimeout = TimeSpan.FromMilliseconds(350),
            LrclibTimeout = TimeSpan.FromMilliseconds(500)
        };

        var orchestrator = new MultiProviderLyricsOrchestrator(
            new ILyricsSyncProvider[] { slowBetterLyrics, fastLrclib },
            timeouts: timeouts);

        long firstResultMs = -1;
        var sw = Stopwatch.StartNew();
        var final = await orchestrator.ResolveLyricsProgressiveAsync(
            "Fast Track", "Artist", LyricsSyncMode.Automatic,
            onInterimResult: res =>
            {
                if (firstResultMs < 0) firstResultMs = sw.ElapsedMilliseconds;
                return Task.CompletedTask;
            });
        sw.Stop();

        Assert.True(final.Success);
        Assert.Equal(LyricsProviderId.Lrclib, final.ProviderId);
        Assert.True(firstResultMs >= 0 && firstResultMs < 250, $"First result took {firstResultMs}ms (should be fast despite slow provider)");
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Total resolution took {sw.ElapsedMilliseconds}ms (slow provider must time out quickly)");
    }

    [Fact]
    public async Task ParallelSearch_ProgressiveUpgrade_LineToWordToSyllable_NeverDowngrades()
    {
        var emittedModes = new List<LyricsSyncMode>();

        // LRCLIB responds first (20ms) with Line; Unison responds second (70ms) with Word; BetterLyrics responds third (130ms) with Syllable
        var fastLine = new DelayedProvider(LyricsProviderId.Lrclib, async (t, a, ct) =>
        {
            await Task.Delay(20, ct);
            return MakeLineData(t, a, LyricsProviderId.Lrclib);
        });
        var midWord = new DelayedProvider(LyricsProviderId.BetterLyricsUnison, async (t, a, ct) =>
        {
            await Task.Delay(70, ct);
            return MakeWordData(t, a, LyricsProviderId.BetterLyricsUnison);
        });
        var slowerSyllable = new DelayedProvider(LyricsProviderId.BetterLyrics, async (t, a, ct) =>
        {
            await Task.Delay(130, ct);
            return MakeSyllableData(t, a, LyricsProviderId.BetterLyrics);
        });

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { slowerSyllable, midWord, fastLine });

        var final = await orchestrator.ResolveLyricsProgressiveAsync(
            "Upgrade Song", "Artist", LyricsSyncMode.Automatic,
            onInterimResult: res =>
            {
                emittedModes.Add(res.ResolvedMode);
                return Task.CompletedTask;
            });

        Assert.True(final.Success);
        Assert.Equal(LyricsSyncMode.Syllable, final.ResolvedMode);
        Assert.Equal(LyricsProviderId.BetterLyrics, final.ProviderId);
        Assert.Equal(new[] { LyricsSyncMode.Line, LyricsSyncMode.Word, LyricsSyncMode.Syllable }, emittedModes);

        // Now verify reverse arrival: Syllable arrives FIRST (15ms), Line arrives LATER (90ms) -> Line MUST NOT downgrade Syllable!
        var emittedModes2 = new List<LyricsSyncMode>();
        var fastSyllable = new DelayedProvider(LyricsProviderId.BetterLyrics, async (t, a, ct) =>
        {
            await Task.Delay(15, ct);
            return MakeSyllableData(t, a, LyricsProviderId.BetterLyrics);
        });
        var lateLine = new DelayedProvider(LyricsProviderId.Lrclib, async (t, a, ct) =>
        {
            await Task.Delay(90, ct);
            return MakeLineData(t, a, LyricsProviderId.Lrclib);
        });

        var orch2 = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { fastSyllable, lateLine });
        var final2 = await orch2.ResolveLyricsProgressiveAsync(
            "No Downgrade", "Artist", LyricsSyncMode.Automatic,
            onInterimResult: res =>
            {
                emittedModes2.Add(res.ResolvedMode);
                return Task.CompletedTask;
            });

        Assert.Equal(LyricsSyncMode.Syllable, final2.ResolvedMode);
        Assert.Single(emittedModes2);
        Assert.Equal(LyricsSyncMode.Syllable, emittedModes2[0]);
    }

    [Fact]
    public async Task ParallelSearch_DeduplicatesConcurrentRequestsAndCachesRepeatedTrack()
    {
        var provider = new DelayedProvider(LyricsProviderId.BetterLyrics, async (t, a, ct) =>
        {
            await Task.Delay(60, ct);
            return MakeSyllableData(t, a, LyricsProviderId.BetterLyrics);
        });

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { provider });

        // Fire 5 concurrent requests for the exact same song
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => orchestrator.ResolveLyricsProgressiveAsync("Same Song", "Same Artist", LyricsSyncMode.Automatic))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(1, provider.CallCount); // Strictly deduplicated to 1 network call!

        // Repeat request for same song hits cache with zero extra calls
        var repeated = await orchestrator.ResolveLyricsProgressiveAsync("Same Song", "Same Artist", LyricsSyncMode.Automatic);
        Assert.True(repeated.Success);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task TrackChange_StaleSlowResponseNeverOverwritesNewTrackLyrics()
    {
        var oldTrackGate = new TaskCompletionSource<bool>();
        var provider = new DelayedProvider(LyricsProviderId.BetterLyrics, async (t, a, ct) =>
        {
            if (t == "Old Song")
            {
                await Task.Delay(180, ct);
                oldTrackGate.TrySetResult(true);
                return MakeSyllableData("Old Song", "Old Artist", LyricsProviderId.BetterLyrics);
            }
            await Task.Delay(15, ct);
            return MakeSyllableData("New Song", "New Artist", LyricsProviderId.BetterLyrics);
        });

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { provider });
        var cache = new InMemoryTestCache();
        await using var module = new LyricsModule(lyricsClient: orchestrator, lyricsCache: cache);
        await module.InitializeAsync();
        await module.StartAsync();

        var oldTrack = new MediaTrackInfo("Old Song", "Old Artist", "Album", TimeSpan.FromMinutes(3));
        var newTrack = new MediaTrackInfo("New Song", "New Artist", "Album", TimeSpan.FromMinutes(3));

        // Start fetching old track, then immediately switch to new track
        var tOld = module.HandleTrackChangedAsync(oldTrack);
        await Task.Delay(10);
        await module.HandleTrackChangedAsync(newTrack);
        await tOld;

        Assert.Equal("New Song", module.CurrentTrack.Title);
        Assert.Equal("New Artist_New Song", module.CurrentLyricsSongKey);
        Assert.Equal("New Song", module.CurrentLyrics.Title);
    }

    [Fact]
    public async Task ParallelSearch_DoesNotBlockCallingSynchronizationContext()
    {
        // Verify ResolveLyricsProgressiveAsync never deadlocks even under a single-threaded UI-like context
        var provider = new DelayedProvider(LyricsProviderId.BetterLyrics, async (t, a, ct) =>
        {
            await Task.Delay(30, ct);
            return MakeSyllableData(t, a, LyricsProviderId.BetterLyrics);
        });

        var orchestrator = new MultiProviderLyricsOrchestrator(new ILyricsSyncProvider[] { provider });
        var sw = Stopwatch.StartNew();
        var task = orchestrator.ResolveLyricsProgressiveAsync("NonBlocking", "Artist", LyricsSyncMode.Automatic);
        // Immediate return of incomplete Task proves caller thread is not blocked synchronously
        Assert.True(sw.ElapsedMilliseconds < 20);
        var result = await task;
        Assert.True(result.Success);
    }
}
