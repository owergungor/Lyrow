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

public class LyrowV34TrackTransitionAndSyncTests
{
    private sealed class MultiTrackMockClient : ILyricsClient
    {
        public int FetchCallCount { get; private set; }
        public List<string> RequestedTracks { get; } = new();

        public Task<LyricsSearchResult> FetchLyricsAsync(
            string trackName,
            string artistName,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default)
        {
            FetchCallCount++;
            RequestedTracks.Add($"{artistName} - {trackName}");

            var lines = new List<LyricLine>
            {
                new(TimeSpan.FromSeconds(0), $"{trackName} Line 1", 0),
                new(TimeSpan.FromSeconds(10), $"{trackName} Line 2", 1),
                new(TimeSpan.FromSeconds(20), $"{trackName} Line 3", 2),
                new(TimeSpan.FromSeconds(30), $"{trackName} Line 4", 3)
            };

            var synced = new SyncedLyrics(trackName, artistName, true, lines, null, duration);
            return Task.FromResult(new LyricsSearchResult(true, synced, "LRCLIB"));
        }
    }

    [Fact]
    public async Task AutomaticTrackChangeLyricsFetchTests_HandlesFourSequentialTracksAndDelayedThumbnailMetadata()
    {
        var eventBus = new InMemoryEventBus();
        var client = new MultiTrackMockClient();
        var module = new LyricsModule(
            mediaSessionMonitor: null,
            lyricsClient: client,
            lyricsCache: null,
            eventBus: eventBus,
            settingsManager: null);

        await module.InitializeAsync();
        await module.StartAsync();

        var trackNames = new[] { "Song A", "Song B", "Song C", "Song D" };

        foreach (var name in trackNames)
        {
            // 1. Initial GSMTC event on automatic transition (Title & Artist arrive first, ThumbnailData is null)
            var initialTrack = new MediaTrackInfo(name, "Artist X", "Album Y", TimeSpan.FromMinutes(3), ThumbnailData: null);
            await module.HandleTrackChangedAsync(initialTrack);

            // 2. Secondary GSMTC property update 30ms later when ThumbnailData arrives for the exact same track
            byte[] fakeArt = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };
            var thumbnailUpdateTrack = initialTrack with { ThumbnailData = fakeArt };
            await module.HandleTrackChangedAsync(thumbnailUpdateTrack);

            // Verify all 4 lines are loaded and not aborted by the secondary property update
            module.CurrentLyrics.IsSynced.Should().BeTrue();
            module.CurrentLyrics.Lines.Count.Should().Be(4);
            module.CurrentLyrics.Lines[0].Text.Should().Be($"{name} Line 1");
            module.CurrentLyrics.Lines[3].Text.Should().Be($"{name} Line 4");
            module.CurrentTrack.ThumbnailData.Should().NotBeNull();

            // Simulate mid-song position at 22s -> must resolve to index 2 ("Line 3")
            module.UpdateTimelineExternal(new MediaTimelineInfo(
                TimeSpan.FromSeconds(22),
                TimeSpan.FromMinutes(3),
                MediaPlaybackStatus.Playing,
                DateTime.UtcNow));

            module.ActiveLineIndex.Should().Be(2);
        }

        // Only 4 network fetches should have been made (1 per unique track A, B, C, D)
        client.FetchCallCount.Should().Be(4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(500)]
    public void SubMillisecondAndOffsetSyncTests_ResolvesExactActiveLineAtBoundaries(int offsetMs)
    {
        var lines = new List<LyricLine>
        {
            new(TimeSpan.FromMilliseconds(1000), "Line 0", 0),
            new(TimeSpan.FromMilliseconds(5000), "Line 1", 1),
            new(TimeSpan.FromMilliseconds(10000), "Line 2", 2)
        };
        var lyrics = new SyncedLyrics("Track", "Artist", true, lines);
        using var sync = new Lyrow.Core.Lyrics.LyricsTimelineSynchronizer();
        sync.SetLyrics(lyrics);
        sync.SyncOffsetMs = offsetMs;

        // When raw position + offsetMs == 5000ms, active line must transition to Line 1
        var triggerPos = TimeSpan.FromMilliseconds(5000 - offsetMs);
        sync.EvaluatePosition(triggerPos);
        sync.CurrentActiveIndex.Should().Be(1);

        // 1ms before boundary -> Line 0
        sync.EvaluatePosition(triggerPos - TimeSpan.FromMilliseconds(1));
        sync.CurrentActiveIndex.Should().Be(0);
    }

    [Fact]
    public void AutomaticTrackChangeBackgroundAndGlowColorTests_ExtractsDistinctPalettesPerTrack()
    {
        // Track A: Vibrant Red/Orange RGBA pixels
        byte[] rgbaTrackA = new byte[64 * 4];
        for (int i = 0; i < 64; i++)
        {
            rgbaTrackA[i * 4 + 0] = 240; // R
            rgbaTrackA[i * 4 + 1] = 45;  // G
            rgbaTrackA[i * 4 + 2] = 30;  // B
            rgbaTrackA[i * 4 + 3] = 255; // A
        }

        // Track B: Vibrant Blue/Cyan RGBA pixels
        byte[] rgbaTrackB = new byte[64 * 4];
        for (int i = 0; i < 64; i++)
        {
            rgbaTrackB[i * 4 + 0] = 25;  // R
            rgbaTrackB[i * 4 + 1] = 150; // G
            rgbaTrackB[i * 4 + 2] = 245; // B
            rgbaTrackB[i * 4 + 3] = 255; // A
        }

        var glowPaletteA = ArtworkColorExtractor.ExtractPaletteFromRgbaPixels(rgbaTrackA);
        var glowPaletteB = ArtworkColorExtractor.ExtractPaletteFromRgbaPixels(rgbaTrackB);

        glowPaletteA.BottomColor.R.Should().BeGreaterThan(glowPaletteA.BottomColor.B);
        glowPaletteB.BottomColor.B.Should().BeGreaterThan(glowPaletteB.BottomColor.R);

        var ambientA = ArtworkPaletteExtractor.ExtractAmbientPalette(glowPaletteA);
        var ambientB = ArtworkPaletteExtractor.ExtractAmbientPalette(glowPaletteB);

        ambientA.Should().NotBe(ambientB);
        ArtworkPaletteExtractor.CalculateLuminance(ambientA.Dominant).Should().BeLessThanOrEqualTo(ArtworkPaletteExtractor.MaxSafeLuminance);
        ArtworkPaletteExtractor.CalculateLuminance(ambientB.Dominant).Should().BeLessThanOrEqualTo(ArtworkPaletteExtractor.MaxSafeLuminance);
    }
}
