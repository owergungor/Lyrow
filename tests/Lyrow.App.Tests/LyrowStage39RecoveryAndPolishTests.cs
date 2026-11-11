using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lyrow.App;
using Lyrow.App.Platform;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Timeline;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Unit and regression tests for Stage 39:
/// 1. Normal mode layout: Hide Lyrics and bottom controls accessible on initial open without clipping.
/// 2. Normal mode layout overflow safety across various screen heights and DPI scales.
/// 3. Wrapped lyrics word highlight timing across sub-lines.
/// 4. Wrapped words on sub-line 1 are strictly passive when the first word on sub-line 0 is active.
/// 5. System tab is strictly absent from visible settings tabs.
/// 6. Updates tab is present and accessible in visible settings tabs.
/// 7. Shortcuts tab is present and accessible in visible settings tabs.
/// 8. Turkish/English localization key parity.
/// 9. Settings screen localization coverage.
/// 10. Compact-to-normal mode transition button has diagonal expand arrow icon (\uE740).
/// 11. Stage 38 remaining time regression check.
/// 12. Stage 38 single active line regression check.
/// </summary>
public sealed class LyrowStage39RecoveryAndPolishTests
{
    [Theory]
    [InlineData(960, 720, true)]
    [InlineData(1000, 750, true)]
    [InlineData(1280, 800, true)]
    [InlineData(960, 640, true)]
    [InlineData(640, 520, true)] // Minimum allowable dimensions
    public void NormalMode_InitialOpen_HideLyricsAndControlsAreReachableWithoutClipping(
        double windowWidth, double windowHeight, bool isLyricsVisible)
    {
        var metrics = LyricsViewportCalculator.CalculateResponsiveMetrics(windowWidth, windowHeight, isLyricsVisible);

        double availableH = windowHeight - 44.0; // Exclude title bar
        double remainingForControls = availableH - metrics.ArtworkSize;

        // Metadata + Seekbar + Time + Device + Transport + Volume + Offset/Toggle row require ~280-330px
        remainingForControls.Should().BeGreaterThanOrEqualTo(320.0,
            "Artwork sizing must leave sufficient vertical room so all controls down to Hide Lyrics are visible without clipping");

        metrics.ArtworkSize.Should().BeInRange(120.0, 560.0);
        metrics.PlayerColumnWidth.Should().Be(metrics.ArtworkSize);
        metrics.ProgressBarWidth.Should().Be(metrics.ArtworkSize);
        metrics.VolumeBarWidth.Should().Be(metrics.ArtworkSize);
    }

    [Theory]
    [InlineData(1.0, 960, 720)]
    [InlineData(1.25, 1200, 900)]
    [InlineData(1.5, 1440, 1080)]
    [InlineData(1.0, 640, 520)]
    public void NormalMode_Layout_DoesNotOverflowAcrossDpiScalesAndResolutions(
        double dpiScale, double physW, double physH)
    {
        double logicalW = physW / dpiScale;
        double logicalH = physH / dpiScale;

        var metrics = LyricsViewportCalculator.CalculateResponsiveMetrics(logicalW, logicalH, isLyricsPanelVisible: true);

        double totalPlayerHeight = metrics.ArtworkSize + 280.0; // Artwork + controls
        double clientAreaH = logicalH - 44.0;

        totalPlayerHeight.Should().BeLessThanOrEqualTo(clientAreaH + 10.0,
            $"Player panel at DPI scale {dpiScale} must fit within the client area to prevent bottom clipping");
    }

    [Fact]
    public void WrappedLyric_WordHighlight_ProgressesSequentiallyAcrossSubLines()
    {
        // Line with 5 words wrapping across 2 sub-lines
        var words = new List<LyricWordTiming>
        {
            new("I", TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(2.0)),
            new("want", TimeSpan.FromSeconds(2.0), TimeSpan.FromSeconds(3.0)),
            new("to", TimeSpan.FromSeconds(3.0), TimeSpan.FromSeconds(4.0)),
            new("break", TimeSpan.FromSeconds(4.0), TimeSpan.FromSeconds(5.0)),
            new("free", TimeSpan.FromSeconds(5.0), TimeSpan.FromSeconds(6.0))
        };

        var line = new LyricLine(
            Timestamp: TimeSpan.FromSeconds(1.0),
            Text: "I want to break free",
            LineIndex: 0,
            Words: words,
            EndTime: TimeSpan.FromSeconds(6.0));

        var subLines = new[] { "I want to", "break free" };

        // 1. At 1.5s (during word "I" on subline 0)
        var pAt15 = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, currentPosition: TimeSpan.FromSeconds(1.5));

        pAt15.Count.Should().Be(2);
        pAt15[0].Should().BeGreaterThan(0.0).And.BeLessThan(1.0);
        pAt15[1].Should().Be(0.0, "Subline 1 must be strictly 0.0 while word on subline 0 is active");

        // 2. At 3.5s (during word "to" on subline 0)
        var pAt35 = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, currentPosition: TimeSpan.FromSeconds(3.5));

        pAt35[0].Should().BeGreaterThan(0.5).And.BeLessThan(1.0);
        pAt35[1].Should().Be(0.0, "Subline 1 must remain strictly 0.0 while subline 0 is in progress");

        // 3. At 4.5s (during word "break" on subline 1)
        var pAt45 = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, currentPosition: TimeSpan.FromSeconds(4.5));

        pAt45[0].Should().Be(1.0, "Subline 0 must be 1.0 once completed");
        pAt45[1].Should().BeGreaterThan(0.0).And.BeLessThan(1.0, "Subline 1 must highlight during its words");

        // 4. At 6.0s (after last word "free")
        var pAt60 = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, currentPosition: TimeSpan.FromSeconds(6.0));

        pAt60[0].Should().Be(1.0);
        pAt60[1].Should().Be(1.0);
    }

    [Fact]
    public void WrappedWords_OnSubLine1_AreNotActiveWhenFirstWordIsActive()
    {
        var words = new List<LyricWordTiming>
        {
            new("First", TimeSpan.FromSeconds(10.0), TimeSpan.FromSeconds(11.0)),
            new("Line", TimeSpan.FromSeconds(11.0), TimeSpan.FromSeconds(12.0)),
            new("Wrapped", TimeSpan.FromSeconds(12.0), TimeSpan.FromSeconds(13.0)),
            new("Second", TimeSpan.FromSeconds(13.0), TimeSpan.FromSeconds(14.0))
        };

        var line = new LyricLine(
            Timestamp: TimeSpan.FromSeconds(10.0),
            Text: "First Line Wrapped Second",
            LineIndex: 0,
            Words: words,
            EndTime: TimeSpan.FromSeconds(14.0));

        var subLines = new[] { "First Line", "Wrapped Second" };

        // Test at the exact moment "First" begins
        var progress = LyricsFillProgressCalculator.CalculateSequentialSubLineProgresses(
            line, subLines, currentPosition: TimeSpan.FromSeconds(10.2));

        progress[0].Should().BeGreaterThan(0.0);
        progress[1].Should().Be(0.0, "Second subline must have 0.0 progress when first word is playing");
    }

    [Fact]
    public void Settings_SystemTab_IsNotInVisibleTabsList()
    {
        MainWindow.VisibleSettingsTabKeys.Should().NotContain("System",
            "The System tab must be strictly removed from visible settings tabs");

        // In XAML, the SettingsFilterBarPanel must not contain BtnFilterSystem as a visible child
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lyrow.App", "MainWindow.xaml");
        if (File.Exists(xamlPath))
        {
            string xaml = File.ReadAllText(xamlPath);
            // Verify SettingsFilterBarPanel does not have BtnFilterSystem
            int panelStart = xaml.IndexOf("x:Name=\"SettingsFilterBarPanel\"", StringComparison.Ordinal);
            panelStart.Should().BeGreaterThan(0);
            int panelEnd = xaml.IndexOf("</StackPanel>", panelStart, StringComparison.Ordinal);
            panelEnd.Should().BeGreaterThan(panelStart);
            string panelContent = xaml.Substring(panelStart, panelEnd - panelStart);
            panelContent.Should().NotContain("BtnFilterSystem");
        }
    }

    [Fact]
    public void Settings_UpdatesTab_IsInVisibleTabsList()
    {
        MainWindow.VisibleSettingsTabKeys.Should().Contain("Updates");
    }

    [Fact]
    public void Settings_ShortcutsTab_IsInVisibleTabsList()
    {
        MainWindow.VisibleSettingsTabKeys.Should().Contain("Shortcuts");
    }

    [Fact]
    public void Localization_KeyParity_BetweenEnglishAndTurkish()
    {
        var loc = LocalizationService.Instance;

        // Stage 39 required keys
        string[] requiredKeys = new[]
        {
            "Theme_SystemDefault", "Theme_Dark", "Theme_Light",
            "Updates_Freq_OnStartup", "Updates_Freq_Daily", "Updates_Freq_Weekly", "Updates_Freq_Monthly",
            "Audio_CaptureEndpoint", "Audio_EndpointStatus", "Audio_FftSize", "Audio_FftSize_Desc",
            "Audio_Fft_1024", "Audio_Fft_2048", "Audio_Fft_4096",
            "Audio_DspRefresh", "Audio_DspRefresh_Status",
            "Audio_SilenceThreshold", "Audio_SilenceThreshold_Status",
            "General_SystemIntegration", "About_Subtitle", "About_TechStack", "About_RestartWizard", "Updates_ViewRelease",
            "Compact_NormalMode", "Compact_ToggleLyrics", "Compact_Player",
            "Lyrics_HideLyrics", "Lyrics_ShowLyrics", "Fullscreen_Toggle",
            "Playback_Prev", "Playback_PlayPause", "Playback_Next", "Playback_MuteUnmute",
            "Offset_Delay", "Offset_Advance", "Offset_Decrease", "Offset_Increase"
        };

        loc.SetLanguage("en");
        foreach (var key in requiredKeys)
        {
            loc[key].Should().NotBeNullOrWhiteSpace($"Key '{key}' must exist in English");
        }

        loc.SetLanguage("tr");
        foreach (var key in requiredKeys)
        {
            loc[key].Should().NotBeNullOrWhiteSpace($"Key '{key}' must exist in Turkish");
        }
    }

    [Fact]
    public void Settings_NoMissingLocalizationKeys()
    {
        var loc = LocalizationService.Instance;
        loc.SetLanguage("tr");

        // System Integration header must say "Sistem ve Entegrasyon", not "Tab_System" / "Sistem"
        loc["General_SystemIntegration"].Should().Be("Sistem ve Entegrasyon");

        // Verify Turkish characters
        loc["Theme_SystemDefault"].Should().Contain("ı");
        loc["Audio_CaptureEndpoint"].Should().Contain("ı");
        loc["Audio_Fft_1024"].Should().Contain("ö");
    }

    [Fact]
    public void CompactToNormal_Icon_IsDiagonalExpandArrow()
    {
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lyrow.App", "LyricsWindow.xaml");
        if (File.Exists(xamlPath))
        {
            string xaml = File.ReadAllText(xamlPath);
            // Verify BtnNormalModeCompact contains FontIcon with &#xE740;
            xaml.Should().MatchRegex(@"<Button\s+x:Name=""BtnNormalModeCompact""[^>]*>[\s\S]*?<FontIcon\s+x:Name=""IconNormalModeCompact""\s+Glyph=""&#xE740;""",
                "BtnNormalModeCompact icon must be diagonal expand arrow &#xE740;");
        }
    }

    [Fact]
    public void Stage38RemainingTime_RegressionCheck_DisplaysPlaceholderDashesWhenUnknown()
    {
        var engine = new PlaybackTimelineEngine();

        // When duration is not yet received after track transition:
        engine.CurrentTrackDuration.Should().Be(TimeSpan.Zero);
        engine.CurrentTrackRemainingFormatted.Should().Be("--:--");
        engine.CurrentTrackRemainingFormatted.Should().NotBe("-0:00");
    }

    [Fact]
    public void Stage38SingleActiveLine_RegressionCheck_OnlySingleActiveLineAtAnyTimestamp()
    {
        var lyrics = new SyncedLyrics(
            title: "Test",
            artist: "Test",
            isSynced: true,
            lines: new[]
            {
                new LyricLine(TimeSpan.FromSeconds(2), "One", 0),
                new LyricLine(TimeSpan.FromSeconds(5), "Two", 1),
                new LyricLine(TimeSpan.FromSeconds(8), "Three", 2)
            });

        // Test at index 1
        int activeCount = 0;
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var target = LyricsViewportCalculator.GetLineVisualTarget(i, activeIndex: 1, 28.0, 20.0);
            if (target.IsActive) activeCount++;
        }

        activeCount.Should().Be(1, "Strictly at most one line must be marked active at any timestamp");
    }
}
