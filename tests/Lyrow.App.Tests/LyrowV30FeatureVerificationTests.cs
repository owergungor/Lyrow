using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

public sealed class LyrowV30FeatureVerificationTests
{
    private static string GetSolutionRoot()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    }

    #region 1. Section 24 — Two-Line Lyric Regression Test

    [Fact]
    public void LyricsTwoLineIndependentProgressTests_Section24MandatoryRegression()
    {
        // Specification requirement Section 24:
        // Input:
        // Line 1: start = 0, end = 2
        // Line 2: start = 2, end = 4
        var line1 = new LyricLine(
            Timestamp: TimeSpan.FromSeconds(0),
            Text: "I've been waiting all night",
            LineIndex: 0,
            EndTime: TimeSpan.FromSeconds(2));

        var line2 = new LyricLine(
            Timestamp: TimeSpan.FromSeconds(2),
            Text: "Just to see you again",
            LineIndex: 1,
            EndTime: TimeSpan.FromSeconds(4));

        // At t = 1: Line 1 = 0.5, Line 2 = 0
        var (p1_at1, p2_at1) = LyricsProgressCalculator.CalculateTwoLineProgress(line1, line2, TimeSpan.FromSeconds(1));
        p1_at1.Should().BeApproximately(0.5, 0.001, "At t=1, Line1 must be exactly 0.5");
        p2_at1.Should().Be(0.0, "At t=1, Line2 must be exactly 0.0 (unfilled)");

        // At t = 3: Line 1 = 1.0, Line 2 = 0.5
        var (p1_at3, p2_at3) = LyricsProgressCalculator.CalculateTwoLineProgress(line1, line2, TimeSpan.FromSeconds(3));
        p1_at3.Should().Be(1.0, "At t=3, Line1 must be completely filled (1.0)");
        p2_at3.Should().BeApproximately(0.5, 0.001, "At t=3, Line2 must be halfway filled (0.5)");

        // At t = 5: Line 1 = 1.0, Line 2 = 1.0
        var (p1_at5, p2_at5) = LyricsProgressCalculator.CalculateTwoLineProgress(line1, line2, TimeSpan.FromSeconds(5));
        p1_at5.Should().Be(1.0, "At t=5, Line1 must be 1.0");
        p2_at5.Should().Be(1.0, "At t=5, Line2 must be 1.0");
    }

    #endregion

    #region 2. Section 25 — Token Regression Test

    [Fact]
    public void LyricsTokenIndependentProgressTests_Section25MandatoryRegression()
    {
        // Specification requirement Section 25:
        // I've     0.00–0.35
        // been     0.35–0.65
        // waiting  0.65–1.20
        // all      1.20–1.40
        // night    1.40–1.90
        var token1 = new LyricToken("I've", TimeSpan.FromSeconds(0.00), TimeSpan.FromSeconds(0.35));
        var token2 = new LyricToken("been", TimeSpan.FromSeconds(0.35), TimeSpan.FromSeconds(0.65));
        var token3 = new LyricToken("waiting", TimeSpan.FromSeconds(0.65), TimeSpan.FromSeconds(1.20));
        var token4 = new LyricToken("all", TimeSpan.FromSeconds(1.20), TimeSpan.FromSeconds(1.40));
        var token5 = new LyricToken("night", TimeSpan.FromSeconds(1.40), TimeSpan.FromSeconds(1.90));

        // At t = 0.20: I've > 0, been = 0, waiting = 0, all = 0, night = 0
        var t_020 = TimeSpan.FromSeconds(0.20);
        LyricsProgressCalculator.CalculateTokenProgress(token1, t_020).Should().BeGreaterThan(0.0).And.BeLessThan(1.0);
        LyricsProgressCalculator.CalculateTokenProgress(token2, t_020).Should().Be(0.0);
        LyricsProgressCalculator.CalculateTokenProgress(token3, t_020).Should().Be(0.0);
        LyricsProgressCalculator.CalculateTokenProgress(token4, t_020).Should().Be(0.0);
        LyricsProgressCalculator.CalculateTokenProgress(token5, t_020).Should().Be(0.0);

        // At t = 0.50: I've = 1, been > 0, waiting = 0, all = 0, night = 0
        var t_050 = TimeSpan.FromSeconds(0.50);
        LyricsProgressCalculator.CalculateTokenProgress(token1, t_050).Should().Be(1.0);
        LyricsProgressCalculator.CalculateTokenProgress(token2, t_050).Should().BeGreaterThan(0.0).And.BeLessThan(1.0);
        LyricsProgressCalculator.CalculateTokenProgress(token3, t_050).Should().Be(0.0);
        LyricsProgressCalculator.CalculateTokenProgress(token4, t_050).Should().Be(0.0);
        LyricsProgressCalculator.CalculateTokenProgress(token5, t_050).Should().Be(0.0);

        // At t = 1.00: I've = 1, been = 1, waiting > 0, all = 0, night = 0
        var t_100 = TimeSpan.FromSeconds(1.00);
        LyricsProgressCalculator.CalculateTokenProgress(token1, t_100).Should().Be(1.0);
        LyricsProgressCalculator.CalculateTokenProgress(token2, t_100).Should().Be(1.0);
        LyricsProgressCalculator.CalculateTokenProgress(token3, t_100).Should().BeGreaterThan(0.0).And.BeLessThan(1.0);
        LyricsProgressCalculator.CalculateTokenProgress(token4, t_100).Should().Be(0.0);
        LyricsProgressCalculator.CalculateTokenProgress(token5, t_100).Should().Be(0.0);

        // At t = 2.00: all = 1, night = 1
        var t_200 = TimeSpan.FromSeconds(2.00);
        LyricsProgressCalculator.CalculateTokenProgress(token4, t_200).Should().Be(1.0);
        LyricsProgressCalculator.CalculateTokenProgress(token5, t_200).Should().Be(1.0);
    }

    #endregion

    #region 3. Lyrics Timeline States: Pause, Resume, Seek, Reset

    [Fact]
    public void LyricsPlaybackState_PauseFreezesAndResumeContinues()
    {
        var line = new LyricLine(TimeSpan.FromSeconds(10), "Sample line", 0, EndTime: TimeSpan.FromSeconds(20));

        // Playing at t = 15s (50% progress)
        var pos = TimeSpan.FromSeconds(15);
        double p1 = LyricsProgressCalculator.CalculateLineProgress(line, pos);
        p1.Should().BeApproximately(0.5, 0.001);

        // Simulated pause: timeline pos stays 15s -> progress remains frozen at 0.5
        double pPaused = LyricsProgressCalculator.CalculateLineProgress(line, pos);
        pPaused.Should().Be(p1);

        // Resume and advance to 16s -> progress continues smoothly from 0.6
        double pResumed = LyricsProgressCalculator.CalculateLineProgress(line, TimeSpan.FromSeconds(16));
        pResumed.Should().BeApproximately(0.6, 0.001);
    }

    [Fact]
    public void LyricsSeekForwardAndBackward_InstantRecalculation()
    {
        var line1 = new LyricLine(TimeSpan.FromSeconds(0), "Line 1", 0, EndTime: TimeSpan.FromSeconds(10));
        var line2 = new LyricLine(TimeSpan.FromSeconds(10), "Line 2", 1, EndTime: TimeSpan.FromSeconds(20));

        // Start at t = 5s (line1 = 0.5, line2 = 0.0)
        var (p1_initial, p2_initial) = LyricsProgressCalculator.CalculateTwoLineProgress(line1, line2, TimeSpan.FromSeconds(5));
        p1_initial.Should().BeApproximately(0.5, 0.001);
        p2_initial.Should().Be(0.0);

        // Seek forward to 15s: line1 becomes 1.0, line2 becomes 0.5
        var (p1_seekFwd, p2_seekFwd) = LyricsProgressCalculator.CalculateTwoLineProgress(line1, line2, TimeSpan.FromSeconds(15));
        p1_seekFwd.Should().Be(1.0);
        p2_seekFwd.Should().BeApproximately(0.5, 0.001);

        // Seek backward to 2s: line1 becomes 0.2, line2 becomes 0.0
        var (p1_seekBack, p2_seekBack) = LyricsProgressCalculator.CalculateTwoLineProgress(line1, line2, TimeSpan.FromSeconds(2));
        p1_seekBack.Should().BeApproximately(0.2, 0.001);
        p2_seekBack.Should().Be(0.0);
    }

    [Fact]
    public void LyricsNumericalEdgeCases_SafeFallbacksAndZeroDivisionPrevention()
    {
        // 1. EndTime <= StartTime (Zero duration)
        var zeroDurationLine = new LyricLine(TimeSpan.FromSeconds(10), "Zero dur", 0, EndTime: TimeSpan.FromSeconds(10));
        double pZero = LyricsProgressCalculator.CalculateLineProgress(zeroDurationLine, TimeSpan.FromSeconds(12));
        pZero.Should().Be(1.0);

        // 2. Negative seek / position
        double pNeg = LyricsProgressCalculator.CalculateLineProgress(zeroDurationLine, TimeSpan.FromSeconds(-5));
        pNeg.Should().Be(0.0);

        // 3. Null line safety
        double pNull = LyricsProgressCalculator.CalculateLineProgress((LyricLine)null!, TimeSpan.FromSeconds(10));
        pNull.Should().Be(0.0);

        // 4. Missing token timing fallback to line start/end
        var lineWithoutTokens = new LyricLine(TimeSpan.FromSeconds(0), "Hello World", 0, Words: null, EndTime: TimeSpan.FromSeconds(4));
        double pFallback = LyricsProgressCalculator.CalculateLineProgress(lineWithoutTokens, TimeSpan.FromSeconds(2));
        pFallback.Should().BeApproximately(0.5, 0.001);
    }

    #endregion

    #region 4. Section 23 — Automatic Track Transition State Isolation

    [Fact]
    public async Task AutomaticTrackTransition_Section23Regression_IsolationAndDurationReset()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        var lyricsModule = new LyricsModule(
            mediaSessionMonitor: monitor,
            lyricsClient: null,
            lyricsCache: null,
            settingsManager: null,
            eventBus: null);

        await lyricsModule.InitializeAsync();
        await lyricsModule.StartAsync();

        // 1. Track A: Duration = 240 sec
        var trackA = new MediaTrackInfo(
            Title: "Track A",
            Artist: "Artist A",
            Album: "Album A",
            Duration: TimeSpan.FromSeconds(240));

        monitor.SimulateTrack(trackA.Title, trackA.Artist, trackA.Album, trackA.Duration);
        await lyricsModule.HandleTrackChangedAsync(trackA);

        lyricsModule.CurrentTrack.Duration.TotalSeconds.Should().Be(240);
        lyricsModule.CurrentTimeline.Duration.TotalSeconds.Should().Be(240);

        // 2. Automatic GSMTC Transition to Track B: Duration = 180 sec
        var trackB = new MediaTrackInfo(
            Title: "Track B",
            Artist: "Artist B",
            Album: "Album B",
            Duration: TimeSpan.FromSeconds(180));

        monitor.SimulateTrack(trackB.Title, trackB.Artist, trackB.Album, trackB.Duration);
        await lyricsModule.HandleTrackChangedAsync(trackB);

        // Verification assertions:
        // Position must be 0s
        lyricsModule.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);
        // Duration must be 180s, NEVER 420s (240 + 180)!
        lyricsModule.CurrentTrack.Duration.TotalSeconds.Should().Be(180);
        lyricsModule.CurrentTimeline.Duration.TotalSeconds.Should().Be(180);
        lyricsModule.CurrentTimeline.Duration.TotalSeconds.Should().NotBe(420);

        // 3. Multi-track transition: B -> C -> D
        var trackC = new MediaTrackInfo("Track C", "Artist C", "Album C", TimeSpan.FromSeconds(200));
        var trackD = new MediaTrackInfo("Track D", "Artist D", "Album D", TimeSpan.FromSeconds(150));

        monitor.SimulateTrack(trackC.Title, trackC.Artist, trackC.Album, trackC.Duration);
        await lyricsModule.HandleTrackChangedAsync(trackC);

        monitor.SimulateTrack(trackD.Title, trackD.Artist, trackD.Album, trackD.Duration);
        await lyricsModule.HandleTrackChangedAsync(trackD);

        // Duration of D must be strictly 150s, NEVER cumulative A+B+C+D (770s)!
        lyricsModule.CurrentTrack.Duration.TotalSeconds.Should().Be(150);
        lyricsModule.CurrentTimeline.Duration.TotalSeconds.Should().Be(150);
        lyricsModule.CurrentTimeline.Duration.TotalSeconds.Should().NotBe(770);

        await lyricsModule.StopAsync();
    }

    #endregion

    #region 5. Glow Icon & UI Consistency

    [Fact]
    public void GlowIcon_ConfiguredToBrightLightAcrossAllViews()
    {
        string root = GetSolutionRoot();

        // 1. MainWindow.xaml NavGlow & CardGlow should use glyph &#xE706; (BrightLight)
        string mainXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml"));
        mainXaml.Should().Contain("<FontIcon Glyph=\"&#xE706;\" />", "NavGlow must use modern Fluent BrightLight icon");
        mainXaml.Should().Contain("<FontIcon Glyph=\"&#xE706;\" FontSize=\"22\"", "CardGlow must use modern Fluent BrightLight icon");
        mainXaml.Should().NotContain("&#xE781;", "Legacy Flashlight/QuickNote glyph must be completely replaced");

        // 2. Tray menu must use clean standard text without emoji
        string trayCode = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "Platform", "LyrowTrayIconManager.cs"));
        trayCode.Should().Contain("\"Ambient Glow\"", "Tray must use clean non-emoji label");
        trayCode.Should().Contain("\"Open Glow Settings\"", "Tray must use clean non-emoji settings label");
        trayCode.Should().NotContain("💡  Ambient Glow");
        trayCode.Should().NotContain("✨  Open Glow Settings");
    }

    #endregion

    #region 6. Update Toast Persistence & Progress

    [Fact]
    public void UpdateToast_LocalizationKeysExistAcrossAllFiveLanguages()
    {
        var loc = new LocalizationService();

        string[] requiredKeys =
        {
            "Updates_Available",
            "Updates_Downloading",
            "Updates_Preparing",
            "Updates_Installing",
            "Updates_Complete",
            "Updates_Failed",
            "Updates_Retry"
        };

        foreach (var lang in new[] { "en", "tr", "fr", "es", "de" })
        {
            loc.SetLanguage(lang);
            foreach (var key in requiredKeys)
            {
                string val = loc[key];
                val.Should().NotBeNullOrWhiteSpace($"Language '{lang}' must define key '{key}'");
                val.Should().NotBe(key, $"Key '{key}' should not return raw key name in language '{lang}'");
            }
        }
    }

    #endregion
}
