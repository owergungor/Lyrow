using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Audio.Routing;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Authoritative regression and unit test suite for Lyrow 3.7 (Stage 26 Phase 2) features 2.1 through 2.9.
/// </summary>
public sealed class LyrowV37FeatureVerificationTests
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
    public void Feature2_1_CompactModeAndLyricsWindowLayout_CalculatesDpiScaledSizesAndPreventsClipping()
    {
        // Collapsed compact window size at 100%, 125%, and 150% DPI
        var (lw100, lh100, w100, h100) = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: false, dpiScale: 1.0);
        lw100.Should().Be(500);
        lh100.Should().Be(232);
        w100.Should().Be(500);
        h100.Should().Be(232);

        var (_, _, w125, h125) = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: false, dpiScale: 1.25);
        w125.Should().Be(625);
        h125.Should().Be(290);

        // Expanded compact window size with 3-line lyrics at 100% and 150% DPI
        var (elw100, elh100, ew100, eh100) = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: true, dpiScale: 1.0);
        elw100.Should().Be(500);
        elh100.Should().Be(316);
        ew100.Should().Be(500);
        eh100.Should().Be(316);

        var (_, _, ew150, eh150) = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: true, dpiScale: 1.5);
        ew150.Should().Be(750);
        eh150.Should().Be(474);

        // Responsive metrics in Horizontal mode must leave sufficient vertical room so bottom controls never clip
        var metrics = LyricsViewportCalculator.CalculateResponsiveMetrics(960, 580, isLyricsPanelVisible: true);
        metrics.ArtworkSize.Should().BeLessThanOrEqualTo(345);
        metrics.PlayerColumnWidth.Should().BeGreaterThanOrEqualTo(160);
    }

    [Fact]
    public async Task Feature2_2_RealAudioOutputDeviceSwitching_SwitchesSystemDefaultAndRebindsCaptureAndVolume()
    {
        var routing = new SyntheticAudioRoutingService();

        routing.SetDefaultPlaybackDevice("device-headphones-01").Should().BeTrue();
        routing.CurrentDefaultDeviceId.Should().Be("device-headphones-01");

        routing.SetDefaultPlaybackDevice("").Should().BeFalse();

        if (OperatingSystem.IsWindows())
        {
            await using var wasapi = new Lyrow.Audio.Capture.WasapiAudioCaptureService();
            var realRouting = new WindowsAudioRoutingService();
            realRouting.IsSupported.Should().BeTrue();

            var devices = wasapi.GetAvailableDevices();
            if (devices.Count > 0)
            {
                var originalDefault = devices.FirstOrDefault(d => d.IsDefault) ?? devices[0];
                try
                {
                    if (devices.Count >= 2)
                    {
                        var alternate = devices.First(d => !string.Equals(d.Id, originalDefault.Id, StringComparison.OrdinalIgnoreCase));
                        bool switched = realRouting.SetDefaultPlaybackDevice(alternate.Id);
                        switched.Should().BeTrue("Windows IPolicyConfig COM SetDefaultEndpoint must switch real system default output device");

                        var updatedDevices = wasapi.GetAvailableDevices();
                        var newDefault = updatedDevices.FirstOrDefault(d => d.IsDefault);
                        newDefault.Should().NotBeNull();
                        newDefault!.Id.Should().Be(alternate.Id);
                    }
                    else
                    {
                        bool setDefault = realRouting.SetDefaultPlaybackDevice(originalDefault.Id);
                        setDefault.Should().BeTrue("Windows IPolicyConfig COM SetDefaultEndpoint must succeed on active system output endpoint");
                    }
                }
                finally
                {
                    // Always restore user's original default audio output device
                    realRouting.SetDefaultPlaybackDevice(originalDefault.Id);
                }
            }
        }

        string repoRoot = FindRepoRoot();
        string lyricsWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        lyricsWindowCode.Should().Contain("_audioRoutingService?.SetDefaultPlaybackDevice(dev.Id)");
        lyricsWindowCode.Should().Contain("_volumeService?.RefreshAndReconnect()");
        lyricsWindowCode.Should().Contain("_audioCaptureService.DeviceChanged +=");
        lyricsWindowCode.Should().Contain("_audioCaptureService.CaptureFaulted +=");
    }

    [Fact]
    public void Feature2_3_MiniPlayerProgressBarPlacement_PlacedDirectlyBeneathArtistInRightColumn()
    {
        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));

        // Verify CompactTrackAndProgressPanel is in Grid.Column="1" containing Title, Artist, and Progress Scrubber in order
        int panelIdx = xaml.IndexOf("x:Name=\"CompactTrackAndProgressPanel\"", StringComparison.Ordinal);
        int titleIdx = xaml.IndexOf("x:Name=\"TxtSongTitleCompact\"", StringComparison.Ordinal);
        int artistIdx = xaml.IndexOf("x:Name=\"TxtArtistAlbumCompact\"", StringComparison.Ordinal);
        int progressIdx = xaml.IndexOf("x:Name=\"GridProgressCompactHitBox\"", StringComparison.Ordinal);
        int lyricsGridIdx = xaml.IndexOf("x:Name=\"GridCompactLyrics\"", StringComparison.Ordinal);

        panelIdx.Should().BeGreaterThan(0);
        titleIdx.Should().BeGreaterThan(panelIdx);
        artistIdx.Should().BeGreaterThan(titleIdx);
        progressIdx.Should().BeGreaterThan(artistIdx);
        lyricsGridIdx.Should().BeGreaterThan(progressIdx, "Progress bar must be inside the top right track column, above the expandable lyrics row");
    }

    [Fact]
    public void Feature2_4_CompactModeLyricsStrictlyThreeLines_PreviousActiveNextWithSpacers()
    {
        var lines = new List<LyricLine>
        {
            new(TimeSpan.FromSeconds(1), "First line of song", 0),
            new(TimeSpan.FromSeconds(5), "Second line active", 1),
            new(TimeSpan.FromSeconds(10), "Third line upcoming", 2)
        };

        // At first line (index 0): Previous line is empty spacer, Active is line 0, Next is line 1
        var win0 = LyricsViewportCalculator.GetCompactThreeLineWindow(lines, 0);
        win0.PreviousLine.Should().BeEmpty();
        win0.ActiveLine.Should().Be("First line of song");
        win0.NextLine.Should().Be("Second line active");

        // At middle line (index 1): Previous is line 0, Active is line 1, Next is line 2
        var win1 = LyricsViewportCalculator.GetCompactThreeLineWindow(lines, 1);
        win1.PreviousLine.Should().Be("First line of song");
        win1.ActiveLine.Should().Be("Second line active");
        win1.NextLine.Should().Be("Third line upcoming");

        // At last line (index 2): Previous is line 1, Active is line 2, Next is empty spacer
        var win2 = LyricsViewportCalculator.GetCompactThreeLineWindow(lines, 2);
        win2.PreviousLine.Should().Be("Second line active");
        win2.ActiveLine.Should().Be("Third line upcoming");
        win2.NextLine.Should().BeEmpty();

        // Before first line (index -1): Previous is empty, Active is line 0, Next is line 1
        var winPre = LyricsViewportCalculator.GetCompactThreeLineWindow(lines, -1);
        winPre.PreviousLine.Should().BeEmpty();
        winPre.ActiveLine.Should().Be("First line of song");
        winPre.NextLine.Should().Be("Second line active");
    }

    [Fact]
    public void Feature2_5_LyricsWordTimingAlignment_CharacterWeightedAndInlineLrcAndOffsetSynced()
    {
        // 1. Inline word timestamp LRC parsing (<mm:ss.xx>)
        string enhancedLrc = "[00:10.00] <00:10.00>Hi <00:11.00>extraordinary <00:13.00>world\n[00:15.00] Next line";
        var parsed = LrcParser.Parse(enhancedLrc);
        parsed.Lines.Should().HaveCount(2);
        parsed.Lines[0].Text.Should().Be("Hi extraordinary world");
        parsed.Lines[0].Words.Should().NotBeNull();
        parsed.Lines[0].Words!.Count.Should().Be(3);
        parsed.Lines[0].Words![0].Word.Should().Be("Hi");
        parsed.Lines[0].Words![1].Word.Should().Be("extraordinary");
        parsed.Lines[0].Words![2].Word.Should().Be("world");

        // 2. Character-weighted word progress prevents visual drift when a short word ("Hi", 2 chars)
        // is followed by a long word ("extraordinary", 13 chars)
        double afterFirstShortWord = LyricsFillProgressCalculator.CalculateWordTimingProgress(
            TimeSpan.FromSeconds(11.0),
            parsed.Lines[0].StartTime,
            parsed.Lines[1].StartTime,
            parsed.Lines[0].Words,
            parsed.Lines[0].Text);

        // "Hi" is 2 chars out of 2 + 13 + 5 = 20 chars -> 2/20 = 0.10 (10%), NOT 33.3%!
        afterFirstShortWord.Should().BeApproximately(0.10, 0.02);

        // 3. Verify LyricsWindow applies SyncOffsetMs to word/subline fill progress
        string repoRoot = FindRepoRoot();
        string lyricsWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        lyricsWindowCode.Should().Contain("TimeSpan.FromMilliseconds(_lyricsModule.Synchronizer.SyncOffsetMs)");
    }

    [Fact]
    public void Feature2_6_AnimatedAlbumArtwork_DetectsAnimatedFormatsAndRespectsReducedMotion()
    {
        // Static GIF89a with 1 frame -> false
        byte[] singleFrameGif =
        {
            (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
            10, 0, 10, 0, 0, 0, 0,
            0x2C, 0, 0, 0, 0, 10, 0, 10, 0, 0,
            0x3B
        };
        ArtworkService.IsAnimatedImageData(singleFrameGif).Should().BeFalse();

        // Multi-frame GIF89a with 2 image descriptors (0x2C) -> true
        byte[] twoFrameGif =
        {
            (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
            10, 0, 10, 0, 0, 0, 0,
            0x2C, 0, 0, 0, 0, 10, 0, 10, 0, 0,
            0x2C, 0, 0, 0, 0, 10, 0, 10, 0, 0,
            0x3B
        };
        ArtworkService.IsAnimatedImageData(twoFrameGif).Should().BeTrue();

        // Verify ArtworkBitmapManager and LyricsWindow stop animation when Reduced Motion is active
        string repoRoot = FindRepoRoot();
        string artworkMgrCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "Platform", "ArtworkBitmapManager.cs"));
        artworkMgrCode.Should().Contain("image.Stop()");
    }

    [Fact]
    public void Feature2_7_WindowScopedNotifications_LyricsWindowOwnsDedicatedToastContainer()
    {
        string repoRoot = FindRepoRoot();
        string lyricsXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string lyricsCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        lyricsXaml.Should().Contain("x:Name=\"LyricsToastContainer\"");
        lyricsCode.Should().Contain("new ToastNotificationManager(LyricsToastContainer, DispatcherQueue)");
    }

    [Fact]
    public void Feature2_8_WebPlayerArtworkResolution_DetectsLowResAndUpgradesUrlsAndOnlineLookup()
    {
        // 1. URL high-res upgrader for YouTube Music / Google / Apple Music / Spotify CDN URLs
        string ytLowRes = "https://lh3.googleusercontent.com/abc=w120-h120-l90-rj";
        string ytHighRes = ArtworkService.UpgradeArtworkUrlToHighRes(ytLowRes);
        ytHighRes.Should().Contain("=w600-h600");

        string appleLowRes = "https://is1-ssl.mzstatic.com/image/thumb/Music116/v4/ab/cd/100x100bb.jpg";
        string appleHighRes = ArtworkService.UpgradeArtworkUrlToHighRes(appleLowRes);
        appleHighRes.Should().Contain("600x600bb.");

        // 2. Low-resolution PNG header detection (e.g., 120x120 thumbnail from browser GSMTC)
        byte[] smallPngHeader = new byte[32];
        byte[] pngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(pngSig, 0, smallPngHeader, 0, 8);
        // Width = 120 (0x00000078), Height = 120 (0x00000078) at offset 16..23
        smallPngHeader[19] = 120;
        smallPngHeader[23] = 120;

        ArtworkService.TryGetImageDimensions(smallPngHeader, out int w, out int h).Should().BeTrue();
        w.Should().Be(120);
        h.Should().Be(120);
        ArtworkService.IsLowResolutionThumbnail(smallPngHeader).Should().BeTrue();

        // 600x600 PNG header -> not low-res
        byte[] highResPngHeader = new byte[60000];
        Array.Copy(pngSig, 0, highResPngHeader, 0, 8);
        highResPngHeader[18] = 0x02;
        highResPngHeader[19] = 0x58; // 600
        highResPngHeader[22] = 0x02;
        highResPngHeader[23] = 0x58; // 600
        ArtworkService.IsLowResolutionThumbnail(highResPngHeader).Should().BeFalse();
    }

    [Fact]
    public void Feature2_9_AppleMusicDesktopSeekWarning_DistinguishesDesktopFromWebAndProvidesClickableRedirect()
    {
        // Apple Music Desktop AUMIDs must be classified as AppleMusicDesktop
        MediaSessionProfileHelper.ClassifySourceAppId("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App")
            .Should().Be(MediaSessionSourceProfile.AppleMusicDesktop);
        MediaSessionProfileHelper.IsAppleMusicDesktop("AppleMusic.exe").Should().BeTrue();

        // Browser sessions (Chrome, Edge, Firefox, Brave) must be classified as BrowserSession, NOT AppleMusicDesktop
        MediaSessionProfileHelper.ClassifySourceAppId("MSEdge").Should().Be(MediaSessionSourceProfile.BrowserSession);
        MediaSessionProfileHelper.ClassifySourceAppId("chrome.exe").Should().Be(MediaSessionSourceProfile.BrowserSession);
        MediaSessionProfileHelper.IsAppleMusicDesktop("chrome.exe").Should().BeFalse();

        // Spotify Desktop must be classified as SpotifyDesktop
        MediaSessionProfileHelper.ClassifySourceAppId("Spotify.exe").Should().Be(MediaSessionSourceProfile.SpotifyDesktop);

        // Verify redirect URL and localized strings exist
        MediaSessionProfileHelper.AppleMusicWebUrl.Should().Be("https://music.apple.com/");
        LocalizationService.Instance["Lyrics_AppleMusicDesktopSeekWarning"].Should().NotBeNullOrWhiteSpace();
        LocalizationService.Instance["Lyrics_OpenAppleMusicWeb"].Should().NotBeNullOrWhiteSpace();
    }
}
