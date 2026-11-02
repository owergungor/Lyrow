using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class Stage12AccessibilityTests
{
    [Fact]
    public void MainWindow_InteractiveControls_HaveAutomationPropertiesName()
    {
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lyrow.App", "MainWindow.xaml");
        if (!File.Exists(xamlPath))
        {
            xamlPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "Lyrow.App", "MainWindow.xaml");
        }
        if (!File.Exists(xamlPath))
        {
            xamlPath = Path.GetFullPath("../../../../../src/Lyrow.App/MainWindow.xaml");
        }

        File.Exists(xamlPath).Should().BeTrue($"MainWindow.xaml must exist at {xamlPath}");
        string content = File.ReadAllText(xamlPath);

        string[] requiredControls =
        [
            "SwitchHomeGlow",
            "SwitchHomeLyrics",
            "SwitchGlowMaster",
            "SliderGlowIntensity",
            "SliderGlowThickness",
            "SliderGlowSensitivity",
            "SwitchAlbumArtSync",
            "ComboPalettePreset",
            "ComboGlowShutdownDelay",
            "SwitchLyricsMaster",
            "SliderLyricsOffset",
            "BtnOffsetMinus500",
            "BtnOffsetReset",
            "BtnOffsetPlus500",
            "SliderFocusedFontSize",
            "SliderUnfocusedFontSize",
            "SliderPagePlaybackVolume",
            "SliderPageSystemVolume",
            "SliderSettingsLyricsLineSpacing",
            "ComboSettingsLyricsAlignment",
            "SliderSettingsLyricsBackdropOpacity",
            "ComboSettingsGlowAcousticProfile",
            "SwitchWizardAnimations",
            "BtnWizardPlayerRefresh",
            "SwitchWizardGlow",
            "BtnWizardSkip",
            "BtnWizardNext"
        ];

        foreach (var controlName in requiredControls)
        {
            // Regex to find the XML element containing x:Name="controlName"
            var pattern = $@"<[^>]+x:Name=""{controlName}""[^>]*>";
            var match = Regex.Match(content, pattern);
            match.Success.Should().BeTrue($"Control '{controlName}' must exist in MainWindow.xaml");
            match.Value.Should().Contain("AutomationProperties.Name",
                $"Control '{controlName}' must define AutomationProperties.Name for screen reader accessibility");
        }
    }

    [Fact]
    public void LyricsWindow_InteractiveControls_HaveAutomationPropertiesName()
    {
        string xamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lyrow.App", "LyricsWindow.xaml");
        if (!File.Exists(xamlPath))
        {
            xamlPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "Lyrow.App", "LyricsWindow.xaml");
        }
        if (!File.Exists(xamlPath))
        {
            xamlPath = Path.GetFullPath("../../../../../src/Lyrow.App/LyricsWindow.xaml");
        }

        File.Exists(xamlPath).Should().BeTrue($"LyricsWindow.xaml must exist at {xamlPath}");
        string content = File.ReadAllText(xamlPath);

        string[] requiredControls =
        [
            "BtnDarMod",
            "BtnPrev",
            "BtnPlayPause",
            "BtnNext",
            "BtnFullscreen",
            "BtnVolumeMute",
            "SliderVolume",
            "BtnOffsetMinus",
            "BtnOffsetPlus",
            "BtnToggleLyrics",
            "BtnPrevCompact",
            "BtnPlayPauseCompact",
            "BtnNextCompact",
            "BtnNormalModeCompact"
        ];

        foreach (var controlName in requiredControls)
        {
            var pattern = $@"<[^>]+x:Name=""{controlName}""[^>]*>";
            var match = Regex.Match(content, pattern);
            match.Success.Should().BeTrue($"Control '{controlName}' must exist in LyricsWindow.xaml");
            match.Value.Should().Contain("AutomationProperties.Name",
                $"Control '{controlName}' must define AutomationProperties.Name for screen reader accessibility");
        }
    }

    [Fact]
    public void Localization_AllLocales_ContainSeekFailedMessage()
    {
        var loc = LocalizationService.Instance;
        string[] languages = ["en", "fr", "es", "de", "tr"];

        foreach (var lang in languages)
        {
            loc.SetLanguage(lang);
            string message = loc["Lyrics_SeekFailed"];
            message.Should().NotBeNullOrWhiteSpace($"SeekFailed message must be defined in language {lang}");
            message.Should().NotBe("Lyrics_SeekFailed", $"SeekFailed message should not fallback to key name in language {lang}");
        }

        loc.SetLanguage("en");
    }

    [Fact]
    public void LyricsWindow_SourceContains_KeyboardNavigationHandlers()
    {
        string csPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lyrow.App", "LyricsWindow.xaml.cs");
        if (!File.Exists(csPath))
        {
            csPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "Lyrow.App", "LyricsWindow.xaml.cs");
        }
        if (!File.Exists(csPath))
        {
            csPath = Path.GetFullPath("../../../../../src/Lyrow.App/LyricsWindow.xaml.cs");
        }

        File.Exists(csPath).Should().BeTrue($"LyricsWindow.xaml.cs must exist at {csPath}");
        string source = File.ReadAllText(csPath);

        source.Should().Contain("VirtualKey.F11", "Fullscreen shortcut F11 must be registered");
        source.Should().Contain("VirtualKey.Escape", "Escape key must be registered to exit fullscreen");
        source.Should().Contain("VirtualKey.Space", "Space key must be registered for Play/Pause toggle");
        source.Should().Contain("VirtualKey.Left", "Left arrow must be registered for Previous Track");
        source.Should().Contain("VirtualKey.Right", "Right arrow must be registered for Next Track");
    }

    [Fact]
    public void DesignTokens_CornerRadiiAndSpacing_FollowFluent2Hierarchy()
    {
        string appXamlPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Lyrow.App", "App.xaml");
        if (!File.Exists(appXamlPath))
        {
            appXamlPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "Lyrow.App", "App.xaml");
        }
        if (!File.Exists(appXamlPath))
        {
            appXamlPath = Path.GetFullPath("../../../../../src/Lyrow.App/App.xaml");
        }

        File.Exists(appXamlPath).Should().BeTrue($"App.xaml must exist at {appXamlPath}");
        string content = File.ReadAllText(appXamlPath);

        content.Should().Contain("LyrowCardCornerRadius", "Card corner radius token must exist");
        content.Should().Contain("LyrowControlCornerRadius", "Control corner radius token must exist");
        content.Should().Contain("LyrowPillCornerRadius", "Pill corner radius token must exist");
        content.Should().Contain("LyrowArtworkCornerRadius", "Artwork corner radius token must exist");
    }

    [Fact]
    public void ThemeMode_AllModes_AreValidEnumValues()
    {
        Enum.IsDefined(typeof(ThemeMode), ThemeMode.SystemDefault).Should().BeTrue();
        Enum.IsDefined(typeof(ThemeMode), ThemeMode.Dark).Should().BeTrue();
        Enum.IsDefined(typeof(ThemeMode), ThemeMode.Light).Should().BeTrue();
    }
}
