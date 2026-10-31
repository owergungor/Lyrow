using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public class LyrowV34CoreVerificationTests
{
    [Fact]
    public void UpdateAutoRelaunchAfterInstallTests_VerifiesPackageAndBuildsRelaunchScript()
    {
        var service = new GitHubUpdateService();
        service.CurrentVersion.Should().Be("3.5");

        // 1. Non-existent or non-PE file must fail verification
        service.VerifyDownloadedPackage("").Should().BeFalse();
        service.VerifyDownloadedPackage(Path.Combine(Path.GetTempPath(), "nonexistent_lyrow_pkg.exe")).Should().BeFalse();

        string tempFakeFile = Path.Combine(Path.GetTempPath(), $"lyrow_test_invalid_{Guid.NewGuid():N}.exe");
        string tempValidPeFile = Path.Combine(Path.GetTempPath(), $"lyrow_test_valid_{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(tempFakeFile, new byte[128]); // Missing MZ header
            service.VerifyDownloadedPackage(tempFakeFile).Should().BeFalse();

            byte[] validPeBytes = new byte[256];
            validPeBytes[0] = 0x4D; // 'M'
            validPeBytes[1] = 0x5A; // 'Z'
            File.WriteAllBytes(tempValidPeFile, validPeBytes);
            service.VerifyDownloadedPackage(tempValidPeFile).Should().BeTrue();

            string script = GitHubUpdateService.BuildPostUpdateRelaunchScript(
                tempValidPeFile,
                @"C:\Program Files\Lyrow\Lyrow.App.exe",
                12345);

            script.Should().Contain("Wait-Process -Id 12345");
            script.Should().Contain(GitHubUpdateService.SilentInstallerArguments);
            script.Should().Contain("$p.ExitCode -eq 0");
            script.Should().Contain("Lyrow.App.exe");
        }
        finally
        {
            if (File.Exists(tempFakeFile)) File.Delete(tempFakeFile);
            if (File.Exists(tempValidPeFile)) File.Delete(tempValidPeFile);
        }
    }

    [Theory]
    [InlineData(960, 640, 1280, 720)]
    [InlineData(1280, 720, 1600, 900)]
    [InlineData(1600, 900, 1920, 1080)]
    public void ResponsiveWindowScalingTests_ScalesArtworkMetadataAndLyricsProportionally(
        double w1, double h1, double w2, double h2)
    {
        var m1 = LyricsViewportCalculator.CalculateResponsiveMetrics(w1, h1, isLyricsPanelVisible: true, 28.0, 20.0);
        var m2 = LyricsViewportCalculator.CalculateResponsiveMetrics(w2, h2, isLyricsPanelVisible: true, 28.0, 20.0);

        // 1. Artwork maintains 1:1 square aspect ratio and grows as window grows
        m2.ArtworkSize.Should().BeGreaterThanOrEqualTo(m1.ArtworkSize);

        // 2. Progress bar and volume bar widths strictly match artwork width
        m1.ProgressBarWidth.Should().Be(m1.ArtworkSize);
        m1.VolumeBarWidth.Should().Be(m1.ArtworkSize);
        m2.ProgressBarWidth.Should().Be(m2.ArtworkSize);
        m2.VolumeBarWidth.Should().Be(m2.ArtworkSize);

        // 3. Typography and controls scale monotonically
        m2.SongTitleFontSize.Should().BeGreaterThanOrEqualTo(m1.SongTitleFontSize);
        m2.ArtistAlbumFontSize.Should().BeGreaterThanOrEqualTo(m1.ArtistAlbumFontSize);
        m2.PlayPauseButtonSize.Should().BeGreaterThanOrEqualTo(m1.PlayPauseButtonSize);
        m2.FocusedLyricFontSize.Should().BeGreaterThanOrEqualTo(m1.FocusedLyricFontSize);
        m2.UnfocusedLyricFontSize.Should().BeGreaterThanOrEqualTo(m1.UnfocusedLyricFontSize);
    }

    [Fact]
    public void CompactModeControlsAndHeightTests_EnforcesThresholdsAndHysteresis()
    {
        // Compact to Normal hysteresis threshold must be >= 640px so manual widening transitions cleanly
        LyricsViewportCalculator.CompactToNormalWidthThreshold.Should().Be(640.0);
        LyricsViewportCalculator.NormalToCompactWidthThreshold.Should().BeLessThan(LyricsViewportCalculator.CompactToNormalWidthThreshold);
    }

    [Fact]
    public void MultiLineSequentialLyricsHighlightTests_HighlightsSubLinesSequentiallyOneByOne()
    {
        string multiLineText = "First line of the chorus\nSecond line of the chorus\nThird line of the chorus";
        var subLines = LyricsFillProgressCalculator.SplitIntoSequentialSubLines(multiLineText);
        subLines.Count.Should().Be(3);

        var start = TimeSpan.FromSeconds(10.0);
        var end = TimeSpan.FromSeconds(16.0);

        // At 10.5s (early in segment): subline 0 is partially white, sublines 1 and 2 MUST be 0.0 (not started)
        var pEarly = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(subLines, TimeSpan.FromSeconds(10.5), start, end);
        pEarly[0].Should().BeGreaterThan(0.0).And.BeLessThan(1.0);
        pEarly[1].Should().Be(0.0);
        pEarly[2].Should().Be(0.0);

        // At 13.0s (middle of segment): subline 0 is 1.0 (completely white), subline 1 is in progress, subline 2 is 0.0
        var pMid = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(subLines, TimeSpan.FromSeconds(13.0), start, end);
        pMid[0].Should().Be(1.0);
        pMid[1].Should().BeGreaterThan(0.0).And.BeLessThan(1.0);
        pMid[2].Should().Be(0.0);

        // At 15.2s (late in segment): sublines 0 and 1 are 1.0, subline 2 is in progress
        var pLate = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(subLines, TimeSpan.FromSeconds(15.2), start, end);
        pLate[0].Should().Be(1.0);
        pLate[1].Should().Be(1.0);
        pLate[2].Should().BeGreaterThan(0.0).And.BeLessThan(1.0);

        // At 16.0s (end of segment): all sublines are 1.0
        var pDone = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(subLines, TimeSpan.FromSeconds(16.0), start, end);
        pDone[0].Should().Be(1.0);
        pDone[1].Should().Be(1.0);
        pDone[2].Should().Be(1.0);
    }

    [Theory]
    [InlineData(0.0, 300.0, 200.0, 0.0)]
    [InlineData(75.0, 300.0, 200.0, 50.0)]
    [InlineData(150.0, 300.0, 200.0, 100.0)]
    [InlineData(300.0, 300.0, 200.0, 200.0)]
    [InlineData(-25.0, 300.0, 200.0, 0.0)]
    [InlineData(400.0, 300.0, 200.0, 200.0)]
    public void ProgressBarSeekAndLyricsSyncTests_CalculatesClampedSeekTimestamp(
        double clickX, double barWidth, double durationSec, double expectedSeekSec)
    {
        var duration = TimeSpan.FromSeconds(durationSec);
        var seekTime = LyricsFillProgressCalculator.CalculateSeekTimeFromPointer(clickX, barWidth, duration);
        seekTime.TotalSeconds.Should().BeApproximately(expectedSeekSec, 0.001);
    }
}
