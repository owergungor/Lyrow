using FluentAssertions;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public sealed class LyrowV46WelcomeExperienceTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Lyrow.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Lyrow.sln not found.");
    }

    [Fact]
    public void WelcomeWindow_UsesImmersiveSingleScreenFluentDesign_WithV3Preferences_AndV45BrandAssets()
    {
        string root = FindRepoRoot();
        string welcomeXamlPath = Path.Combine(root, "src", "Lyrow.App", "WelcomeWindow.xaml");
        string heroAssetPath = Path.Combine(root, "src", "Lyrow.App", "Assets", "lyrow-welcome-hero.png");

        File.Exists(welcomeXamlPath).Should().BeTrue();
        File.Exists(heroAssetPath).Should().BeTrue();

        string xaml = File.ReadAllText(welcomeXamlPath);
        xaml.Should().Contain("<MicaBackdrop Kind=\"BaseAlt\" />");
        xaml.Should().Contain("ms-appx:///Assets/lyrow-logo-300.png");

        // Welcome V4 5-tab Fluent structure & live previews
        xaml.Should().Contain("x:Name=\"PanelWelcomeTabStart\"");
        xaml.Should().Contain("x:Name=\"PanelWelcomeTabAppearance\"");
        xaml.Should().Contain("x:Name=\"PanelWelcomeTabReactivity\"");
        xaml.Should().Contain("x:Name=\"PanelWelcomeTabShortcuts\"");
        xaml.Should().Contain("x:Name=\"PanelWelcomeTabFinish\"");
        xaml.Should().Contain("x:Name=\"EdgeLightAuraOuter\"");
        xaml.Should().Contain("x:Name=\"TxtPreviewLyricActive\"");
        xaml.Should().Contain("x:Name=\"ReactivityEqScale1\"");

        // v5.0 Welcome V4 preference controls
        xaml.Should().Contain("x:Name=\"ComboWelcomeLanguage\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeThemeSystem\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeThemeDark\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeThemeLight\"");
        xaml.Should().Contain("x:Name=\"ToggleWelcomeEdgeLight\"");
        xaml.Should().Contain("x:Name=\"ToggleWelcomeDynamicBg\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeSensCalm\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeSensBalanced\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeSensExpressive\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeHotkeyGlow\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeHotkeyLyrics\"");

        // Single prominent primary launch CTA on Finish tab (Customize Later removed)
        xaml.Should().Contain("x:Name=\"BtnWelcomeStartListening\"");
        xaml.Should().NotContain("x:Name=\"BtnWelcomeCustomizeLater\"");
        xaml.Should().Contain("AutomationProperties.Name=");
    }

    [Fact]
    public void WelcomeLocalization_ProvidesCompleteEnglishAndTurkishV47V3Copy()
    {
        var loc = LocalizationService.Instance;
        string prevLang = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("en");
            loc["Welcome_V47_Headline"].Should().Be("Your Music Comes to Life.");
            loc["Welcome_V47_Subtitle"].Should().Be("Lyrics, visuals and sound-reactive effects — together in one experience.");
            loc["Welcome_Pref_Language"].Should().Be("Language");
            loc["Welcome_Pref_Theme"].Should().Be("Theme");
            loc["Welcome_Pref_Experience"].Should().Be("Music Experience");
            loc["Welcome_Theme_SystemShort"].Should().Be("System");
            loc["Welcome_Theme_DarkShort"].Should().Be("Dark");
            loc["Welcome_Theme_LightShort"].Should().Be("Light");
            loc["Welcome_Pref_EdgeLightShort"].Should().Be("Edge Light");
            loc["Welcome_Pref_DynamicBgShort"].Should().Be("Dynamic Background");
            loc["Welcome_Btn_StartListening"].Should().Be("Start Listening");
            loc["Welcome_Btn_CustomizeLater"].Should().Be("Customize later");

            loc.SetLanguage("tr");
            loc["Welcome_V47_Headline"].Should().Be("Müziğin Hayat Buluyor.");
            loc["Welcome_V47_Subtitle"].Should().NotBeNullOrWhiteSpace();
            loc["Welcome_Pref_Language"].Should().Be("Dil");
            loc["Welcome_Pref_Theme"].Should().Be("Tema");
            loc["Welcome_Pref_Experience"].Should().Be("Müzik Deneyimi");
            loc["Welcome_Theme_SystemShort"].Should().Be("Sistem");
            loc["Welcome_Theme_DarkShort"].Should().Be("Koyu");
            loc["Welcome_Theme_LightShort"].Should().Be("Açık");
            loc["Welcome_Pref_EdgeLightShort"].Should().Be("Kenar Işığı");
            loc["Welcome_Pref_DynamicBgShort"].Should().Be("Dinamik Arka Plan");
            loc["Welcome_Btn_StartListening"].Should().Be("Dinlemeye Başla");
            loc["Welcome_Btn_CustomizeLater"].Should().Be("Daha sonra özelleştir");
        }
        finally
        {
            loc.SetLanguage(prevLang);
        }
    }

    [Fact]
    public async Task WelcomeCoordinator_V3_LiveLanguageAndThemeSelection_PersistsAndPreselectsOnManualReopen()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_v47v3_{Guid.NewGuid():N}.json");
        try
        {
            var settingsMgr = new SettingsManager(tempPath);
            await settingsMgr.LoadAsync();

            var coord = new WelcomeOnboardingCoordinator(settingsMgr);
            string? raisedLang = null;
            ThemeMode? raisedTheme = null;
            coord.LanguageSelectionChanged += (_, lang) => raisedLang = lang;
            coord.ThemeSelectionChanged += (_, mode) => raisedTheme = mode;

            coord.SelectedLanguage = "tr";
            coord.SelectedTheme = ThemeMode.Light;
            coord.EnableEdgeLight = true;
            coord.EnableDynamicBackground = false;

            raisedLang.Should().Be("tr");
            raisedTheme.Should().Be(ThemeMode.Light);

            (await coord.CompleteAsync()).Should().BeTrue();

            var reloadedMgr = new SettingsManager(tempPath);
            var reloaded = await reloadedMgr.LoadAsync();
            reloaded.HasCompletedWelcome.Should().BeTrue();
            reloaded.Language.Should().Be("tr");
            reloaded.Theme.Should().Be(ThemeMode.Light);
            reloaded.GlowEnabled.Should().BeTrue();
            reloaded.VisualIntegrationEnabled.Should().BeFalse();

            // Manual reopen must preselect saved preferences
            var reopen = new WelcomeOnboardingCoordinator(reloadedMgr, isManualReopen: true);
            reopen.SelectedLanguage.Should().Be("tr");
            reopen.SelectedTheme.Should().Be(ThemeMode.Light);
            reopen.EnableEdgeLight.Should().BeTrue();
            reopen.EnableDynamicBackground.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task WelcomeCoordinator_V3_NewUser_CustomizeLater_DiscardsTemporaryEditsAndKeepsDefaults()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_v47v3_skip_{Guid.NewGuid():N}.json");
        try
        {
            var settingsMgr = new SettingsManager(tempPath);
            await settingsMgr.LoadAsync();

            var coord = new WelcomeOnboardingCoordinator(settingsMgr);
            // User previews temporary choices, then decides "Customize later"
            coord.SelectedLanguage = "tr";
            coord.SelectedTheme = ThemeMode.Light;
            coord.EnableEdgeLight = true;
            coord.EnableDynamicBackground = false;

            (await coord.SkipAsync()).Should().BeTrue();

            var reloadedMgr = new SettingsManager(tempPath);
            var reloaded = await reloadedMgr.LoadAsync();
            reloaded.HasCompletedWelcome.Should().BeTrue();
            reloaded.Language.Should().Be("en");
            reloaded.Theme.Should().Be(ThemeMode.SystemDefault);
            reloaded.GlowEnabled.Should().BeFalse();
            reloaded.VisualIntegrationEnabled.Should().BeTrue();
            WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(reloaded).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task WelcomeCoordinator_V3_ManualReopen_StartListening_SavesNewPreferencesWithoutResettingUnrelatedSettings()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_v47v3_reopen_save_{Guid.NewGuid():N}.json");
        try
        {
            var initialMgr = new SettingsManager(tempPath);
            var existing = LyrowSettings.CreateDefault();
            existing.Language = "en";
            existing.Theme = ThemeMode.Dark;
            existing.GlowEnabled = false;
            existing.VisualIntegrationEnabled = true;
            existing.FocusedLyricFontSize = 36.0;
            existing.HasCompletedWelcome = true;
            await initialMgr.SaveAsync(existing);

            var reopenMgr = new SettingsManager(tempPath);
            await reopenMgr.LoadAsync();
            var coord = new WelcomeOnboardingCoordinator(reopenMgr, isManualReopen: true);
            coord.SelectedLanguage = "tr";
            coord.SelectedTheme = ThemeMode.Light;
            coord.EnableEdgeLight = true;
            coord.EnableDynamicBackground = false;

            (await coord.CompleteAsync()).Should().BeTrue();

            var verifyMgr = new SettingsManager(tempPath);
            var verified = await verifyMgr.LoadAsync();
            verified.HasCompletedWelcome.Should().BeTrue();
            verified.Language.Should().Be("tr");
            verified.Theme.Should().Be(ThemeMode.Light);
            verified.GlowEnabled.Should().BeTrue();
            verified.VisualIntegrationEnabled.Should().BeFalse();
            verified.FocusedLyricFontSize.Should().Be(36.0);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task WelcomeCoordinator_V3_ManualReopen_CustomizeLater_PreservesPreviouslySavedSettings()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_v47v3_reopen_skip_{Guid.NewGuid():N}.json");
        try
        {
            var initialMgr = new SettingsManager(tempPath);
            var existing = LyrowSettings.CreateDefault();
            existing.Language = "tr";
            existing.Theme = ThemeMode.Dark;
            existing.GlowEnabled = true;
            existing.VisualIntegrationEnabled = false;
            existing.FocusedLyricFontSize = 34.0;
            existing.HasCompletedWelcome = true;
            await initialMgr.SaveAsync(existing);

            var reopenMgr = new SettingsManager(tempPath);
            await reopenMgr.LoadAsync();
            var coord = new WelcomeOnboardingCoordinator(reopenMgr, isManualReopen: true);

            // User previews different options, then clicks Customize Later -> old settings must remain intact
            coord.SelectedLanguage = "en";
            coord.SelectedTheme = ThemeMode.Light;
            coord.EnableEdgeLight = false;
            coord.EnableDynamicBackground = true;

            (await coord.SkipAsync()).Should().BeTrue();

            var verifyMgr = new SettingsManager(tempPath);
            var verified = await verifyMgr.LoadAsync();
            verified.HasCompletedWelcome.Should().BeTrue();
            verified.Language.Should().Be("tr");
            verified.Theme.Should().Be(ThemeMode.Dark);
            verified.GlowEnabled.Should().BeTrue();
            verified.VisualIntegrationEnabled.Should().BeFalse();
            verified.FocusedLyricFontSize.Should().Be(34.0);
            WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(verified).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task WelcomeCoordinator_V3_CloseWindow_DiscardsTemporaryEditsAndDoesNotMarkCompleted()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_v47_close_{Guid.NewGuid():N}.json");
        try
        {
            var settingsMgr = new SettingsManager(tempPath);
            await settingsMgr.LoadAsync();

            var coord = new WelcomeOnboardingCoordinator(settingsMgr);
            coord.SelectedLanguage = "tr";
            coord.SelectedTheme = ThemeMode.Light;
            coord.EnableEdgeLight = true;
            coord.EnableDynamicBackground = false;

            (await coord.CloseWindowAsync()).Should().BeTrue();

            // Must NOT mark HasCompletedWelcome = true and must restore defaults in memory
            settingsMgr.Current.HasCompletedWelcome.Should().BeFalse();
            settingsMgr.Current.Language.Should().Be("en");
            settingsMgr.Current.Theme.Should().Be(ThemeMode.SystemDefault);
            settingsMgr.Current.GlowEnabled.Should().BeFalse();
            settingsMgr.Current.VisualIntegrationEnabled.Should().BeTrue();
            WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(settingsMgr.Current).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void WelcomeWindow_HasSingleStartListeningAction_AndSafeDismissOnCloseOrEscape()
    {
        string root = FindRepoRoot();
        string welcomeCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "WelcomeWindow.xaml.cs"));

        welcomeCs.Should().Contain("BtnWelcomeStartListening.Click += OnStartListeningClicked;");
        welcomeCs.Should().NotContain("BtnWelcomeCustomizeLater");
        welcomeCs.Should().Contain("private async Task ExecuteStartListeningAsync()");
        welcomeCs.Should().Contain("private async Task DismissWithoutSavingAsync()");
        welcomeCs.Should().Contain("_coordinator.CompleteAsync(");
        welcomeCs.Should().Contain("_coordinator.CloseWindowAsync(");
        welcomeCs.Should().Contain("LocalizationService.Instance.SetLanguage(_coordinator.InitialLanguage)");
        welcomeCs.Should().Contain("App.PrimaryWindowInstance?.ApplyTheme(_coordinator.InitialTheme)");
    }

    [Fact]
    public void LightThemeReadability_UsesThemeAwareAmbientScrimVignetteAndLyricBrushes()
    {
        string root = FindRepoRoot();
        string appXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml"));
        string lyricsXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string lyricsCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        appXaml.Should().Contain("x:Key=\"LyrowAmbientScrimBrush\"");
        appXaml.Should().Contain("x:Key=\"LyrowLyricActiveBrush\"");
        appXaml.Should().Contain("x:Key=\"LyrowLyricInactiveBrush\"");
        lyricsXaml.Should().Contain("x:Name=\"AmbientScrimRect\"");
        lyricsXaml.Should().Contain("x:Name=\"AmbientVignetteMid\"");
        lyricsXaml.Should().Contain("x:Name=\"AmbientVignetteOuter\"");
        lyricsCs.Should().Contain("private void RefreshThemeDependentVisuals()");
        lyricsCs.Should().Contain("private bool IsEffectiveLightTheme()");
        lyricsCs.Should().Contain("ArtworkPaletteExtractor.CreateLightAmbientPalette(_currentAmbientPalette)");
        lyricsCs.Should().Contain("RootLayout.ActualThemeChanged");
    }

    [Fact]
    public void AlbumAdaptiveLightTheme_PreservesDistinctHueCharacterAcrossPurpleBlueRedGreenAndWarmAlbums()
    {
        // Purple album -> lavender / pastel purple (B & R > G)
        var purpleDark = ArtworkPaletteExtractor.ExtractAmbientPalette(
            new GlowPalette(GlowColor.FromRgb(140, 60, 220), GlowColor.FromRgb(110, 50, 200), GlowColor.FromRgb(160, 80, 230), GlowColor.FromRgb(90, 70, 210), GlowColor.FromRgb(150, 70, 225)));
        var purpleLight = ArtworkPaletteExtractor.CreateLightAmbientPalette(purpleDark);
        ArtworkPaletteExtractor.CalculateLuminance(purpleLight.Dominant).Should().BeGreaterThan(185);
        purpleLight.Dominant.B.Should().BeGreaterThan(purpleLight.Dominant.G);

        // Blue album -> ice sky blue (B > R)
        var blueDark = ArtworkPaletteExtractor.ExtractAmbientPalette(
            new GlowPalette(GlowColor.FromRgb(30, 120, 230), GlowColor.FromRgb(20, 90, 210), GlowColor.FromRgb(40, 150, 240), GlowColor.FromRgb(35, 110, 220), GlowColor.FromRgb(50, 140, 235)));
        var blueLight = ArtworkPaletteExtractor.CreateLightAmbientPalette(blueDark);
        ArtworkPaletteExtractor.CalculateLuminance(blueLight.Dominant).Should().BeGreaterThan(185);
        blueLight.Dominant.B.Should().BeGreaterThan(blueLight.Dominant.R);

        // Red/Pink album -> rose / pastel pink (R > G and R > B)
        var redDark = ArtworkPaletteExtractor.ExtractAmbientPalette(
            new GlowPalette(GlowColor.FromRgb(220, 55, 85), GlowColor.FromRgb(200, 45, 75), GlowColor.FromRgb(230, 80, 110), GlowColor.FromRgb(190, 50, 80), GlowColor.FromRgb(225, 65, 95)));
        var redLight = ArtworkPaletteExtractor.CreateLightAmbientPalette(redDark);
        ArtworkPaletteExtractor.CalculateLuminance(redLight.Dominant).Should().BeGreaterThan(185);
        redLight.Dominant.R.Should().BeGreaterThan(redLight.Dominant.G);

        // Green album -> soft sage green (G > R and G > B)
        var greenDark = ArtworkPaletteExtractor.ExtractAmbientPalette(
            new GlowPalette(GlowColor.FromRgb(45, 180, 95), GlowColor.FromRgb(35, 160, 85), GlowColor.FromRgb(60, 190, 110), GlowColor.FromRgb(40, 170, 90), GlowColor.FromRgb(55, 185, 100)));
        var greenLight = ArtworkPaletteExtractor.CreateLightAmbientPalette(greenDark);
        ArtworkPaletteExtractor.CalculateLuminance(greenLight.Dominant).Should().BeGreaterThan(185);
        greenLight.Dominant.G.Should().BeGreaterThan(greenLight.Dominant.R);

        // Warm Amber/Ochre (e.g. KATSEYE) -> warm cream / peach (R > B)
        var warmDark = ArtworkPaletteExtractor.ExtractAmbientPalette(
            new GlowPalette(GlowColor.FromRgb(210, 145, 55), GlowColor.FromRgb(185, 125, 45), GlowColor.FromRgb(75, 150, 145), GlowColor.FromRgb(195, 135, 50), GlowColor.FromRgb(205, 140, 60)));
        var warmLight = ArtworkPaletteExtractor.CreateLightAmbientPalette(warmDark);
        ArtworkPaletteExtractor.CalculateLuminance(warmLight.Dominant).Should().BeGreaterThan(185);
        warmLight.Dominant.R.Should().BeGreaterThan(warmLight.Dominant.B);
    }

    [Fact]
    public void SettingsAndStartup_WireShowWelcomeScreen_AndNonBlockingStartupFlow()
    {
        string root = FindRepoRoot();
        string lyricsXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));
        string appCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml.cs"));

        lyricsXaml.Should().NotContain("x:Name=\"BtnModalShowWelcome\"");
        lyricsXaml.Should().Contain("x:Name=\"BtnModalShowWelcomeAbout\"");
        lyricsXaml.Should().Contain("Text=\"Lyrow v5.0\"");

        v4Cs.Should().Contain("public void OpenWelcomeScreenFromSettings()");
        v4Cs.Should().Contain("isManualReopen: true");
        v4Cs.Should().Contain("public async Task ApplyOnboardingPreferencesAsync()");
        v4Cs.Should().Contain("LocalizationService.Instance.SetLanguage(current.Language)");

        appCs.Should().Contain("public const string CurrentReleaseVersion = \"5.0\";");
        appCs.Should().Contain("WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(settings, IsSmokeTestMode)");
        appCs.Should().Contain("services.AddTransient<WelcomeOnboardingCoordinator>");
    }
}
