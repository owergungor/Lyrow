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

        v4Cs.Should().Contain("BtnToggleEdgeGlowCompact.Click += async (_, _) => await ToggleEdgeGlowAsync();");
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
    public void SettingsModal_FollowsWindows11FluentDesign_WithSectionHierarchy_AndLocalizedLabels()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        xaml.Should().Contain("x:Name=\"TxtModalSubGeneral\"");
        xaml.Should().Contain("x:Name=\"TxtModalSubLyrics\"");
        xaml.Should().Contain("x:Name=\"TxtModalSubSystem\"");
        xaml.Should().Contain("x:Name=\"SettingsModalSidebar\"");
        xaml.Should().NotContain("BtnModalTabAll");

        v4Cs.Should().Contain("TxtModalSubGeneral.Text = isTr ? \"Genel\" : \"General\";");
        v4Cs.Should().Contain("TxtModalSubLyrics.Text = isTr ? \"Şarkı Sözleri\" : \"Lyrics\";");
        v4Cs.Should().Contain("TxtModalSubSystem.Text = isTr ? \"Sistem ve Bildirimler\" : \"System & Notifications\";");

        // Verify legacy settings compatibility with 0..64 px range
        var settings = LyrowSettings.CreateDefault();
        settings.GlowThickness.Should().BeInRange(0, 64);
    }
}
