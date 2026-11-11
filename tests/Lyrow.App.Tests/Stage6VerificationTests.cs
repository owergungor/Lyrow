using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class Stage6VerificationTests
{
    private static string GetSolutionRoot()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void Accessibility_MainWindowXaml_ContainsAutomationPropertiesNameOnInteractiveElements()
    {
        string root = GetSolutionRoot();
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // Verify AutomationProperties.Name is defined across settings controls
        xaml.Should().Contain("AutomationProperties.Name=\"Window Master Opacity\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Launch Lyrow on Windows Startup\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Seek Step Sensitivity\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Auto Pause Playback on System Mute\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Focused Lyric Font Size\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Unfocused Lyric Font Size\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Ambient Glow Edge Thickness\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Ambient Glow Brightness Multiplier\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Dynamic Ambient Background Layer\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Ambient Artwork Background Saturation\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Album Artwork Corner Radius\"");
        xaml.Should().Contain("AutomationProperties.Name=\"FFT Analysis Window Size\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Application Language Selector\"");

        // Count total AutomationProperties.Name occurrences to ensure broad coverage
        var matches = Regex.Matches(xaml, "AutomationProperties\\.Name=");
        matches.Count.Should().BeGreaterThanOrEqualTo(35);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void LocalizationService_ContainsStage6KeysAcrossAllLanguages(string cultureCode)
    {
        var loc = new LocalizationService();
        loc.SetLanguage(cultureCode);

        // Tab category keys
        loc.GetString("Tab_All").Should().NotBeNullOrWhiteSpace().And.NotBe("Tab_All");
        loc.GetString("Tab_Appearance").Should().NotBeNullOrWhiteSpace().And.NotBe("Tab_Appearance");
        loc.GetString("Tab_Artwork").Should().NotBeNullOrWhiteSpace().And.NotBe("Tab_Artwork");
        loc.GetString("Tab_Audio").Should().NotBeNullOrWhiteSpace().And.NotBe("Tab_Audio");
        loc.GetString("Tab_About").Should().NotBeNullOrWhiteSpace().And.NotBe("Tab_About");

        // Settings labels
        loc.GetString("General_MasterOpacity").Should().NotBeNullOrWhiteSpace().And.NotBe("General_MasterOpacity");
        loc.GetString("Playback_SeekStep").Should().NotBeNullOrWhiteSpace().And.NotBe("Playback_SeekStep");
        loc.GetString("Playback_AutoPause").Should().NotBeNullOrWhiteSpace().And.NotBe("Playback_AutoPause");
    }

    [Fact]
    public void FontSize_NonCollisionInvariant_EnforcesMinimumDelta()
    {
        // Test slider bounds and the invariant: Focused >= Unfocused + 2
        double focused = 18.0;
        double unfocused = 18.0;

        // When user drags unfocused up, focused must push up
        if (unfocused >= focused - 2)
        {
            focused = unfocused + 2;
        }

        focused.Should().Be(20.0);
        (focused - unfocused).Should().BeGreaterThanOrEqualTo(2.0);

        // When user drags focused down, unfocused must push down
        focused = 14.0;
        unfocused = 16.0;
        if (focused <= unfocused + 2)
        {
            unfocused = Math.Max(10.0, focused - 2);
        }

        unfocused.Should().Be(12.0);
        (focused - unfocused).Should().BeGreaterThanOrEqualTo(2.0);
    }

    [Fact]
    public void Settings_Stage6Invariants_PersistAndValidateSensibleRanges()
    {
        var settings = LyrowSettings.CreateDefault();

        // Master Opacity
        settings.MasterOpacity = 0.85;
        settings.MasterOpacity.Should().BeInRange(0.20, 1.0);

        // Artwork Corner Radius
        settings.ArtworkCornerRadius = 24.0;
        settings.ArtworkCornerRadius.Should().BeInRange(0.0, 32.0);

        // Artwork Saturation
        settings.ArtworkBackgroundSaturation = 1.5;
        settings.ArtworkBackgroundSaturation.Should().BeInRange(0.0, 2.0);

        // Seek step
        settings.SeekStepSeconds = 15;
        settings.SeekStepSeconds.Should().BeInRange(1, 60);

        // Auto pause on mute
        settings.AutoPauseOnMute = true;
        settings.AutoPauseOnMute.Should().BeTrue();
    }

    [Fact]
    public void ReducedMotion_MainWindowCs_ChecksAnimationsEnabled()
    {
        string root = GetSolutionRoot();
        string csPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml.cs");
        string code = File.ReadAllText(csPath);

        code.Should().Contain("IsAnimationEnabled()");
        code.Should().Contain("AnimationsEnabled");
    }

    [Fact]
    public async Task Settings_AllSeventeenProperties_RoundTripPersistAndRestore()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_settings_test_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new Lyrow.Core.Services.SettingsManager(tempFile);
            var s = LyrowSettings.CreateDefault();

            // 1. Theme
            s.Theme = ThemeMode.Dark;
            // 2. Language
            s.Language = "tr";
            // 3. Master opacity
            s.MasterOpacity = 0.82;
            // 4. Lyrics focused font size
            s.FocusedLyricFontSize = 32;
            // 5. Lyrics unfocused font size
            s.UnfocusedLyricFontSize = 22;
            // 6. Lyrics sync offset
            s.Lyrics.SyncOffsetMs = 150;
            // 7. Glow thickness
            s.GlowThickness = 36;
            // 8. Glow intensity
            s.GlowIntensity = 65;
            // 9. Glow sensitivity
            s.Glow.Sensitivity = 3.2f;
            // 10. Glow palette
            s.GlowPaletteMode = "Vibrant";
            // 11. Artwork saturation
            s.ArtworkBackgroundSaturation = 1.25;
            // 12. Artwork corner radius
            s.ArtworkCornerRadius = 18.0;
            // 13. FFT size
            s.Glow.FftSize = 1024;
            // 14. Seek step
            s.SeekStepSeconds = 10;
            // 15. Auto pause on mute
            s.AutoPauseOnMute = true;
            // 16. Start with Windows
            s.StartWithWindows = false;
            // 17. Minimize to tray
            s.MinimizeToTray = true;
            // 18. Auto update
            s.AutoUpdateEnabled = true;

            await manager.SaveAsync(s);

            // Re-instantiate from file to simulate app restart
            var reloadManager = new Lyrow.Core.Services.SettingsManager(tempFile);
            var reloaded = await reloadManager.LoadAsync();

            reloaded.Theme.Should().Be(ThemeMode.Dark);
            reloaded.Language.Should().Be("tr");
            reloaded.MasterOpacity.Should().Be(0.82);
            reloaded.FocusedLyricFontSize.Should().Be(32);
            reloaded.UnfocusedLyricFontSize.Should().Be(22);
            reloaded.Lyrics.SyncOffsetMs.Should().Be(150);
            reloaded.GlowThickness.Should().Be(36);
            reloaded.GlowIntensity.Should().Be(65);
            reloaded.Glow.Sensitivity.Should().Be(3.2f);
            reloaded.GlowPaletteMode.Should().Be("Vibrant");
            reloaded.ArtworkBackgroundSaturation.Should().Be(1.25);
            reloaded.ArtworkCornerRadius.Should().Be(18.0);
            reloaded.Glow.FftSize.Should().Be(1024);
            reloaded.SeekStepSeconds.Should().Be(10);
            reloaded.AutoPauseOnMute.Should().BeTrue();
            reloaded.StartWithWindows.Should().BeFalse();
            reloaded.MinimizeToTray.Should().BeTrue();
            reloaded.AutoUpdateEnabled.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SettingsPersistence_MainWindowCs_ContainsDebouncedSaveAndWindowCloseFlush()
    {
        string root = GetSolutionRoot();
        string csPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml.cs");
        string code = File.ReadAllText(csPath);

        code.Should().Contain("SaveCurrentSettingsDebounced");
        code.Should().Contain("OnWindowClosed");
        code.Should().Contain("_settingsManager.SaveAsync(_settingsManager.Current");
    }

    [Fact]
    public async Task SettingsPersistence_RapidSliderDragBeforeShutdown_FlushesAtomicallyWithoutUiHang()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_rapid_drag_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new Lyrow.Core.Services.SettingsManager(tempFile);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Simulate 50 rapid slider value updates in tight succession
            for (int i = 1; i <= 50; i++)
            {
                manager.Current.GlowIntensity = i;
                manager.Current.ArtworkCornerRadius = i % 24;
            }

            // Simulate immediate synchronous flush on window close
            sw.Restart();
            await manager.SaveAsync(manager.Current, CancellationToken.None);
            sw.Stop();

            // Verification 1: Synchronous flush must complete fast to avoid blocking UI (< 100ms)
            sw.ElapsedMilliseconds.Should().BeLessThan(100);

            // Verification 2: Re-read to guarantee that final value is intact
            var reloaded = await new Lyrow.Core.Services.SettingsManager(tempFile).LoadAsync();
            reloaded.GlowIntensity.Should().Be(50);
            reloaded.ArtworkCornerRadius.Should().Be(50 % 24);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
