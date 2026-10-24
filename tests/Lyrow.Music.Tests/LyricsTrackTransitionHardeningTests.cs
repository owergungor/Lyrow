using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyricsTrackTransitionHardeningTests
{
    [Fact]
    public async Task TrackTransition_ResetsTimelineAndLyricsAtomically()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new InMemoryLyricsCache();
        var client = new ConstantLyricsClient(new SyncedLyrics("Song", "Artist", true, Array.Empty<LyricLine>()));

        var module = new LyricsModule(monitor, client, cache);
        await module.InitializeAsync();
        await module.StartAsync();

        // Track 1
        var track1 = new MediaTrackInfo("Song 1", "Artist 1", "Album 1", TimeSpan.FromMinutes(3));
        await module.HandleTrackChangedAsync(track1);

        Assert.Equal("Song 1", module.CurrentTrack.Title);
        Assert.Equal(TimeSpan.Zero, module.CurrentTimeline.Position);
        Assert.Equal(MediaPlaybackStatus.Changing, module.CurrentTimeline.Status);

        // Transition to Track 2
        var track2 = new MediaTrackInfo("Song 2", "Artist 2", "Album 2", TimeSpan.FromMinutes(4));
        await module.HandleTrackChangedAsync(track2);

        Assert.Equal("Song 2", module.CurrentTrack.Title);
        Assert.Equal(TimeSpan.Zero, module.CurrentTimeline.Position);
        Assert.Equal(TimeSpan.FromMinutes(4), module.CurrentTimeline.Duration);
        Assert.Equal(MediaPlaybackStatus.Changing, module.CurrentTimeline.Status);

        await module.StopAsync();
    }

    [Fact]
    public async Task StaleLyricsResponse_IsIgnoredWhenSequenceIncrements()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new InMemoryLyricsCache();

        var slowTcs = new TaskCompletionSource<LyricsSearchResult>();
        var controlledClient = new ControlledTestLyricsClient(slowTcs);

        var module = new LyricsModule(monitor, controlledClient, cache);
        await module.InitializeAsync();
        await module.StartAsync();

        // Start slow track query
        var t1 = module.HandleTrackChangedAsync(new MediaTrackInfo("Slow Track", "Artist", "Album", TimeSpan.FromMinutes(3)));

        // Immediately switch to fast track
        await module.HandleTrackChangedAsync(new MediaTrackInfo("Fast Track", "Artist", "Album", TimeSpan.FromMinutes(3)));

        // Complete slow track after switch
        var slowLyrics = new SyncedLyrics("Slow Track", "Artist", true, new[] { new LyricLine(TimeSpan.FromSeconds(1), "Slow", 0) });
        slowTcs.SetResult(new LyricsSearchResult(true, slowLyrics, "LRCLIB"));
        await t1;

        // Current track must remain Fast Track, not overwritten by stale slow response
        Assert.Equal("Fast Track", module.CurrentTrack.Title);

        await module.StopAsync();
    }

    private sealed class ConstantLyricsClient : ILyricsClient
    {
        private readonly SyncedLyrics _lyrics;
        public ConstantLyricsClient(SyncedLyrics lyrics) => _lyrics = lyrics;

        public Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LyricsSearchResult(true, _lyrics, "LRCLIB"));
        }
    }

    private sealed class ControlledTestLyricsClient : ILyricsClient
    {
        private readonly TaskCompletionSource<LyricsSearchResult> _slowTcs;

        public ControlledTestLyricsClient(TaskCompletionSource<LyricsSearchResult> slowTcs)
        {
            _slowTcs = slowTcs;
        }

        public async Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            if (trackName == "Slow Track")
            {
                return await _slowTcs.Task;
            }

            var fastLyrics = new SyncedLyrics("Fast Track", "Artist", true, new[] { new LyricLine(TimeSpan.FromSeconds(1), "Fast", 0) });
            return new LyricsSearchResult(true, fastLyrics, "LRCLIB");
        }
    }

    private sealed class InMemoryLyricsCache : ILyricsCache
    {
        public Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
            => Task.FromResult<SyncedLyrics?>(null);

        public Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
