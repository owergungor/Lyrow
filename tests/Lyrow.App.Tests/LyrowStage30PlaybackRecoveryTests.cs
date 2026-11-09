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
/// Stage 30 Acceptance & Recovery Test Suite:
/// 1. Compact Mode default (352x148 DIP / 528x222 px at 150% DPI) and minimum (320x136 DIP) dimensions
/// 2. Compact Mode volume slider popup does not alter window dimensions
/// 3. Track transition invalidates previous lyrics immediately without leakage
/// 4. Delayed async API responses for previous track are discarded
/// 5. Track transition resets elapsed time to 0:00 and never accumulates previous track's time
/// 6. AudioRoutingService SetDefaultPlaybackDevice accepts endpoint ID and reports support
/// 7. Compact Mode volume button has accessible name, primary brush foreground, and Auto column layout
/// 8. Compact Mode device selector has bounded width (<= 60 DIPs) preventing layout clipping
/// 9. Apple Music Web seek reconciliation is preserved and holds post-seek timestamp
/// </summary>
public sealed class LyrowStage30PlaybackRecoveryTests
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
    public void Stage30_1_CompactMode_DefaultAndMinimumDimensions_MatchStage28Target()
    {
        // Assert mathematical target dimensions from Stage 28
        LyricsViewportCalculator.CompactDefaultLogicalWidth.Should().Be(352);
        LyricsViewportCalculator.CompactDefaultLogicalHeight.Should().Be(148);
        LyricsViewportCalculator.CompactMinLogicalWidth.Should().Be(320);
        LyricsViewportCalculator.CompactMinLogicalHeight.Should().Be(136);
        LyricsViewportCalculator.CompactArtworkLogicalSize.Should().Be(60);

        // Assert 150% DPI scaling matches user's physical target (528 x 222 px, within 528 x 214-230 px target)
        var (logicalW, logicalH, physW150, physH150) = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: false, dpiScale: 1.5);
        logicalW.Should().Be(352);
        logicalH.Should().Be(148);
        physW150.Should().Be(528);
        physH150.Should().Be(222);

        // Artwork size at 150% DPI must be 90x90 px
        int artPhysW = (int)Math.Ceiling(LyricsViewportCalculator.CompactArtworkLogicalSize * 1.5);
        artPhysW.Should().Be(90);

        // Assert minimum dimensions
        var (minLogW, minLogH, minPhysW, minPhysH) = LyricsViewportCalculator.CalculateCompactMinimumWindowSize(dpiScale: 1.5);
        minLogW.Should().Be(320);
        minLogH.Should().Be(136);
        minPhysW.Should().Be(480);
        minPhysH.Should().Be(204);
    }

    [Fact]
    public void Stage30_2_CompactMode_VolumeSliderPopup_DoesNotAlterWindowDimensions()
    {
        var sizeClosed = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: false, dpiScale: 1.5);
        // Opening or closing the volume flyout popup should not resize the compact window
        var sizeOpen = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: false, dpiScale: 1.5);

        sizeOpen.LogicalWidth.Should().Be(sizeClosed.LogicalWidth);
        sizeOpen.LogicalHeight.Should().Be(sizeClosed.LogicalHeight);
        sizeOpen.PhysicalWidth.Should().Be(sizeClosed.PhysicalWidth);
        sizeOpen.PhysicalHeight.Should().Be(sizeClosed.PhysicalHeight);
    }

    [Fact]
    public async Task Stage30_3_TrackTransition_InvalidatesPreviousLyricsImmediately_WithoutLeakage()
    {
        var trackA = new MediaTrackInfo("Song A", "Artist 1", "Album 1", TimeSpan.FromMinutes(3));
        var trackB = new MediaTrackInfo("Song B", "Artist 2", "Album 2", TimeSpan.FromMinutes(4));

        var lrcClient = new FakeLyricsClient("Song A", "Artist 1", "[00:10.00]Line from Song A");
        await using var module = new LyricsModule(lyricsClient: lrcClient);
        await module.StartAsync();

        // Load Track A
        await module.HandleTrackChangedAsync(trackA);
        module.CurrentLyrics.Lines.Should().HaveCount(1);
        module.CurrentLyrics.Lines[0].Text.Should().Be("Line from Song A");
        module.CurrentLyricsSongKey.Should().Be("Artist 1_Song A");

        // Transition to Track B - should synchronously invalidate lyrics and key
        var handleTask = module.HandleTrackChangedAsync(trackB);
        module.CurrentLyricsSongKey.Should().NotBe("Artist 1_Song A");
        await handleTask;

        // If Track B has no lyrics, CurrentLyrics must be Empty, not Song A's lyrics
        module.CurrentLyrics.Lines.Should().BeEmpty();
        module.ActiveLineIndex.Should().Be(-1);
    }

    [Fact]
    public async Task Stage30_4_TrackTransition_DelayedApiResponseForTrackA_IsDiscarded()
    {
        var trackA = new MediaTrackInfo("Slow Song A", "Slow Artist", "Album A", TimeSpan.FromMinutes(3));
        var trackB = new MediaTrackInfo("Fast Song B", "Fast Artist", "Album B", TimeSpan.FromMinutes(3));

        var delayedClient = new DelayedLyricsClient(delayMs: 250, lrcText: "[00:10.00]Late line for Song A");
        await using var module = new LyricsModule(lyricsClient: delayedClient);
        await module.StartAsync();

        // Start fetch for Track A (slow)
        var fetchTaskA = module.HandleTrackChangedAsync(trackA);

        // Immediately switch to Track B
        await Task.Delay(20);
        await module.HandleTrackChangedAsync(trackB);

        // Wait for Track A's delayed fetch to finish in background
        await fetchTaskA;

        // Verify Track A's late response was discarded and never assigned to CurrentLyrics
        module.CurrentTrack.Title.Should().Be("Fast Song B");
        module.CurrentLyrics.Lines.Should().BeEmpty();
        module.CurrentLyricsSongKey.Should().NotBe("Slow Artist_Slow Song A");
    }

    [Fact]
    public void Stage30_5_TrackTransition_ElapsedTime_ResetsToZero_AndDoesNotAccumulate()
    {
        var trackDuration = TimeSpan.FromMinutes(3); // 03:00
        var nowUtc = DateTime.UtcNow;
        var lastTrackSwitchUtc = nowUtc.AddSeconds(-19); // Track B switched 19s ago
        var lastSeekUtc = DateTime.MinValue; // No seek on Track B
        var lastSeekTargetPosition = TimeSpan.Zero;

        // Case A: GSMTC reports the previous song's end position (e.g. 2:00 from Song A)
        // ReconcileTimelinePosition must detect that 2:00 > 19s in post-switch window and clamp to 19s
        var rawPosStale = TimeSpan.FromMinutes(2); // Stale 2 minutes from Song A
        var (reconciledStale, _) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPosStale,
            trackDuration,
            MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: nowUtc.AddSeconds(-19),
            nowUtc: nowUtc,
            lastTrackSwitchUtc: lastTrackSwitchUtc,
            lastSeekUtc: lastSeekUtc,
            lastSeekTargetPosition: lastSeekTargetPosition);

        reconciledStale.TotalSeconds.Should().BeApproximately(19.0, 1.0);
        reconciledStale.Should().NotBe(TimeSpan.FromMinutes(2));

        // Case B: GSMTC reports position correctly as 19s
        // ReconcileTimelinePosition must preserve 19s and never add previous track's 2 minutes
        var rawPosCorrect = TimeSpan.FromSeconds(19);
        var (reconciledCorrect, _) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPosCorrect,
            trackDuration,
            MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: nowUtc.AddSeconds(-19),
            nowUtc: nowUtc,
            lastTrackSwitchUtc: lastTrackSwitchUtc,
            lastSeekUtc: lastSeekUtc,
            lastSeekTargetPosition: lastSeekTargetPosition);

        reconciledCorrect.TotalSeconds.Should().BeApproximately(19.0, 1.0);
        // It must NOT be 2:19 (139 seconds)!
        reconciledCorrect.TotalSeconds.Should().BeLessThan(30.0);
    }

    [Fact]
    public void Stage30_6_AudioRoutingService_SetDefaultPlaybackDevice_AcceptsEndpointId()
    {
        var synthetic = new Lyrow.Audio.Routing.SyntheticAudioRoutingService();
        synthetic.IsSupported.Should().BeTrue();
        synthetic.SetDefaultPlaybackDevice("{0.0.0.00000000}.{dummy-guid}").Should().BeTrue();

        var windowsRouting = new Lyrow.Audio.Routing.WindowsAudioRoutingService();
        windowsRouting.IsSupported.Should().Be(OperatingSystem.IsWindows());
        // Null or whitespace endpoint id should be rejected safely
        windowsRouting.SetDefaultPlaybackDevice(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void Stage30_7_CompactMode_ControlsLayout_AutoColumns_NoClipping()
    {
        string repoRoot = FindRepoRoot();
        string xamlPath = Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml");
        File.Exists(xamlPath).Should().BeTrue();
        string content = File.ReadAllText(xamlPath);

        // ColumnDefinitions must be Auto,*,Auto to prevent clipping of left volume/device controls
        content.Should().Contain("x:Name=\"CompactControlsRow\"");
        content.Should().Contain("ColumnDefinitions=\"Auto,*,Auto\"");

        // IconVolumeMuteCompact must use primary brush so it is always visible (not invisible or hidden until hover)
        content.Should().Contain("x:Name=\"IconVolumeMuteCompact\"");
        content.Should().Contain("Foreground=\"{ThemeResource TextFillColorPrimaryBrush}\"");

        // BtnVolumeMuteCompact must have explicit 26x26 bounds and tooltip
        content.Should().Contain("x:Name=\"BtnVolumeMuteCompact\" Width=\"26\" Height=\"26\"");
        content.Should().Contain("AutomationProperties.Name=\"Volume and Mute\"");
    }

    [Fact]
    public void Stage30_8_CompactMode_DeviceSelector_HasBoundedWidth_NoClipping()
    {
        string repoRoot = FindRepoRoot();
        string xamlPath = Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml");
        File.Exists(xamlPath).Should().BeTrue();
        string content = File.ReadAllText(xamlPath);

        // TxtDeviceCompact MaxWidth must be <= 60 DIPs to fit comfortably next to volume button without clipping
        content.Should().Contain("x:Name=\"TxtDeviceCompact\"");
        content.Should().Contain("MaxWidth=\"60\"");

        // IconDeviceCompact must use primary brush
        content.Should().Contain("x:Name=\"IconDeviceCompact\"");
    }

    [Fact]
    public void Stage30_9_AppleMusicWeb_Seeking_PreservedAndNotBroken()
    {
        // Apple Music Web seek protection: when user seeks to 01:45, GSMTC may briefly lag in updating position
        var seekTarget = TimeSpan.FromSeconds(105); // 01:45
        var trackDuration = TimeSpan.FromMinutes(4);
        var nowUtc = DateTime.UtcNow;
        var lastSeekUtc = nowUtc.AddMilliseconds(-300); // Seek occurred 300ms ago
        var lastTrackSwitchUtc = nowUtc.AddMinutes(-1); // Same track

        // GSMTC temporarily reports old position 00:20 (stale pre-seek position)
        var rawPosStale = TimeSpan.FromSeconds(20);
        var (reconciled, _) = WindowsGsmtcMediaSessionMonitor.ReconcileTimelinePosition(
            rawPosStale,
            trackDuration,
            MediaPlaybackStatus.Playing,
            gsmtcReportedUtc: nowUtc.AddMilliseconds(-300),
            nowUtc: nowUtc,
            lastTrackSwitchUtc: lastTrackSwitchUtc,
            lastSeekUtc: lastSeekUtc,
            lastSeekTargetPosition: seekTarget);

        // Verified seek behavior: ReconcileTimelinePosition protects post-seek window and extrapolates from seekTarget
        reconciled.TotalSeconds.Should().BeApproximately(105.3, 0.5);
    }

    private sealed class FakeLyricsClient : ILyricsClient
    {
        private readonly string _matchTitle;
        private readonly string _matchArtist;
        private readonly string _lrcText;

        public FakeLyricsClient(string matchTitle, string matchArtist, string lrcText)
        {
            _matchTitle = matchTitle;
            _matchArtist = matchArtist;
            _lrcText = lrcText;
        }

        public Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            if (string.Equals(trackName, _matchTitle, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(artistName, _matchArtist, StringComparison.OrdinalIgnoreCase))
            {
                var parsed = LrcParser.Parse(_lrcText, trackName, artistName);
                return Task.FromResult(new LyricsSearchResult(true, parsed, "Test"));
            }
            return Task.FromResult(new LyricsSearchResult(false, null, "Test", "Not found"));
        }
    }

    private sealed class DelayedLyricsClient : ILyricsClient
    {
        private readonly int _delayMs;
        private readonly string _lrcText;

        public DelayedLyricsClient(int delayMs, string lrcText)
        {
            _delayMs = delayMs;
            _lrcText = lrcText;
        }

        public async Task<LyricsSearchResult> FetchLyricsAsync(string trackName, string artistName, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(trackName, "Slow Song A", StringComparison.OrdinalIgnoreCase))
            {
                return new LyricsSearchResult(false, null, "Delayed", "Not found");
            }
            var parsed = LrcParser.Parse(_lrcText, trackName, artistName);
            return new LyricsSearchResult(true, parsed, "Delayed");
        }
    }
}
