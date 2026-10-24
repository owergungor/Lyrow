using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV25FeatureVerificationTests
{
    private static string GetSolutionRoot()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void Versioning_IsStrictlyTwoPart_V25()
    {
        Assert.True(GitHubUpdateService.IsValidTwoPartVersionTag("v2.5"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("v2.5.0"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("2.5.0"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("v2.5.1"));
        Assert.True(GitHubUpdateService.IsNewerVersion("2.4", "2.5"));
        Assert.False(GitHubUpdateService.IsNewerVersion("2.5", "2.5"));
    }

    [Fact]
    public void LegacyNames_HaveZeroHitsInActiveSourceFiles()
    {
        string root = GetSolutionRoot();
        string[] searchDirs = { "src", "tests", "installer" };

        foreach (var dir in searchDirs)
        {
            var dirPath = Path.Combine(root, dir);
            if (!Directory.Exists(dirPath)) continue;

            var files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                {
                    continue;
                }

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".cs" or ".xaml" or ".iss" or ".props" or ".json" or ".manifest")) continue;

                string content = File.ReadAllText(file);
                string pattern1 = @"\b" + "l" + @"umn\b";
                string pattern2 = @"\b" + "v" + @"erci\b";
                Regex.IsMatch(content, pattern1, RegexOptions.IgnoreCase).Should().BeFalse($"File {file} should not contain legacy whole-word");
                Regex.IsMatch(content, pattern2, RegexOptions.IgnoreCase).Should().BeFalse($"File {file} should not contain legacy whole-word");
            }
        }
    }

    [Fact]
    public void Localization_AllFiveLanguages_ContainRequiredKeys()
    {
        string[] languages = { "en", "tr", "fr", "es", "de" };
        string[] testKeys = { "Nav_Home", "Nav_Playback", "Nav_Glow", "Nav_Lyrics", "Nav_Settings", "Settings_Title", "General_Language" };

        var loc = new LocalizationService();

        foreach (var lang in languages)
        {
            loc.SetLanguage(lang);

            foreach (var key in testKeys)
            {
                string text = loc.GetString(key);
                text.Should().NotBeNullOrWhiteSpace($"Language '{lang}' must have a translation for '{key}'");
                text.Should().NotBe(key, $"Language '{lang}' key '{key}' should not return the raw key name");
            }
        }
    }

    [Fact]
    public void LyricsMotion_StateTransitions_CoverVoxelVerseAppleMusicSpec()
    {
        Assert.Equal(LyricLineMotionState.Active, LyricsViewportCalculator.ResolveLineMotionState(3, 3));
        Assert.Equal(LyricLineMotionState.ApproachingActive, LyricsViewportCalculator.ResolveLineMotionState(4, 3));
        Assert.Equal(LyricLineMotionState.Idle, LyricsViewportCalculator.ResolveLineMotionState(5, 3));
        Assert.Equal(LyricLineMotionState.Passed, LyricsViewportCalculator.ResolveLineMotionState(2, 3));
    }

    [Fact]
    public void DynamicArtworkPalette_LuminanceCapping_EnsuresHighContrastLyrics()
    {
        var brightColor = GlowColor.FromRgb(255, 255, 0);
        var safeColor = ArtworkPaletteExtractor.PrepareSafeAmbientColor(brightColor);

        double luminance = ArtworkPaletteExtractor.CalculateLuminance(safeColor);
        luminance.Should().BeLessThanOrEqualTo(55.0);
    }
}
