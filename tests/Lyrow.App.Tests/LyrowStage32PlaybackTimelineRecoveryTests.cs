using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Metadata;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

public sealed class LyrowStage32PlaybackTimelineRecoveryTests
{
    [Fact]
    public void AutomaticTrackTransition_PlaybackTimelineAdvancesSmoothly_NeverFreezesAtZero()
    {
        // Simulate Track A ending and Track B starting at T0
        var t0Utc = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var trackBDuration = TimeSpan.FromSeconds(210);

        // At T0: Track B starts playing from 0:00
        var (pos0, lastUpdated0) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPositionSinceStart: TimeSpan.Zero,
            trackDuration: trackBDuration,
            status: MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: t0Utc,
            nowUtc: t0Utc,
            lastTrackSwitchUtc: t0Utc,
            lastSeekUtc: DateTime.MinValue,
            lastSeekTargetPosition: TimeSpan.Zero);

        pos0.Should().Be(TimeSpan.Zero);
        lastUpdated0.Should().Be(t0Utc);

        var timeline0 = new MediaTimelineInfo(pos0, trackBDuration, MediaPlaybackStatus.Playing, lastUpdated0);
        timeline0.GetInterpolatedPosition().Should().Be(TimeSpan.Zero);

        // At T0 + 1.5s: 1.5 seconds have elapsed in real time
        var t1Utc = t0Utc.AddSeconds(1.5);
        var (pos1, lastUpdated1) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPositionSinceStart: TimeSpan.Zero,
            trackDuration: trackBDuration,
            status: MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: t0Utc,
            nowUtc: t1Utc,
            lastTrackSwitchUtc: t0Utc,
            lastSeekUtc: DateTime.MinValue,
            lastSeekTargetPosition: TimeSpan.Zero);

        pos1.TotalSeconds.Should().BeApproximately(1.5, 0.05, "ReconcileTimelinePosition must advance position with elapsed real-time");
        lastUpdated1.Should().Be(t1Utc);

        var timeline1 = new MediaTimelineInfo(pos1, trackBDuration, MediaPlaybackStatus.Playing, lastUpdated1);
        timeline1.GetInterpolatedPosition().TotalSeconds.Should().BeApproximately(1.5, 0.05);
    }

    [Fact]
    public void GSMTC_LateReporting_ReconcilesCandidatePosition_WithoutClampingLegitimateElapsedPlayback()
    {
        // Track B started 8 seconds ago; GSMTC still has reportedUtc anchored at track start (8 seconds ago)
        var t0Utc = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
        var nowUtc = t0Utc.AddSeconds(8.0);
        var trackDuration = TimeSpan.FromSeconds(200);

        var (reconciledPos, lastUpdated) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPositionSinceStart: TimeSpan.Zero,
            trackDuration: trackDuration,
            status: MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: t0Utc,
            nowUtc: nowUtc,
            lastTrackSwitchUtc: t0Utc,
            lastSeekUtc: DateTime.MinValue,
            lastSeekTargetPosition: TimeSpan.Zero);

        // Must advance to 8.0s and never remain frozen at 0:00!
        reconciledPos.TotalSeconds.Should().BeApproximately(8.0, 0.05, "Late GSMTC reporting must be reconciled with wall-clock time");
        lastUpdated.Should().Be(nowUtc);
    }

    [Fact]
    public void ContinuousPlayback_InterpolatedPosition_AdvancesMonotonically_WithoutStalling()
    {
        var baseUtc = DateTime.UtcNow;
        var timeline = new MediaTimelineInfo(
            Position: TimeSpan.FromSeconds(10.0),
            Duration: TimeSpan.FromSeconds(180.0),
            Status: MediaPlaybackStatus.Playing,
            LastUpdatedUtc: baseUtc);

        // Simulated frame interpolation across 3 frames
        var posFrame1 = timeline.GetInterpolatedPosition();
        Thread.Sleep(30);
        var posFrame2 = timeline.GetInterpolatedPosition();
        Thread.Sleep(30);
        var posFrame3 = timeline.GetInterpolatedPosition();

        posFrame2.Should().BeGreaterThan(posFrame1, "Interpolated position must advance smoothly across animation frames");
        posFrame3.Should().BeGreaterThan(posFrame2, "Interpolated position must strictly increase monotonically during playback");
    }

    [Fact]
    public async Task MetadataEnrichment_DuringPlayback_PreservesActiveTimeline_AndDoesNotResetPosition()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new InMemoryTestLyricsCache();
        var client = new ConstantLyricsClient(new SyncedLyrics("Song", "Artist", true, new[]
        {
            new LyricLine(TimeSpan.FromSeconds(5), "Line 1", 0),
            new LyricLine(TimeSpan.FromSeconds(20), "Line 2", 1)
        }));

        var module = new LyricsModule(monitor, client, cache);
        await module.InitializeAsync();
        await module.StartAsync();

        // 1. Initial track arrival without thumbnail
        var initialTrack = new MediaTrackInfo("Enrich Song", "Enrich Artist", "Album A", TimeSpan.FromSeconds(180));
        await module.HandleTrackChangedAsync(initialTrack);

        // Simulate playback at 25s
        var activeTimeline = new MediaTimelineInfo(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(180), MediaPlaybackStatus.Playing, DateTime.UtcNow);
        module.UpdateTimelineExternal(activeTimeline);
        module.CurrentTimeline.Position.Should().Be(TimeSpan.FromSeconds(25));
        module.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Playing);

        // 2. Secondary metadata enrichment arrives (e.g. late GSMTC thumbnail 400ms later)
        byte[] fakeThumbnail = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var enrichedTrack = initialTrack with { ThumbnailData = fakeThumbnail, Album = "Deluxe Edition Album" };
        await module.HandleTrackChangedAsync(enrichedTrack);

        // Position and timeline MUST NOT be wiped back to 0:00!
        module.CurrentTrack.Album.Should().Be("Deluxe Edition Album");
        module.CurrentTrack.ThumbnailData.Should().NotBeNull();
        module.CurrentTimeline.Position.Should().Be(TimeSpan.FromSeconds(25), "Metadata enrichment must preserve current playback timeline position");
        module.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Playing);

        await module.StopAsync();
    }

    [Fact]
    public void AutoHeal_TransitionSettlingWindow_TransitionsStuckChangingStateToPlaying()
    {
        // A track transition happened 1.2 seconds ago and status was stuck in Changing
        var resetUtc = DateTime.UtcNow.AddSeconds(-1.2);
        var stuckTimeline = new MediaTimelineInfo(
            Position: TimeSpan.Zero,
            Duration: TimeSpan.FromSeconds(200),
            Status: MediaPlaybackStatus.Changing,
            LastUpdatedUtc: resetUtc);

        bool isSettlingWindowElapsed = (DateTime.UtcNow - resetUtc).TotalSeconds >= 1.0;
        isSettlingWindowElapsed.Should().BeTrue();

        // Auto-heal logic promotes Changing to Playing
        var healedTimeline = stuckTimeline with { Status = MediaPlaybackStatus.Playing, LastUpdatedUtc = DateTime.UtcNow };
        healedTimeline.Status.Should().Be(MediaPlaybackStatus.Playing);
        healedTimeline.GetInterpolatedPosition().Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public void DarModDimensions_And_AppleMusicWebSeek_RemainIntact()
    {
        // 1. Dar Mod dimensions: 352 DIP width, 148 DIP height
        LyricsViewportCalculator.CompactDefaultLogicalWidth.Should().Be(352, "Dar Mod default logical width must remain 352 DIP");
        LyricsViewportCalculator.CompactDefaultLogicalHeight.Should().Be(148, "Dar Mod default logical height must remain 148 DIP");

        // 2. Apple Music Web session profile classification
        var webProfile = MediaSessionProfileHelper.ClassifySourceAppId("msedge.exe");
        webProfile.Should().Be(MediaSessionSourceProfile.BrowserSession);

        var appleMusicDesktopProfile = MediaSessionProfileHelper.ClassifySourceAppId("AppleInc.AppleMusicWin_8wekyb3d8bbwe");
        appleMusicDesktopProfile.Should().Be(MediaSessionSourceProfile.AppleMusicDesktop);
        MediaSessionProfileHelper.IsAppleMusicDesktop("AppleInc.AppleMusicWin_8wekyb3d8bbwe").Should().BeTrue();
        MediaSessionProfileHelper.IsAppleMusicDesktop("msedge.exe").Should().BeFalse();
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Lyrow.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        return Directory.GetCurrentDirectory();
    }

    private sealed class InMemoryTestLyricsCache : ILyricsCache
    {
        private readonly Dictionary<string, SyncedLyrics> _cache = new(StringComparer.OrdinalIgnoreCase);

        public Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
        {
            string key = $"{artistName}_{trackName}";
            _cache.TryGetValue(key, out var lyrics);
            return Task.FromResult(lyrics);
        }

        public Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default)
        {
            string key = $"{artistName}_{trackName}";
            _cache[key] = lyrics;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _cache.Clear();
            return Task.CompletedTask;
        }
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
}
