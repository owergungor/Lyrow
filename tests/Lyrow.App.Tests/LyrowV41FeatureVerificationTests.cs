using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Comprehensive verification suite for Lyrow v4.1:
/// 1. Glow defaults to OFF on fresh install and persists across restarts.
/// 2. Active Glow button uses AccentButtonStyle (same colors as Play/Pause); inactive uses Ghost/Neumorphism style.
/// 3. Compact / Dar Mod has zero Glow UI button and zero Glow visual effect while preserving preference.
/// 4. Hiding lyrics hides Glow button without altering saved Glow preference; showing lyrics restores it.
/// 5. Edge Light (Kenar Işığı) button exists to the left of - ms and binds to GlowModule + GlowOverlayManager.
/// 6. Bottom control buttons share identical 28x28 sizing and - ms / + ms group is geometrically centered with Play/Pause.
/// 7. Settings modal removes 'All'/'Tümü' tab completely; Turkish and English naming is consistent.
/// 8. Control Panel (MainWindow) is completely removed from the project.
/// 9. MSIX x64 packaging template & build script integration present.
/// </summary>
public sealed class LyrowV41FeatureVerificationTests
{
    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Lyrow.sln")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    [Fact]
    public void Glow_DefaultsToOffOnFreshInstall_AndPersistsBackwardCompatibly()
    {
        var fresh = LyrowSettings.CreateDefault();
        fresh.Lyrics.LyricsTextGlowEnabled.Should().BeFalse("Glow must default to OFF on fresh install");

        // Legacy settings JSON without LyricsTextGlowEnabled defaults to false
        var legacyJson = """{"Lyrics":{"SyncOffsetMs":0,"BackdropOpacity":85}}""";
        var loaded = JsonSerializer.Deserialize<LyrowSettings>(legacyJson);
        loaded.Should().NotBeNull();
        loaded!.Lyrics.LyricsTextGlowEnabled.Should().BeFalse();

        // Persisted true preference round-trips cleanly
        fresh.Lyrics.LyricsTextGlowEnabled = true;
        string saved = JsonSerializer.Serialize(fresh);
        var reloaded = JsonSerializer.Deserialize<LyrowSettings>(saved);
        reloaded!.Lyrics.LyricsTextGlowEnabled.Should().BeTrue();
    }

    [Fact]
    public void GlowButton_UsesPlayPauseAccentColorsWhenActive_AndGhostStyleWhenInactive()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        // Play/Pause uses AccentButtonStyle
        xaml.Should().Contain("x:Name=\"BtnPlayPause\"");
        xaml.Should().Contain("Style=\"{ThemeResource AccentButtonStyle}\"");

        // Glow button switches between AccentButtonStyle (active) and LyrowGhostButtonStyle (inactive)
        v4Cs.Should().Contain("BtnToggleLyricsGlow.Style = _isLyricsGlowEnabled ? accentStyle : ghostStyle;");
    }

    [Fact]
    public void CompactMode_HasNoGlowButtonAndNoGlowVisuals_WhilePreservingUserPreference()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        xaml.Should().NotContain("BtnToggleLyricsGlowCompact");
        v4Cs.Should().Contain("bool isCompact = _currentMode == LyricsWindowMode.Compact;");
        v4Cs.Should().Contain("bool shouldShowButton = _isLyricsEnabled && !isCompact;");
        v4Cs.Should().Contain("bool shouldRenderGlow = _isLyricsGlowEnabled && _isLyricsEnabled && !isCompact;");
        v4Cs.Should().Contain("GridCompactLyrics.Background = TransparentBrush;");
    }

    [Fact]
    public void HidingLyrics_HidesGlowButtonWithoutChangingSavedPreference()
    {
        string root = FindRepoRoot();
        string lyricsCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        // ToggleLyricsPanel calls UpdateLyricsGlowVisualState without mutating _isLyricsGlowEnabled
        lyricsCs.Should().Contain("public void ToggleLyricsPanel()");
        lyricsCs.Should().Contain("UpdateLyricsGlowVisualState();");
        v4Cs.Should().Contain("BtnToggleLyricsGlow.Visibility = shouldShowButton ? Visibility.Visible : Visibility.Collapsed;");
    }

    [Fact]
    public void BottomControls_EqualSizes_EdgeLightButtonLeft_AndOffsetGroupGeometricallyCentered()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        // GridOffsetRow uses symmetric *,Auto,* columns for true geometric centering aligned with Play/Pause
        int rowStart = xaml.IndexOf("x:Name=\"GridOffsetRow\"", StringComparison.Ordinal);
        int rowEnd = xaml.IndexOf("x:Name=\"LyricsColumnContainer\"", rowStart, StringComparison.Ordinal);
        rowStart.Should().BeGreaterThan(0);
        rowEnd.Should().BeGreaterThan(rowStart);
        string rowBlock = xaml.Substring(rowStart, rowEnd - rowStart);

        rowBlock.Should().Contain("<ColumnDefinition Width=\"*\" />");
        rowBlock.Should().Contain("<ColumnDefinition Width=\"Auto\" />");
        rowBlock.Should().Contain("x:Name=\"EdgeLightLeftControlsPanel\" Grid.Column=\"0\" Orientation=\"Horizontal\" HorizontalAlignment=\"Left\"");
        rowBlock.Should().Contain("x:Name=\"BtnToggleEdgeGlow\"");
        rowBlock.Should().Contain("x:Name=\"BtnEdgeGlowBrightness\"");
        rowBlock.Should().Contain("x:Name=\"GridOffsetCenterGroup\" Grid.Column=\"1\" HorizontalAlignment=\"Center\"");
        rowBlock.Should().Contain("x:Name=\"BtnOffsetMinus\"");
        rowBlock.Should().Contain("x:Name=\"BtnOffsetPlus\"");
        rowBlock.Should().Contain("x:Name=\"BtnToggleLyricsGlow\"");
        rowBlock.Should().Contain("x:Name=\"BtnToggleLyrics\"");

        // All 5 bottom control buttons share identical 28x28 sizing
        int count28 = rowBlock.Split("Width=\"28\" Height=\"28\"").Length - 1;
        count28.Should().BeGreaterThanOrEqualTo(5, "Edge Light, -ms, +ms, Glow, and Toggle Lyrics must all have identical 28x28 dimensions");

        // Edge Light connects to existing GlowModule + GlowOverlayManager
        v4Cs.Should().Contain("public async Task ToggleEdgeGlowAsync()");
        v4Cs.Should().Contain("moduleManager.SetModuleActiveAsync(\"glow\", active)");
        v4Cs.Should().Contain("glowOverlayManager?.StartOverlays()");
    }

    [Fact]
    public void SettingsModal_RemovesAllTab_AndHasConsistentTurkishAndEnglishLocalization()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));

        xaml.Should().NotContain("BtnModalTabAll");
        xaml.Should().NotContain("BtnModalOpenDashboard");
        SettingsNavigationModel.VisibleSettingsTabKeys.Should().NotContain("All");
        SettingsNavigationModel.VisibleSettingsTabKeys.Should().HaveCount(7);

        var loc = LocalizationService.Instance;
        string prev = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("tr");
            loc["Tab_All"].Should().Be("Tab_All", "Tab_All key must be removed from Turkish localization");
            loc["Tab_Appearance"].Should().Be("Görünüm");
            loc["Tab_Artwork"].Should().Be("Albüm Kapağı");
            loc["Tab_Audio"].Should().Be("Ses ve Kenar Işığı");
            loc["EdgeLight_On"].Should().Be("Kenar Işığı: Açık");
            loc["EdgeLight_Off"].Should().Be("Kenar Işığı: Kapalı");
            loc["Lyrics_GlowOn"].Should().Be("Şarkı Sözü Parlaması: Açık");
            loc["Lyrics_GlowOff"].Should().Be("Şarkı Sözü Parlaması: Kapalı");

            loc.SetLanguage("en");
            loc["Tab_All"].Should().Be("Tab_All", "Tab_All key must be removed from English localization");
            loc["Tab_Appearance"].Should().Be("Appearance");
            loc["Tab_Artwork"].Should().Be("Artwork");
            loc["Tab_Audio"].Should().Be("Audio & Edge Light");
            loc["EdgeLight_On"].Should().Be("Edge Light: On");
            loc["EdgeLight_Off"].Should().Be("Edge Light: Off");
            loc["Compact_Player"].Should().Be("Compact Mode", "English string must not contain Turkish '(Dar Mod)'");
        }
        finally
        {
            loc.SetLanguage(prev);
        }
    }

    [Fact]
    public void ControlPanel_MainWindow_IsCompletelyRemovedFromProject()
    {
        string root = FindRepoRoot();
        File.Exists(Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml")).Should().BeFalse();
        File.Exists(Path.Combine(root, "src", "Lyrow.App", "MainWindow.xaml.cs")).Should().BeFalse();

        string appCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml.cs"));
        appCs.Should().NotContain("new MainWindow");
        appCs.Should().NotContain("MainWindowInstance");
        appCs.Should().NotContain("MainWindow?");
        appCs.Should().Contain("PrimaryWindowInstance");
    }

    [Fact]
    public void MsixPackaging_ManifestTemplateAndBuildPipelineConfiguredForX64()
    {
        string root = FindRepoRoot();
        string manifestPath = Path.Combine(root, "installer", "AppxManifest.xml.template");
        File.Exists(manifestPath).Should().BeTrue("MSIX AppxManifest template must exist");

        string manifest = File.ReadAllText(manifestPath);
        manifest.Should().Contain("ProcessorArchitecture=\"x64\"");
        manifest.Should().Contain("Executable=\"Lyrow.App.exe\"");
        manifest.Should().Contain("rescap:Capability Name=\"runFullTrust\"");

        string script = File.ReadAllText(Path.Combine(root, "installer", "build_packages.ps1"));
        script.Should().Contain("makeappx.exe");
        script.Should().Contain(".msix");
    }

    [Fact]
    public void ToggleLyricsButton_MatchesGlowButtonVisualStyle_ActiveInactiveStates_AndFixed28x28Size()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));
        string lyricsCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // Both BtnToggleLyricsGlow and BtnToggleLyrics share 28x28, Padding=0, CornerRadius=6, FontSize=13
        int btnStart = xaml.IndexOf("x:Name=\"BtnToggleLyrics\"", StringComparison.Ordinal);
        int btnEnd = xaml.IndexOf("</Button>", btnStart, StringComparison.Ordinal);
        btnStart.Should().BeGreaterThan(0);
        btnEnd.Should().BeGreaterThan(btnStart);
        string btnBlock = xaml.Substring(btnStart, btnEnd - btnStart);
        btnBlock.Should().Contain("Width=\"28\" Height=\"28\" Padding=\"0\" CornerRadius=\"6\"");
        btnBlock.Should().Contain("Style=\"{ThemeResource AccentButtonStyle}\"");
        btnBlock.Should().Contain("Glyph=\"&#xEC4F;\" FontSize=\"13\"");

        // Active/Inactive visual state logic matches BtnToggleLyricsGlow exactly
        v4Cs.Should().Contain("private void UpdateLyricsToggleVisualState()");
        v4Cs.Should().Contain("BtnToggleLyrics.Style = _isLyricsEnabled ? accentStyle : ghostStyle;");
        v4Cs.Should().Contain("IconToggleLyrics.Opacity = _isLyricsEnabled ? 1.0 : 0.72;");
        v4Cs.Should().Contain("string glyph = _isLyricsEnabled ? \"\\uEC4F\" : \"\\uE8D6\";");

        // ToggleLyricsPanel invokes UpdateLyricsToggleVisualState and responsive layout does not override 28x28
        lyricsCs.Should().Contain("UpdateLyricsToggleVisualState();");
        lyricsCs.Should().NotContain("BtnToggleLyrics.Width = metrics.UtilityButtonSize;");
        lyricsCs.Should().NotContain("BtnToggleLyrics.Height = metrics.UtilityButtonSize;");
    }
}
