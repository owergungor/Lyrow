using FluentAssertions;
using Xunit;

namespace Lyrow.App.Tests;

public sealed class LyrowV45BrandingVerificationTests
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
    public void AppIconFiles_AreTrueMultiResolutionWindowsIcoContainers()
    {
        string root = FindRepoRoot();
        foreach (string name in new[] { "lyrow.ico", "lyrow-logo.ico" })
        {
            string path = Path.Combine(root, "src", "Lyrow.App", "Assets", name);
            File.Exists(path).Should().BeTrue($"{name} must exist");
            byte[] bytes = File.ReadAllBytes(path);
            bytes.Length.Should().BeGreaterThan(22);

            // Standard ICO header: 00 00 01 00 <countLo> <countHi>
            bytes[0].Should().Be(0x00);
            bytes[1].Should().Be(0x00);
            bytes[2].Should().Be(0x01);
            bytes[3].Should().Be(0x00);
            int frameCount = bytes[4] | (bytes[5] << 8);
            frameCount.Should().BeGreaterThanOrEqualTo(7, "ICO must embed multi-resolution frames (16..256 px)");
        }
    }

    [Fact]
    public void BrandPngAndInstallerWizardAssets_AllExistAndAreWired()
    {
        string root = FindRepoRoot();
        string[] requiredPngs =
        {
            "src/Lyrow.App/Assets/lyrow.png",
            "src/Lyrow.App/Assets/lyrow-logo.png",
            "src/Lyrow.App/Assets/lyrow-logo-300.png",
            "src/Lyrow.App/Assets/lyrow-logo-150.png",
            "src/Lyrow.App/Assets/lyrow-logo-71.png",
            "docs/assets/lyrow-hero-banner.png",
            "docs/assets/lyrow-logo.png",
            "docs/assets/lyrow-poster.png"
        };

        foreach (string rel in requiredPngs)
        {
            string full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(full).Should().BeTrue($"{rel} must exist");
            byte[] header = File.ReadAllBytes(full)[..8];
            header.Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, $"{rel} must be a valid PNG");
        }

        File.Exists(Path.Combine(root, "installer", "assets", "wizard-sidebar.bmp")).Should().BeTrue();
        File.Exists(Path.Combine(root, "installer", "assets", "wizard-small.bmp")).Should().BeTrue();

        string setupIss = File.ReadAllText(Path.Combine(root, "installer", "setup.iss"));
        setupIss.Should().Contain("WizardImageFile=assets\\wizard-sidebar.bmp");
        setupIss.Should().Contain("WizardSmallImageFile=assets\\wizard-small.bmp");
    }

    [Fact]
    public void MsixManifestTemplate_PreservesPartnerCenterIdentity_AndWiresAllVisualTiles()
    {
        string root = FindRepoRoot();
        string manifest = File.ReadAllText(Path.Combine(root, "installer", "AppxManifest.xml.template"));

        manifest.Should().Contain("Name=\"crius.Lyrow\"");
        manifest.Should().Contain("Publisher=\"CN=2F9A5694-FADC-405A-AF51-BB9269D3FFF1\"");
        manifest.Should().Contain("<PublisherDisplayName>crius</PublisherDisplayName>");
        manifest.Should().Contain("<DisplayName>Lyrow</DisplayName>");
        manifest.Should().Contain("<rescap:Capability Name=\"runFullTrust\" />");

        manifest.Should().Contain("Assets\\StoreLogo.png");
        manifest.Should().Contain("Assets\\Square44x44Logo.png");
        manifest.Should().Contain("Assets\\Square71x71Logo.png");
        manifest.Should().Contain("Assets\\Square150x150Logo.png");
        manifest.Should().Contain("Assets\\Wide310x150Logo.png");
        manifest.Should().Contain("Assets\\Square310x310Logo.png");
        manifest.Should().Contain("Assets\\SplashScreen.png");
    }

    [Fact]
    public void WinUiApp_IntegratesNewLogoInAboutToastAndAppWindowIcon()
    {
        string root = FindRepoRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string cs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        string toastCs = File.ReadAllText(Path.Combine(root, "src", "Lyrow.App", "Controls", "ToastNotificationManager.cs"));

        xaml.Should().Contain("x:Name=\"ImgModalAboutLogo\"");
        xaml.Should().Contain("ms-appx:///Assets/lyrow-logo-300.png");
        xaml.Should().Contain("Text=\"Lyrow v5.0\"");
        cs.Should().Contain("AppWindow.SetIcon(appIconPath)");
        toastCs.Should().Contain("ms-appx:///Assets/lyrow-logo-150.png");
    }
}
