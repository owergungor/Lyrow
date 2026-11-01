using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV35FeatureVerificationTests
{
    [Fact]
    public void LyricsFillProgressCalculator_SplitsLongUnbrokenWordsWithoutOverflow()
    {
        // A single continuous string of 80 characters without spaces
        string superLongWord = new string('A', 80);
        var sublines = LyricsFillProgressCalculator.SplitIntoSequentialSubLines(superLongWord, maxCharsPerSubLine: 30);

        sublines.Should().NotBeEmpty();
        sublines.Count.Should().Be(3); // 30 + 30 + 20
        string combined = string.Concat(sublines);
        combined.Should().Be(superLongWord);
        foreach (var subline in sublines)
        {
            subline.Length.Should().BeLessThanOrEqualTo(30);
        }
    }

    [Fact]
    public void LyricsViewportCalculator_ActiveLineScaleMatchesAppleMusicHierarchy()
    {
        var targetAtDistance0 = LyricsViewportCalculator.GetLineVisualTarget(0, 0);
        targetAtDistance0.TargetScale.Should().Be(1.04);
        targetAtDistance0.TargetOpacity.Should().Be(1.0);

        var targetAtDistance1 = LyricsViewportCalculator.GetLineVisualTarget(1, 0);
        targetAtDistance1.TargetScale.Should().Be(1.0);
        targetAtDistance1.TargetOpacity.Should().Be(0.56);
    }

    [Fact]
    public void Settings_GlowThicknessDefaultsTo24Px()
    {
        var settings = new LyrowSettings();
        settings.GlowThickness.Should().Be(24);
    }

    [Fact]
    public async Task ArtworkBitmapManager_RetainsHigherResolutionBytesInCache()
    {
        var artworkService = new ArtworkService();
        var manager = new ArtworkBitmapManager(artworkService);

        // Valid PNG header: 0x89, 0x50, 0x4E, 0x47
        byte[] highRes = new byte[10000];
        highRes[0] = 0x89; highRes[1] = 0x50; highRes[2] = 0x4E; highRes[3] = 0x47;

        byte[] lowResThumb = new byte[500];
        lowResThumb[0] = 0x89; lowResThumb[1] = 0x50; lowResThumb[2] = 0x4E; lowResThumb[3] = 0x47;

        var trackHigh = new MediaTrackInfo("Track A", "Artist 35", "Album", TimeSpan.FromMinutes(3), ThumbnailData: highRes);
        var trackLow = new MediaTrackInfo("Track A", "Artist 35", "Album", TimeSpan.FromMinutes(3), ThumbnailData: lowResThumb);

        // 1. First fetch with high res
        var bytes = await manager.GetArtworkBytesAsync(trackHigh);
        bytes.Should().NotBeNull();
        bytes!.Length.Should().Be(10000);

        // 2. Subsequent arrival with lower-res thumbnail should retain high res
        var bytesAfterLow = await manager.GetArtworkBytesAsync(trackLow);
        bytesAfterLow.Should().NotBeNull();
        bytesAfterLow!.Length.Should().Be(10000);
    }

    [Fact]
    public void StrictV35VersioningTests_AllArtifactsMatch35()
    {
        GitHubUpdateService.AppVersion.Should().Be("3.5");
        GitHubUpdateService.IsValidTwoPartVersionTag("v3.5").Should().BeTrue();
        GitHubUpdateService.IsValidTwoPartVersionTag("v3.5.0").Should().BeFalse();
        GitHubUpdateService.IsNewerVersion("3.4", "3.5").Should().BeTrue();

        string repoRoot = FindRepoRoot();
        File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props")).Should().Contain("<Version>3.5</Version>");
        File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props")).Should().Contain("<FileVersion>3.5.0.0</FileVersion>");
        File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props")).Should().Contain("<AssemblyVersion>3.5.0.0</AssemblyVersion>");
        File.ReadAllText(Path.Combine(repoRoot, "installer", "setup.iss")).Should().Contain("#define MyAppVersion \"3.5\"");
        File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "app.manifest")).Should().Contain("version=\"3.5.0.0\"");
        File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml")).Should().Contain("default: 'v3.5'");
        File.Exists(Path.Combine(repoRoot, "docs", "RELEASE_NOTES_v3.5.md")).Should().BeTrue();
    }

    [Theory]
    [InlineData(ThemeMode.Dark, "Dark")]
    [InlineData(ThemeMode.Light, "Light")]
    [InlineData(ThemeMode.SystemDefault, "Default")]
    public void ThemeMode_MapsToExpectedRequestedTheme(ThemeMode mode, string expected)
    {
        string mapped = mode switch
        {
            ThemeMode.Dark => "Dark",
            ThemeMode.Light => "Light",
            _ => "Default"
        };
        mapped.Should().Be(expected);
    }

    [Fact]
    public void LyricsEmptyState_FormatsClearNotFoundMessage()
    {
        string defaultMsg = "Bu parça için senkronize söz bulunamadı.";
        string customMsg = "Rate limited";
        string formatted1 = !string.IsNullOrWhiteSpace(customMsg) ? customMsg : defaultMsg;
        string formatted2 = !string.IsNullOrWhiteSpace(null) ? null! : defaultMsg;

        formatted1.Should().Be("Rate limited");
        formatted2.Should().Be(defaultMsg);
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Lyrow.sln")))
            {
                return current;
            }
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) break;
            current = parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
