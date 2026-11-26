using FluentAssertions;
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
    public void WelcomeWindow_UsesMicaFluentDesign_FiveStepPanels_AndV45BrandAssets()
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

        // All 5 steps and progress dots exist
        xaml.Should().Contain("x:Name=\"StepDot0\"");
        xaml.Should().Contain("x:Name=\"StepDot4\"");
        xaml.Should().Contain("x:Name=\"StepPanelWelcome\"");
        xaml.Should().Contain("x:Name=\"StepPanelAppearance\"");
        xaml.Should().Contain("x:Name=\"StepPanelExperience\"");
        xaml.Should().Contain("x:Name=\"StepPanelAudio\"");
        xaml.Should().Contain("x:Name=\"StepPanelReady\"");

        // Navigation buttons & accessibility labels
        xaml.Should().Contain("x:Name=\"BtnWelcomeGetStarted\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeContinue\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeBack\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeSkip\"");
        xaml.Should().Contain("x:Name=\"BtnWelcomeStartListening\"");
        xaml.Should().Contain("AutomationProperties.Name=");
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
        lyricsXaml.Should().Contain("Text=\"Lyrow v4.6\"");

        v4Cs.Should().Contain("public void OpenWelcomeScreenFromSettings()");
        v4Cs.Should().Contain("isManualReopen: true");
        v4Cs.Should().Contain("public async Task ApplyOnboardingPreferencesAsync()");

        appCs.Should().Contain("public const string CurrentReleaseVersion = \"4.6\";");
        appCs.Should().Contain("WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(settings, IsSmokeTestMode)");
        appCs.Should().Contain("services.AddTransient<WelcomeOnboardingCoordinator>");
    }
}
