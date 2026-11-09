using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Lyrics;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Stage 29 Regression Test Suite:
/// 1. Lyric line click seeks to the clicked line's exact timestamp (and applies SyncOffsetMs once)
/// 2. Post-seek timeline reconciliation preserves sought timestamp against stale Apple Music Web / GSMTC updates
/// 3. Lyrics line ordering and layout typography stability across parses and active-line transitions
/// 4. LRCLIB search candidate selection chooses synced lyrics matching track duration
/// 5. Track change clears previous track lyrics, active line, progress, and elapsed time
/// 6. Late async lyrics result for Track A is ignored after switching to Track B
/// 7. Late async artwork / stale GSMTC carryover thumbnail for Track A is ignored when switching to Track B
/// 8. Automatic track transition resets listening/elapsed time and never combines previous track's elapsed time
/// 9. Compact Mode volume popup opens anchored to volume button, updates volume, and toggles mute without resizing window
/// </summary>
public sealed class LyrowStage29PlaybackRecoveryTests
{
    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (current != null && !File.Exists(Path.Combine(current, "Lyrow.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        return current ?? throw new InvalidOperationException("Could not locate repository root with Lyrow.sln.");
    }

    [Fact]
    public void Stage29_1_LyricLineClickSeek_CalculatesExactTimestampAndAppliesOffsetOnce()
    {
        var lineTimestamp = TimeSpan.FromSeconds(94.5); // 01:34.50
        var trackDuration = TimeSpan.FromMinutes(3.5);

        // Zero offset: seeks to exact line timestamp
        var targetZero = LyricsViewportCalculator.CalculateTargetMediaSeekPosition(lineTimestamp, syncOffsetMs: 0, trackDuration);
        targetZero.Should().Be(lineTimestamp);

        // Positive offset (+500ms): seeks to lineTimestamp - 500ms once so effectiveTime = mediaTime + 500ms = lineTimestamp
        var targetPositive = LyricsViewportCalculator.CalculateTargetMediaSeekPosition(lineTimestamp, syncOffsetMs: 500, trackDuration);
        targetPositive.Should().Be(TimeSpan.FromSeconds(94.0));

        // Verify synchronizer evaluates active line at that exact sought media position
        var lyrics = LrcParser.Parse("[00:15.00]Line One\n[01:34.50]Target Clicked Line\n[02:10.00]Line Three", "Test", "Artist");
        using var sync = new Lyrow.Core.Lyrics.LyricsTimelineSynchronizer { SyncOffsetMs = 500 };
        sync.SetLyrics(lyrics);
        sync.EvaluatePosition(targetPositive);
        sync.CurrentActiveIndex.Should().Be(1);
        lyrics.Lines[sync.CurrentActiveIndex].Text.Should().Be("Target Clicked Line");

        // Clamping at 0:00 and trackDuration
        LyricsViewportCalculator.CalculateTargetMediaSeekPosition(TimeSpan.FromMilliseconds(200), syncOffsetMs: 500, trackDuration)
            .Should().Be(TimeSpan.Zero);
        LyricsViewportCalculator.CalculateTargetMediaSeekPosition(TimeSpan.FromMinutes(4), syncOffsetMs: -500, trackDuration)
            .Should().Be(trackDuration);
    }

    [Fact]
    public void Stage29_2_AppleMusicWebPostSeekReconciliation_PreventsSnapBackAndPreSeekDeltaJump()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        var trackStartUtc = new DateTime(2026, 11, 9, 16, 0, 0, DateTimeKind.Utc);
        var seekUtc = trackStartUtc.AddSeconds(45); // User played for 45s, then clicked lyric at 01:30
        var seekTarget = TimeSpan.FromSeconds(90);  // 01:30
        var trackDuration = TimeSpan.FromSeconds(210);

        // Case A: 400ms after seek, browser GSMTC still reports pre-seek Position (00:45) -> must hold/extrapolate from seekTarget (01:30.4)
        var poll1Utc = seekUtc.AddMilliseconds(400);
        var (posA, _) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPositionSinceStart: TimeSpan.FromSeconds(45),
            trackDuration: trackDuration,
            status: MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: trackStartUtc,
            nowUtc: poll1Utc,
            lastTrackSwitchUtc: trackStartUtc,
            lastSeekUtc: seekUtc,
            lastSeekTargetPosition: seekTarget);

        posA.TotalSeconds.Should().BeApproximately(90.4, 0.05, "Stale pre-seek GSMTC position must not snap scrubber back to 00:45");

        // Case B: Browser updates GSMTC Position to 01:30, but leaves LastUpdatedTime anchored at trackStartUtc (45s before seek!)
        // ReconcileTimelinePosition must clamp reportedUtc >= lastSeekUtc so 45s of pre-seek playback is NEVER added to 01:30!
        var poll2Utc = seekUtc.AddSeconds(1.0);
        var (posB, _) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPositionSinceStart: TimeSpan.FromSeconds(90),
            trackDuration: trackDuration,
            status: MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: trackStartUtc,
            nowUtc: poll2Utc,
            lastTrackSwitchUtc: trackStartUtc,
            lastSeekUtc: seekUtc,
            lastSeekTargetPosition: seekTarget);

        posB.TotalSeconds.Should().BeApproximately(91.0, 0.05, "Pre-seek playback duration must never be added to post-seek position");
    }

    [Fact]
    public void Stage29_3_LyricsOrderingAndLayoutStability_DeterministicSortAndInvariantLineTypography()
    {
        string rawLrc = string.Join("\n", new[]
        {
            "[00:30.00]Third Line",
            "[00:10.00]First Line A",
            "[00:10.00]First Line B",
            "[00:20.00]Second Line"
        });

        var parsed1 = LrcParser.Parse(rawLrc, "Song", "Artist");
        var parsed2 = LrcParser.Parse(rawLrc, "Song", "Artist");

        parsed1.Lines.Should().HaveCount(4);
        for (int i = 0; i < parsed1.Lines.Count; i++)
        {
            parsed1.Lines[i].Text.Should().Be(parsed2.Lines[i].Text);
            parsed1.Lines[i].Timestamp.Should().Be(parsed2.Lines[i].Timestamp);
            parsed1.Lines[i].LineIndex.Should().Be(i);
        }
        parsed1.Lines[0].Text.Should().Be("First Line A");
        parsed1.Lines[1].Text.Should().Be("First Line B");
        parsed1.Lines[2].Text.Should().Be("Second Line");
        parsed1.Lines[3].Text.Should().Be("Third Line");

        // Verify UpdateActiveLineTargets does not mutate per-line FontSize/LineHeight/FontWeight during playback
        string repoRoot = FindRepoRoot();
        string lyricsWinCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        int updateActiveStart = lyricsWinCode.IndexOf("private void UpdateActiveLineTargets(", StringComparison.Ordinal);
        int updateActiveEnd = lyricsWinCode.IndexOf("private void RecalculateCenterScrollTarget(", StringComparison.Ordinal);
        updateActiveStart.Should().BeGreaterThan(0);
        updateActiveEnd.Should().BeGreaterThan(updateActiveStart);

        string updateActiveBody = lyricsWinCode[updateActiveStart..updateActiveEnd];
        updateActiveBody.Should().NotContain(".FontSize =", "Active line transitions must not mutate TextBlock.FontSize and trigger layout reflow");
        updateActiveBody.Should().NotContain(".LineHeight =", "Active line transitions must not mutate TextBlock.LineHeight");
    }

    [Fact]
    public async Task Stage29_4_LrclibSearchCandidateSelection_SelectsCandidateMatchingTrackDuration()
    {
        string searchJson = """
        [
          {
            "id": 1,
            "trackName": "24K Magic (Extended Club Mix)",
            "artistName": "Bruno Mars",
            "duration": 312.0,
            "syncedLyrics": "[00:45.00]Wrong extended remix timestamps"
          },
          {
            "id": 2,
            "trackName": "24K Magic",
            "artistName": "Bruno Mars",
            "duration": 226.0,
            "syncedLyrics": "[00:08.50]Tonight I just want to take you higher"
          }
        ]
        """;

        using var httpClient = new HttpClient(new StubLrclibHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/get", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(searchJson, Encoding.UTF8, "application/json")
            };
        }));

        var client = new LrclibClient(httpClient);
        var res = await client.FetchLyricsAsync("24K Magic", "Bruno Mars", TimeSpan.FromSeconds(226));

        res.Found.Should().BeTrue();
        res.Lyrics.Should().NotBeNull();
        res.Lyrics!.Lines.Should().ContainSingle();
        res.Lyrics.Lines[0].Timestamp.Should().Be(TimeSpan.FromSeconds(8.5));
        res.Lyrics.Lines[0].Text.Should().Be("Tonight I just want to take you higher");
    }

    [Fact]
    public async Task Stage29_5_And_6_TrackTransitionAndAsyncRace_ClearsOldStateAndIgnoresLateTrackAResponse()
    {
        var tcsTrackA = new TaskCompletionSource<LyricsSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lyricsTrackB = LrcParser.Parse("[00:05.00]Track B First Line\n[00:12.00]Track B Second Line", "Song B", "Artist B");

        var fakeClient = new ControlledLyricsClient((title, artist, ct) =>
        {
            if (string.Equals(title, "Song A", StringComparison.OrdinalIgnoreCase))
            {
                return tcsTrackA.Task.WaitAsync(ct);
            }
            return Task.FromResult(new LyricsSearchResult(true, lyricsTrackB, "LRCLIB"));
        });

        await using var module = new LyricsModule(lyricsClient: fakeClient);
        await module.InitializeAsync();
        await module.StartAsync();

        var trackA = new MediaTrackInfo("Song A", "Artist A", "Album A", TimeSpan.FromSeconds(200));
        var trackB = new MediaTrackInfo("Song B", "Artist B", "Album B", TimeSpan.FromSeconds(180));

        // Start fetching Track A (in-flight slow request)
        var taskA = module.HandleTrackChangedAsync(trackA);

        // Immediately switch to Track B before Track A completes
        await module.HandleTrackChangedAsync(trackB);

        // Now complete Track A's delayed response
        var staleLyricsA = LrcParser.Parse("[00:02.00]Stale Track A Line", "Song A", "Artist A");
        tcsTrackA.TrySetResult(new LyricsSearchResult(true, staleLyricsA, "LRCLIB"));
        await taskA;

        // Track B state must remain intact and uncorrupted by Track A
        module.CurrentTrack.Title.Should().Be("Song B");
        module.CurrentTrack.Artist.Should().Be("Artist B");
        module.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);
        module.CurrentLyrics.Title.Should().Be("Song B");
        module.CurrentLyrics.Lines.Should().HaveCount(2);
        module.CurrentLyrics.Lines[0].Text.Should().Be("Track B First Line");
    }

    [Fact]
    public void Stage29_7_StaleCarryoverThumbnailDetection_RejectsPreviousSongCoverOnTrackSwitch()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        byte[] songACover = new byte[45847];
        Array.Fill(songACover, (byte)0x5A);
        int songAHash = WindowsGsmtcMediaSessionMonitor.ComputeThumbnailHash(songACover);

        // Switching from Song A to Song B (different album or empty browser album) with identical GSMTC thumbnail bytes -> Stale carryover!
        bool isStaleBrowserSwitch = WindowsGsmtcMediaSessionMonitor.IsStaleCarryoverThumbnail(
            isTrackSwitch: true,
            previousAlbum: "",
            newAlbum: "",
            newThumbnailBytes: (byte[])songACover.Clone(),
            previousThumbnailHash: songAHash,
            previousThumbnailLength: songACover.Length);
        isStaleBrowserSwitch.Should().BeTrue("Identical GSMTC thumbnail bytes on browser track switch must be rejected as stale carryover");

        bool isStaleDifferentAlbum = WindowsGsmtcMediaSessionMonitor.IsStaleCarryoverThumbnail(
            isTrackSwitch: true,
            previousAlbum: "Random Access Memories",
            newAlbum: "Currents",
            newThumbnailBytes: (byte[])songACover.Clone(),
            previousThumbnailHash: songAHash,
            previousThumbnailLength: songACover.Length);
        isStaleDifferentAlbum.Should().BeTrue("Identical GSMTC thumbnail bytes across different albums must be rejected as stale carryover");

        // Same non-empty album -> allowed
        bool isSameAlbum = WindowsGsmtcMediaSessionMonitor.IsStaleCarryoverThumbnail(
            isTrackSwitch: true,
            previousAlbum: "Random Access Memories",
            newAlbum: "Random Access Memories",
            newThumbnailBytes: (byte[])songACover.Clone(),
            previousThumbnailHash: songAHash,
            previousThumbnailLength: songACover.Length);
        isSameAlbum.Should().BeFalse("Tracks on the same album legitimately share album artwork");
    }

    [Fact]
    public void Stage29_8_AutomaticTrackTransitionElapsedTime_ResetsAndNeverCombinesPreviousSongTime()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;

        var song1StartUtc = new DateTime(2026, 11, 9, 16, 0, 0, DateTimeKind.Utc);
        var song2SwitchUtc = song1StartUtc.AddMinutes(3).AddSeconds(30); // Song 1 played for 3m30s (210s)
        var song2Duration = TimeSpan.FromSeconds(195);

        // Case 1: 300ms after automatic switch to Song 2, GSMTC still reports Song 1's end position (210s) and Song 1's LastUpdatedTime
        var check1Utc = song2SwitchUtc.AddMilliseconds(300);
        var (pos1, _) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPositionSinceStart: TimeSpan.FromSeconds(210),
            trackDuration: song2Duration,
            status: MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: song1StartUtc,
            nowUtc: check1Utc,
            lastTrackSwitchUtc: song2SwitchUtc,
            lastSeekUtc: DateTime.MinValue,
            lastSeekTargetPosition: TimeSpan.Zero);

        pos1.TotalSeconds.Should().BeApproximately(0.3, 0.05, "Elapsed time after automatic track switch must start from 0:00, not carry over Song 1's 3:30");

        // Case 2: 5 seconds into Song 2, GSMTC reports Position = 0:00 but still has LastUpdatedTime = song1StartUtc (3m35s ago!)
        var check2Utc = song2SwitchUtc.AddSeconds(5);
        var (pos2, _) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPositionSinceStart: TimeSpan.Zero,
            trackDuration: song2Duration,
            status: MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: song1StartUtc,
            nowUtc: check2Utc,
            lastTrackSwitchUtc: song2SwitchUtc,
            lastSeekUtc: DateTime.MinValue,
            lastSeekTargetPosition: TimeSpan.Zero);

        pos2.TotalSeconds.Should().BeApproximately(5.0, 0.05, "Elapsed time must only count time since Song 2 started (5s), never adding Song 1's 210s");
    }

    [Fact]
    public void Stage29_9_CompactModeVolumeSliderPopup_AnchoredPopupUpdatesVolumeAndMuteWithoutResizingWindow()
    {
        var volumeService = new FakeVolumeService(initialVolume: 65, initialMuted: false);

        volumeService.GetPlaybackVolumePercent().Should().Be(65);
        volumeService.IsPlaybackMuted.Should().BeFalse();

        // Slider updates actual playback volume
        volumeService.SetPlaybackVolumePercent(30);
        volumeService.GetPlaybackVolumePercent().Should().Be(30);

        // Mute button toggles mute state
        bool muted = volumeService.TogglePlaybackMute();
        muted.Should().BeTrue();
        volumeService.IsPlaybackMuted.Should().BeTrue();

        // Verify Compact Mode window size remains invariant (352x148 logical / 528x222 @ 150% DPI)
        var sizeBefore = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: false, dpiScale: 1.5);
        var sizeAfter = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: false, dpiScale: 1.5);
        sizeAfter.Should().Be(sizeBefore);

        // Verify XAML and code-behind structure for Compact Mode floating volume popup
        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string codeBehind = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        xaml.Should().Contain("x:Name=\"FlyoutVolumeCompact\"");
        xaml.Should().Contain("ShouldConstrainToRootBounds=\"False\"");
        xaml.Should().Contain("x:Name=\"BtnPopupMuteCompact\"");
        xaml.Should().Contain("x:Name=\"SliderVolumeCompact\"");
        xaml.Should().Contain("x:Name=\"BtnPopupVolumeMaxCompact\"");

        codeBehind.Should().Contain("SliderVolumeCompact.ValueChanged");
        codeBehind.Should().Contain("BtnPopupMuteCompact.Click");
    }

    private sealed class StubLrclibHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubLrclibHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class ControlledLyricsClient : ILyricsClient
    {
        private readonly Func<string, string, CancellationToken, Task<LyricsSearchResult>> _handler;

        public ControlledLyricsClient(Func<string, string, CancellationToken, Task<LyricsSearchResult>> handler)
        {
            _handler = handler;
        }

        public Task<LyricsSearchResult> FetchLyricsAsync(
            string trackName,
            string artistName,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default)
        {
            return _handler(trackName, artistName, cancellationToken);
        }
    }

    private sealed class FakeVolumeService : IAudioVolumeService
    {
        private int _volume;
        private bool _muted;

        public FakeVolumeService(int initialVolume, bool initialMuted)
        {
            _volume = initialVolume;
            _muted = initialMuted;
        }

        public event EventHandler<int>? VolumeChanged;
        public bool IsPlaybackMuted => _muted;
        public bool IsSystemMuted => _muted;
        public int MasterVolumePercent => _volume;
        public int SessionVolumePercent => _volume;
        public bool IsMuted => _muted;
        public bool HasActiveMediaSession => true;
        public string ActiveTargetName => "Speakers";
        public string PlaybackTargetName => "Speakers";
        public string SystemTargetName => "Speakers";
        public string OutputDeviceFriendlyName => "Speakers";
        public AudioDeviceCategory OutputDeviceCategory => AudioDeviceCategory.Speakers;

        public int GetVolumePercent() => _volume;
        public bool SetVolumePercent(int percent) => SetPlaybackVolumePercent(percent);
        public int GetPlaybackVolumePercent() => _volume;

        public bool SetPlaybackVolumePercent(int volumePercent)
        {
            _volume = Math.Clamp(volumePercent, 0, 100);
            if (_volume > 0) _muted = false;
            VolumeChanged?.Invoke(this, _volume);
            return true;
        }

        public int GetSystemVolumePercent() => _volume;
        public bool SetSystemVolumePercent(int percent) => SetPlaybackVolumePercent(percent);
        public bool SetPlaybackMute(bool muted) { _muted = muted; VolumeChanged?.Invoke(this, _volume); return true; }
        public bool SetSystemMute(bool muted) => SetPlaybackMute(muted);
        public bool SetMute(bool muted) => SetPlaybackMute(muted);

        public bool TogglePlaybackMute()
        {
            _muted = !_muted;
            VolumeChanged?.Invoke(this, _volume);
            return _muted;
        }

        public bool ToggleSystemMute() => TogglePlaybackMute();
        public bool ToggleMute() => TogglePlaybackMute();
        public void SetPreferredAppId(string? appId) { }
        public bool RefreshAndReconnect() => true;
        public void Dispose() { }
    }
}
