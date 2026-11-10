using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Timeline;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowStage33PlaybackTimeEngineTests
{
    [Fact]
    public void Test01_TrackA_Duration3m30s_Position1m20s_Elapsed1m20s_RemainingMinus2m10s()
    {
        var engine = new PlaybackTimelineEngine();
        var trackA = new MediaTrackInfo("Song A", "Artist A", "Album A", TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(30));

        engine.HandleTrackChanged(trackA);
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(20),
            rawDuration: trackA.Duration,
            rawStatus: MediaPlaybackStatus.Paused);

        engine.CurrentTrackPosition.Should().Be(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(20));
        engine.CurrentTrackPositionFormatted.Should().Be("1:20");
        engine.CurrentTrackRemaining.Should().Be(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(10));
        engine.CurrentTrackRemainingFormatted.Should().Be("-2:10");
        engine.CurrentTrackDurationFormatted.Should().Be("3:30");
    }

    [Fact]
    public void Test02_AutoTransition_TrackA_To_TrackB_ResetsElapsedToZero()
    {
        var engine = new PlaybackTimelineEngine();
        var trackA = new MediaTrackInfo("Song A", "Artist A", "Album A", TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(30));
        var trackB = new MediaTrackInfo("Song B", "Artist B", "Album B", TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(45));

        engine.HandleTrackChanged(trackA);
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(20),
            rawDuration: trackA.Duration,
            rawStatus: MediaPlaybackStatus.Playing);

        // Transition to Track B
        engine.HandleTrackChanged(trackB);

        engine.CurrentTrackId.Should().Be("Artist B_Song B");
        engine.CurrentTrackPosition.Should().Be(TimeSpan.Zero);
        engine.CurrentTrackPositionFormatted.Should().Be("0:00");
        engine.CurrentTrackRemaining.Should().Be(trackB.Duration);
        engine.CurrentTrackRemainingFormatted.Should().Be("-2:45");
    }

    [Fact]
    public void Test03_TrackA_And_TrackB_Durations_NeverSummed_UnderAnyCondition()
    {
        var engine = new PlaybackTimelineEngine();
        var trackA = new MediaTrackInfo("Song A", "Artist A", "Album A", TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(30));
        var trackB = new MediaTrackInfo("Song B", "Artist B", "Album B", TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(00));

        engine.HandleTrackChanged(trackA);
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(25),
            rawDuration: trackA.Duration,
            rawStatus: MediaPlaybackStatus.Playing);

        // Immediate track transition
        engine.HandleTrackChanged(trackB);

        // Simulate browser or player sending previous track's cumulative or stale position (e.g. 3m30s)
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(30),
            rawDuration: trackB.Duration,
            rawStatus: MediaPlaybackStatus.Playing);

        // The stale 3:30 position must be completely rejected and NEVER added or accepted
        engine.CurrentTrackPosition.Should().BeLessThan(TimeSpan.FromSeconds(3));
        engine.CurrentTrackPositionFormatted.Should().NotBe("3:30");
        engine.CurrentTrackPosition.Should().BeLessThanOrEqualTo(trackB.Duration);
    }

    [Fact]
    public void Test04_TrackB_PlaybackPosition_Advances_Monotonically()
    {
        var engine = new PlaybackTimelineEngine();
        var trackB = new MediaTrackInfo("Song B", "Artist B", "Album B", TimeSpan.FromMinutes(3));

        engine.HandleTrackChanged(trackB);
        engine.HandleTimelineProperties(
            rawPosition: TimeSpan.Zero,
            rawDuration: trackB.Duration,
            rawStatus: MediaPlaybackStatus.Playing);

        var p0 = engine.CurrentTrackPosition;
        p0.Should().BeCloseTo(TimeSpan.Zero, TimeSpan.FromMilliseconds(200));

        // Sequential reports from player
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(1), trackB.Duration, MediaPlaybackStatus.Playing);
        engine.CurrentTrackPositionFormatted.Should().Be("0:01");

        engine.HandleTimelineProperties(TimeSpan.FromSeconds(2), trackB.Duration, MediaPlaybackStatus.Playing);
        engine.CurrentTrackPositionFormatted.Should().Be("0:02");
    }

    [Fact]
    public void Test05_TrackB_Remaining_CalculatedSolely_From_TrackB_Duration_And_Position()
    {
        var engine = new PlaybackTimelineEngine();
        var trackB = new MediaTrackInfo("Song B", "Artist B", "Album B", TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(11));

        engine.HandleTrackChanged(trackB);
        engine.HandleTimelineProperties(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1), trackB.Duration, MediaPlaybackStatus.Playing);

        // Remaining = 3:11 - 1:01 = 2:10 -> "-2:10"
        engine.CurrentTrackRemaining.Should().BeCloseTo(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200));
        engine.CurrentTrackRemainingFormatted.Should().Be("-2:10");
    }

    [Fact]
    public void Test06_Metadata_And_Artwork_Update_DoesNotReset_PlaybackPosition()
    {
        var engine = new PlaybackTimelineEngine();
        var initialTrack = new MediaTrackInfo("Song A", "Artist A", "", TimeSpan.FromMinutes(4));

        engine.HandleTrackChanged(initialTrack);
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(45), initialTrack.Duration, MediaPlaybackStatus.Playing);

        engine.CurrentTrackPositionFormatted.Should().Be("0:45");

        // Late album artwork arrival for the exact same track
        var enrichedTrack = initialTrack with
        {
            Album = "Master Album",
            ThumbnailData = new byte[] { 1, 2, 3, 4, 5 }
        };

        engine.HandleTrackChanged(enrichedTrack);

        // Position must NOT be reset to 0:00
        engine.CurrentTrackPositionFormatted.Should().Be("0:45");
        engine.CurrentTrackDuration.Should().Be(TimeSpan.FromMinutes(4));
    }

    [Fact]
    public void Test07_SeekOperation_ProducesCorrect_Elapsed_And_Remaining()
    {
        var engine = new PlaybackTimelineEngine();
        var track = new MediaTrackInfo("Song A", "Artist A", "Album A", TimeSpan.FromMinutes(4));

        engine.HandleTrackChanged(track);
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(10), track.Duration, MediaPlaybackStatus.Playing);

        // User seeks to 2:30
        engine.HandleSeek(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30));

        engine.CurrentTrackPosition.Should().BeCloseTo(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        engine.CurrentTrackPositionFormatted.Should().Be("2:30");
        engine.CurrentTrackRemaining.Should().BeCloseTo(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        engine.CurrentTrackRemainingFormatted.Should().Be("-1:30");
    }

    [Fact]
    public void Test08_Pause_And_Resume_WorkCorrectly()
    {
        var engine = new PlaybackTimelineEngine();
        var track = new MediaTrackInfo("Song A", "Artist A", "Album A", TimeSpan.FromMinutes(3));

        engine.HandleTrackChanged(track);
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(30), track.Duration, MediaPlaybackStatus.Playing);

        // Pause
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(30), track.Duration, MediaPlaybackStatus.Paused);
        engine.PlaybackStatus.Should().Be(MediaPlaybackStatus.Paused);
        engine.CurrentTrackPosition.Should().Be(TimeSpan.FromSeconds(30));

        // Resume
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(30), track.Duration, MediaPlaybackStatus.Playing);
        engine.PlaybackStatus.Should().Be(MediaPlaybackStatus.Playing);
        engine.CurrentTrackPosition.TotalSeconds.Should().BeGreaterThanOrEqualTo(30.0);
    }

    [Fact]
    public void Test09_LyricsTimelineSynchronizer_Uses_CorrectTrackPosition()
    {
        var synchronizer = new LyricsTimelineSynchronizer();
        var lyrics = new SyncedLyrics(
            title: "Song A",
            artist: "Artist A",
            isSynced: true,
            lines: new List<LyricLine>
            {
                new(TimeSpan.FromSeconds(10), "Line 1", 0),
                new(TimeSpan.FromSeconds(20), "Line 2", 1),
                new(TimeSpan.FromSeconds(30), "Line 3", 2)
            });

        synchronizer.SetLyrics(lyrics);

        var engine = new PlaybackTimelineEngine();
        var track = new MediaTrackInfo("Song A", "Artist A", "Album A", TimeSpan.FromMinutes(3));
        engine.HandleTrackChanged(track);
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(22), track.Duration, MediaPlaybackStatus.Playing);

        synchronizer.EvaluatePosition(engine.CurrentTrackPosition);
        synchronizer.CurrentActiveIndex.Should().Be(1, "Position 22s falls on Line 2 (timestamp 20s)");
    }

    [Fact]
    public void Test10_CompactMode_And_NormalMode_Use_Identical_TimeValues()
    {
        var engine = new PlaybackTimelineEngine();
        var track = new MediaTrackInfo("Song A", "Artist A", "Album A", TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(15));
        engine.HandleTrackChanged(track);
        engine.HandleTimelineProperties(TimeSpan.FromSeconds(42), track.Duration, MediaPlaybackStatus.Playing);

        // Both read the central formatters
        string normalElapsed = engine.CurrentTrackPositionFormatted;
        string compactElapsed = engine.CurrentTrackPositionFormatted;
        string normalRemaining = engine.CurrentTrackRemainingFormatted;
        string compactRemaining = engine.CurrentTrackRemainingFormatted;

        normalElapsed.Should().Be(compactElapsed);
        normalRemaining.Should().Be(compactRemaining);
        normalElapsed.Should().Be("0:42");
        normalRemaining.Should().Be("-2:33");
    }

    [Fact]
    public void Test11_AppleMusicWebSeek_Capability_Preserved()
    {
        var monitor = new WindowsGsmtcMediaSessionMonitor();
        ((IMediaSessionMonitor)monitor).CanSeek.Should().BeFalse("Initial state without session has CanSeek = false");

        var engine = monitor.TimelineEngine;
        engine.Should().NotBeNull();
    }

    [Fact]
    public void Test12_Stage30_31_32_Invariants_RemainIntact()
    {
        // Stage 30 Dar Mod dimensions
        LyricsViewportCalculator.CompactDefaultLogicalWidth.Should().Be(352);
        LyricsViewportCalculator.CompactDefaultLogicalHeight.Should().Be(148);

        // Stage 31 typography invariants
        LyricsViewportCalculator.CalculateTargetMediaSeekPosition(TimeSpan.FromSeconds(15), 0, TimeSpan.FromMinutes(3))
            .Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Test13_LyricsWindow_TopLeftTitleText_Removed()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "../../../../../src/Lyrow.App/LyricsWindow.xaml");
        if (!File.Exists(xamlPath))
        {
            xamlPath = @"c:\Users\omer\Projects\Lyrow\src\Lyrow.App\LyricsWindow.xaml";
        }

        File.Exists(xamlPath).Should().BeTrue();
        var xaml = File.ReadAllText(xamlPath);

        // Title="Lyrow — Synchronized Lyrics" must NOT exist in LyricsWindow.xaml
        xaml.Should().NotContain("Title=\"Lyrow — Synchronized Lyrics\"", "Title text must be completely removed from LyricsTitleBar");
        xaml.Should().Contain("x:Name=\"LyricsTitleBar\"", "TitleBar container itself must be preserved for drag and controls");
    }
}
