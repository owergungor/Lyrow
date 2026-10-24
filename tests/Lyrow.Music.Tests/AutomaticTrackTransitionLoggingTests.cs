using System;
using System.Threading;
using System.Threading.Tasks;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Session;
using Xunit;
using Xunit.Abstractions;

namespace Lyrow.Music.Tests;

public class AutomaticTrackTransitionLoggingTests
{
    private readonly ITestOutputHelper _output;

    public AutomaticTrackTransitionLoggingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AutomaticTrackTransition_LogsAndVerifiesStateIsolation()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new MemoryLyricsCache();
        var client = new MockTransitionLyricsClient();

        var module = new LyricsModule(monitor, client, cache);
        await module.InitializeAsync();
        await module.StartAsync();

        // 1. Track A plays to completion (240s)
        var trackA = new MediaTrackInfo(
            Title: "Track A",
            Artist: "Artist A",
            Album: "Album A",
            Duration: TimeSpan.FromSeconds(240),
            ThumbnailData: new byte[] { 0xAA, 0xAA });

        await module.HandleTrackChangedAsync(trackA);
        module.UpdateTimelineExternal(new MediaTimelineInfo(
            TimeSpan.FromSeconds(239.5),
            TimeSpan.FromSeconds(240),
            MediaPlaybackStatus.Playing,
            DateTime.UtcNow));

        var durA = module.CurrentTimeline.Duration;

        // 2. Track A finishes and automatic transition occurs to Track B (180s)
        var trackB = new MediaTrackInfo(
            Title: "Track B",
            Artist: "Artist B",
            Album: "Album B",
            Duration: TimeSpan.FromSeconds(180),
            ThumbnailData: new byte[] { 0xBB, 0xBB });

        await module.HandleTrackChangedAsync(trackB);

        var durBInitial = module.CurrentTrack.Duration;
        var posBInitial = module.CurrentTimeline.Position;
        var durBAfterTransition = module.CurrentTimeline.Duration;
        var lyricsCountB = module.CurrentLyrics.Lines.Count;
        var artworkIdentityB = module.CurrentTrack.ThumbnailData != null ? "artwork-b" : "none";
        long sequence = module.TrackSequence;

        _output.WriteLine($"Track A duration: {durA.TotalSeconds}s");
        _output.WriteLine($"Track B duration: {durBInitial.TotalSeconds}s");
        _output.WriteLine($"Track B initial position: {posBInitial.TotalSeconds}s");
        _output.WriteLine($"Track B duration after transition: {durBAfterTransition.TotalSeconds}s");
        _output.WriteLine($"Track B lyrics count: {lyricsCountB}");
        _output.WriteLine($"Track B artwork identity: {artworkIdentityB}");
        _output.WriteLine($"trackSequence/generation: {sequence}");

        // Assertions matching Section 4 specification
        Assert.True(posBInitial == TimeSpan.Zero, "Track B initial position must be 0");
        Assert.Equal(TimeSpan.FromSeconds(180), durBAfterTransition);
        Assert.NotEqual(TimeSpan.FromSeconds(420), durBAfterTransition);
        Assert.Equal("Track B", module.CurrentTrack.Title);
        Assert.Equal(3, lyricsCountB);
        Assert.Equal("Lyrics line 1 for Track B", module.CurrentLyrics.Lines[0].Text);
        Assert.Equal(new byte[] { 0xBB, 0xBB }, module.CurrentTrack.ThumbnailData);
        Assert.True(sequence >= 2, "trackSequence should be incremented atomically");

        await module.StopAsync();
    }

    private sealed class MockTransitionLyricsClient : ILyricsClient
    {
        public Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            if (trackName == "Track A")
            {
                var lyricsA = new SyncedLyrics("Track A", "Artist A", true, new[]
                {
                    new LyricLine(TimeSpan.FromSeconds(10), "Lyrics line 1 for Track A", 0),
                    new LyricLine(TimeSpan.FromSeconds(20), "Lyrics line 2 for Track A", 1)
                });
                return Task.FromResult(new LyricsSearchResult(true, lyricsA, "LRCLIB"));
            }

            var lyricsB = new SyncedLyrics("Track B", "Artist B", true, new[]
            {
                new LyricLine(TimeSpan.FromSeconds(5), "Lyrics line 1 for Track B", 0),
                new LyricLine(TimeSpan.FromSeconds(15), "Lyrics line 2 for Track B", 1),
                new LyricLine(TimeSpan.FromSeconds(25), "Lyrics line 3 for Track B", 2)
            });
            return Task.FromResult(new LyricsSearchResult(true, lyricsB, "LRCLIB"));
        }
    }

    private sealed class MemoryLyricsCache : ILyricsCache
    {
        public Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
            => Task.FromResult<SyncedLyrics?>(null);

        public Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
