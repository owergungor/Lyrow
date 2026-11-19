using FluentAssertions;
using Lyrow.Core.Layout;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Comprehensive v4.2 feature verification tests covering:
/// 1. Album art dominant color extraction, caching, fallback, and Edge Light palette wiring
/// 2. Edge Light hover thickness slider flyout, live updates, delayed close, keyboard accessibility, persistence
/// 3. Compact Mode hiding the three-dot Settings button (BtnOpenSettingsModal)
/// 4. Ghost Button style across all 7 requested playback/control buttons (with Play/Pause hierarchy preserved)
/// 5. Redesigned Windows 11 two-pane Settings Modal (all settings preserved, no All tab, Light/Dark, TR/EN)
/// 6. Normal Mode Show/Hide Lyrics 2x / 0.5x smooth window width animation across DPIs and screen bounds
/// </summary>
public sealed class LyrowV42FeatureVerificationTests
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

    private static byte[] CreateSolidRgbaPixels(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }
        return pixels;
    }

    [Fact]
    public void AlbumArtColorExtraction_ProducesDistinctPalettesForDifferentCovers_AndFallsBackWhenMissing()
    {
        var redCover = CreateSolidRgbaPixels(32, 32, 220, 35, 40);
        var blueCover = CreateSolidRgbaPixels(32, 32, 30, 95, 235);
        var greenCover = CreateSolidRgbaPixels(32, 32, 35, 200, 75);

        var redPalette = ArtworkColorExtractor.ExtractPaletteFromRgbaPixels(redCover);
        var bluePalette = ArtworkColorExtractor.ExtractPaletteFromRgbaPixels(blueCover);
        var greenPalette = ArtworkColorExtractor.ExtractPaletteFromRgbaPixels(greenCover);
        var fallbackPalette = ArtworkColorExtractor.ExtractPaletteFromRgbaPixels(ReadOnlySpan<byte>.Empty);

        // Dominant (BottomColor) must reflect each cover's dominant hue
        redPalette.BottomColor.R.Should().BeGreaterThan(redPalette.BottomColor.B);
        bluePalette.BottomColor.B.Should().BeGreaterThan(bluePalette.BottomColor.R);
        greenPalette.BottomColor.G.Should().BeGreaterThan(greenPalette.BottomColor.R);

        // Distinct covers yield distinct palettes
        redPalette.BottomColor.Should().NotBe(bluePalette.BottomColor);
        bluePalette.BottomColor.Should().NotBe(greenPalette.BottomColor);

        // Missing artwork falls back safely to GlowPalette.Default
        fallbackPalette.TopColor.Should().Be(GlowPalette.Default.TopColor);
        fallbackPalette.BottomColor.Should().Be(GlowPalette.Default.BottomColor);
    }

    [Fact]
    public void EdgeLightColor_IsWiredToArtworkExtraction_ContentHashCached_AndSmoothlyCrossFaded()
    {
        string root = FindRepoRoot();
        string lyricsCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));
        string artMgrCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "Platform", "ArtworkBitmapManager.cs"));

        // UpdateArtworkAsync pushes extracted palette to Edge Light and falls back on error/empty track
        lyricsCs.Should().Contain("SyncEdgeGlowPalette(palette);");
        lyricsCs.Should().Contain("SyncEdgeGlowPalette(GlowPalette.Default);");

        // SyncEdgeGlowPalette calls GlowOverlayManager.SetCustomPalette (which smooth-lerps in GlowOverlayWindow)
        v4Cs.Should().Contain("internal void SyncEdgeGlowPalette(GlowPalette? artworkPalette)");
        v4Cs.Should().Contain("glowOverlayManager.SetCustomPalette(target);");
        v4Cs.Should().Contain("SyncEdgeGlowPalette(_lastArtworkGlowPalette);");

        // Content-hash cache prevents re-analyzing identical covers across tracks
        artMgrCs.Should().Contain("_paletteByContentHash");
        artMgrCs.Should().Contain("ComputeContentHash(rawBytes)");
    }

    [Fact]
    public void EdgeLightThicknessHoverSlider_IsPresent_HoverDelayed_KeyboardAccessible_AndPersisted()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        xaml.Should().Contain("x:Name=\"FlyoutEdgeGlowThickness\"");
        xaml.Should().Contain("x:Name=\"GridEdgeThicknessPopup\"");
        xaml.Should().Contain("x:Name=\"SliderEdgeGlowThickness\"");
        xaml.Should().Contain("x:Name=\"TxtEdgeGlowThicknessHoverValue\"");

        v4Cs.Should().Contain("InitializeEdgeGlowThicknessHoverFlyout()");
        v4Cs.Should().Contain("BtnToggleEdgeGlow.PointerEntered +=");
        v4Cs.Should().Contain("BtnToggleEdgeGlow.PointerExited +=");
        v4Cs.Should().Contain("BtnToggleEdgeGlow.KeyDown +=");
        v4Cs.Should().Contain("_edgeThicknessCloseTimer");
        v4Cs.Should().Contain("_settingsManager.Current.GlowThickness = thickness;");
        v4Cs.Should().Contain("_edgeThicknessSaveDebounceTimer");
    }

    [Fact]
    public void CompactMode_HidesThreeDotSettingsButton_AndNormalModeRestoresIt()
    {
        string root = FindRepoRoot();
        string lyricsCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        int compactStart = lyricsCs.IndexOf("if (mode == LyricsWindowMode.Compact)", StringComparison.Ordinal);
        int normalStart = lyricsCs.IndexOf("CompactPlayerView.Visibility = Visibility.Collapsed;", compactStart, StringComparison.Ordinal);
        compactStart.Should().BeGreaterThan(0);
        normalStart.Should().BeGreaterThan(compactStart);

        string compactBranch = lyricsCs.Substring(compactStart, normalStart - compactStart);
        string normalBranch = lyricsCs.Substring(normalStart, Math.Min(900, lyricsCs.Length - normalStart));

        compactBranch.Should().Contain("BtnOpenSettingsModal.Visibility = Visibility.Collapsed;");
        normalBranch.Should().Contain("BtnOpenSettingsModal.Visibility = Visibility.Visible;");
    }

    [Fact]
    public void AllSevenRequestedControls_UseGhostButtonStyle_AndPlayPausePreservesCenterHierarchy()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));

        string[] ghostButtons =
        {
            "BtnDarMod",
            "BtnFullscreen",
            "BtnPrev",
            "BtnPlayPause",
            "BtnNext",
            "BtnOffsetMinus",
            "BtnOffsetPlus"
        };

        foreach (string name in ghostButtons)
        {
            int idx = xaml.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
            idx.Should().BeGreaterThan(0, $"{name} must exist in LyricsWindow.xaml");
            int endIdx = xaml.IndexOf(">", idx, StringComparison.Ordinal);
            string tag = xaml.Substring(idx, endIdx - idx + 1);
            tag.Should().Contain("Style=\"{StaticResource LyrowGhostButtonStyle}\"", $"{name} must use Ghost Button style in v4.2");
        }

        // Play/Pause retains larger 46x46 circular center hierarchy
        int ppIdx = xaml.IndexOf("x:Name=\"BtnPlayPause\"", StringComparison.Ordinal);
        string ppTag = xaml.Substring(ppIdx, xaml.IndexOf(">", ppIdx, StringComparison.Ordinal) - ppIdx + 1);
        ppTag.Should().Contain("Width=\"46\" Height=\"46\"");
        ppTag.Should().Contain("CornerRadius=\"23\"");
    }

    [Fact]
    public void SettingsModal_HasWindows11TwoPaneSidebarLayout_PreservesAllControls_AndHasNoAllTab()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));

        // Windows 11 two-pane sidebar + independent scrollable content + accent indicator pills
        xaml.Should().Contain("x:Name=\"SettingsModalSidebar\"");
        xaml.Should().Contain("x:Name=\"ColSettingsSidebar\"");
        xaml.Should().Contain("x:Name=\"NavIndicatorAppearance\"");
        xaml.Should().Contain("x:Name=\"NavIndicatorArtwork\"");
        xaml.Should().Contain("x:Name=\"NavIndicatorAudio\"");
        xaml.Should().Contain("x:Name=\"NavIndicatorUpdates\"");
        xaml.Should().Contain("x:Name=\"NavIndicatorShortcuts\"");
        xaml.Should().Contain("x:Name=\"NavIndicatorAbout\"");
        xaml.Should().Contain("x:Name=\"SettingsModalScrollViewer\"");
        xaml.Should().NotContain("BtnModalTabAll");

        // All legacy settings controls remain intact
        string[] preservedControls =
        {
            "ComboModalLanguage", "ComboModalTheme", "SwitchModalLyricsGlow", "SwitchModalEnableAnimations",
            "SliderModalMasterOpacity", "SliderModalBackdropOpacity", "SliderModalFocusedFontSize",
            "SliderModalUnfocusedFontSize", "SwitchModalStartWithWindows", "SwitchModalSystemTray",
            "SwitchModalMinimizeToTray", "SwitchModalNotifications", "SwitchModalNotifyOnTrackChange",
            "SwitchModalArtworkDynamicBg", "SliderModalArtworkSaturation", "SliderModalArtworkCornerRadius",
            "SwitchModalAmbientEdgeGlow", "SliderModalGlowIntensity", "SliderModalGlowThickness",
            "SwitchModalSyncWithAlbumArt", "ComboModalFftSize", "SwitchModalAutoUpdate",
            "ComboModalUpdateFrequency", "BtnModalCheckUpdates", "TxtModalHotkeyGlow",
            "TxtModalHotkeyLyrics", "BtnModalClearHotkeys", "BtnModalSaveHotkeys",
            "BtnModalResetSettings", "BtnModalOpenLogs"
        };

        foreach (string ctrl in preservedControls)
        {
            xaml.Should().Contain($"x:Name=\"{ctrl}\"", $"Settings control {ctrl} must be preserved");
        }

        v4Cs.Should().Contain("UpdateSettingsModalResponsiveLayout(");
    }

    [Theory]
    [InlineData(1.0, 1920, 1040, 200, 120, 960, 720)]
    [InlineData(1.25, 2560, 1400, 300, 100, 1200, 900)]
    [InlineData(1.5, 3840, 2100, 400, 150, 1440, 1080)]
    [InlineData(2.0, 3840, 2100, 100, 80, 1920, 1440)]
    public void LyricsToggleWindowWidth_HalvesOnHide_DoublesOnShow_AndPreservesYAndHeight(
        double dpiScale, int workW, int workH, int startX, int startY, int dualW, int height)
    {
        var workArea = new WindowRectBounds(0, 0, workW, workH);
        var dualBounds = new WindowRectBounds(startX, startY, dualW, height);

        // 1. Hide lyrics: exact half width, Y and Height strictly unchanged, X preserved
        var hideResult = LyricsWindowWidthAnimatorCalculator.ComputeTargetBounds(
            dualBounds, workArea, isExpandingToShowLyrics: false, savedSinglePanelWidthPx: 0, dpiScale);

        hideResult.TargetBounds.Width.Should().Be(dualW / 2);
        hideResult.TargetBounds.Height.Should().Be(height);
        hideResult.TargetBounds.Y.Should().Be(startY);
        hideResult.TargetBounds.X.Should().Be(startX);
        hideResult.RecordedSinglePanelWidthPx.Should().Be(dualW / 2);

        // 2. Show lyrics again: exact double width, Y and Height strictly unchanged
        var showResult = LyricsWindowWidthAnimatorCalculator.ComputeTargetBounds(
            hideResult.TargetBounds, workArea, isExpandingToShowLyrics: true, hideResult.RecordedSinglePanelWidthPx, dpiScale);

        showResult.TargetBounds.Width.Should().Be(dualW);
        showResult.TargetBounds.Height.Should().Be(height);
        showResult.TargetBounds.Y.Should().Be(startY);
        showResult.TargetBounds.X.Should().Be(startX);
    }

    [Fact]
    public void LyricsToggleWindowWidth_ClampsInsideMonitorWorkArea_WhenExpandingNearRightEdge()
    {
        var workArea = new WindowRectBounds(0, 0, 1366, 728);
        // Single-panel window parked near right edge (X=866, Width=480 -> right=1346)
        var nearRightEdge = new WindowRectBounds(866, 40, 480, 680);

        var expandResult = LyricsWindowWidthAnimatorCalculator.ComputeTargetBounds(
            nearRightEdge, workArea, isExpandingToShowLyrics: true, savedSinglePanelWidthPx: 480, dpiScale: 1.0);

        expandResult.TargetBounds.Width.Should().Be(960);
        expandResult.TargetBounds.Height.Should().Be(680);
        expandResult.TargetBounds.Y.Should().Be(40);
        // Shifted left just enough so right edge does not overflow 1366
        (expandResult.TargetBounds.X + expandResult.TargetBounds.Width).Should().BeLessThanOrEqualTo(workArea.Width);
        expandResult.TargetBounds.X.Should().Be(1366 - 960);
    }

    [Fact]
    public void LyricsToggleWindowWidth_Interpolation_IsSmooth_AndPreservesYAndHeightAtEveryStep()
    {
        var start = new WindowRectBounds(200, 100, 480, 720);
        var target = new WindowRectBounds(200, 100, 960, 720);

        var mid = LyricsWindowWidthAnimatorCalculator.InterpolateBounds(start, target, 0.5);
        var end = LyricsWindowWidthAnimatorCalculator.InterpolateBounds(start, target, 1.0);

        mid.Width.Should().BeGreaterThan(720).And.BeLessThan(960); // Ease-out cubic is front-loaded
        mid.Y.Should().Be(100);
        mid.Height.Should().Be(720);
        end.Should().Be(target);
    }

    [Fact]
    public void Startup_DoesNotBlockWinUiDispatcherThread_WithSyncOverAsyncModuleActivation()
    {
        string root = FindRepoRoot();
        string appCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml.cs"));

        // Prevents WinUI 3 UI-thread deadlock when WinRT GSMTC RequestAsync() runs during OnLaunched
        appCs.Should().NotContain("ApplyExecutionModeAsync(ModuleExecutionMode.LyricsOnly).GetAwaiter().GetResult()");
        appCs.Should().Contain("await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.LyricsOnly).ConfigureAwait(false)");
    }
}
