using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Lyrow.Music.Lyrics;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class VerciModuleIntegrationTests : IDisposable
{
    private readonly string _tempCacheDir;

    public VerciModuleIntegrationTests()
    {
        _tempCacheDir = Path.Combine(Path.GetTempPath(), "Lyrow_VerciTest_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task VerciModule_ShouldFetchLyricsAndTrackTimeline_IndependentlyFromLumn()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new LocalFileLyricsCache(_tempCacheDir);

        string sampleLrc = """
            [00:02.00]First verse
            [00:08.00]Second verse
            [00:15.00]Chorus
            """;
        var mockLyrics = LrcParser.Parse(sampleLrc, "Test Song", "Test Artist");
        var client = new MockLyricsClient(new LyricsSearchResult(true, mockLyrics, "LRCLIB"));

        var verci = new VerciModule(monitor, client, cache, eventBus);

        var trackEvents = new List<MediaTrackInfo>();
        var lyricsEvents = new List<LyricsLoadedEvent>();
        var lineEvents = new List<ActiveLyricLineChangedEvent>();

        using var subTrack = eventBus.Subscribe<MediaTrackChangedEvent>(e =>
        {
            lock (trackEvents) trackEvents.Add(e.Track);
        });
        using var subLyrics = eventBus.Subscribe<LyricsLoadedEvent>(e =>
        {
            lock (lyricsEvents) lyricsEvents.Add(e);
        });
        using var subLine = eventBus.Subscribe<ActiveLyricLineChangedEvent>(e =>
        {
            lock (lineEvents) lineEvents.Add(e);
        });

        // Act - Initialize and Start
        await verci.InitializeAsync();
        verci.State.Should().Be(ModuleState.Initialized);

        await verci.StartAsync();
        verci.State.Should().Be(ModuleState.Running);
        monitor.IsMonitoring.Should().BeTrue();

        // 1. Simulate new track detected by GSMTC
        monitor.SimulateTrack("Test Song", "Test Artist", "Test Album", TimeSpan.FromMinutes(3));
        await Task.Delay(100);

        // Assert - Track and Lyrics loaded
        lock (trackEvents)
        {
            trackEvents.Should().ContainSingle(t => t.Title == "Test Song" && t.Artist == "Test Artist");
        }
        lock (lyricsEvents)
        {
            lyricsEvents.Should().ContainSingle(l => l.Source == "LRCLIB" && l.Lyrics.Lines.Count == 3);
        }

        verci.CurrentLyrics.Lines.Should().HaveCount(3);

        // 2. Simulate playback timeline advancing to 5 seconds (within line 0 at 2s)
        monitor.SimulatePlaybackState(MediaPlaybackStatus.Playing, TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        verci.ActiveLineIndex.Should().Be(0);

        // 3. Simulate Seek to 16 seconds (within line 2 at 15s)
        monitor.SimulateSeek(TimeSpan.FromSeconds(16));
        await Task.Delay(50);

        verci.ActiveLineIndex.Should().Be(2);

        // 4. Verify disk caching occurred
        var cached = await cache.GetAsync("Test Song", "Test Artist");
        cached.Should().NotBeNull();
        cached!.Lines.Should().HaveCount(3);

        // 5. Stop Verci
        await verci.StopAsync();
        verci.State.Should().Be(ModuleState.Stopped);
        monitor.IsMonitoring.Should().BeFalse();
        verci.CurrentLyrics.Lines.Should().BeEmpty();

        await verci.DisposeAsync();
    }

    [Fact]
    public async Task VerciModule_ShouldHandleOfflineCacheGracefully_WhenNetworkClientFails()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new LocalFileLyricsCache(_tempCacheDir);
        var failingClient = new MockLyricsClient(new LyricsSearchResult(false, null, "LRCLIB", "Network error"));

        // Pre-populate cache
        var preCached = LrcParser.Parse("[00:05.00]Pre-cached lyric line", "Cached Song", "Cached Artist");
        await cache.SaveAsync("Cached Song", "Cached Artist", preCached);

        var verci = new VerciModule(monitor, failingClient, cache);
        await verci.InitializeAsync();
        await verci.StartAsync();

        // Simulate track change for pre-cached song
        monitor.SimulateTrack("Cached Song", "Cached Artist", "Album", TimeSpan.FromMinutes(2));

        // Deterministically wait for lyrics to be loaded from cache (polling up to 3 seconds)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(3) &&
               !verci.CurrentLyrics.Lines.Any(l => l.Text == "Pre-cached lyric line"))
        {
            await Task.Delay(25);
        }

        // Should successfully use cache even though network client fails!
        verci.CurrentLyrics.Lines.Should().ContainSingle(l => l.Text == "Pre-cached lyric line");

        await verci.StopAsync();
        await verci.DisposeAsync();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempCacheDir))
            {
                Directory.Delete(_tempCacheDir, true);
            }
        }
        catch { }
    }

    private sealed class MockLyricsClient : ILyricsClient
    {
        private readonly LyricsSearchResult _result;

        public MockLyricsClient(LyricsSearchResult result)
        {
            _result = result;
        }

        public Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
