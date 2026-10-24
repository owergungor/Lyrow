using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV24FeatureVerificationTests
{
    [Fact]
    public void Versioning_IsStrictlyTwoPart_V24()
    {
        Assert.Equal("2.4", GitHubUpdateService.AppVersion);
        Assert.True(GitHubUpdateService.IsValidTwoPartVersionTag("v2.4"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("v2.4.0"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("2.4.0"));
        Assert.False(GitHubUpdateService.IsValidTwoPartVersionTag("v2.4.1"));
        Assert.True(GitHubUpdateService.IsNewerVersion("2.3", "2.4"));
        Assert.False(GitHubUpdateService.IsNewerVersion("2.4", "2.4"));
    }

    [Fact]
    public void BrandingAssets_OfficialLogoAndIcon_ExistAndHaveValidSizes()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string root = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));

        string logoPng = Path.Combine(root, "src", "Lyrow.App", "Assets", "lyrow-logo.png");
        string logoIco = Path.Combine(root, "src", "Lyrow.App", "Assets", "lyrow-logo.ico");
        string appIco = Path.Combine(root, "src", "Lyrow.App", "Assets", "lyrow.ico");
        string installerIco = Path.Combine(root, "installer", "Lyrow.ico");

        File.Exists(logoPng).Should().BeTrue();
        File.Exists(logoIco).Should().BeTrue();
        File.Exists(appIco).Should().BeTrue();
        File.Exists(installerIco).Should().BeTrue();

        new FileInfo(logoPng).Length.Should().BeGreaterThan(500_000); // 1.09 MB
        new FileInfo(logoIco).Length.Should().BeGreaterThan(100_000); // 240 KB
    }

    [Fact]
    public void GlowReactivity_ExtendedRange_ZeroToFiveHundredPercent()
    {
        var beatEngine = new GlowBeatEngine();
        var levels = new AudioLevels(0.7f, 0.9f, -3f, false);
        var spectrum = new FrequencySpectrum(
            rawFrequencies: Array.Empty<float>(),
            bands: Array.Empty<float>(),
            subBass: 0.5f,
            bass: 0.8f,
            mid: 0.6f,
            treble: 0.7f,
            levels: levels);

        // 0% reactivity -> no blow up
        beatEngine.Process(spectrum, 0.0f, 16.6);
        beatEngine.OverallGlowIntensity.Should().BeInRange(0f, 1f);

        // 100% normal reactivity
        beatEngine.Process(spectrum, 1.0f, 16.6);
        beatEngine.OverallGlowIntensity.Should().BeInRange(0f, 1f);

        // 500% high reactivity -> hyperbolic tonemapping ensures clamp in range [0, 1] without overflow
        beatEngine.Process(spectrum, 5.0f, 16.6);
        beatEngine.OverallGlowIntensity.Should().BeInRange(0f, 1f);
        beatEngine.TransientEnergy.Should().BeInRange(0f, 1f);
    }

    [Fact]
    public void EdgeBloomThickness_ExtendedRange_ZeroToTwoHundredFiftySixPx()
    {
        var settings = new LyrowSettings
        {
            GlowThickness = 256
        };

        settings.GlowThickness.Should().Be(256);
        settings.GlowThickness = 0;
        settings.GlowThickness.Should().Be(0);
    }

    [Fact]
    public void Navigation_DashboardRenamedToHome_AcrossAllLanguages()
    {
        var loc = new LocalizationService();

        loc.SetLanguage("en");
        loc.GetString("Nav_Home").Should().Be("Home");
        loc.GetString("Nav_Playback").Should().Be("Playback");

        loc.SetLanguage("tr");
        loc.GetString("Nav_Home").Should().Be("Ana Sayfa");
        loc.GetString("Nav_Playback").Should().Be("Oynatma");

        loc.SetLanguage("fr");
        loc.GetString("Nav_Home").Should().Be("Accueil");
        loc.GetString("Nav_Playback").Should().Be("Lecture");

        loc.SetLanguage("es");
        loc.GetString("Nav_Home").Should().Be("Inicio");
        loc.GetString("Nav_Playback").Should().Be("Reproducción");

        loc.SetLanguage("de");
        loc.GetString("Nav_Home").Should().Be("Startseite");
        loc.GetString("Nav_Playback").Should().Be("Wiedergabe");
    }

    [Fact]
    public void SystemTrayManager_FullLifecycle_InitializedAndDisposedSafely()
    {
        using var tray = new LyrowTrayIconManager();
        int show = 0, glow = 0, lyrics = 0, ct = 0, settings = 0, exit = 0;

        tray.Initialize(
            () => show++,
            () => glow++,
            () => lyrics++,
            () => ct++,
            () => settings++,
            () => exit++);

        tray.IsVisible.Should().BeTrue();

        tray.TriggerShowMainWindow();
        tray.TriggerToggleGlow();
        tray.TriggerToggleLyrics();
        tray.TriggerToggleClickThrough();
        tray.TriggerOpenSettings();
        tray.TriggerExit();

        show.Should().Be(1);
        glow.Should().Be(1);
        lyrics.Should().Be(1);
        ct.Should().Be(1);
        settings.Should().Be(1);
        exit.Should().Be(1);

        tray.UpdateStates(true, false, true);
    }
}
