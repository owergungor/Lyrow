using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Stage 28 Regression Test Suite:
/// 1. Compact window default width/height test
/// 2. Compact minimum width/height test
/// 3. Compact mode transition AppWindow size change test
/// 4. Lyrics expanded compact layout test
/// 5. Web artwork null thumbnail fallback test
/// 6. Web artwork download/decode &amp; UI thread safety test
/// 7. Artwork cache invalidation regression test
/// 8. Spotify artwork regression test
/// 9. Browser redirect regression test
/// </summary>
public sealed class LyrowStage28AcceptanceRegressionTests
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
    public void Stage28_Test1_CompactWindowDefaultWidthAndHeight_MatchesTargetMiniPlayerSize()
    {
        // Logical DIPs: 352 x 148 -> at 150% DPI (scale = 1.5): 528 x 222 physical pixels (within 528 x 214-230 px target)
        var (logicalW, logicalH, physW150, physH150) = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: false, dpiScale: 1.5);
        logicalW.Should().Be(352);
        logicalH.Should().Be(148);
        physW150.Should().Be(528);
        physH150.Should().BeInRange(214, 230);

        // Artwork logical size: 60 x 60 DIPs -> at 150% DPI: 90 x 90 physical pixels
        LyricsViewportCalculator.CompactArtworkLogicalSize.Should().Be(60);
        ((int)(LyricsViewportCalculator.CompactArtworkLogicalSize * 1.5)).Should().Be(90);

        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        xaml.Should().Contain("x:Name=\"BorderTrackArtworkCompact\" Width=\"60\" Height=\"60\"");
    }

    [Fact]
    public void Stage28_Test2_CompactMinimumWidthAndHeight_NeverForcesOversizedWindow()
    {
        var (minLogicalW, minLogicalH, minPhysW150, minPhysH150) = LyricsViewportCalculator.CalculateCompactMinimumWindowSize(dpiScale: 1.5);
        minLogicalW.Should().Be(320);
        minLogicalH.Should().Be(136);
        minPhysW150.Should().BeLessThanOrEqualTo(528);
        minPhysH150.Should().BeLessThanOrEqualTo(214);

        WindowStateManager.MinimumSize.Width.Should().Be(320);
        WindowStateManager.MinimumSize.Height.Should().Be(136);
        WindowStateManager.DefaultCompactSize.Width.Should().Be(528);
        WindowStateManager.DefaultCompactSize.Height.Should().Be(222);
    }

    [Fact]
    public void Stage28_Test3_CompactModeTransition_ResizesAppWindowOnBothButtonAndAutoTransition()
    {
        string repoRoot = FindRepoRoot();
        string code = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // 1. Both button transition and auto-transition resize AppWindow to compact target dimensions
        code.Should().Contain("SetWindowMode(LyricsWindowMode.Compact, resizeWindow: true);");
        code.Should().Contain("ResizeCompactWindow(_isCompactLyricsExpanded);");

        // 2. MainViewportArea spans row 0 & 1 in Compact mode so TitleBar does not add dead vertical height
        code.Should().Contain("Grid.SetRow(MainViewportArea, 0);");
        code.Should().Contain("Grid.SetRowSpan(MainViewportArea, 2);");
        code.Should().Contain("overlapped.PreferredMinimumWidth = minPhysW;");
        code.Should().Contain("overlapped.PreferredMinimumHeight = minPhysH;");
    }

    [Fact]
    public void Stage28_Test4_LyricsExpandedCompactLayout_FitsThreeLinesCleanlyInCompactHeight()
    {
        var (logicalW, logicalH, physW150, physH150) = LyricsViewportCalculator.CalculateCompactWindowSize(isLyricsExpanded: true, dpiScale: 1.5);
        logicalW.Should().Be(352);
        logicalH.Should().Be(212);
        physW150.Should().Be(528);
        physH150.Should().Be(318);

        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        xaml.Should().Contain("x:Name=\"GridCompactLyrics\" Grid.Row=\"1\" Visibility=\"Collapsed\" Height=\"60\"");
    }

    [Fact]
    public async Task Stage28_Test5_WebArtworkNullThumbnailFallback_ResolvesCompoundYouTubeAndAppleWebTitles()
    {
        // Compound YouTube Music title from real user log:
        // "Waiting For the End - Unshatter Film Soundtrack (Live in São Paulo)" -> core title "Waiting For the End"
        string compoundTitle = "Waiting For the End - Unshatter Film Soundtrack (Live in São Paulo)";
        string coreTitle = ArtworkService.ExtractCoreSongTitle(compoundTitle);
        coreTitle.Should().Be("Waiting For the End");

        byte[] highResCover = CreateValidPngBytes(600, 600, 30_000);
        using var handler = new MockHttpMessageHandler(req =>
        {
            string uri = req.RequestUri!.AbsoluteUri;
            if (uri.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
            {
                // Simulate 0 results on full compound title, 1 match on fallback core title query
                if (uri.Contains("Unshatter", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"resultCount\":0,\"results\":[]}", Encoding.UTF8, "application/json")
                    };
                }

                string json = """
                {
                  "resultCount": 1,
                  "results": [
                    {
                      "artistName": "LINKIN PARK",
                      "trackName": "Waiting For the End",
                      "collectionName": "A Thousand Suns",
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
                Content = new ByteArrayContent(highResCover)
            };
        });

        using var httpClient = new HttpClient(handler);
        var service = new ArtworkService(httpClient);
        var ytTrack = new MediaTrackInfo(
            Title: compoundTitle,
            Artist: "LINKIN PARK",
            Album: string.Empty,
            Duration: TimeSpan.FromSeconds(240),
            ThumbnailData: null);

        var bytes = await service.GetArtworkBytesAsync(ytTrack);
        bytes.Should().NotBeNull();
        bytes!.Length.Should().Be(highResCover.Length);
    }

    [Fact]
    public void Stage28_Test6_WebArtworkDecodeAndUiThreadAffinity_NeverUsesConfigureAwaitFalseBeforeBitmapImage()
    {
        // Verify GetArtworkBitmapAsync never uses ConfigureAwait(false), which previously caused
        // new BitmapImage() to execute on a ThreadPool thread and throw 0x8001010E (RPC_E_WRONG_THREAD)
        // after awaiting online HTTP artwork for YouTube Music Web and Apple Music Web.
        string repoRoot = FindRepoRoot();
        string code = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "Platform", "ArtworkBitmapManager.cs"));

        int methodStart = code.IndexOf("public async Task<BitmapImage?> GetArtworkBitmapAsync", StringComparison.Ordinal);
        int methodEnd = code.IndexOf("public static async Task<byte[]?> TranscodeToPngBytesAsync", StringComparison.Ordinal);
        methodStart.Should().BeGreaterThan(0);
        methodEnd.Should().BeGreaterThan(methodStart);

        string getBitmapBody = code[methodStart..methodEnd];
        getBitmapBody.Should().NotContain("ConfigureAwait(false)",
            "GetArtworkBitmapAsync must resume on the WinUI 3 UI Dispatcher thread so BitmapImage construction and SetSourceAsync never fail with RPC_E_WRONG_THREAD");
        getBitmapBody.Should().Contain("writer.DetachStream()");

        // Verify WebP header detection
        byte[] webpBytes = CreateValidWebpVp8xBytes(120, 120, 4096);
        ArtworkService.IsWebpImageData(webpBytes).Should().BeTrue();
        byte[] pngBytes = CreateValidPngBytes(600, 600, 24_000);
        ArtworkService.IsWebpImageData(pngBytes).Should().BeFalse();
    }

    [Fact]
    public async Task Stage28_Test7_ArtworkCacheInvalidationRegression_DoesNotEvictHighResCacheOnLowResOrWebpThumbnail()
    {
        byte[] onlineHighResPng = CreateValidPngBytes(600, 600, 32_000);
        byte[] delayedBrowserWebp = CreateValidWebpVp8xBytes(96, 96, 3_500);

        using var handler = new MockHttpMessageHandler(req =>
        {
            if (req.RequestUri!.Host.Contains("itunes.apple.com", StringComparison.OrdinalIgnoreCase))
            {
                string json = """
                {
                  "resultCount": 1,
                  "results": [
                    {
                      "artistName": "Justin Timberlake",
                      "trackName": "Señorita",
                      "collectionName": "Justified",
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
                Content = new ByteArrayContent(onlineHighResPng)
            };
        });

        using var httpClient = new HttpClient(handler);
        var artworkService = new ArtworkService(httpClient);
        var bitmapManager = new ArtworkBitmapManager(artworkService);

        // Initial web track with null GSMTC thumbnail fetches & caches 600x600 PNG
        var initialTrack = new MediaTrackInfo("Señorita", "Justin Timberlake", "Justified", TimeSpan.FromSeconds(294), ThumbnailData: null);
        var firstBytes = await artworkService.GetArtworkBytesAsync(initialTrack);
        firstBytes.Should().NotBeNull();
        firstBytes!.Length.Should().Be(onlineHighResPng.Length);

        // Delayed GSMTC probe arrives with 96x96 WebP thumbnail -> high-res cached PNG must be retained
        var delayedTrack = initialTrack with { ThumbnailData = delayedBrowserWebp };
        var secondBytes = await artworkService.GetArtworkBytesAsync(delayedTrack);
        secondBytes.Should().NotBeNull();
        secondBytes!.Length.Should().Be(onlineHighResPng.Length);

        string repoRoot = FindRepoRoot();
        string lyricsCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        lyricsCode.Should().Contain("!ArtworkService.IsLowResolutionThumbnail(newTrack.ThumbnailData)");
        lyricsCode.Should().Contain("!ArtworkService.IsWebpImageData(newTrack.ThumbnailData)");
    }

    [Fact]
    public async Task Stage28_Test8_SpotifyArtworkRegression_PreservesDirectHighResGsmtcCover()
    {
        int httpCalls = 0;
        using var handler = new MockHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref httpCalls);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var httpClient = new HttpClient(handler);
        var artworkService = new ArtworkService(httpClient);

        byte[] spotifyCover = CreateValidPngBytes(640, 640, 48_000);
        var spotifyTrack = new MediaTrackInfo("I Love Rock 'N' Roll", "Britney Spears", "Britney", TimeSpan.FromSeconds(206), ThumbnailData: spotifyCover);

        var resolved = await artworkService.GetArtworkBytesAsync(spotifyTrack);
        resolved.Should().BeSameAs(spotifyCover);
        httpCalls.Should().Be(0);
    }

    [Fact]
    public void Stage28_Test9_BrowserRedirectRegression_LaunchesDefaultBrowserDirectlyWithUseShellExecuteFalse()
    {
        var psi = DefaultWebBrowserLauncher.BuildBrowserProcessStartInfo(
            @"C:\Users\omer\AppData\Local\Programs\Opera\opera.exe",
            DefaultWebBrowserLauncher.AppleMusicWebUrl);

        psi.UseShellExecute.Should().BeFalse();
        psi.FileName.Should().EndWith("opera.exe");
        psi.ArgumentList.Should().ContainSingle().Which.Should().Be("https://music.apple.com/");
        DefaultWebBrowserLauncher.IsDisallowedBrowserTarget("AppleMusic.exe").Should().BeTrue();
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
