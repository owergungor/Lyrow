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
        xaml.Should().Contain("ms-appx:///Assets/lyrow-welcome-hero.png");
        xaml.Should().Contain("ms-appx:///Assets/lyrow-logo-300.png");

        // V2/V3 Cinematic hero preview preserved
        xaml.Should().Contain("x:Name=\"EdgeLightAuraOuter\"");
        xaml.Should().Contain("x:Name=\"TxtPreviewLyricActive\"");
        xaml.Should().Contain("x:Name=\"EqBar1\"");
        xaml.Should().Contain("x:Name=\"TxtWelcomeHeadline\"");
        xaml.Should().Contain("x:Name=\"TxtWelcomeSubtitle\"");

        // V3 compact Fluent preference controls
        xaml.Should().Contain("x:Name=\"BtnWelcomeLangTr\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeLangEn\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeThemeSystem\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeThemeDark\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeThemeLight\"");
        xaml.Should().Contain("x:Name=\"ChkWelcomeEdgeLight\"");
        xaml.Should().Contain("x:Name=\"ChkWelcomeDynamicBg\"");

        // Primary CTA & quiet secondary action
        xaml.Should().Contain("x:Name=\"BtnWelcomeStartListening\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeCustomizeLater\"");
        xaml.Should().Contain("AutomationProperties.Name=");

        // Multi-step wizard clutter remains removed
        xaml.Should().NotContain("x:Name=\"StepDot0\"");
        xaml.Should().NotContain("x:Name=\"StepPanelAppearance\"");
        xaml.Should().NotContain("x:Name=\"StepPanelAudio\"");
        xaml.Should().NotContain("x:Name=\"BtnWelcomeBack\"");
        xaml.Should().NotContain("x:Name=\"BtnWelcomeContinue\"");
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
    public async Task WelcomeCoordinator_V3_CustomizeLater_UsesDefaultsAndMarksCompleted()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_v47v3_skip_{Guid.NewGuid():N}.json");
        try
        {
            var settingsMgr = new SettingsManager(tempPath);
            await settingsMgr.LoadAsync();

            var coord = new WelcomeOnboardingCoordinator(settingsMgr);
            (await coord.SkipAsync()).Should().BeTrue();

            var reloadedMgr = new SettingsManager(tempPath);
            var reloaded = await reloadedMgr.LoadAsync();
            reloaded.HasCompletedWelcome.Should().BeTrue();
            reloaded.Language.Should().Be("en");
            reloaded.Theme.Should().Be(ThemeMode.SystemDefault);
            reloaded.GlowEnabled.Should().BeFalse();
            reloaded.VisualIntegrationEnabled.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void SettingsAndStartup_WireShowWelcomeScreen_AndNonBlockingStartupFlow()
    {
        string root = FindRepoRoot();
        string lyricsXaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));
        string appCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "App.xaml.cs"));

        lyricsXaml.Should().Contain("x:Name=\"BtnModalShowWelcome\"");
        lyricsXaml.Should().Contain("x:Name=\"BtnModalShowWelcomeAbout\"");
        lyricsXaml.Should().Contain("Text=\"Lyrow v4.7\"");

        v4Cs.Should().Contain("public void OpenWelcomeScreenFromSettings()");
        v4Cs.Should().Contain("isManualReopen: true");
        v4Cs.Should().Contain("public async Task ApplyOnboardingPreferencesAsync()");
        v4Cs.Should().Contain("LocalizationService.Instance.SetLanguage(current.Language)");

        appCs.Should().Contain("public const string CurrentReleaseVersion = \"4.7\";");
        appCs.Should().Contain("WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(settings, IsSmokeTestMode)");
        appCs.Should().Contain("services.AddTransient<WelcomeOnboardingCoordinator>");
    }
}
