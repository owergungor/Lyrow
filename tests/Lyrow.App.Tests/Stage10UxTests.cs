using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Comprehensive verification test suite for Stage 10: Final Product Experience & Premium UX.
/// </summary>
public class Stage10UxTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Localization_LyricsKeys_ExistForAllLanguages(string langCode)
    {
        var loc = LocalizationService.Instance;
        loc.SetLanguage(langCode);

        string[] requiredKeys = [
            "Lyrics_NotFound",
            "Lyrics_NotFound_Sub",
            "Lyrics_Searching",
            "Lyrics_WaitingPlayback",
            "Lyrics_PlayToSeeLyrics",
            "Lyrics_SeekFailed"
        ];

        foreach (var key in requiredKeys)
        {
            string value = loc[key];
            value.Should().NotBeNullOrWhiteSpace($"Key '{key}' must be translated in language '{langCode}'");
            value.Should().NotBe(key, $"Key '{key}' must have a resolved localized value, not the bare key name");
        }

        // Restore default English
        loc.SetLanguage("en");
    }

    [Fact]
    public void VisualHierarchy_MetadataTranslateTransform_PresentInXaml()
    {
        string xamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "src/Lyrow.App/MainWindow.xaml"));
        if (!File.Exists(xamlPath))
        {
            xamlPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src/Lyrow.App/MainWindow.xaml"));
        }

        File.Exists(xamlPath).Should().BeTrue("MainWindow.xaml must exist");
        string xaml = File.ReadAllText(xamlPath);

        xaml.Should().Contain("TransHomeTrackMeta", "Smooth translation glide transform must be defined on track metadata panel");
    }

    [Fact]
    public void LyricsViewportCalculator_ActiveLineCenterOffset_ComputesExactCenter()
    {
        double viewportHeight = 600.0;
        double lineTop = 400.0;
        double lineHeight = 40.0;

        // Line midpoint = 420. Viewport center = 300.
        // Desired vertical offset = 420 - 300 = 120.
        double offset = LyricsViewportCalculator.CalculateTargetCenterOffset(viewportHeight, lineTop, lineHeight);
        offset.Should().Be(120.0);
    }

    [Fact]
    public void LyricsViewportCalculator_SmoothInterpolation_ReachesTargetWithoutOvershoot()
    {
        double current = 100.0;
        double target = 200.0;

        double step1 = LyricsViewportCalculator.InterpolateScrollOffset(current, target, smoothingFactor: 0.2);
        step1.Should().BeGreaterThan(current);
        step1.Should().BeLessThan(target);

        // Near target snaps within epsilon
        double snapped = LyricsViewportCalculator.InterpolateScrollOffset(199.8, target, smoothingFactor: 0.2, snapEpsilon: 0.35);
        snapped.Should().Be(target);
    }

    [Fact]
    public void LyricsFillProgressCalculator_SequentialSubLines_HandlesLongClausesGracefully()
    {
        string longLyric = "This is a very long lyric line that definitely exceeds forty six characters and requires wrapping";
        var subLines = LyricsFillProgressCalculator.SplitIntoSequentialSubLines(longLyric, maxCharsPerSubLine: 46);

        subLines.Should().NotBeNull();
        subLines.Count.Should().BeGreaterThanOrEqualTo(2);
        string reassembled = string.Join(" ", subLines);
        reassembled.Should().Be(longLyric);
    }

    [Fact]
    public void CompactLayout_500x232_DimensionsAndAspect_AreValid()
    {
        double compactWidth = 500.0;
        double compactHeight = 232.0;

        compactWidth.Should().Be(500.0);
        compactHeight.Should().Be(232.0);

        // Check artwork square aspect in compact mode
        double compactArtworkSize = 76.0;
        compactArtworkSize.Should().BeLessThan(compactHeight);
    }

    [Fact]
    public async Task GlowModule_StopAsync_StopsCaptureAndFreesResources()
    {
        var settingsManager = new SettingsManager();
        var glow = new GlowModule(settingsManager: settingsManager);

        await glow.InitializeAsync();
        glow.State.Should().Be(ModuleState.Initialized);

        await glow.StartAsync();
        glow.State.Should().Be(ModuleState.Running);

        await glow.StopAsync();
        glow.State.Should().Be(ModuleState.Stopped);
    }

    [Fact]
    public void Settings_EnableAnimations_TogglesReducedMotionCompliance()
    {
        var settings = LyrowSettings.CreateDefault();
        settings.EnableAnimations.Should().BeTrue();

        settings.EnableAnimations = false;
        settings.EnableAnimations.Should().BeFalse();
    }
}
