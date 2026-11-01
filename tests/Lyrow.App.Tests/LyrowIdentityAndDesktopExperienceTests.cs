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
    public void MainWindowXaml_DefinesEightCategorizedSettingsSections()
    {
        string root = GetSolutionRoot();
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // 1. Appearance / General
        xaml.Should().Contain("x:Name=\"SectionGeneral\"");
        xaml.Should().Contain("x:Name=\"SliderMasterOpacity\"");

        // 2. Playback
        xaml.Should().Contain("x:Name=\"SectionSettingsPlayback\"");
        xaml.Should().Contain("x:Name=\"ComboSeekStep\"");
        xaml.Should().Contain("x:Name=\"SwitchAutoPauseOnMute\"");

        // 3. Lyrics
        xaml.Should().Contain("x:Name=\"SectionSettingsLyrics\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsFocusedFontSize\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsUnfocusedFontSize\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsLyricsOffset\"");

        // 4. Ambient Glow
        xaml.Should().Contain("x:Name=\"SectionSettingsGlow\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsGlowThickness\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsGlowIntensity\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsGlowSensitivity\"");
        xaml.Should().Contain("x:Name=\"ComboSettingsGlowPalette\"");

        // 5. Artwork
        xaml.Should().Contain("x:Name=\"SectionSettingsArtwork\"");
        xaml.Should().Contain("x:Name=\"SwitchArtworkDynamicBackground\"");
        xaml.Should().Contain("x:Name=\"SliderArtworkBackgroundSaturation\"");
        xaml.Should().Contain("x:Name=\"SliderArtworkCornerRadius\"");

        // 6. Audio
        xaml.Should().Contain("x:Name=\"SectionSettingsAudio\"");
        xaml.Should().Contain("x:Name=\"ComboFftSize\"");

        // 7. System (Shortcuts + Updates)
        xaml.Should().Contain("x:Name=\"SectionShortcuts\"");
        xaml.Should().Contain("x:Name=\"SectionUpdates\"");

        // 8. About
        xaml.Should().Contain("x:Name=\"SectionAbout\"");
    }

    [Fact]
    public void MainWindowXaml_DefinesInteractiveLivePreviewElements()
    {
        string root = GetSolutionRoot();
        string xamlPath = Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // Lyrics Live Preview controls
        xaml.Should().Contain("x:Name=\"TxtLyricsPreviewFocused\"");
        xaml.Should().Contain("x:Name=\"TxtLyricsPreviewPrev\"");
        xaml.Should().Contain("x:Name=\"TxtLyricsPreviewNext\"");

        // Glow 4-Edge Live Preview controls
        xaml.Should().Contain("x:Name=\"BorderGlowPreviewFrame\"");
        xaml.Should().Contain("x:Name=\"GlowPreviewTop\"");
        xaml.Should().Contain("x:Name=\"GlowPreviewBottom\"");
        xaml.Should().Contain("x:Name=\"GlowPreviewLeft\"");
        xaml.Should().Contain("x:Name=\"GlowPreviewRight\"");
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
