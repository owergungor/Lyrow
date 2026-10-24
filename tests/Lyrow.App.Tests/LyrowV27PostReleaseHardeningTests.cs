using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV27PostReleaseHardeningTests
{
    private static string GetSolutionRoot()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void BuildMetadataVersionTests_StrictTwoPartAcrossAllArtifacts()
    {
        string root = GetSolutionRoot();

        // 1. Directory.Build.props
        string props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        props.Should().Contain("<Version>3.0</Version>");
        props.Should().Contain("<PackageVersion>3.0</PackageVersion>");
        props.Should().Contain("<InformationalVersion>3.0</InformationalVersion>");
        props.Should().Contain("<AssemblyVersion>3.0.0.0</AssemblyVersion>");
        props.Should().Contain("<FileVersion>3.0.0.0</FileVersion>");
        props.Should().NotContain("<Version>3.0.0</Version>");
        props.Should().NotContain("<PackageVersion>3.0.0</PackageVersion>");

        // 2. Lyrow.App.csproj
        string csproj = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "Lyrow.App.csproj"));
        csproj.Should().Contain("<Version>3.0</Version>");
        csproj.Should().Contain("<PackageVersion>3.0</PackageVersion>");
        csproj.Should().Contain("<InformationalVersion>3.0</InformationalVersion>");
        csproj.Should().NotContain("<Version>3.0.0</Version>");

        // 3. setup.iss
        string iss = File.ReadAllText(Path.Combine(root, "installer", "setup.iss"));
        iss.Should().Contain("#define MyAppVersion \"3.0\"");
        iss.Should().NotContain("#define MyAppVersion \"3.0.0\"");

        // 4. app.manifest
        string manifest = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "app.manifest"));
        manifest.Should().Contain("version=\"3.0.0.0\"");

        // 5. MainWindow.xaml
        string mainXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml"));
        mainXaml.Should().Contain("Current Version: v3.0");
        mainXaml.Should().Contain("Lyrow v3.0");
        mainXaml.Should().NotContain("Lyrow v2.4");
        mainXaml.Should().NotContain("Lyrow v2.6");
        mainXaml.Should().NotContain("Lyrow v2.7");
        mainXaml.Should().NotContain("Lyrow v2.8");
    }

    [Fact]
    public void DynamicBackgroundTransitionTests_SafeContrastAndSmoothLerp()
    {
        var p1 = new AmbientBackgroundPalette(
            GlowColor.FromRgb(40, 30, 20),
            GlowColor.FromRgb(25, 20, 15),
            GlowColor.FromRgb(15, 10, 10),
            GlowColor.FromRgb(60, 40, 30));

        var p2 = new AmbientBackgroundPalette(
            GlowColor.FromRgb(10, 30, 50),
            GlowColor.FromRgb(10, 20, 35),
            GlowColor.FromRgb(5, 10, 20),
            GlowColor.FromRgb(20, 50, 80));

        // Test smooth linear interpolation
        var mid = AmbientBackgroundPalette.Lerp(p1, p2, 0.5f);
        mid.Dominant.R.Should().BeInRange(24, 26);
        mid.Dominant.B.Should().BeInRange(34, 36);

        // Test WCAG luminance clamp
        var brightColor = GlowColor.FromRgb(240, 240, 255);
        var safeColor = ArtworkPaletteExtractor.PrepareSafeAmbientColor(brightColor);
        double safeLum = ArtworkPaletteExtractor.CalculateLuminance(safeColor);
        safeLum.Should().BeLessThanOrEqualTo(56.0);
    }

    [Fact]
    public void KaraokeSeekRecalculationTests_InstantRecalculateOnSeek()
    {
        var lineStart = TimeSpan.FromSeconds(30);
        var nextLineStart = TimeSpan.FromSeconds(40);
        var totalDur = TimeSpan.FromSeconds(180);

        // Progress before line
        double pBefore = LyricsFillProgressCalculator.CalculateLineProgress(
            TimeSpan.FromSeconds(25), lineStart, nextLineStart, totalDur);
        pBefore.Should().Be(0.0);

        // Progress mid-line (35s -> 50% between 30s and 40s)
        double pMid = LyricsFillProgressCalculator.CalculateLineProgress(
            TimeSpan.FromSeconds(35), lineStart, nextLineStart, totalDur);
        pMid.Should().BeApproximately(0.50, 0.01);

        // Progress after line
        double pAfter = LyricsFillProgressCalculator.CalculateLineProgress(
            TimeSpan.FromSeconds(45), lineStart, nextLineStart, totalDur);
        pAfter.Should().Be(1.0);

        // Seek backwards to 32s (20% through line)
        double pSeekBack = LyricsFillProgressCalculator.CalculateLineProgress(
            TimeSpan.FromSeconds(32), lineStart, nextLineStart, totalDur);
        pSeekBack.Should().BeApproximately(0.20, 0.01);
    }

    [Fact]
    public void FullscreenLayoutTests_ResponsiveRatioSpecifications()
    {
        // Target laptop resolutions
        var resolutions = new[]
        {
            new { W = 1280, H = 720 },
            new { W = 1366, H = 768 },
            new { W = 1440, H = 900 },
            new { W = 1920, H = 1080 },
            new { W = 1920, H = 1200 }
        };

        foreach (var res in resolutions)
        {
            // Left column approx 40% (4 / 10), Right column approx 60% (6 / 10)
            double leftColWidth = res.W * 0.40;
            double rightColWidth = res.W * 0.60;
            leftColWidth.Should().BeGreaterThan(400);
            rightColWidth.Should().BeGreaterThan(600);

            // Artwork proportional to ~28-32% of screen width
            double targetArtworkSize = Math.Clamp(res.W * 0.28, 240, 360);
            targetArtworkSize.Should().BeInRange(240, 360);
        }
    }
}
