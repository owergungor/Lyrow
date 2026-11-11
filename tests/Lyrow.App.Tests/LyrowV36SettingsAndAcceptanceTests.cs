using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV36SettingsAndAcceptanceTests
{
    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Lyrow.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new InvalidOperationException("Could not find Lyrow.sln");
    }

    [Fact]
    public void Settings_DuplicateNavigationAndUiRegression_NoDuplicatesInSettings()
    {
        string repoRoot = FindRepoRoot();
        string xamlPath = Path.Combine(repoRoot, "src", "Lyrow.App", "MainWindow.xaml");
        string xaml = File.ReadAllText(xamlPath);

        // 1. Settings page MUST NOT contain duplicate cards for Playback, Lyrics, or Glow
        xaml.Should().NotContain("x:Name=\"SectionSettingsPlayback\"");
        xaml.Should().NotContain("x:Name=\"SectionSettingsLyrics\"");
        xaml.Should().NotContain("x:Name=\"SectionSettingsGlow\"");

        // 2. Dedicated sidebar pages MUST contain their respective configuration controls
        // PagePlayback has seek step and auto pause
        xaml.Should().Contain("x:Name=\"PagePlayback\"");
        xaml.Should().Contain("x:Name=\"ComboSeekStep\"");
        xaml.Should().Contain("x:Name=\"SwitchAutoPauseOnMute\"");

        // PageLyrics has typography and styling controls
        xaml.Should().Contain("x:Name=\"PageLyrics\"");
        xaml.Should().Contain("x:Name=\"SliderFocusedFontSize\"");
        xaml.Should().Contain("x:Name=\"SliderUnfocusedFontSize\"");
        xaml.Should().Contain("x:Name=\"SliderLyricsOffset\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsLyricsLineSpacing\"");
        xaml.Should().Contain("x:Name=\"ComboSettingsLyricsAlignment\"");
        xaml.Should().Contain("x:Name=\"SliderSettingsLyricsBackdropOpacity\"");

        // PageGlow has glow controls and acoustic profile
        xaml.Should().Contain("x:Name=\"PageGlow\"");
        xaml.Should().Contain("x:Name=\"SliderGlowIntensity\"");
        xaml.Should().Contain("x:Name=\"SliderGlowThickness\"");
        xaml.Should().Contain("x:Name=\"SliderGlowSensitivity\"");
        xaml.Should().Contain("x:Name=\"ComboPalettePreset\"");
        xaml.Should().Contain("x:Name=\"ComboSettingsGlowAcousticProfile\"");

        // 3. PageSettings contains real application configuration sections
        xaml.Should().Contain("x:Name=\"PageSettings\"");
        xaml.Should().Contain("x:Name=\"SectionGeneral\"");
        xaml.Should().Contain("x:Name=\"SectionSystem\"");
        xaml.Should().Contain("x:Name=\"SectionSettingsArtwork\"");
        xaml.Should().Contain("x:Name=\"SectionSettingsAudio\"");
        xaml.Should().Contain("x:Name=\"SectionUpdates\"");
        xaml.Should().Contain("x:Name=\"SectionShortcuts\"");
        xaml.Should().Contain("x:Name=\"SectionAbout\"");
    }

    [Fact]
    public async Task Settings_Persistence_SavesAndRestoresAllConfigurationValues()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Lyrow_Stage24_SettingsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string settingsPath = Path.Combine(tempDir, "settings.json");

        try
        {
            var manager = new SettingsManager(settingsPath);
            manager.Current.Theme = ThemeMode.Dark;
            manager.Current.Language = "tr";
            manager.Current.MasterOpacity = 88.0;
            manager.Current.EnableAnimations = false;
            manager.Current.StartWithWindows = true;
            manager.Current.SystemTrayEnabled = true;
            manager.Current.NotificationsEnabled = false;
            manager.Current.NotifyOnTrackChange = false;
            manager.Current.SeekStepSeconds = 15;
            manager.Current.AutoPauseOnMute = true;
            manager.Current.LyricsLineSpacing = 24;
            manager.Current.LyricsAlignment = "Center";
            manager.Current.LyricsBackdropOpacity = 92;
            manager.Current.GlowAcousticProfile = GlowAcousticProfile.BassBoost;
            manager.Current.ArtworkBackgroundSaturation = 1.35;
            manager.Current.ArtworkCornerRadius = 18.0;
            manager.Current.Glow.FftSize = 4096;

            await manager.SaveAsync(manager.Current);

            // Load in fresh manager instance
            var freshManager = new SettingsManager(settingsPath);
            var loaded = await freshManager.LoadAsync();

            loaded.Theme.Should().Be(ThemeMode.Dark);
            loaded.Language.Should().Be("tr");
            loaded.MasterOpacity.Should().Be(88.0);
            loaded.EnableAnimations.Should().BeFalse();
            loaded.StartWithWindows.Should().BeTrue();
            loaded.SystemTrayEnabled.Should().BeTrue();
            loaded.NotificationsEnabled.Should().BeFalse();
            loaded.NotifyOnTrackChange.Should().BeFalse();
            loaded.SeekStepSeconds.Should().Be(15);
            loaded.AutoPauseOnMute.Should().BeTrue();
            loaded.LyricsLineSpacing.Should().Be(24);
            loaded.LyricsAlignment.Should().Be("Center");
            loaded.LyricsBackdropOpacity.Should().Be(92);
            loaded.GlowAcousticProfile.Should().Be(GlowAcousticProfile.BassBoost);
            loaded.ArtworkBackgroundSaturation.Should().Be(1.35);
            loaded.ArtworkCornerRadius.Should().Be(18.0);
            loaded.Glow.FftSize.Should().Be(4096);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Settings_ThemeBehavior_AppliesSystemDarkAndLightModes()
    {
        var settings = LyrowSettings.CreateDefault();
        settings.Theme.Should().Be(ThemeMode.SystemDefault);

        settings.Theme = ThemeMode.Dark;
        settings.Theme.Should().Be(ThemeMode.Dark);

        settings.Theme = ThemeMode.Light;
        settings.Theme.Should().Be(ThemeMode.Light);

        // Verify ThemeMode values are valid enum numbers
        Enum.IsDefined(typeof(ThemeMode), ThemeMode.SystemDefault).Should().BeTrue();
        Enum.IsDefined(typeof(ThemeMode), ThemeMode.Dark).Should().BeTrue();
        Enum.IsDefined(typeof(ThemeMode), ThemeMode.Light).Should().BeTrue();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Settings_LocalizationBehavior_AllLanguagesProvideSettingsKeys(string langCode)
    {
        var loc = LocalizationService.Instance;
        loc.SetLanguage(langCode);
        loc.CurrentLanguage.Should().Be(langCode);

        // Core navigation and settings keys must be localized
        loc["Nav_Home"].Should().NotBeNullOrWhiteSpace();
        loc["Nav_Playback"].Should().NotBeNullOrWhiteSpace();
        loc["Nav_Glow"].Should().NotBeNullOrWhiteSpace();
        loc["Nav_Lyrics"].Should().NotBeNullOrWhiteSpace();
        loc["Settings_Title"].Should().NotBeNullOrWhiteSpace();
        loc["Tab_All"].Should().NotBeNullOrWhiteSpace();
        loc["Tab_Appearance"].Should().NotBeNullOrWhiteSpace();
        loc["General_AppTheme"].Should().NotBeNullOrWhiteSpace();
        loc["General_Language"].Should().NotBeNullOrWhiteSpace();
        loc["General_Startup"].Should().NotBeNullOrWhiteSpace();
        loc["General_SystemTray"].Should().NotBeNullOrWhiteSpace();

        // Reset to default en
        loc.SetLanguage("en");
    }
}
