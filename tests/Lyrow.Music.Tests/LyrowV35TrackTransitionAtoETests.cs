using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Music.Tests;

public class LyrowV35TrackTransitionAtoETests
{
    private sealed class DelayedMultiTrackClient : ILyricsClient
    {
        private readonly Dictionary<string, int> _delaysMs;
        public List<string> FetchLog { get; } = new();

        public DelayedMultiTrackClient(Dictionary<string, int>? delaysMs = null)
        {
            _delaysMs = delaysMs ?? new Dictionary<string, int>();
        }

        public async Task<LyricsSearchResult> FetchLyricsAsync(
            string trackName,
            string artistName,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default)
        {
            FetchLog.Add($"{artistName} - {trackName}");

            if (_delaysMs.TryGetValue(trackName, out int delay) && delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }

            var lines = new List<LyricLine>
            {
                new(TimeSpan.FromSeconds(0), $"{trackName} L1", 0),
                new(TimeSpan.FromSeconds(5), $"{trackName} L2", 1),
                new(TimeSpan.FromSeconds(10), $"{trackName} L3", 2)
            };

            var synced = new SyncedLyrics(trackName, artistName, true, lines, null, duration);
            return new LyricsSearchResult(true, synced, "LRCLIB");
        }
    }

    [Fact]
    public async Task AutomaticTrackTransition_FiveSequentialTracks_AtoE_AtomicallyUpdatesAndPurgesState()
    {
        var eventBus = new InMemoryEventBus();
        var client = new DelayedMultiTrackClient();
        var module = new LyricsModule(
            mediaSessionMonitor: null,
            lyricsClient: client,
            lyricsCache: null,
            eventBus: eventBus,
            settingsManager: null);

        await module.InitializeAsync();
        await module.StartAsync();

        var trackNames = new[] { "Song A", "Song B", "Song C", "Song D", "Song E" };

        for (int i = 0; i < trackNames.Length; i++)
        {
            string trackName = trackNames[i];
            var trackInfo = new MediaTrackInfo(
                trackName,
                "Artist 35",
                "Album 35",
                TimeSpan.FromMinutes(3),
                ThumbnailData: null
            );

            await module.HandleTrackChangedAsync(trackInfo);

            module.CurrentLyrics.Should().NotBeNull();
            module.CurrentLyrics.Title.Should().Be(trackName);
            module.CurrentLyrics.Lines.Count.Should().Be(3);
            module.CurrentLyrics.Lines[0].Text.Should().Be($"{trackName} L1");
            module.CurrentLyrics.Lines[1].Text.Should().Be($"{trackName} L2");
            module.CurrentLyrics.Lines[2].Text.Should().Be($"{trackName} L3");
        }

        client.FetchLog.Count.Should().Be(5);
        client.FetchLog.Should().ContainInOrder(
            "Artist 35 - Song A",
            "Artist 35 - Song B",
            "Artist 35 - Song C",
            "Artist 35 - Song D",
            "Artist 35 - Song E"
        );
    }

    [Fact]
    public async Task AutomaticTrackTransition_DelayedInFlightSongA_DoesNotOverwriteSongB()
    {
        var eventBus = new InMemoryEventBus();
        // Give Song A a 100ms delay, while Song B has 0ms delay
        var delays = new Dictionary<string, int>
        {
            { "Song A", 100 },
            { "Song B", 0 }
        };
        var client = new DelayedMultiTrackClient(delays);
        var module = new LyricsModule(
            mediaSessionMonitor: null,
            lyricsClient: client,
            lyricsCache: null,
            eventBus: eventBus,
            settingsManager: null);

        await module.InitializeAsync();
        await module.StartAsync();

        // 1. Fire Song A
        var trackA = new MediaTrackInfo("Song A", "Artist 35", "Album", TimeSpan.FromMinutes(3), ThumbnailData: null);
        var taskA = module.HandleTrackChangedAsync(trackA);

        // 2. Immediately switch to Song B before Song A's 100ms fetch completes
        await Task.Delay(15);
        var trackB = new MediaTrackInfo("Song B", "Artist 35", "Album", TimeSpan.FromMinutes(3), ThumbnailData: null);
        var taskB = module.HandleTrackChangedAsync(trackB);

        // Wait for both to finish
        await Task.WhenAll(taskA, taskB);

        // Song B must be active, Song A's delayed response must NOT overwrite Song B!
        module.CurrentLyrics.Should().NotBeNull();
        module.CurrentLyrics.Title.Should().Be("Song B");
        module.CurrentLyrics.Lines[0].Text.Should().Be("Song B L1");
    }
}
