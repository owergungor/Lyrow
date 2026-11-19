using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Comprehensive verification test suite for Lyrow v4.0:
/// 1. Auto-open LyricsWindow on startup with duplicate-window prevention.
/// 2. Lyrics Glow toggle button (left of Show/Hide Lyrics) in Normal and Compact modes.
/// 3. Top-left three-dots Ghost Button opening centered, dimmed, independently scrollable Modal Settings Dialog.
/// 4. Updated Compact / Dar Mod button icon (\uE73F) paired with Normal Mode expand icon (\uE740).
/// 5. Dark/Light theme support, neumorphism tokens, Turkish/English localization parity, and settings backward compatibility.
/// </summary>
public sealed class LyrowV40FeatureVerificationTests
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
    public void Startup_AutoOpensLyricsWindow_AndPreventsDuplicateInstances()
    {
        string root = FindRepoRoot();
        string appCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml.cs"));

        // v4.1: App.xaml.cs creates LyricsWindow directly as the single PrimaryWindowInstance
        appCs.Should().Contain("if (PrimaryWindowInstance == null)");
        appCs.Should().Contain("PrimaryWindowInstance = new LyricsWindow(");
        appCs.Should().Contain("lyricsWindow.Activate();");
    }

    [Fact]
    public void LyricsGlowToggleButton_PositionedLeftOfShowHideLyrics_InNormalModeAndAbsentInCompactMode()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));

        // Normal mode: BtnToggleLyricsGlow appears immediately before BtnToggleLyrics
        int glowIdx = xaml.IndexOf("x:Name=\"BtnToggleLyricsGlow\"", StringComparison.Ordinal);
        int toggleLyricsIdx = xaml.IndexOf("x:Name=\"BtnToggleLyrics\"", StringComparison.Ordinal);
        glowIdx.Should().BeGreaterThan(0);
        toggleLyricsIdx.Should().BeGreaterThan(glowIdx, "Glow toggle button must be positioned to the left of Show/Hide Lyrics button");

        // v4.1: Compact mode must NOT have any Glow button
        xaml.Should().NotContain("BtnToggleLyricsGlowCompact");

        // Lightweight GPU glow aura layer present and non-interactive
        xaml.Should().Contain("x:Name=\"LyricsGlowAuraLayer\" IsHitTestVisible=\"False\"");
    }

    [Fact]
    public void LyricsGlowToggle_HasDistinctOnOffVisualStates_AndPersistsBackwardCompatibly()
    {
        string root = FindRepoRoot();
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        v4Cs.Should().Contain("public void ToggleLyricsGlow()");
        v4Cs.Should().Contain("public void SetLyricsGlowEnabled(bool enabled, bool persist = true)");
        v4Cs.Should().Contain("\\uE706"); // Active glow glyph
        v4Cs.Should().Contain("\\uE708"); // Inactive dimmed glyph
        v4Cs.Should().Contain("AccentButtonStyle"); // Active matches Play/Pause
        v4Cs.Should().Contain("LyrowGhostButtonStyle"); // Inactive uses Ghost/Neumorphism style

        // v4.1: Backward compatibility: older settings JSON without LyricsTextGlowEnabled defaults to false (OFF)
        var legacyJson = """{"Lyrics":{"SyncOffsetMs":0,"BackdropOpacity":85}}""";
        var deserialized = JsonSerializer.Deserialize<LyrowSettings>(legacyJson);
        deserialized.Should().NotBeNull();
        deserialized!.Lyrics.LyricsTextGlowEnabled.Should().BeFalse();
    }

    [Fact]
    public void TopLeftGhostSettingsButton_PresentInTitleBarLeftHeader_WithThreeDotsIcon()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string appXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml"));

        xaml.Should().Contain("<TitleBar.LeftHeader>");
        xaml.Should().Contain("x:Name=\"BtnOpenSettingsModal\"");
        xaml.Should().Contain("Style=\"{StaticResource LyrowGhostButtonStyle}\"");
        xaml.Should().Contain("Glyph=\"&#xE712;\"");

        // Ghost button style defines transparent resting state and distinct PointerOver/Pressed states
        appXaml.Should().Contain("x:Key=\"LyrowGhostButtonStyle\"");
        appXaml.Should().Contain("x:Name=\"PointerOver\"");
        appXaml.Should().Contain("x:Name=\"Pressed\"");
    }

    [Fact]
    public void CenteredModalSettingsDialog_OverlaysWindow_DimsBackdrop_BlocksBackground_AndScrollsIndependently()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));
        string lyricsCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // Modal overlay structure
        xaml.Should().Contain("x:Name=\"SettingsModalOverlay\" Visibility=\"Collapsed\" Canvas.ZIndex=\"2000\"");
        xaml.Should().Contain("x:Name=\"SettingsModalBackdrop\"");
        xaml.Should().Contain("Background=\"{ThemeResource LyrowModalBackdropBrush}\"");
        xaml.Should().Contain("x:Name=\"SettingsModalCard\"");
        xaml.Should().Contain("HorizontalAlignment=\"Center\"");
        xaml.Should().Contain("VerticalAlignment=\"Center\"");
        xaml.Should().Contain("x:Name=\"SettingsModalScrollViewer\"");
        xaml.Should().Contain("x:Name=\"BtnCloseSettingsModal\"");

        // All core settings sections accessible in modal
        xaml.Should().Contain("x:Name=\"ModalSectionAppearance\"");
        xaml.Should().Contain("x:Name=\"ModalSectionArtwork\"");
        xaml.Should().Contain("x:Name=\"ModalSectionAudio\"");
        xaml.Should().Contain("x:Name=\"ModalSectionUpdates\"");
        xaml.Should().Contain("x:Name=\"ModalSectionShortcuts\"");
        xaml.Should().Contain("x:Name=\"ModalSectionAbout\"");

        // Smooth open/close animations & independent scroll & ESC handling
        v4Cs.Should().Contain("public void OpenSettingsModal()");
        v4Cs.Should().Contain("public void CloseSettingsModal()");
        v4Cs.Should().Contain("OnSettingsModalPointerWheelChanged");
        lyricsCs.Should().Contain("if (_isSettingsModalOpen)");
        lyricsCs.Should().Contain("CloseSettingsModal();");
    }

    [Fact]
    public void DarModButton_UsesUpdatedCompactContractIcon_PairedWithNormalModeExpandIcon()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));

        xaml.Should().Contain("x:Name=\"IconDarMod\" Glyph=\"&#xE73F;\"");
        xaml.Should().NotContain("Glyph=\"&#xEE49;\"");
        xaml.Should().Contain("x:Name=\"IconNormalModeCompact\" Glyph=\"&#xE740;\"");
    }

    [Fact]
    public void ThemeDictionaries_SupportBothDarkAndLightForV4NeumorphismAndModal()
    {
        string root = FindRepoRoot();
        string appXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml"));

        // Ensure tokens appear in both Default (Dark) and Light dictionaries
        int lightIdx = appXaml.IndexOf("<ResourceDictionary x:Key=\"Light\">", StringComparison.Ordinal);
        lightIdx.Should().BeGreaterThan(0);

        string darkPart = appXaml.Substring(0, lightIdx);
        string lightPart = appXaml.Substring(lightIdx);

        foreach (var token in new[]
        {
            "LyrowGhostHoverBackgroundBrush",
            "LyrowGhostPressedBackgroundBrush",
            "LyrowModalBackdropBrush",
            "LyrowModalSurfaceBrush",
            "LyrowNeumorphActiveBgBrush",
            "LyrowNeumorphInactiveBgBrush"
        })
        {
            darkPart.Should().Contain(token);
            lightPart.Should().Contain(token);
        }
    }

    [Fact]
    public void Localization_V4Keys_PresentInEnglishAndTurkish()
    {
        var keys = new[]
        {
            "Lyrics_GlowOn",
            "Lyrics_GlowOff",
            "Lyrics_GlowToggle",
            "Lyrics_OpenSettings",
            "Modal_Close",
            "Settings_LyricsGlowLabel",
            "Settings_LyricsGlowDesc",
            "Settings_MinimizeToTrayLabel",
            "EdgeLight_On",
            "EdgeLight_Off"
        };

        var loc = LocalizationService.Instance;
        string prevLang = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("en");
            foreach (var k in keys)
            {
                loc[k].Should().NotBeNullOrWhiteSpace().And.NotBe(k);
            }

            loc.SetLanguage("tr");
            foreach (var k in keys)
            {
                loc[k].Should().NotBeNullOrWhiteSpace().And.NotBe(k);
            }
        }
        finally
        {
            loc.SetLanguage(prevLang);
        }
    }
}
