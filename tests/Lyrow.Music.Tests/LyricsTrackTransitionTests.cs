using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Lyrow.Music.Lyrics;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyricsTrackTransitionTests : IDisposable
{
    private readonly string _tempCacheDir;

    public LyricsTrackTransitionTests()
    {
        _tempCacheDir = Path.Combine(Path.GetTempPath(), "LyrowLyricsTrackTransitionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempCacheDir);
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

    [Fact]
    public async Task TrackTransition_ImmediatelyClearsPreviousState_AndResetsTimeline()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new LocalFileLyricsCache(_tempCacheDir);
        var eventBus = new InMemoryEventBus();

        string sampleLrc = """
            [00:01.00]Line 1
            [00:05.00]Line 2
            """;
        var mockLyrics = LrcParser.Parse(sampleLrc, "Song 1", "Artist 1");

        var delayedTcs = new TaskCompletionSource<LyricsSearchResult>();
        var client = new TestableLyricsClient(mockLyrics, delayedTcs);

        var module = new LyricsModule(monitor, client, cache, eventBus);
        await module.InitializeAsync();
        await module.StartAsync();

        // 1. Play Track 1
        monitor.SimulateTrack("Song 1", "Artist 1", "Album 1", TimeSpan.FromMinutes(3));
        await Task.Delay(50);

        Assert.Equal("Song 1", module.CurrentLyrics?.Title);

        // 2. Switch to Track 2 (client will be delayed via delayedTcs)
        monitor.SimulateTrack("Song 2", "Artist 2", "Album 2", TimeSpan.FromMinutes(4));

        // Immediately after transition, prior lyrics should be wiped and timeline reset to 0
        Assert.True(module.CurrentLyrics == null || module.CurrentLyrics.Lines.Count == 0);
        Assert.Equal(TimeSpan.Zero, monitor.CurrentTimeline.Position);
        Assert.Equal(TimeSpan.FromMinutes(4), monitor.CurrentTimeline.Duration);

        // Unblock delayed task and stop
        delayedTcs.TrySetResult(new LyricsSearchResult(false, null, "None"));
        await module.StopAsync();
    }

    private sealed class TestableLyricsClient : ILyricsClient
    {
        private readonly SyncedLyrics _firstDoc;
        private readonly TaskCompletionSource<LyricsSearchResult> _delayedTcs;
        private int _callCount = 0;

        public TestableLyricsClient(SyncedLyrics firstDoc, TaskCompletionSource<LyricsSearchResult> delayedTcs)
        {
            _firstDoc = firstDoc;
            _delayedTcs = delayedTcs;
        }

        public Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                return Task.FromResult(new LyricsSearchResult(true, _firstDoc, "LRCLIB"));
            }
            return _delayedTcs.Task;
        }
    }
}
