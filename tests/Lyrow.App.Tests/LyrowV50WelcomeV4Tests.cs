using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV50WelcomeV4Tests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private sealed class FailingSaveSettingsManager : ISettingsManager
    {
        public LyrowSettings Current { get; private set; } = LyrowSettings.CreateDefault();
        public event EventHandler<LyrowSettings>? SettingsChanged { add { } remove { } }
        public Task<LyrowSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(LyrowSettings settings, CancellationToken cancellationToken = default)
            => throw new IOException("Simulated disk write failure");
        public Task UpdateAsync(Action<LyrowSettings> mutator, CancellationToken cancellationToken = default)
            => throw new IOException("Simulated disk write failure");
    }

    [Fact]
    public void WelcomeV4_HasFiveCanonicalTabs_Stepper_Progress_ResponsiveScroll_AndNoFftOrSyncJargon()
    {
        string xaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "WelcomeWindow.xaml"));
        string cs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "WelcomeWindow.xaml.cs"));

        // 5 tabs in exact order: Start, Appearance, Music Reactivity, Keyboard Shortcuts, Finish
        int t0 = xaml.IndexOf("x:Name=\"PanelWelcomeTabStart\"", StringComparison.Ordinal);
        int t1 = xaml.IndexOf("x:Name=\"PanelWelcomeTabAppearance\"", StringComparison.Ordinal);
        int t2 = xaml.IndexOf("x:Name=\"PanelWelcomeTabReactivity\"", StringComparison.Ordinal);
        int t3 = xaml.IndexOf("x:Name=\"PanelWelcomeTabShortcuts\"", StringComparison.Ordinal);
        int t4 = xaml.IndexOf("x:Name=\"PanelWelcomeTabFinish\"", StringComparison.Ordinal);
        Assert.True(t0 > 0 && t1 > t0 && t2 > t1 && t3 > t2 && t4 > t3);

        // Stepper buttons, progress bar, footer navigation, responsive ScrollViewer
        for (int i = 0; i < 5; i++)
        {
            Assert.Contains($"x:Name=\"BtnWelcomeTab{i}\"", xaml);
        }
        Assert.Contains("x:Name=\"WelcomeProgressBar\"", xaml);
        Assert.Contains("x:Name=\"BtnWelcomeBack\"", xaml);
        Assert.Contains("x:Name=\"BtnWelcomeNext\"", xaml);
        Assert.Contains("x:Name=\"BtnWelcomeUseDefaults\"", xaml);
        Assert.Contains("x:Name=\"WelcomeScrollViewer\"", xaml);
        Assert.Contains("WorkArea", cs);

        // No FFT jargon and no sync mode selector in Welcome V4
        Assert.DoesNotContain("FFT", xaml);
        Assert.DoesNotContain("SyncMode", xaml);

        // Scientifically accurate DSP mapping: 4096=Calm, 2048=Balanced, 1024=Expressive
        Assert.Contains("x:Name=\"BtnWelcomeSensCalm\" Grid.Row=\"0\" Grid.Column=\"0\" Tag=\"4096\"", xaml);
        Assert.Contains("x:Name=\"BtnWelcomeSensBalanced\" Grid.Row=\"0\" Grid.Column=\"1\" Tag=\"2048\"", xaml);
        Assert.Contains("x:Name=\"BtnWelcomeSensExpressive\" Grid.Row=\"0\" Grid.Column=\"2\" Tag=\"1024\"", xaml);

        // Overflow-proof responsive layout invariants
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", xaml);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml);
        Assert.Contains("x:Name=\"WelcomeStepperBar\" ColumnDefinitions=\"*,*,*,*,*\"", xaml);
        Assert.Contains("WelcomeRootLayout.SizeChanged +=", cs);
        Assert.Contains("WelcomeResponsiveLayoutCalculator.Compute(", cs);

        // Keystroke Recorder reused directly inside Welcome V4
        Assert.Contains("x:Name=\"BtnWelcomeHotkeyGlow\"", xaml);
        Assert.Contains("x:Name=\"BtnWelcomeHotkeyLyrics\"", xaml);
        Assert.Contains("KeystrokeRecorderSession", cs);
        Assert.Contains("button.PreviewKeyDown +=", cs);
        Assert.Contains("button.PreviewKeyUp +=", cs);
        Assert.Contains("button.LostFocus +=", cs);
        Assert.Contains("WindowActivationState.Deactivated", cs);
        Assert.Contains("_hotkeyTimeoutTimer", cs);
        Assert.Contains("hotkeyManager.UnregisterAll()", cs);

        // Summary fields and single launch CTA on Finish tab
        Assert.Contains("x:Name=\"TxtSummaryLanguageVal\"", xaml);
        Assert.Contains("x:Name=\"TxtSummaryThemeVal\"", xaml);
        Assert.Contains("x:Name=\"TxtSummaryEdgeLightVal\"", xaml);
        Assert.Contains("x:Name=\"TxtSummaryDynamicBgVal\"", xaml);
        Assert.Contains("x:Name=\"TxtSummaryReactivityVal\"", xaml);
        Assert.Contains("x:Name=\"TxtSummaryShortcutsVal\"", xaml);
        Assert.Contains("x:Name=\"TxtWelcomeSaveError\"", xaml);
        Assert.Contains("x:Name=\"BtnWelcomeStartListening\"", xaml);
    }

    [Fact]
    public void WelcomeV4_Coordinator_FreeTabNavigation_PreservesSelectionsAcrossSteps()
    {
        var sm = new SettingsManager(Path.Combine(Path.GetTempPath(), $"lyrow_v4nav_{Guid.NewGuid():N}.json"));
        var coord = new WelcomeOnboardingCoordinator(sm);

        Assert.Equal(WelcomeStep.Welcome, coord.CurrentStep);
        Assert.False(coord.CanGoBack);
        Assert.True(coord.CanContinue);

        // Jump freely to Tab 3, set shortcuts, jump back to Tab 0, change language & theme, step forward to Tab 4
        coord.GoToStep(WelcomeStep.Audio);
        coord.SelectedShortcutGlow = "Ctrl+Shift+G";
        coord.SelectedShortcutLyrics = "Ctrl+Shift+L";

        coord.GoToStep(WelcomeStep.Welcome);
        coord.SelectedLanguage = "tr";
        coord.SelectedTheme = ThemeMode.Light;

        Assert.True(coord.Continue());
        Assert.Equal(WelcomeStep.Appearance, coord.CurrentStep);
        coord.EnableEdgeLight = true;
        coord.EnableDynamicBackground = false;

        Assert.True(coord.Continue());
        Assert.Equal(WelcomeStep.Experience, coord.CurrentStep);
        coord.SelectedFftSize = 1024; // Expressive

        Assert.True(coord.Continue());
        Assert.Equal(WelcomeStep.Audio, coord.CurrentStep);
        Assert.True(coord.Continue());
        Assert.Equal(WelcomeStep.Ready, coord.CurrentStep);
        Assert.True(coord.IsReadyStep);
        Assert.False(coord.CanContinue);

        // All selections preserved across arbitrary tab navigation
        Assert.Equal("tr", coord.SelectedLanguage);
        Assert.Equal(ThemeMode.Light, coord.SelectedTheme);
        Assert.True(coord.EnableEdgeLight);
        Assert.False(coord.EnableDynamicBackground);
        Assert.Equal(1024, coord.SelectedFftSize);
        Assert.Equal("Ctrl+Shift+G", coord.SelectedShortcutGlow);
        Assert.Equal("Ctrl+Shift+L", coord.SelectedShortcutLyrics);

        Assert.True(coord.Back());
        Assert.Equal(WelcomeStep.Audio, coord.CurrentStep);
    }

    [Fact]
    public async Task WelcomeV4_LaunchLyrow_PersistsAllPreferences_AndSharesSingleSourceWithOverview()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_v4save_{Guid.NewGuid():N}.json");
        try
        {
            var sm = new SettingsManager(tempFile);
            await sm.LoadAsync();
            var coord = new WelcomeOnboardingCoordinator(sm);

            coord.SelectedLanguage = "tr";
            coord.SelectedTheme = ThemeMode.Dark;
            coord.EnableEdgeLight = true;
            coord.EnableDynamicBackground = true;
            coord.SelectedFftSize = 4096; // Calm
            coord.SelectedShortcutGlow = "Ctrl+Alt+K";
            coord.SelectedShortcutLyrics = "Ctrl+Shift+M";

            bool ok = await coord.CompleteAsync();
            Assert.True(ok);
            Assert.Null(coord.LastError);

            // Reload from disk (same source used by Overview settings)
            var smReload = new SettingsManager(tempFile);
            var reloaded = await smReload.LoadAsync();
            Assert.Equal("tr", reloaded.Language);
            Assert.Equal(ThemeMode.Dark, reloaded.Theme);
            Assert.True(reloaded.GlowEnabled);
            Assert.True(reloaded.VisualIntegrationEnabled);
            Assert.Equal(4096, reloaded.Glow.FftSize);
            Assert.Equal("Alt+Ctrl+K", reloaded.Hotkeys.ToggleGlow);
            Assert.Equal("Ctrl+Shift+M", reloaded.Hotkeys.ToggleLyrics);
            Assert.True(reloaded.HasCompletedWelcome);
            Assert.False(WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(reloaded));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WelcomeV4_ShortcutCollision_ResetDefaults_AndSaveFailureKeepWelcomeOpen()
    {
        // 1. Collision blocks CompleteAsync and preserves error
        var sm = new SettingsManager(Path.Combine(Path.GetTempPath(), $"lyrow_v4col_{Guid.NewGuid():N}.json"));
        var coord = new WelcomeOnboardingCoordinator(sm);
        coord.SelectedShortcutGlow = "Ctrl+Alt+G";
        coord.SelectedShortcutLyrics = "Alt+Ctrl+G";

        bool colResult = await coord.CompleteAsync();
        Assert.False(colResult);
        Assert.NotNull(coord.LastError);
        Assert.False(sm.Current.HasCompletedWelcome);

        // Reset shortcuts to defaults resolves collision
        coord.ResetShortcutsToDefaults();
        Assert.True(coord.ValidatePendingPreferences(out _));

        // 2. Save failure keeps Welcome open, rolls back in-memory settings, and reports error
        var failingSm = new FailingSaveSettingsManager();
        var failCoord = new WelcomeOnboardingCoordinator(failingSm);
        failCoord.SelectedLanguage = "tr";
        failCoord.SelectedTheme = ThemeMode.Light;
        failCoord.SelectedShortcutGlow = "Ctrl+Shift+Z";

        bool saveResult = await failCoord.CompleteAsync();
        Assert.False(saveResult);
        Assert.Contains("Simulated disk write failure", failCoord.LastError);
        Assert.False(failingSm.Current.HasCompletedWelcome);
        Assert.Equal(failCoord.InitialLanguage, failingSm.Current.Language);
        Assert.Equal(failCoord.InitialTheme, failingSm.Current.Theme);
    }

    [Fact]
    public async Task WelcomeV4_CloseOrEscapeDiscardsTemporaryEdits_AndPreservesMigrationForExistingUsers()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_v4esc_{Guid.NewGuid():N}.json");
        try
        {
            // Simulate existing v4.7 user who already completed Welcome
            var existingSm = new SettingsManager(tempFile);
            await existingSm.LoadAsync();
            existingSm.Current.Language = "en";
            existingSm.Current.Theme = ThemeMode.Dark;
            existingSm.Current.Glow.FftSize = 2048;
            existingSm.Current.Hotkeys.ToggleGlow = "Alt+Ctrl+G";
            existingSm.Current.HasCompletedWelcome = true;
            await existingSm.SaveAsync(existingSm.Current);

            // Existing user must NOT be forced into Welcome on upgrade/startup
            Assert.False(WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(existingSm.Current));

            // Manual reopen from Settings -> user makes temporary edits -> closes with ESC/X
            var reopenCoord = new WelcomeOnboardingCoordinator(existingSm, isManualReopen: true);
            reopenCoord.SelectedLanguage = "tr";
            reopenCoord.SelectedTheme = ThemeMode.Light;
            reopenCoord.SelectedFftSize = 1024;
            reopenCoord.SelectedShortcutGlow = "Ctrl+Shift+Q";

            await reopenCoord.CloseWindowAsync();

            // All pre-Welcome settings remain intact
            Assert.Equal("en", existingSm.Current.Language);
            Assert.Equal(ThemeMode.Dark, existingSm.Current.Theme);
            Assert.Equal(2048, existingSm.Current.Glow.FftSize);
            Assert.Equal("Alt+Ctrl+G", existingSm.Current.Hotkeys.ToggleGlow);
            Assert.True(existingSm.Current.HasCompletedWelcome);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void WelcomeV4_Localization_HasCompleteTurkishAndEnglishStrings()
    {
        var loc = LocalizationService.Instance;

        loc.SetLanguage("tr");
        Assert.Equal("Başlangıç", loc["WelcomeV4_Tab_Start"]);
        Assert.Equal("Görünüm", loc["WelcomeV4_Tab_Appearance"]);
        Assert.Equal("Müziğe Tepki", loc["WelcomeV4_Tab_Reactivity"]);
        Assert.Equal("Klavye Kısayolları", loc["WelcomeV4_Tab_Shortcuts"]);
        Assert.Equal("Tamamla", loc["WelcomeV4_Tab_Finish"]);
        Assert.Equal("Müziğe Tepki Hassasiyeti", loc["WelcomeV4_Reactivity_Title"]);
        Assert.Equal("Sakin", loc["WelcomeV4_Sens_Calm"]);
        Assert.Equal("Dengeli", loc["WelcomeV4_Sens_Balanced"]);
        Assert.Equal("Belirgin", loc["WelcomeV4_Sens_Expressive"]);
        Assert.Equal("Lyrow'u Başlat", loc["WelcomeV4_Btn_Launch"]);

        loc.SetLanguage("en");
        Assert.Equal("Start", loc["WelcomeV4_Tab_Start"]);
        Assert.Equal("Appearance", loc["WelcomeV4_Tab_Appearance"]);
        Assert.Equal("Music Reactivity", loc["WelcomeV4_Tab_Reactivity"]);
        Assert.Equal("Shortcuts", loc["WelcomeV4_Tab_Shortcuts"]);
        Assert.Equal("Finish", loc["WelcomeV4_Tab_Finish"]);
        Assert.Equal("Calm", loc["WelcomeV4_Sens_Calm"]);
        Assert.Equal("Balanced", loc["WelcomeV4_Sens_Balanced"]);
        Assert.Equal("Expressive", loc["WelcomeV4_Sens_Expressive"]);
        Assert.Equal("Launch Lyrow", loc["WelcomeV4_Btn_Launch"]);
    }

    [Theory]
    [InlineData(1332, 100)]
    [InlineData(1332, 125)]
    [InlineData(1332, 150)]
    [InlineData(1280, 100)]
    [InlineData(1280, 125)]
    [InlineData(1280, 150)]
    [InlineData(1366, 100)]
    [InlineData(1366, 125)]
    [InlineData(1366, 150)]
    [InlineData(1920, 100)]
    [InlineData(1920, 125)]
    [InlineData(1920, 150)]
    public void WelcomeV4_ResponsiveLayout_NeverOverflowsHorizontally_AcrossResolutionsAndDpi(int physicalWidth, int dpiPercent)
    {
        double dipWidth = WelcomeResponsiveLayoutCalculator.ToDipWidth(physicalWidth, dpiPercent);
        var metrics = WelcomeResponsiveLayoutCalculator.Compute(dipWidth);

        // Total horizontal footprint (content + left/right padding + scrollbar reserve) must never exceed viewport DIP width
        double totalFootprint = metrics.ContentMaxWidth + (metrics.HorizontalPadding * 2.0) + WelcomeResponsiveLayoutCalculator.ScrollbarReserveDip;
        Assert.True(totalFootprint <= dipWidth + 0.01,
            $"Overflow at {physicalWidth}px @ {dpiPercent}% ({dipWidth:F1} DIP): footprint {totalFootprint:F1} > viewport {dipWidth:F1}");

        // Language combo and shortcut recorder must fit comfortably inside ContentMaxWidth
        Assert.True(metrics.LanguageComboWidth < metrics.ContentMaxWidth);
        Assert.True(metrics.ShortcutRecorderMinWidth < metrics.ContentMaxWidth);
    }

    [Fact]
    public void WelcomeV4_ResponsiveLayout_StacksCardsAndRowsOnNarrowViewports_AndShowsThreeColumnsOnWide()
    {
        // Wide desktop viewport (e.g. 1332px @ 150% DPI = 888 DIP): 3 theme cards side-by-side without overflow
        var wide = WelcomeResponsiveLayoutCalculator.Compute(888);
        Assert.False(wide.StackThemeCards);
        Assert.False(wide.StackReactivityCards);
        Assert.False(wide.StackLanguageRow);
        Assert.False(wide.StackSummaryColumns);
        Assert.True(wide.ContentMaxWidth <= 760);

        // Narrow resized viewport (e.g. 520 DIP): stacks cards & language row cleanly so nothing clips
        var narrow = WelcomeResponsiveLayoutCalculator.Compute(520);
        Assert.True(narrow.UseCompactStepper);
        Assert.True(narrow.StackThemeCards);
        Assert.True(narrow.StackReactivityCards);
        Assert.True(narrow.StackLanguageRow);
        Assert.True(narrow.StackShortcutRows);
        Assert.True(narrow.StackSummaryColumns);
        Assert.True(narrow.ContentMaxWidth + (narrow.HorizontalPadding * 2) <= 520);
    }
}
