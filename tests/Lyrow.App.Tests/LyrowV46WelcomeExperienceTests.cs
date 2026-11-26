using FluentAssertions;
using Lyrow.Core.Localization;
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
    public void WelcomeWindow_UsesSingleScreenMicaFluentDesign_AndV45BrandAssets()
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

        // Single-screen v4.7 layout: headline, subtitle, 3 benefits, primary CTA, subtle secondary action
        xaml.Should().Contain("x:Name=\"TxtWelcomeHeadline\"");
        xaml.Should().Contain("x:Name=\"TxtWelcomeSubtitle\"");
        xaml.Should().Contain("x:Name=\"TxtWelcomeBenefit1\"");
        xaml.Should().Contain("x:Name=\"TxtWelcomeBenefit2\"");
        xaml.Should().Contain("x:Name=\"TxtWelcomeBenefit3\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeStartListening\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeCustomizeLater\"");
        xaml.Should().Contain("AutomationProperties.Name=");

        // Multi-step wizard clutter removed in v4.7
        xaml.Should().NotContain("x:Name=\"StepDot0\"");
        xaml.Should().NotContain("x:Name=\"StepPanelAppearance\"");
        xaml.Should().NotContain("x:Name=\"StepPanelAudio\"");
        xaml.Should().NotContain("x:Name=\"BtnWelcomeBack\"");
        xaml.Should().NotContain("x:Name=\"BtnWelcomeContinue\"");
    }

    [Fact]
    public void WelcomeLocalization_ProvidesCompleteEnglishAndTurkishV47Copy()
    {
        var loc = LocalizationService.Instance;
        string prevLang = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("en");
            loc["Welcome_V47_Headline"].Should().Be("Your Music. Your Lyrics. Your Atmosphere.");
            loc["Welcome_V47_Subtitle"].Should().Be("Lyrow brings your lyrics and music visuals together in one beautiful experience.");
            loc["Welcome_V47_Benefit1"].Should().NotBeNullOrWhiteSpace();
            loc["Welcome_V47_Benefit2"].Should().NotBeNullOrWhiteSpace();
            loc["Welcome_V47_Benefit3"].Should().NotBeNullOrWhiteSpace();
            loc["Welcome_Btn_StartListening"].Should().Be("Start Listening");
            loc["Welcome_Btn_CustomizeLater"].Should().Be("Customize later");

            loc.SetLanguage("tr");
            loc["Welcome_V47_Headline"].Should().Be("Müziğin. Şarkı Sözlerin. Atmosferin.");
            loc["Welcome_V47_Subtitle"].Should().NotBeNullOrWhiteSpace();
            loc["Welcome_V47_Benefit1"].Should().Be("Şarkı sözlerini müzikle eş zamanlı takip et.");
            loc["Welcome_V47_Benefit2"].Should().Be("Müziğine eşlik eden görselleri keşfet.");
            loc["Welcome_V47_Benefit3"].Should().Be("Sesle tepki veren ışık efektlerini deneyimle.");
            loc["Welcome_Btn_StartListening"].Should().Be("Dinlemeye Başla");
            loc["Welcome_Btn_CustomizeLater"].Should().Be("Daha sonra özelleştir");
        }
        finally
        {
            loc.SetLanguage(prevLang);
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

        appCs.Should().Contain("public const string CurrentReleaseVersion = \"4.7\";");
        appCs.Should().Contain("WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(settings, IsSmokeTestMode)");
        appCs.Should().Contain("services.AddTransient<WelcomeOnboardingCoordinator>");
    }
}
