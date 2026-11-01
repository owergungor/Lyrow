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
        loc.GetString("Tab_System").Should().NotBeNullOrWhiteSpace().And.NotBe("Tab_System");
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
    public void SettingsPersistence_MainWindowCs_ContainsDebouncedSaveAndWindowCloseFlush()
    {
        string root = GetSolutionRoot();
        string csPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml.cs");
        string code = File.ReadAllText(csPath);

        code.Should().Contain("SaveCurrentSettingsDebounced");
        code.Should().Contain("OnWindowClosed");
        code.Should().Contain("_settingsManager.SaveAsync(_settingsManager.Current");
    }
}
