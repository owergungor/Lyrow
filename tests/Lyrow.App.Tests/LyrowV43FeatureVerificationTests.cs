using FluentAssertions;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Comprehensive v4.3 verification tests covering:
/// 1. Compact Mode Ghost Button style across all 6 controls
/// 2. Compact Mode icon-only Audio Output button with 1:1 dimensions matching Volume & Mute (26x26)
/// 3. Compact Mode Edge Light toggle button immediately to the left of Compact Lyrics toggle
/// 4. Normal Mode Edge Light thickness slider: click-only toggle (no hover), 0-64 px range, 0 px turns OFF, persisted & synced
/// 5. Windows 11 Fluent Design Settings Modal: iconography, section hierarchy, Light/Dark, TR/EN, all settings preserved, no All tab
/// </summary>
public sealed class LyrowV43FeatureVerificationTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lyrow.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate Lyrow.sln");
    }

    private static string ExtractOpeningTag(string xaml, string elementName)
    {
        int idx = xaml.IndexOf($"x:Name=\"{elementName}\"", StringComparison.Ordinal);
        idx.Should().BeGreaterThan(0, $"{elementName} must exist in LyricsWindow.xaml");
        int endIdx = xaml.IndexOf(">", idx, StringComparison.Ordinal);
        return xaml.Substring(idx, endIdx - idx + 1);
    }

    [Fact]
    public void CompactMode_AllSixButtons_UseGhostButtonStyle()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));

        string[] compactButtons =
        {
            "BtnVolumeMuteCompact",
            "BtnPlayPauseCompact",
            "BtnPrevCompact",
            "BtnNextCompact",
            "BtnNormalModeCompact",
            "BtnExpandFromCompact"
        };

        foreach (string btn in compactButtons)
        {
            string tag = ExtractOpeningTag(xaml, btn);
            tag.Should().Contain("Style=\"{StaticResource LyrowGhostButtonStyle}\"", $"{btn} must use Ghost Button style in v4.3");
        }
    }

    [Fact]
    public void CompactMode_AudioOutputButton_IsIconOnly_MatchesVolumeMuteDimensions_AndUsesGhostStyle()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));

        string volTag = ExtractOpeningTag(xaml, "BtnVolumeMuteCompact");
        string devTag = ExtractOpeningTag(xaml, "BtnDeviceSelectorCompact");

        volTag.Should().Contain("Width=\"26\" Height=\"26\"");
        devTag.Should().Contain("Width=\"26\" Height=\"26\"");
        devTag.Should().Contain("CornerRadius=\"6\"");
        devTag.Should().Contain("Style=\"{StaticResource LyrowGhostButtonStyle}\"");
        devTag.Should().Contain("ToolTipService.ToolTip=\"Audio Output\"");

        // Text label completely removed from Compact Audio Output button; icon & flyout preserved
        xaml.Should().NotContain("x:Name=\"TxtDeviceCompact\"");
        xaml.Should().Contain("x:Name=\"IconDeviceCompact\"");
        xaml.Should().Contain("x:Name=\"FlyoutAudioDevicesCompact\"");
    }

    [Fact]
    public void CompactMode_EdgeLightButton_IsPlacedLeftOfCompactLyricsToggle_AndWiredToSharedEngine()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        int edgeIdx = xaml.IndexOf("x:Name=\"BtnToggleEdgeGlowCompact\"", StringComparison.Ordinal);
        int lyricsIdx = xaml.IndexOf("x:Name=\"BtnExpandFromCompact\"", StringComparison.Ordinal);
        edgeIdx.Should().BeGreaterThan(0);
        lyricsIdx.Should().BeGreaterThan(edgeIdx, "Edge Light button must be immediately to the left of Compact Lyrics toggle");

        string edgeTag = ExtractOpeningTag(xaml, "BtnToggleEdgeGlowCompact");
        edgeTag.Should().Contain("Width=\"26\" Height=\"26\"");
        edgeTag.Should().Contain("Style=\"{StaticResource LyrowGhostButtonStyle}\"");

        // Compact Edge Light opens 0-64 px thickness slider flyout cleanly outside compact bounds
        xaml.Should().Contain("x:Name=\"FlyoutEdgeGlowThicknessCompact\"");
        xaml.Should().Contain("Placement=\"TopEdgeAlignedRight\"");
        xaml.Should().Contain("ShouldConstrainToRootBounds=\"False\"");
        string compactSliderTag = ExtractOpeningTag(xaml, "SliderEdgeGlowThicknessCompact");
        compactSliderTag.Should().Contain("Minimum=\"0\" Maximum=\"64\"");

        v4Cs.Should().Contain("BtnToggleEdgeGlowCompact.Click += (_, _) => ToggleEdgeThicknessCompactFlyout();");
        v4Cs.Should().Contain("ApplyEdgeGlowThicknessChange(thickness, source: \"compact\");");
        v4Cs.Should().Contain("BtnToggleEdgeGlowCompact.Style = _isEdgeGlowActive ? accentStyle : ghostStyle;");
    }

    [Fact]
    public void NormalMode_EdgeLightThicknessSlider_IsClickOnly_0To64Px_TurnsOffAtZero_AndPersists()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        // Slider range 0-64 px in both Normal Mode flyout and Settings Modal
        string flyoutSliderTag = ExtractOpeningTag(xaml, "SliderEdgeGlowThickness");
        flyoutSliderTag.Should().Contain("Minimum=\"0\" Maximum=\"64\"");

        string modalSliderTag = ExtractOpeningTag(xaml, "SliderModalGlowThickness");
        modalSliderTag.Should().Contain("Minimum=\"0\" Maximum=\"64\"");

        // Click-only toggle (NO hover open on BtnToggleEdgeGlow)
        v4Cs.Should().Contain("BtnToggleEdgeGlow.Click += (_, _) => ToggleEdgeThicknessFlyout();");
        v4Cs.Should().NotContain("BtnToggleEdgeGlow.PointerEntered");
        v4Cs.Should().NotContain("BtnToggleEdgeGlow.PointerExited");

        // 0 px turns Edge Light off; 1-64 px turns it on; clamped 0..64 and persisted
        v4Cs.Should().Contain("if (thickness == 0 && _isEdgeGlowActive)");
        v4Cs.Should().Contain("else if (thickness > 0 && !_isEdgeGlowActive)");
        v4Cs.Should().Contain("Math.Clamp(thickness, 0, 64)");
        v4Cs.Should().Contain("_edgeThicknessSaveDebounceTimer");
    }

    [Fact]
    public void SettingsModal_HasSevenCanonicalTabs_InExactOrder_WithCorrectSettingDistribution_AndLeftAlignedSidebar()
    {
        string root = FindRepoRoot();
        string appXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml"));
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        // Ghost button template honors HorizontalContentAlignment so sidebar items stay flush-left
        appXaml.Should().Contain("HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\"");

        // Exactly 7 tabs in exact required order: General, Appearance, Lighting, Lyrics, System, Updates, About
        SettingsNavigationModel.VisibleSettingsTabKeys.Should().Equal(
            "General", "Appearance", "Lighting", "Lyrics", "System", "Updates", "About");
        SettingsNavigationModel.VisibleSettingsTabKeys.Should().NotContain("Shortcuts");
        SettingsNavigationModel.VisibleSettingsTabKeys.Should().NotContain("All");

        string[] tabButtons =
        {
            "BtnModalTabGeneral", "BtnModalTabAppearance", "BtnModalTabLighting",
            "BtnModalTabLyrics", "BtnModalTabSystem", "BtnModalTabUpdates", "BtnModalTabAbout"
        };

        int prevPos = -1;
        foreach (string tabBtn in tabButtons)
        {
            int pos = xaml.IndexOf($"x:Name=\"{tabBtn}\"", StringComparison.Ordinal);
            pos.Should().BeGreaterThan(prevPos, $"{tabBtn} must appear in exact sidebar order");
            ExtractOpeningTag(xaml, tabBtn).Should().Contain("HorizontalContentAlignment=\"Left\"");
            prevPos = pos;
        }

        xaml.Should().NotContain("BtnModalTabShortcuts");
        xaml.Should().NotContain("BtnModalTabAll");

        // Verify each of the 7 sections contains its designated settings
        string SliceSection(string startName, string? nextName)
        {
            int s = xaml.IndexOf($"x:Name=\"{startName}\"", StringComparison.Ordinal);
            s.Should().BeGreaterThan(0);
            int e = nextName != null ? xaml.IndexOf($"x:Name=\"{nextName}\"", s, StringComparison.Ordinal) : xaml.Length;
            e.Should().BeGreaterThan(s);
            return xaml.Substring(s, e - s);
        }

        SliceSection("ModalSectionGeneral", "ModalSectionAppearance").Should().Contain("ComboModalLanguage").And.Contain("ComboModalTheme");
        SliceSection("ModalSectionAppearance", "ModalSectionLighting").Should().Contain("SliderModalMasterOpacity").And.Contain("SwitchModalArtworkDynamicBg").And.Contain("SliderModalArtworkCornerRadius");
        SliceSection("ModalSectionLighting", "ModalSectionLyrics").Should().Contain("SwitchModalAmbientEdgeGlow").And.Contain("SliderModalGlowThickness").And.Contain("ComboModalFftSize");
        SliceSection("ModalSectionLyrics", "ModalSectionSystem").Should().Contain("SwitchModalLyricsGlow").And.Contain("SliderModalBackdropOpacity").And.Contain("SliderModalFocusedFontSize");
        SliceSection("ModalSectionSystem", "ModalSectionUpdates").Should().Contain("SwitchModalStartWithWindows").And.Contain("SwitchModalSystemTray").And.Contain("SwitchModalNotifications");
        SliceSection("ModalSectionUpdates", "ModalSectionAbout").Should().Contain("SwitchModalAutoUpdate").And.Contain("ComboModalUpdateFrequency").And.Contain("BtnModalCheckUpdates");
        SliceSection("ModalSectionAbout", null).Should().Contain("TxtModalHotkeyGlow").And.Contain("TxtModalHotkeyLyrics").And.Contain("BtnModalResetSettings").And.Contain("TxtModalAboutLicense");

        // Fixed modal dimensions (Width + Height) prevent size jumping on tab switch
        string cardTag = ExtractOpeningTag(xaml, "SettingsModalCard");
        cardTag.Should().Contain("Width=\"740\"");
        cardTag.Should().Contain("Height=\"520\"");
        v4Cs.Should().Contain("SettingsModalCard.Width = Math.Min(740, availableW);");
        v4Cs.Should().Contain("SettingsModalCard.Height = Math.Min(520, availableH);");

        // Turkish and English tab labels match exact naming rules
        v4Cs.Should().Contain("string tabGeneral = isTr ? \"Genel\" : \"General\";");
        v4Cs.Should().Contain("string tabAppearance = isTr ? \"Görünüm\" : \"Appearance\";");
        v4Cs.Should().Contain("string tabLighting = isTr ? \"Işık\" : \"Lighting\";");
        v4Cs.Should().Contain("string tabLyrics = isTr ? \"Şarkı Sözleri\" : \"Lyrics\";");
        v4Cs.Should().Contain("string tabSystem = isTr ? \"Sistem\" : \"System\";");
        v4Cs.Should().Contain("string tabUpdates = isTr ? \"Güncellemeler\" : \"Updates\";");
        v4Cs.Should().Contain("string tabAbout = isTr ? \"Hakkında\" : \"About\";");

        // Legacy settings compatibility with 0..64 px range
        var settings = LyrowSettings.CreateDefault();
        settings.GlowThickness.Should().BeInRange(0, 64);
    }

    [Fact]
    public void EdgeLightBrightnessButton_IsImmediatelyRightOfThicknessButton_SameSize_0To100Percent_SyncedAndPersisted()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        // Placement: immediately to the right of BtnToggleEdgeGlow inside EdgeLightLeftControlsPanel (shared by Normal & Fullscreen modes)
        int thickIdx = xaml.IndexOf("x:Name=\"BtnToggleEdgeGlow\"", StringComparison.Ordinal);
        int brightIdx = xaml.IndexOf("x:Name=\"BtnEdgeGlowBrightness\"", StringComparison.Ordinal);
        int centerIdx = xaml.IndexOf("x:Name=\"GridOffsetCenterGroup\"", StringComparison.Ordinal);
        thickIdx.Should().BeGreaterThan(0);
        brightIdx.Should().BeGreaterThan(thickIdx, "Brightness button must sit immediately right of Thickness button");
        centerIdx.Should().BeGreaterThan(brightIdx, "Brightness button must remain in the left group before offset controls");

        // Identical 28x28 size, corner radius 6, Ghost Button style
        string thickTag = ExtractOpeningTag(xaml, "BtnToggleEdgeGlow");
        string brightTag = ExtractOpeningTag(xaml, "BtnEdgeGlowBrightness");
        thickTag.Should().Contain("Width=\"28\" Height=\"28\" Padding=\"0\" CornerRadius=\"6\"");
        brightTag.Should().Contain("Width=\"28\" Height=\"28\" Padding=\"0\" CornerRadius=\"6\"");
        brightTag.Should().Contain("Style=\"{StaticResource LyrowGhostButtonStyle}\"");
        brightTag.Should().Contain("ToolTipService.ToolTip=\"Edge Light Brightness\"");

        // Slider 0-100% in both toolbar flyout and Settings > Lighting tab
        ExtractOpeningTag(xaml, "SliderEdgeGlowBrightness").Should().Contain("Minimum=\"0\" Maximum=\"100\"");
        ExtractOpeningTag(xaml, "SliderModalGlowIntensity").Should().Contain("Minimum=\"0\" Maximum=\"100\"");
        xaml.Should().Contain("x:Name=\"TxtEdgeGlowBrightnessValue\"");

        // Click-to-toggle flyout, 2-way sync, independent persistence, TR/EN tooltips
        v4Cs.Should().Contain("InitializeEdgeGlowBrightnessClickFlyout();");
        v4Cs.Should().Contain("BtnEdgeGlowBrightness.Click += (_, _) => ToggleEdgeBrightnessFlyout();");
        v4Cs.Should().Contain("ApplyEdgeGlowBrightnessChange(brightness, source: \"toolbar\");");
        v4Cs.Should().Contain("ApplyEdgeGlowBrightnessChange(val, source: \"modal\");");
        v4Cs.Should().Contain("_settingsManager.Current.GlowIntensity = brightness;");
        v4Cs.Should().Contain("string brightnessTip = isTr ? \"Kenar Işığı Parlaklığı\" : \"Edge Light Brightness\";");
    }
}
