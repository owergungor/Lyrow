using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyricsAutomaticTransitionAndResetTests
{
    [Fact]
    public async Task AutomaticTrackTransition_ResetsDurationAndPosition_NoSummation()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new DummyLyricsCache();
        var client = new DummyLyricsClient();

        var module = new LyricsModule(monitor, client, cache);
        await module.InitializeAsync();
        await module.StartAsync();

        // 1. Track A plays (240s)
        var trackA = new MediaTrackInfo("Track A", "Artist A", "Album A", TimeSpan.FromSeconds(240));
        await module.HandleTrackChangedAsync(trackA);

        Assert.Equal("Track A", module.CurrentTrack.Title);
        Assert.Equal(TimeSpan.FromSeconds(240), module.CurrentTimeline.Duration);
        Assert.Equal(TimeSpan.Zero, module.CurrentTimeline.Position);

        // Simulate playback reaching end of track A (239s)
        module.UpdateTimelineExternal(new MediaTimelineInfo(TimeSpan.FromSeconds(239), TimeSpan.FromSeconds(240), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        Assert.Equal(TimeSpan.FromSeconds(239), module.CurrentTimeline.Position);

        // 2. Automatic Track B starts (180s)
        var trackB = new MediaTrackInfo("Track B", "Artist B", "Album B", TimeSpan.FromSeconds(180));
        await module.HandleTrackChangedAsync(trackB);

        // MUST be 180s, NEVER 240 + 180 = 420s
        Assert.Equal("Track B", module.CurrentTrack.Title);
        Assert.Equal(TimeSpan.FromSeconds(180), module.CurrentTimeline.Duration);
        Assert.NotEqual(TimeSpan.FromSeconds(420), module.CurrentTimeline.Duration);
        Assert.Equal(TimeSpan.Zero, module.CurrentTimeline.Position);

        await module.StopAsync();
    }

    [Fact]
    public async Task TrackSequenceId_ProtectsAgainstStaleOverwrites()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new DummyLyricsCache();

        var slowTcs = new TaskCompletionSource<LyricsSearchResult>();
        var client = new StaleTestLyricsClient(slowTcs);

        var module = new LyricsModule(monitor, client, cache);
        await module.InitializeAsync();
        await module.StartAsync();

        // Start slow Track A query
        var tA = module.HandleTrackChangedAsync(new MediaTrackInfo("Track A", "Artist A", "Album A", TimeSpan.FromSeconds(200)));

        // Transition immediately to Track B
        await module.HandleTrackChangedAsync(new MediaTrackInfo("Track B", "Artist B", "Album B", TimeSpan.FromSeconds(150)));

        // Late response arrives from Track A
        var slowLyricsA = new SyncedLyrics("Track A", "Artist A", true, new[] { new LyricLine(TimeSpan.FromSeconds(1), "Old Lyrics A", 0) });
        slowTcs.SetResult(new LyricsSearchResult(true, slowLyricsA, "LRCLIB"));
        await tA;

        // Current track and lyrics state must remain Track B, NOT contaminated by Track A
        Assert.Equal("Track B", module.CurrentTrack.Title);

        await module.StopAsync();
    }

    private sealed class StaleTestLyricsClient : ILyricsClient
    {
        private readonly TaskCompletionSource<LyricsSearchResult> _slowTcs;
        public StaleTestLyricsClient(TaskCompletionSource<LyricsSearchResult> slowTcs) => _slowTcs = slowTcs;

        public async Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            if (trackName == "Track A")
            {
                return await _slowTcs.Task;
            }
            var bLyrics = new SyncedLyrics("Track B", "Artist B", true, new[] { new LyricLine(TimeSpan.FromSeconds(1), "Lyrics B", 0) });
            return new LyricsSearchResult(true, bLyrics, "LRCLIB");
        }
    }

    private sealed class DummyLyricsClient : ILyricsClient
    {
        public Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LyricsSearchResult(true, new SyncedLyrics(trackName, artistName, true, Array.Empty<LyricLine>()), "Test"));
        }
    }

    private sealed class DummyLyricsCache : ILyricsCache
    {
        public Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default) => Task.FromResult<SyncedLyrics?>(null);
        public Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
