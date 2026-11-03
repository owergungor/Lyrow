using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowIdentityAndDesktopExperienceTests
{
    private static string GetSolutionRoot()
    {
        string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void DesignSystem_AppXaml_DefinesRequiredDesignTokens()
    {
        string root = GetSolutionRoot();
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "App.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // CornerRadius design tokens
        xaml.Should().Contain("x:Key=\"LyrowCardCornerRadius\"");
        xaml.Should().Contain("x:Key=\"LyrowControlCornerRadius\"");
        xaml.Should().Contain("x:Key=\"LyrowPillCornerRadius\"");
        xaml.Should().Contain("x:Key=\"LyrowArtworkCornerRadius\"");
        xaml.Should().Contain("x:Key=\"LyrowBadgeCornerRadius\"");

        // Typography hierarchy tokens
        xaml.Should().Contain("x:Key=\"LyrowFontSizeTitle\"");
        xaml.Should().Contain("x:Key=\"LyrowFontSizeSection\"");
        xaml.Should().Contain("x:Key=\"LyrowFontSizeBody\"");
        xaml.Should().Contain("x:Key=\"LyrowFontSizeCaption\"");
        xaml.Should().Contain("x:Key=\"LyrowFontSizeFocusedLyric\"");
        xaml.Should().Contain("x:Key=\"LyrowFontSizeUnfocusedLyric\"");

        // Animation duration tokens
        xaml.Should().Contain("x:Key=\"LyrowAnimDurationFast\"");
        xaml.Should().Contain("x:Key=\"LyrowAnimDurationNormal\"");
        xaml.Should().Contain("x:Key=\"LyrowAnimDurationSmooth\"");

        // Theme dictionaries
        xaml.Should().Contain("<ResourceDictionary x:Key=\"Default\">");
        xaml.Should().Contain("<ResourceDictionary x:Key=\"Light\">");
        xaml.Should().Contain("x:Key=\"LyrowAccentBrush\"");
        xaml.Should().Contain("x:Key=\"LyrowSuccessBrush\"");
        xaml.Should().Contain("x:Key=\"LyrowSurfaceBrush\"");
    }

    [Fact]
    public void MainWindowXaml_DefinesCategorizedSettingsAndSidebarPagesWithoutDuplicates()
    {
        string root = GetSolutionRoot();
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // 1. Appearance / General in Settings
        xaml.Should().Contain("x:Name=\"SectionGeneral\"");
        xaml.Should().Contain("x:Name=\"SliderMasterOpacity\"");

        // 2. Playback transport settings live in PagePlayback
        xaml.Should().Contain("x:Name=\"PagePlayback\"");
        xaml.Should().Contain("x:Name=\"ComboSeekStep\"");
        xaml.Should().Contain("x:Name=\"SwitchAutoPauseOnMute\"");

        // 3. Lyrics typography & appearance settings live in PageLyrics
        xaml.Should().Contain("x:Name=\"PageLyrics\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsLyricsLineSpacing\"");
        xaml.Should().Contain("x:Name=\"ComboSettingsLyricsAlignment\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsLyricsBackdropOpacity\"");

        // 4. Ambient Glow profile lives in PageGlow
        xaml.Should().Contain("x:Name=\"PageGlow\"");
        xaml.Should().Contain("x:Name=\"ComboSettingsGlowAcousticProfile\"");

        // 5. System (Startup, Tray, Notifications, Shortcuts, Updates) in Settings
        xaml.Should().Contain("x:Name=\"SectionSystem\"");
        xaml.Should().Contain("x:Name=\"SwitchStartWithWindows\"");
        xaml.Should().Contain("x:Name=\"SwitchSystemTray\"");
        xaml.Should().Contain("x:Name=\"SectionShortcuts\"");
        xaml.Should().Contain("x:Name=\"SectionUpdates\"");

        // 6. Artwork & Audio in Settings
        xaml.Should().Contain("x:Name=\"SectionSettingsArtwork\"");
        xaml.Should().Contain("x:Name=\"SwitchArtworkDynamicBackground\"");
        xaml.Should().Contain("x:Name=\"SliderArtworkBackgroundSaturation\"");
        xaml.Should().Contain("x:Name=\"SliderArtworkCornerRadius\"");
        xaml.Should().Contain("x:Name=\"SectionSettingsAudio\"");
        xaml.Should().Contain("x:Name=\"ComboFftSize\"");

        // 7. About in Settings
        xaml.Should().Contain("x:Name=\"SectionAbout\"");

        // 8. Settings page does not contain redundant duplicate sections
        xaml.Should().NotContain("x:Name=\"SectionSettingsPlayback\"");
        xaml.Should().NotContain("x:Name=\"SectionSettingsLyrics\"");
        xaml.Should().NotContain("x:Name=\"SectionSettingsGlow\"");
    }

    [Fact]
    public void MainWindowXaml_DefinesLiveInteractivePersonalizationElements()
    {
        string root = GetSolutionRoot();
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // Lyrics interactive elements on PageLyrics
        xaml.Should().Contain("x:Name=\"SliderFocusedFontSize\"");
        xaml.Should().Contain("x:Name=\"SliderUnfocusedFontSize\"");
        xaml.Should().Contain("x:Name=\"SliderLyricsOffset\"");

        // Glow interactive elements on PageGlow
        xaml.Should().Contain("x:Name=\"SliderGlowIntensity\"");
        xaml.Should().Contain("x:Name=\"SliderGlowThickness\"");
        xaml.Should().Contain("x:Name=\"SliderGlowSensitivity\"");
        xaml.Should().Contain("x:Name=\"SwitchAlbumArtSync\"");
    }

    [Fact]
    public void MainWindowXaml_DefinesNavigationAndCategoryFilterButtons()
    {
        string root = GetSolutionRoot();
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // All 8 category filter buttons + All
        xaml.Should().Contain("x:Name=\"BtnFilterAllSettings\"");
        xaml.Should().Contain("x:Name=\"BtnFilterAppearance\"");
        xaml.Should().Contain("x:Name=\"BtnFilterPlayback\"");
        xaml.Should().Contain("x:Name=\"BtnFilterLyrics\"");
        xaml.Should().Contain("x:Name=\"BtnFilterGlow\"");
        xaml.Should().Contain("x:Name=\"BtnFilterArtwork\"");
        xaml.Should().Contain("x:Name=\"BtnFilterAudio\"");
        xaml.Should().Contain("x:Name=\"BtnFilterSystem\"");
        xaml.Should().Contain("x:Name=\"BtnFilterAbout\"");

        // Compatibility aliases
        xaml.Should().Contain("x:Name=\"BtnFilterGeneral\"");
        xaml.Should().Contain("x:Name=\"BtnFilterUpdates\"");
        xaml.Should().Contain("x:Name=\"BtnFilterShortcuts\"");
    }

    [Fact]
    public void LyrowSettings_NewProperties_HaveSensibleDefaultsAndCanBeMutated()
    {
        var settings = LyrowSettings.CreateDefault();

        settings.ArtworkBackgroundSaturation.Should().Be(1.0);
        settings.ArtworkCornerRadius.Should().Be(12.0);
        settings.SeekStepSeconds.Should().Be(5);
        settings.AutoPauseOnMute.Should().BeFalse();

        // Mutate properties
        settings.ArtworkBackgroundSaturation = 1.25;
        settings.ArtworkCornerRadius = 16.0;
        settings.SeekStepSeconds = 10;
        settings.AutoPauseOnMute = true;

        settings.ArtworkBackgroundSaturation.Should().Be(1.25);
        settings.ArtworkCornerRadius.Should().Be(16.0);
        settings.SeekStepSeconds.Should().Be(10);
        settings.AutoPauseOnMute.Should().BeTrue();
    }

    [Fact]
    public void MainWindowCs_ContainsKeyboardNavigationShortcuts()
    {
        string root = GetSolutionRoot();
        string csPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml.cs");
        string code = File.ReadAllText(csPath);

        code.Should().Contain("VirtualKey.Number1");
        code.Should().Contain("VirtualKey.Number2");
        code.Should().Contain("VirtualKey.Number3");
        code.Should().Contain("VirtualKey.Number4");
        code.Should().Contain("VirtualKey.Number5");
    }
}
