using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV32FeatureVerificationTests
{
    #region 1. Lyrics Line Advancement & Duplicate Prevention Verification

    [Fact]
    public void LyricsDoesNotDuplicateWhenAdvancingToNextLine_VisualTargetsAreStrictlySeparated()
    {
        // Line 0 was active; now advancing to Line 1
        var targetLine0 = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 0, activeIndex: 1, focusedFontSize: 28, unfocusedFontSize: 20);
        var targetLine1 = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: 1, activeIndex: 1, focusedFontSize: 28, unfocusedFontSize: 20);

        // Line 0 is distance 1 (Passed / surrounding)
        targetLine0.IsActive.Should().BeFalse("Line 0 must not remain active when line 1 becomes active");
        targetLine0.State.Should().Be(LyricLineMotionState.Passed);
        targetLine0.FontSize.Should().Be(20);
        targetLine0.TargetOpacity.Should().Be(0.56);
        targetLine0.TargetScale.Should().Be(1.0);

        // Line 1 is Active
        targetLine1.IsActive.Should().BeTrue("Line 1 must be active");
        targetLine1.State.Should().Be(LyricLineMotionState.Active);
        targetLine1.FontSize.Should().Be(28);
        targetLine1.TargetOpacity.Should().Be(1.0);
        targetLine1.TargetScale.Should().Be(1.04);
    }

    [Fact]
    public void LyricsVisualIdentityRemainsUnique_OnlyActiveLineHasNonZeroTargetScaleAndActiveState()
    {
        for (int line = 0; line < 10; line++)
        {
            var target = LyricsViewportCalculator.GetLineVisualTarget(lineIndex: line, activeIndex: 4, focusedFontSize: 28, unfocusedFontSize: 20);
            if (line == 4)
            {
                target.IsActive.Should().BeTrue();
                target.State.Should().Be(LyricLineMotionState.Active);
                target.TargetOpacity.Should().Be(1.0);
                target.TargetScale.Should().Be(1.04);
            }
            else
            {
                target.IsActive.Should().BeFalse();
                target.TargetOpacity.Should().BeLessThan(1.0);
                target.TargetScale.Should().BeLessThanOrEqualTo(1.0);
            }
        }
    }

    [Fact]
    public void LyricsTransitionRemovesStaleVisual_PassedLinesTransitionSmoothly()
    {
        // When at line 2:
        // lines 0 and 1 are Passed
        var target0 = LyricsViewportCalculator.GetLineVisualTarget(0, 2);
        var target1 = LyricsViewportCalculator.GetLineVisualTarget(1, 2);
        var target2 = LyricsViewportCalculator.GetLineVisualTarget(2, 2);

        target0.State.Should().Be(LyricLineMotionState.Passed);
        target1.State.Should().Be(LyricLineMotionState.Passed);
        target2.State.Should().Be(LyricLineMotionState.Active);

        target0.TargetOpacity.Should().BeLessThan(target1.TargetOpacity);
        target1.TargetOpacity.Should().BeLessThan(target2.TargetOpacity);
    }

    #endregion

    #region 2. Artwork-Derived Lyrics Background & Smooth Crossfade Verification

    [Fact]
    public void ArtworkPaletteDrivesLyricsBackground_RedArtworkProducesRedTones()
    {
        // Vibrant red album art (e.g. Red, Blood Orange)
        var redPalette = new GlowPalette(
            top: GlowColor.FromRgb(255, 30, 45),      // accent
            bottom: GlowColor.FromRgb(210, 15, 30),   // dominant
            left: GlowColor.FromRgb(180, 20, 50),     // secondary
            right: GlowColor.FromRgb(150, 10, 40),    // tertiary
            accent: GlowColor.FromRgb(255, 30, 45));

        var ambient = ArtworkPaletteExtractor.ExtractAmbientPalette(redPalette);

        // Dominant must carry red channel prominence
        ambient.Dominant.R.Should().BeGreaterThan(ambient.Dominant.G);
        ambient.Dominant.R.Should().BeGreaterThan(ambient.Dominant.B);

        // Luminance must respect safe readability limit
        double lum = ArtworkPaletteExtractor.CalculateLuminance(ambient.Dominant);
        lum.Should().BeLessThanOrEqualTo(ArtworkPaletteExtractor.MaxSafeLuminance);
    }

    [Fact]
    public void ArtworkPaletteDrivesLyricsBackground_BlueArtworkProducesBlueTones()
    {
        // Vibrant blue album art (e.g. Blue, Cyan)
        var bluePalette = new GlowPalette(
            top: GlowColor.FromRgb(20, 120, 255),
            bottom: GlowColor.FromRgb(10, 60, 220),
            left: GlowColor.FromRgb(15, 80, 190),
            right: GlowColor.FromRgb(30, 40, 160),
            accent: GlowColor.FromRgb(20, 120, 255));

        var ambient = ArtworkPaletteExtractor.ExtractAmbientPalette(bluePalette);

        // Dominant must carry blue channel prominence
        ambient.Dominant.B.Should().BeGreaterThan(ambient.Dominant.R);
        ambient.Dominant.B.Should().BeGreaterThan(ambient.Dominant.G);

        double lum = ArtworkPaletteExtractor.CalculateLuminance(ambient.Dominant);
        lum.Should().BeLessThanOrEqualTo(ArtworkPaletteExtractor.MaxSafeLuminance);
    }

    [Fact]
    public void ArtworkChangeCrossfadesBackground_LerpProducesGradualTransitionWithoutSingleFrameJump()
    {
        var paletteA = new AmbientBackgroundPalette(
            GlowColor.FromRgb(200, 20, 20),
            GlowColor.FromRgb(160, 15, 15),
            GlowColor.FromRgb(120, 10, 10),
            GlowColor.FromRgb(240, 30, 30));

        var paletteB = new AmbientBackgroundPalette(
            GlowColor.FromRgb(20, 20, 200),
            GlowColor.FromRgb(15, 15, 160),
            GlowColor.FromRgb(10, 10, 120),
            GlowColor.FromRgb(30, 30, 240));

        // Single step of 0.065f factor
        var step1 = AmbientBackgroundPalette.Lerp(paletteA, paletteB, 0.065f);

        // Step 1 must be close to A, not jump directly to B
        step1.Dominant.R.Should().BeInRange(180, 195);
        step1.Dominant.B.Should().BeInRange(25, 40);

        // After 20 steps, smooth progressive interpolation
        var current = paletteA;
        for (int i = 0; i < 20; i++)
        {
            current = AmbientBackgroundPalette.Lerp(current, paletteB, 0.065f);
        }

        current.Dominant.R.Should().BeLessThan(step1.Dominant.R);
        current.Dominant.B.Should().BeGreaterThan(step1.Dominant.B);
    }

    [Fact]
    public void TrackChangeClearsLyricsVisualState_SyncedLyricsEmptyResetsActiveLineToMinusOne()
    {
        var synchronizer = new LyricsTimelineSynchronizer();
        var lyrics = new SyncedLyrics("Track A", "Artist A", true, new List<LyricLine>
        {
            new(TimeSpan.FromSeconds(10), "Line 1", 0),
            new(TimeSpan.FromSeconds(20), "Line 2", 1)
        });

        synchronizer.SetLyrics(lyrics);
        synchronizer.UpdateTimeline(new MediaTimelineInfo(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(180), MediaPlaybackStatus.Playing, DateTime.UtcNow));
        synchronizer.CurrentActiveIndex.Should().Be(0);

        // On track switch: wipe lyrics
        synchronizer.SetLyrics(SyncedLyrics.Empty);
        synchronizer.CurrentActiveIndex.Should().Be(-1);
    }

    #endregion

    #region 3. Versioning & Legacy Name Audits

    [Fact]
    public void TwoPartVersioning_v32_Enforced()
    {
        GitHubUpdateService.AppVersion.Should().Be("3.4");
        GitHubUpdateService.IsValidTwoPartVersionTag("v3.4").Should().BeTrue();
        GitHubUpdateService.IsValidTwoPartVersionTag("v3.4.0").Should().BeFalse();
        GitHubUpdateService.IsValidTwoPartVersionTag("3.4.0").Should().BeFalse();
        GitHubUpdateService.IsValidTwoPartVersionTag("v3.4.1").Should().BeFalse();
        GitHubUpdateService.IsNewerVersion("3.3", "3.4").Should().BeTrue();
        GitHubUpdateService.IsNewerVersion("3.4", "3.4").Should().BeFalse();

        var repoRoot = FindRepoRoot();
        File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props")).Should().Contain("<Version>3.4</Version>");
        File.ReadAllText(Path.Combine(repoRoot, "installer", "setup.iss")).Should().Contain("#define MyAppVersion \"3.4\"");
        File.ReadAllText(Path.Combine(repoRoot, "src", "Lyrow.App", "app.manifest")).Should().Contain("version=\"3.4.0.0\"");
    }

    [Fact]
    public void LegacyProjectNames_CompletelyAbsent()
    {
        var repoRoot = FindRepoRoot();
        var candidateFiles = Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories);

        var forbiddenPattern = new Regex(@"\b(" + "l" + "umn|" + "v" + @"erci)\b", RegexOptions.IgnoreCase);

        foreach (var file in candidateFiles)
        {
            if (file.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "dist" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "publish" + Path.DirectorySeparatorChar) ||
                file.EndsWith(".exe") || file.EndsWith(".zip") || file.EndsWith(".dll") || file.EndsWith(".pdb") ||
                file.EndsWith(".ico") || file.EndsWith(".png") ||
                file.EndsWith("patch_lyrics_window.py") || file.EndsWith("patch_version_v32.py") || file.EndsWith("patch_tests_v32.py") ||
                file.EndsWith("LyrowV32FeatureVerificationTests.cs") ||
                file.EndsWith("LyrowV31FeatureVerificationTests.cs") ||
                file.EndsWith("LyrowV27FeatureVerificationTests.cs") ||
                file.EndsWith("LyrowV27PostReleaseHardeningTests.cs") ||
                file.EndsWith("LyrowV25FeatureVerificationTests.cs"))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            forbiddenPattern.IsMatch(content).Should().BeFalse(
                $"File '{file}' must not contain legacy project names.");
        }
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

    #endregion
}
