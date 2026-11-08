using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Lyrow.Music.Metadata;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Stage 27 Manual Acceptance Defect Fixes regression suite:
/// 1. Compact layout target arrangement (Dar Mod UI)
/// 2. Artwork null/valid stream behavior
/// 3. YouTube Music Web artwork retrieval pipeline
/// 4. Apple Music Web artwork retrieval pipeline
/// 5. Artwork cache and binding regression
/// 6. Spotify Desktop artwork regression
/// 7. Browser executable resolution
/// 8. Apple Music Web URL browser launch arguments
/// 9. Apple Music Desktop URL interception prevention
/// </summary>
public sealed class LyrowStage27AcceptanceRegressionTests
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

    private static byte[] CreateValidPngBytes(int width, int height, int totalByteSize = 64)
    {
        byte[] buffer = new byte[Math.Max(32, totalByteSize)];
        byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(sig, 0, buffer, 0, 8);
        buffer[16] = (byte)((width >> 24) & 0xFF);
        buffer[17] = (byte)((width >> 16) & 0xFF);
        buffer[18] = (byte)((width >> 8) & 0xFF);
        buffer[19] = (byte)(width & 0xFF);
        buffer[20] = (byte)((height >> 24) & 0xFF);
        buffer[21] = (byte)((height >> 16) & 0xFF);
        buffer[22] = (byte)((height >> 8) & 0xFF);
        buffer[23] = (byte)(height & 0xFF);
        return buffer;
    }

    private static byte[] CreateValidWebpVp8xBytes(int width, int height, int totalByteSize = 64)
    {
        byte[] buffer = new byte[Math.Max(32, totalByteSize)];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(buffer, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(buffer, 8);
        Encoding.ASCII.GetBytes("VP8X").CopyTo(buffer, 12);
        int wMinus1 = Math.Max(0, width - 1);
        int hMinus1 = Math.Max(0, height - 1);
        buffer[24] = (byte)(wMinus1 & 0xFF);
        buffer[25] = (byte)((wMinus1 >> 8) & 0xFF);
        buffer[26] = (byte)((wMinus1 >> 16) & 0xFF);
        buffer[27] = (byte)(hMinus1 & 0xFF);
        buffer[28] = (byte)((hMinus1 >> 8) & 0xFF);
        buffer[29] = (byte)((hMinus1 >> 16) & 0xFF);
        return buffer;
    }

    [Fact]
    public void Regression1_CompactLayoutTargetArrangement_MatchesReferenceHorizontalHierarchy()
    {
        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // 1. Top row: Artwork on left (Column 0), Title -> Artist -> Progress -> Elapsed/Remaining on right (Column 1)
        int topRowIdx = xaml.IndexOf("x:Name=\"CompactTopRow\"", StringComparison.Ordinal);
        int artworkIdx = xaml.IndexOf("x:Name=\"BorderTrackArtworkCompact\"", StringComparison.Ordinal);
        int rightPanelIdx = xaml.IndexOf("x:Name=\"CompactTrackAndProgressPanel\"", StringComparison.Ordinal);
        int titleIdx = xaml.IndexOf("x:Name=\"TxtSongTitleCompact\"", StringComparison.Ordinal);
        int artistIdx = xaml.IndexOf("x:Name=\"TxtArtistAlbumCompact\"", StringComparison.Ordinal);
        int progressIdx = xaml.IndexOf("x:Name=\"ProgressTimelineCompact\"", StringComparison.Ordinal);
        int elapsedIdx = xaml.IndexOf("x:Name=\"TxtElapsedCompact\"", StringComparison.Ordinal);
        int remainingIdx = xaml.IndexOf("x:Name=\"TxtRemainingCompact\"", StringComparison.Ordinal);

        topRowIdx.Should().BeGreaterThan(0);
        artworkIdx.Should().BeGreaterThan(topRowIdx);
        rightPanelIdx.Should().BeGreaterThan(artworkIdx);
        titleIdx.Should().BeGreaterThan(rightPanelIdx);
        artistIdx.Should().BeGreaterThan(titleIdx);
        progressIdx.Should().BeGreaterThan(artistIdx);
        elapsedIdx.Should().BeGreaterThan(progressIdx);
        remainingIdx.Should().BeGreaterThan(elapsedIdx);

        // 2. Bottom control bar: Left (Volume/Mute + Device), Center (Prev / Play-Pause / Next), Right (Lyrics + Normal Mode)
        int controlsRowIdx = xaml.IndexOf("x:Name=\"CompactControlsRow\"", StringComparison.Ordinal);
        int leftPanelIdx = xaml.IndexOf("x:Name=\"CompactLeftControlsPanel\"", StringComparison.Ordinal);
        int volMuteIdx = xaml.IndexOf("x:Name=\"BtnVolumeMuteCompact\"", StringComparison.Ordinal);
        int deviceIdx = xaml.IndexOf("x:Name=\"BtnDeviceSelectorCompact\"", StringComparison.Ordinal);
        int centerPanelIdx = xaml.IndexOf("x:Name=\"CompactCenterTransportPanel\"", StringComparison.Ordinal);
        int prevIdx = xaml.IndexOf("x:Name=\"BtnPrevCompact\"", StringComparison.Ordinal);
        int playIdx = xaml.IndexOf("x:Name=\"BtnPlayPauseCompact\"", StringComparison.Ordinal);
        int nextIdx = xaml.IndexOf("x:Name=\"BtnNextCompact\"", StringComparison.Ordinal);
        int rightControlsIdx = xaml.IndexOf("x:Name=\"CompactRightControlsPanel\"", StringComparison.Ordinal);
        int lyricsBtnIdx = xaml.IndexOf("x:Name=\"BtnExpandFromCompact\"", StringComparison.Ordinal);
        int normalModeBtnIdx = xaml.IndexOf("x:Name=\"BtnNormalModeCompact\"", StringComparison.Ordinal);

        controlsRowIdx.Should().BeGreaterThan(remainingIdx);
        leftPanelIdx.Should().BeGreaterThan(controlsRowIdx);
        volMuteIdx.Should().BeGreaterThan(leftPanelIdx);
        deviceIdx.Should().BeGreaterThan(volMuteIdx);
        centerPanelIdx.Should().BeGreaterThan(deviceIdx);
        prevIdx.Should().BeGreaterThan(centerPanelIdx);
        playIdx.Should().BeGreaterThan(prevIdx);
        nextIdx.Should().BeGreaterThan(playIdx);
        rightControlsIdx.Should().BeGreaterThan(nextIdx);
        lyricsBtnIdx.Should().BeGreaterThan(rightControlsIdx);
        normalModeBtnIdx.Should().BeGreaterThan(lyricsBtnIdx, "Lyrics button then Normal/List mode button on the bottom-right");

        // 3. TitleBar caption controls (Minimize, Maximize/Restore, Close) enabled on OverlappedPresenter
        code.Should().Contain("overlapped.IsMinimizable = true;");
        code.Should().Contain("overlapped.IsMaximizable = true;");
        code.Should().Contain("overlapped.IsResizable = true;");
    }

    [Fact]
    public async Task Regression2_ArtworkNullAndCorruptStreamBehavior_NeverThrowsAndRejectsCorruptData()
    {
        // Null GSMTC stream reference returns null safely
        var nullStreamResult = await WindowsGsmtcMediaSessionMonitor.ReadThumbnailStreamAsync(null);
        nullStreamResult.Should().BeNull();

        var service = new ArtworkService();

        // Null thumbnail on placeholder track returns null
        var emptyThumbTrack = new MediaTrackInfo("Unknown Track", "Unknown Artist", "", TimeSpan.FromMinutes(3), ThumbnailData: null);
        (await service.GetArtworkBytesAsync(emptyThumbTrack)).Should().BeNull();

        // Corrupt thumbnail data is rejected and returns null
        var corruptTrack = new MediaTrackInfo("Broken Cover", "Faulty Artist", "", TimeSpan.FromMinutes(3), ThumbnailData: new byte[] { 0x00, 0x01, 0x02 });
        (await service.GetArtworkBytesAsync(corruptTrack)).Should().BeNull();
    }

    [Fact]
    public async Task Regression3_YouTubeMusicWebArtworkPipeline_NormalizesTopicAndFetchesVerifiedArtworkWhenGsmtcThumbnailNull()
    {
        // 1. MediaMetadataNormalizer strips "- Topic" without polluting Album = "Topic"
        var normalizer = new MediaMetadataNormalizer();
        var normalized = normalizer.Normalize(
            "Blinding Lights (Official Video)",
            "The Weeknd - Topic",
            "");
        normalized.CleanSearchTitle.Should().Be("Blinding Lights");
        normalized.Artist.Should().Be("The Weeknd");
        normalized.Album.Should().BeEmpty();

        // 2. CleanSearchMetadataForArtwork removes "- Topic", "VEVO", "(Official Audio)", etc.
        var (cleanArtist, cleanTitle) = ArtworkService.CleanSearchMetadataForArtwork(
            "DaftPunkVEVO",
            "Get Lucky (Official Audio)");
        cleanArtist.Should().Be("DaftPunk");
        cleanTitle.Should().Be("Get Lucky");

        // 3. When YouTube Music Web session has ThumbnailData == null, ArtworkService fetches high-res cover from online API
        byte[] expectedHighResPng = CreateValidPngBytes(600, 600, 24_000);
        using var handler = new MockArtworkHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Host.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
            {
                string json = """
                {
                  "resultCount": 1,
                  "results": [
                    {
                      "artistName": "The Weeknd",
                      "trackName": "Blinding Lights",
                      "collectionName": "After Hours",
                      "artworkUrl100": "https://is1-ssl.mzstatic.com/image/thumb/Music125/v4/100x100bb.jpg"
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            if (req.RequestUri.AbsoluteUri.Contains("600x600bb.jpg", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(expectedHighResPng)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        var artworkService = new ArtworkService(httpClient);

        var ytWebTrackWithNullThumb = new MediaTrackInfo(
            Title: "Blinding Lights (Official Video)",
            Artist: "The Weeknd - Topic",
            Album: string.Empty,
            Duration: TimeSpan.FromSeconds(200),
            ThumbnailData: null);

        var resolvedBytes = await artworkService.GetArtworkBytesAsync(ytWebTrackWithNullThumb);
        resolvedBytes.Should().NotBeNull();
        resolvedBytes.Should().HaveCount(expectedHighResPng.Length);

        // 4. Mismatched artist/song results from online search must be rejected so wrong cover is never shown
        var unmatchedTrack = new MediaTrackInfo(
            Title: "NonExistentSongXYZ",
            Artist: "CompletelyDifferentArtist",
            Album: string.Empty,
            Duration: TimeSpan.FromSeconds(180),
            ThumbnailData: null);

        var unmatchedBytes = await artworkService.GetArtworkBytesAsync(unmatchedTrack);
        unmatchedBytes.Should().BeNull("ArtworkService must never return artwork belonging to a different artist/song");
    }

    [Fact]
    public async Task Regression4_AppleMusicWebArtworkPipeline_ParsesWebpAndUpgradesLowResOrDirectUri()
    {
        // 1. WebP VP8X dimension extraction for browser GSMTC thumbnails
        byte[] lowResWebp = CreateValidWebpVp8xBytes(96, 96, 4096);
        ArtworkService.TryGetImageDimensions(lowResWebp, out int w, out int h).Should().BeTrue();
        w.Should().Be(96);
        h.Should().Be(96);
        ArtworkService.IsLowResolutionThumbnail(lowResWebp).Should().BeTrue();

        byte[] highResWebp = CreateValidWebpVp8xBytes(600, 600, 32_000);
        ArtworkService.TryGetImageDimensions(highResWebp, out int hw, out int hh).Should().BeTrue();
        hw.Should().Be(600);
        hh.Should().Be(600);
        ArtworkService.IsLowResolutionThumbnail(highResWebp).Should().BeFalse();

        // 2. Apple Music Web track with low-res 96x96 WebP thumbnail upgrades to 600x600 high-res cover
        byte[] upgradedAppleCover = CreateValidPngBytes(600, 600, 28_000);
        using var handler = new MockArtworkHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Host.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
            {
                string json = """
                {
                  "resultCount": 1,
                  "results": [
                    {
                      "artistName": "Dua Lipa",
                      "trackName": "Levitating",
                      "collectionName": "Future Nostalgia",
                      "artworkUrl100": "https://is1-ssl.mzstatic.com/image/thumb/Music116/v4/100x100bb.jpg"
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(upgradedAppleCover)
            };
        });

        using var httpClient = new HttpClient(handler);
        var artworkService = new ArtworkService(httpClient);

        var appleWebTrack = new MediaTrackInfo(
            Title: "Levitating",
            Artist: "Dua Lipa",
            Album: "Future Nostalgia",
            Duration: TimeSpan.FromSeconds(203),
            ThumbnailData: lowResWebp);

        var result = await artworkService.GetArtworkBytesAsync(appleWebTrack);
        result.Should().NotBeNull();
        result!.Length.Should().Be(upgradedAppleCover.Length);
    }

    [Fact]
    public async Task Regression5_ArtworkCacheAndBinding_PreservesCachedOnlineArtworkAcrossNullThumbnailPolls()
    {
        byte[] onlineCover = CreateValidPngBytes(600, 600, 25_000);
        int networkCalls = 0;

        using var handler = new MockArtworkHttpMessageHandler(req =>
        {
            Interlocked.Increment(ref networkCalls);
            if (req.RequestUri!.Host.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
            {
                string json = """
                {
                  "resultCount": 1,
                  "results": [
                    {
                      "artistName": "Arctic Monkeys",
                      "trackName": "Do I Wanna Know?",
                      "collectionName": "AM",
                      "artworkUrl100": "https://is1-ssl.mzstatic.com/image/thumb/Music115/v4/100x100bb.jpg"
                    }
                  ]
                }
                """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(onlineCover)
            };
        });

        using var httpClient = new HttpClient(handler);
        var artworkService = new ArtworkService(httpClient);
        var bitmapManager = new ArtworkBitmapManager(artworkService);

        var trackPoll1 = new MediaTrackInfo("Do I Wanna Know?", "Arctic Monkeys", "AM", TimeSpan.FromSeconds(272), ThumbnailData: null);
        var bytes1 = await bitmapManager.GetArtworkBytesAsync(trackPoll1);
        bytes1.Should().NotBeNull();
        int callsAfterFirstFetch = networkCalls;
        callsAfterFirstFetch.Should().Be(2);

        // Subsequent poll for the same track with ThumbnailData == null must hit cache without re-fetching
        var bytes2 = await bitmapManager.GetArtworkBytesAsync(trackPoll1);
        bytes2.Should().NotBeNull();
        bytes2!.Length.Should().Be(onlineCover.Length);
        networkCalls.Should().Be(callsAfterFirstFetch, "Cached online artwork must not be evicted when subsequent GSMTC polls report ThumbnailData == null");
    }

    [Fact]
    public async Task Regression6_SpotifyDesktopArtwork_UsesNativeHighResGsmtcThumbnailWithoutNetworkCalls()
    {
        int networkCalls = 0;
        using var handler = new MockArtworkHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref networkCalls);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var httpClient = new HttpClient(handler);
        var artworkService = new ArtworkService(httpClient);

        byte[] spotifyNativeCover = CreateValidPngBytes(640, 640, 42_000);
        var spotifyTrack = new MediaTrackInfo(
            Title: "Starboy",
            Artist: "The Weeknd",
            Album: "Starboy",
            Duration: TimeSpan.FromSeconds(230),
            ThumbnailData: spotifyNativeCover);

        var bytes = await artworkService.GetArtworkBytesAsync(spotifyTrack);
        bytes.Should().BeSameAs(spotifyNativeCover);
        networkCalls.Should().Be(0, "Spotify Desktop high-resolution GSMTC artwork must be used directly with 0 online requests");
    }

    [Fact]
    public void Regression7_BrowserExecutableResolution_ExtractsCommandAndResolvesRealBrowser()
    {
        string cmdWithQuotes = "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --single-argument %1";
        DefaultWebBrowserLauncher.ExtractExecutablePathFromCommand(cmdWithQuotes)
            .Should().Be(@"C:\Program Files\Google\Chrome\Application\chrome.exe");

        string edgeCmd = "\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" \"%1\"";
        DefaultWebBrowserLauncher.ExtractExecutablePathFromCommand(edgeCmd)
            .Should().Be(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe");

        if (OperatingSystem.IsWindows())
        {
            string? resolvedBrowser = DefaultWebBrowserLauncher.ResolveDefaultBrowserExecutablePath();
            resolvedBrowser.Should().NotBeNullOrWhiteSpace("Windows environment must have at least Edge or a default browser installed");
            File.Exists(resolvedBrowser).Should().BeTrue();
            DefaultWebBrowserLauncher.IsDisallowedBrowserTarget(resolvedBrowser).Should().BeFalse();
        }
    }

    [Fact]
    public void Regression8_AppleMusicWebBrowserLaunchArguments_UsesDirectExecutableWithUseShellExecuteFalse()
    {
        string fakeBrowserExe = @"C:\Program Files\Mozilla Firefox\firefox.exe";
        var psi = DefaultWebBrowserLauncher.BuildBrowserProcessStartInfo(fakeBrowserExe, DefaultWebBrowserLauncher.AppleMusicWebUrl);

        psi.Should().NotBeNull();
        psi.FileName.Should().Be(fakeBrowserExe);
        psi.UseShellExecute.Should().BeFalse("UseShellExecute must be false so Windows AppUriHandler never redirects music.apple.com to Apple Music Desktop");
        psi.ArgumentList.Should().ContainSingle().Which.Should().Be("https://music.apple.com/");
    }

    [Fact]
    public void Regression9_AppleMusicDesktopUrlInterceptionPrevention_BlocksAppleMusicAndShellHandlers()
    {
        // Disallow Apple Music Desktop, iTunes, LaunchWinApp, explorer, rundll32
        DefaultWebBrowserLauncher.IsDisallowedBrowserTarget(@"C:\Program Files\WindowsApps\AppleInc.AppleMusicWin_1.1284.20225.0_x64__nzyj5cx40ttqa\AppleMusic.exe")
            .Should().BeTrue();
        DefaultWebBrowserLauncher.IsDisallowedBrowserTarget("AppleMusic.exe").Should().BeTrue();
        DefaultWebBrowserLauncher.IsDisallowedBrowserTarget("iTunes.exe").Should().BeTrue();
        DefaultWebBrowserLauncher.IsDisallowedBrowserTarget(@"C:\Windows\explorer.exe").Should().BeTrue();
        DefaultWebBrowserLauncher.IsDisallowedBrowserTarget(@"C:\Windows\System32\LaunchWinApp.exe").Should().BeTrue();

        // Allow genuine web browsers
        DefaultWebBrowserLauncher.IsDisallowedBrowserTarget(@"C:\Program Files\Google\Chrome\Application\chrome.exe").Should().BeFalse();
        DefaultWebBrowserLauncher.IsDisallowedBrowserTarget(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe").Should().BeFalse();
        DefaultWebBrowserLauncher.IsDisallowedBrowserTarget(@"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe").Should().BeFalse();

        // BuildBrowserProcessStartInfo throws ArgumentException if passed a disallowed Apple Music target
        Action act = () => DefaultWebBrowserLauncher.BuildBrowserProcessStartInfo("AppleMusic.exe", "https://music.apple.com/");
        act.Should().Throw<ArgumentException>();

        // Verify neither LyricsWindow nor MainWindow opens AppleMusicWebUrl via UseShellExecute = true anymore
        string repoRoot = FindRepoRoot();
        string lyricsCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        string mainCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "MainWindow.xaml.cs"));

        lyricsCode.Should().Contain("DefaultWebBrowserLauncher.TryOpenUrlInDefaultBrowser(MediaSessionProfileHelper.AppleMusicWebUrl");
        mainCode.Should().Contain("DefaultWebBrowserLauncher.TryOpenUrlInDefaultBrowser(MediaSessionProfileHelper.AppleMusicWebUrl");
        lyricsCode.Should().NotContain("ProcessStartInfo(MediaSessionProfileHelper.AppleMusicWebUrl)");
        mainCode.Should().NotContain("ProcessStartInfo(MediaSessionProfileHelper.AppleMusicWebUrl)");
    }

    private sealed class MockArtworkHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockArtworkHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
