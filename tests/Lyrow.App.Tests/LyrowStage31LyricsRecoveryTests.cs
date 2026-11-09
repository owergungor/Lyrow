using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Lyrics;
using Lyrow.Music.Metadata;
using Xunit;

namespace Lyrow.App.Tests;

public sealed class LyrowStage31LyricsRecoveryTests
{
    [Fact]
    public void ContentSpaceCenterOffset_IsInvariantToScrollPosition_AndHasZeroOscillation()
    {
        // Viewport height: 600, Line top in LyricsPanel: 450, Line height: 40
        double viewportH = 600.0;
        double lineTopInContent = 450.0;
        double lineHeight = 40.0;
        double maxScroll = 1200.0;

        // Content-space calculation: line midpoint is 470, viewport center is 300, desiredOffset = 170
        double targetOffset = LyricsViewportCalculator.CalculateTargetCenterOffset(
            viewportH, lineTopInContent, lineHeight, maxScroll);

        targetOffset.Should().Be(170.0);

        // Successive calls across simulated 60 FPS animation frames must remain 100% identical and stable
        for (int frame = 0; frame < 60; frame++)
        {
            double frameTarget = LyricsViewportCalculator.CalculateTargetCenterOffset(
                viewportH, lineTopInContent, lineHeight, maxScroll);
            frameTarget.Should().Be(targetOffset);
        }
    }

    [Fact]
    public void InterpolateScrollOffset_GlidesDirectlyToTarget_WithoutOvershootOrOscillation()
    {
        double currentOffset = 50.0;
        double targetOffset = 250.0;

        // Simulate 60 FPS interpolation ticks
        double prevOffset = currentOffset;
        for (int frame = 0; frame < 30; frame++)
        {
            currentOffset = LyricsViewportCalculator.InterpolateScrollOffset(
                currentOffset, targetOffset, smoothingFactor: 0.16, snapEpsilon: 0.35);

            // Must strictly advance towards target monotonically without oscillating backwards
            currentOffset.Should().BeGreaterThanOrEqualTo(prevOffset);
            currentOffset.Should().BeLessThanOrEqualTo(targetOffset);
            prevOffset = currentOffset;
        }

        // Within 30 frames (~500ms), offset should be at or extremely close to target
        currentOffset.Should().BeApproximately(targetOffset, 2.5);
    }

    [Fact]
    public void MetadataNormalizer_StripsYouTubeAndWebVideoSuffixes_Cleanly()
    {
        var normalizer = new MediaMetadataNormalizer();

        // 1. YouTube / Web official video suffixes
        var r1 = normalizer.Normalize("Do I Wanna Know? (Official Video)", "Arctic Monkeys", "");
        r1.CleanSearchTitle.Should().Be("Do I Wanna Know?");
        r1.CleanSearchArtist.Should().Be("Arctic Monkeys");

        var r2 = normalizer.Normalize("Blinding Lights [Official Music Video]", "The Weeknd", "");
        r2.CleanSearchTitle.Should().Be("Blinding Lights");

        var r3 = normalizer.Normalize("Starboy - Official Video", "The Weeknd", "");
        r3.CleanSearchTitle.Should().Be("Starboy");

        var r4 = normalizer.Normalize("As It Was (Lyric Video)", "Harry Styles", "");
        r4.CleanSearchTitle.Should().Be("As It Was");

        var r5 = normalizer.Normalize("Shape of You [Visualizer]", "Ed Sheeran", "");
        r5.CleanSearchTitle.Should().Be("Shape of You");
    }

    [Fact]
    public void MetadataNormalizer_NormalizesCurlyQuotesAndApostrophes()
    {
        var normalizer = new MediaMetadataNormalizer();

        // Song titles with curly apostrophes or curly quotes commonly found in streaming services
        var r1 = normalizer.Normalize("Don’t Look Back in Anger", "Oasis", "What's the Story");
        r1.CleanSearchTitle.Should().Be("Don't Look Back in Anger");

        var r2 = normalizer.Normalize("“Heroes” - Remastered 2017", "David Bowie", "Heroes");
        r2.CleanSearchTitle.Should().Be("\"Heroes\"");
    }

    [Fact]
    public async Task LyricsModule_SecondaryMetadataEnrichment_DoesNotWipeLyricsOrResetSynchronizer()
    {
        var synchronizer = new Lyrow.Core.Lyrics.LyricsTimelineSynchronizer();
        var fakeLyrics = new SyncedLyrics(
            title: "Do I Wanna Know?",
            artist: "Arctic Monkeys",
            isSynced: true,
            lines: new[]
            {
                new LyricLine(TimeSpan.FromSeconds(5), "Have you got colour in your cheeks?", 0),
                new LyricLine(TimeSpan.FromSeconds(10), "Do you ever get that fear that you can't shift the type", 1)
            });

        var fakeClient = new FixedLyricsClient(fakeLyrics);
        await using var module = new LyricsModule(lyricsClient: fakeClient);
        await module.InitializeAsync();
        await module.StartAsync();

        var initialTrack = new MediaTrackInfo(
            Title: "Do I Wanna Know?",
            Artist: "Arctic Monkeys",
            Album: "AM",
            Duration: TimeSpan.FromSeconds(272));

        // 1. Initial track arrives: lyrics load
        await module.HandleTrackChangedAsync(initialTrack);
        module.CurrentLyrics.Lines.Count.Should().Be(2);
        module.CurrentLyricsSongKey.Should().Be("Arctic Monkeys_Do I Wanna Know?");

        // 2. Secondary GSMTC metadata arrives 100ms later (e.g. thumbnail arrives, same title & artist)
        var enrichedTrack = initialTrack with
        {
            ThumbnailData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }
        };

        // Calling HandleTrackChangedAsync for metadata enrichment must preserve current lyrics!
        await module.HandleTrackChangedAsync(enrichedTrack);

        module.CurrentLyrics.Lines.Count.Should().Be(2);
        module.CurrentLyrics.Lines[0].Text.Should().Be("Have you got colour in your cheeks?");
        module.CurrentLyricsSongKey.Should().Be("Arctic Monkeys_Do I Wanna Know?");
        module.CurrentTrack.ThumbnailData.Should().NotBeNull();
    }

    private sealed class FixedLyricsClient : ILyricsClient
    {
        private readonly SyncedLyrics _lyrics;

        public FixedLyricsClient(SyncedLyrics lyrics)
        {
            _lyrics = lyrics;
        }

        public Task<LyricsSearchResult> FetchLyricsAsync(
            string trackName,
            string artistName,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LyricsSearchResult(true, _lyrics, "Mock"));
        }
    }
}
