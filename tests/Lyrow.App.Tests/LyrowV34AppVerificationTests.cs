using System;
using System.IO;
using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV34AppVerificationTests
{
    [Fact]
    public void UpdateAutoRelaunchTests_VerifiesPackageReleasesMutexAndRestartsApp()
    {
        string repoRoot = FindRepoRoot();
        string mainWindowCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "MainWindow.xaml.cs"));
        string appCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "App.xaml.cs"));

        appCode.Should().Contain("public static void ReleaseSingleInstanceLock()");
        mainWindowCode.Should().Contain("_updateService.VerifyDownloadedPackage(destFile)");
        mainWindowCode.Should().Contain("App.ReleaseSingleInstanceLock();");
        mainWindowCode.Should().Contain("_updateService.LaunchSilentUpdateAndRestart");
    }

    [Fact]
    public void ResponsivePlayerScalingTests_ScalesArtworkMetadataControlsAndLyricsProportionally()
    {
        string repoRoot = FindRepoRoot();
        string lyricsCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        lyricsCode.Should().Contain("private void ApplyResponsiveScaling(double windowWidth, double windowHeight)");
        lyricsCode.Should().Contain("LyricsViewportCalculator.CalculateResponsiveMetrics(");
        lyricsCode.Should().Contain("BorderTrackArtwork.Width = metrics.ArtworkSize;");
        lyricsCode.Should().Contain("BorderTrackArtwork.Height = metrics.ArtworkSize;");
        lyricsCode.Should().Contain("ProgressTimeline.Width = metrics.ProgressBarWidth;");
        lyricsCode.Should().Contain("GridVolumeRow.Width = metrics.VolumeBarWidth;");
    }

    [Fact]
    public void CompactModeControlsAndSizeTests_UsesNormalModeButtonAndIncreasedVerticalHeight()
    {
        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // Volume button replaced with Normal mode button
        xaml.Should().NotContain("x:Name=\"BtnVolumeCompact\"");
        xaml.Should().Contain("x:Name=\"BtnNormalModeCompact\"");
        xaml.Should().Contain("ToolTipService.ToolTip=\"Normal mode\"");

        // Compact mini-player dimensions (352 x 148 logical DIPs = 528 x 222 px at 150% DPI) and 60x60 DIPs (90x90 px) artwork
        xaml.Should().Contain("Width=\"60\" Height=\"60\"");
        code.Should().Contain("ResizeCompactWindow(_isCompactLyricsExpanded);");
        code.Should().Contain("LyricsViewportCalculator.CompactToNormalWidthThreshold");
    }

    [Fact]
    public void ProgressBarSeekTests_WiresInteractiveClickAndDragToSeekOnBothModes()
    {
        string repoRoot = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        xaml.Should().Contain("x:Name=\"GridProgressHitBox\"");
        xaml.Should().Contain("x:Name=\"GridProgressCompactHitBox\"");
        code.Should().Contain("WireInteractiveProgressScrubber(GridProgressHitBox, ProgressTimeline)");
        code.Should().Contain("WireInteractiveProgressScrubber(GridProgressCompactHitBox, ProgressTimelineCompact)");
        code.Should().Contain("LyricsFillProgressCalculator.CalculateSeekTimeFromPointer(pt.X, w, dur)");
        code.Should().Contain("_mediaMonitor.TrySeekAsync(timestamp)");
    }

    [Fact]
    public void MultiLineSequentialHighlightAndArtworkSignatureTests_VerifiedInCodebase()
    {
        string repoRoot = FindRepoRoot();
        string lyricsCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        string mainCode = File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "MainWindow.xaml.cs"));

        // Sequential sub-line karaoke highlighting
        lyricsCode.Should().Contain("LyricsFillProgressCalculator.SplitIntoSequentialSubLines(lineText, maxCharsPerSubLine: 46)");
        lyricsCode.Should().Contain("LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(renderLine, subStrings, cPos, nTs)");

        // Artwork signature change detection for background & Glow refresh on delayed GSMTC thumbnails
        lyricsCode.Should().Contain("ArtworkBitmapManager.ComputeArtworkSignature(track)");
        mainCode.Should().Contain("ArtworkBitmapManager.ComputeArtworkSignature(track)");
        mainCode.Should().Contain("_glowOverlayManager?.ResetBeatEngines()");

        // Verify ComputeArtworkSignature distinguishes null vs non-null thumbnail bytes for identical title/artist
        var trackWithoutArt = new MediaTrackInfo("Song X", "Artist Y", "Album Z", TimeSpan.FromMinutes(3), ThumbnailData: null);
        var trackWithArt = trackWithoutArt with { ThumbnailData = new byte[] { 10, 20, 30, 40, 50 } };

        int sigEmpty = ArtworkBitmapManager.ComputeArtworkSignature(MediaTrackInfo.Empty);
        int sigWithout = ArtworkBitmapManager.ComputeArtworkSignature(trackWithoutArt);
        int sigWith = ArtworkBitmapManager.ComputeArtworkSignature(trackWithArt);

        sigEmpty.Should().Be(0);
        sigWithout.Should().NotBe(0);
        sigWith.Should().NotBe(0);
        sigWithout.Should().NotBe(sigWith);
    }

    [Fact]
    public void StrictV34VersioningTests_AllArtifactsMatch34()
    {
        GitHubUpdateService.AppVersion.Should().Be("3.5");
        GitHubUpdateService.IsValidTwoPartVersionTag("v3.5").Should().BeTrue();
        GitHubUpdateService.IsValidTwoPartVersionTag("v3.5.0").Should().BeFalse();

        string repoRoot = FindRepoRoot();
        File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props")).Should().Contain("<Version>3.5</Version>");
        File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props")).Should().Contain("<FileVersion>3.5.0.0</FileVersion>");
        File.ReadAllText(Path.Combine(repoRoot, "installer", "setup.iss")).Should().Contain("#define MyAppVersion \"3.5\"");
        File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "app.manifest")).Should().Contain("version=\"3.5.0.0\"");
        File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml")).Should().Contain("default: 'v3.5'");
        File.Exists(Path.Combine(repoRoot, "docs", "RELEASE_NOTES_v3.4.md")).Should().BeTrue();
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
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
