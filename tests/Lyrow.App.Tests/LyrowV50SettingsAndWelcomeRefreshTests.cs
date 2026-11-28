using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV50SettingsAndWelcomeRefreshTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task Hotkeys_ValidateConflictsInvalidCombos_AndPersistAcrossReload()
    {
        // Invalid single-key or bad modifier rejected
        Assert.False(HotkeyValidator.IsValidGesture("G", out _));
        Assert.False(HotkeyValidator.IsValidGesture("Foo+G", out _));
        Assert.True(HotkeyValidator.IsValidGesture("Ctrl+Alt+G", out _));
        Assert.True(HotkeyValidator.IsValidGesture(string.Empty, out _));

        // Internal collision rejected
        var colliding = new HotkeySettings { ToggleGlow = "Ctrl+Alt+G", ToggleLyrics = "alt+ctrl+g" };
        Assert.False(HotkeyValidator.CheckInternalCollisions(colliding, out _));

        // Valid persistence across SettingsManager reload
        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_hk_{Guid.NewGuid():N}.json");
        try
        {
            var sm1 = new SettingsManager(tempFile);
            await sm1.LoadAsync();
            sm1.Current.Hotkeys.ToggleGlow = HotkeyValidator.NormalizeGesture("shift+ctrl+g");
            sm1.Current.Hotkeys.ToggleLyrics = HotkeyValidator.NormalizeGesture("ctrl+alt+l");
            await sm1.SaveAsync(sm1.Current);

            var sm2 = new SettingsManager(tempFile);
            var reloaded = await sm2.LoadAsync();
            Assert.Equal("Ctrl+Shift+G", reloaded.Hotkeys.ToggleGlow);
            Assert.Equal("Alt+Ctrl+L", reloaded.Hotkeys.ToggleLyrics);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SettingsModal_HasOpaqueBackground_ZeroTopDeadSpace_AndOverviewFirstTab()
    {
        string appXaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "App.xaml"));
        string lyricsXaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));

        // Fully opaque Dark (#FF191A24) and Light (#FFFFFFFF) modal card surfaces
        Assert.Contains("x:Key=\"LyrowModalSurfaceBrush\" Color=\"#FF191A24\"", appXaml);
        Assert.Contains("x:Key=\"LyrowModalSurfaceBrush\" Color=\"#FFFFFFFF\"", appXaml);

        // Translucent Dark (#59000000) and Light (#400F172A) outer modal backdrop so main Lyrow UI remains visible
        Assert.Contains("x:Key=\"LyrowModalBackdropBrush\" Color=\"#59000000\"", appXaml);
        Assert.Contains("x:Key=\"LyrowModalBackdropBrush\" Color=\"#400F172A\"", appXaml);
        Assert.DoesNotContain("x:Key=\"LyrowModalBackdropBrush\" Color=\"#FF", appXaml);

        // Centered true modal separated from Windows title bar caption buttons
        Assert.Contains("x:Name=\"SettingsModalCard\"", lyricsXaml);
        Assert.Contains("VerticalAlignment=\"Center\"", lyricsXaml);
        Assert.Contains("Margin=\"24,44,24,24\"", lyricsXaml);
        Assert.Contains("TabFocusNavigation=\"Cycle\"", lyricsXaml);

        // Updates tab removed from sidebar and consolidated into About
        Assert.DoesNotContain("x:Name=\"BtnModalTabUpdates\"", lyricsXaml);
        Assert.DoesNotContain("x:Name=\"ModalSectionUpdates\"", lyricsXaml);
        int idxAbout = lyricsXaml.IndexOf("x:Name=\"ModalSectionAbout\"", StringComparison.Ordinal);
        int idxAutoUpdate = lyricsXaml.IndexOf("x:Name=\"SwitchModalAutoUpdate\"", StringComparison.Ordinal);
        Assert.True(idxAbout > 0 && idxAutoUpdate > idxAbout);

        // True modal background lock & Esc closing verified in code-behind
        string v4Cs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));
        string lyricsCs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));
        Assert.Contains("MainViewportArea.IsHitTestVisible = false;", v4Cs);
        Assert.Contains("MainViewportArea.IsHitTestVisible = true;", v4Cs);
        Assert.Contains("SettingsModalBackdrop.PointerPressed += (_, e) =>", v4Cs);
        Assert.DoesNotContain("SettingsModalBackdrop.PointerPressed += (_, _) => CloseSettingsModal();", v4Cs);
        Assert.Contains("CloseSettingsModal();", lyricsCs);

        // Overview tab is first and contains the 5 primary controls
        int idxOverview = lyricsXaml.IndexOf("x:Name=\"BtnModalTabOverview\"", StringComparison.Ordinal);
        int idxGeneral = lyricsXaml.IndexOf("x:Name=\"BtnModalTabGeneral\"", StringComparison.Ordinal);
        Assert.True(idxOverview > 0 && idxOverview < idxGeneral);

        Assert.Contains("x:Name=\"ComboOverviewMusicSensitivity\"", lyricsXaml);
        Assert.Contains("x:Name=\"SliderOverviewFocusedFontSize\"", lyricsXaml);
        Assert.Contains("x:Name=\"SliderOverviewUnfocusedFontSize\"", lyricsXaml);
        Assert.Contains("x:Name=\"TxtModalHotkeyGlow\"", lyricsXaml);
        Assert.Contains("x:Name=\"TxtModalHotkeyLyrics\"", lyricsXaml);
        Assert.Contains("x:Name=\"BtnOverviewCheckUpdates\"", lyricsXaml);

        // Show Welcome removed from General, kept only in About
        Assert.DoesNotContain("x:Name=\"BtnModalShowWelcome\"", lyricsXaml);
        Assert.Contains("x:Name=\"BtnModalShowWelcomeAbout\"", lyricsXaml);

        // Sync mode dropdown removed (always Automatic)
        Assert.DoesNotContain("x:Name=\"ComboModalLyricsSyncMode\"", lyricsXaml);
    }

    [Fact]
    public void AlbumArtworkCornerRadius_DefaultsTo24_AndSupports0To64Range()
    {
        var defaults = LyrowSettings.CreateDefault();
        Assert.Equal(24.0, defaults.ArtworkCornerRadius);

        string lyricsXaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        Assert.Contains("x:Name=\"SliderModalArtworkCornerRadius\" Minimum=\"0\" Maximum=\"64\" Value=\"24\"", lyricsXaml);
    }

    [Fact]
    public async Task Welcome_DynamicLanguageDropdown_AndMusicReactivitySensitivityPersist()
    {
        string welcomeXaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "WelcomeWindow.xaml"));
        string welcomeCs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "WelcomeWindow.xaml.cs"));

        // Dynamic dropdown populated from LocalizationService.SupportedLanguages (not hardcoded to 2 buttons)
        Assert.Contains("x:Name=\"ComboWelcomeLanguage\"", welcomeXaml);
        Assert.Contains("LocalizationService.SupportedLanguages", welcomeCs);
        Assert.True(LocalizationService.SupportedLanguages.Count >= 5);

        // User-friendly Music Reactivity Sensitivity cards (no FFT jargon in UI)
        Assert.Contains("x:Name=\"BtnWelcomeSensCalm\"", welcomeXaml);
        Assert.Contains("x:Name=\"BtnWelcomeSensBalanced\"", welcomeXaml);
        Assert.Contains("x:Name=\"BtnWelcomeSensExpressive\"", welcomeXaml);
        Assert.DoesNotContain("FFT", welcomeXaml);

        // Sensitivity selection persists via WelcomeOnboardingCoordinator
        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_welcome_sens_{Guid.NewGuid():N}.json");
        try
        {
            var sm = new SettingsManager(tempFile);
            await sm.LoadAsync();
            var coord = new WelcomeOnboardingCoordinator(sm);
            Assert.Equal(1024, coord.SelectedFftSize); // Default Expressive (Belirgin) on brand-new install
            coord.SelectedFftSize = 4096; // Calm (Sakin)
            coord.SelectedLanguage = "tr";
            await coord.CompleteAsync();

            var smReload = new SettingsManager(tempFile);
            var reloaded = await smReload.LoadAsync();
            Assert.Equal(4096, reloaded.Glow.FftSize);
            Assert.Equal("tr", reloaded.Language);
            Assert.True(reloaded.HasCompletedWelcome);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Localization_UserFriendlySensitivityAndOverviewExistInEnAndTr()
    {
        var loc = LocalizationService.Instance;
        loc.SetLanguage("en");
        Assert.Equal("Overview", loc["Tab_Overview"]);
        Assert.Equal("Music Reactivity Sensitivity", loc["Settings_MusicSensitivity"]);
        Assert.DoesNotContain("FFT", loc["Settings_MusicSensitivity"]);

        loc.SetLanguage("tr");
        Assert.Equal("Genel Bakış", loc["Tab_Overview"]);
        Assert.Equal("Müziğe Tepki Hassasiyeti", loc["Settings_MusicSensitivity"]);
        Assert.DoesNotContain("FFT", loc["Settings_MusicSensitivity"]);

        loc.SetLanguage("en");
    }
}
