using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Comprehensive verification test suite for Stage 7: First-Run Experience & Windows Integration.
/// </summary>
public class Stage7VerificationTests
{
    [Fact]
    public void FirstRunWizard_InitialState_DefaultsToNotCompleted()
    {
        var settings = LyrowSettings.CreateDefault();
        settings.HasCompletedFirstRun.Should().BeFalse();
        settings.EnableAnimations.Should().BeTrue();
        settings.NotificationsEnabled.Should().BeTrue();
        settings.NotifyOnTrackChange.Should().BeTrue();
    }

    [Fact]
    public async Task FirstRunWizard_CompleteWizard_SetsFlagAndPersists()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_wizard_test_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new SettingsManager(tempFile);
            var settings = await manager.LoadAsync();
            settings.HasCompletedFirstRun.Should().BeFalse();

            // Simulate user completing setup wizard
            settings.HasCompletedFirstRun = true;
            settings.Theme = ThemeMode.Dark;
            settings.Language = "tr";
            settings.GlowEnabled = true;

            await manager.SaveAsync(settings);

            // Reload and verify
            var reloadManager = new SettingsManager(tempFile);
            var reloaded = await reloadManager.LoadAsync();

            reloaded.HasCompletedFirstRun.Should().BeTrue();
            reloaded.Theme.Should().Be(ThemeMode.Dark);
            reloaded.Language.Should().Be("tr");
            reloaded.GlowEnabled.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FirstRunWizard_SkipWizard_MarksCompletedWithDefaults()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_wizard_skip_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new SettingsManager(tempFile);
            var settings = await manager.LoadAsync();

            // User clicks "Skip Introduction"
            settings.HasCompletedFirstRun = true;
            await manager.SaveAsync(settings);

            var reloadManager = new SettingsManager(tempFile);
            var reloaded = await reloadManager.LoadAsync();

            reloaded.HasCompletedFirstRun.Should().BeTrue();
            reloaded.Theme.Should().Be(ThemeMode.SystemDefault);
            reloaded.Language.Should().Be("en");
            reloaded.GlowEnabled.Should().BeFalse(); // Defaults remain off
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FirstRunWizard_RestartWizard_AllowsReEnteringSetup()
    {
        var settings = LyrowSettings.CreateDefault();
        settings.HasCompletedFirstRun = true;

        // User triggers "Restart Setup Wizard" from Settings
        settings.HasCompletedFirstRun = false;
        settings.HasCompletedFirstRun.Should().BeFalse();
    }

    [Fact]
    public void StartWithWindows_SyntheticManager_TogglesCorrectly()
    {
        var synthetic = new SyntheticStartupManager();
        synthetic.IsSupported.Should().BeTrue();
        synthetic.IsStartupEnabled().Should().BeFalse();

        synthetic.SetStartupEnabled(true).Should().BeTrue();
        synthetic.IsStartupEnabled().Should().BeTrue();

        synthetic.SetStartupEnabled(false).Should().BeTrue();
        synthetic.IsStartupEnabled().Should().BeFalse();
    }

    [Fact]
    public void SingleInstance_WakeEvent_SignalsAndReceivesCleanly()
    {
        string wakeEventName = $"Lyrow_Wake_Test_{Guid.NewGuid():N}";
        using var wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, wakeEventName);

        bool signalReceived = false;
        var thread = new Thread(() =>
        {
            if (wakeEvent.WaitOne(TimeSpan.FromSeconds(2)))
            {
                signalReceived = true;
            }
        })
        {
            IsBackground = true
        };
        thread.Start();

        // Simulate second instance opening existing wake event and signaling
        using (var secondInstanceHandle = EventWaitHandle.OpenExisting(wakeEventName))
        {
            secondInstanceHandle.Set();
        }

        thread.Join(TimeSpan.FromSeconds(2));
        signalReceived.Should().BeTrue();
    }

    [Fact]
    public void SystemTray_ExtendedCommandDispatch_ExecutesAllCallbacks()
    {
        using var tray = new LyrowTrayIconManager();

        bool showTriggered = false;
        bool toggleGlowTriggered = false;
        bool toggleLyricsTriggered = false;
        bool settingsTriggered = false;
        bool exitTriggered = false;
        bool playPauseTriggered = false;
        bool prevTriggered = false;
        bool nextTriggered = false;

        tray.Initialize(
            onShowMainWindow: () => showTriggered = true,
            onToggleGlow: () => toggleGlowTriggered = true,
            onToggleLyrics: () => toggleLyricsTriggered = true,
            onToggleClickThrough: () => { },
            onOpenSettings: () => settingsTriggered = true,
            onExit: () => exitTriggered = true);

        tray.AttachExtendedHandlers(
            onPlayPause: () => playPauseTriggered = true,
            onPrev: () => prevTriggered = true,
            onNext: () => nextTriggered = true,
            onToggleMute: () => { },
            onOpenPlayback: () => { },
            onOpenGlow: () => { },
            onCheckUpdates: () => { },
            onToggleLyricsFullscreen: () => { });

        tray.TriggerShowMainWindow();
        tray.TriggerToggleGlow();
        tray.TriggerToggleLyrics();
        tray.TriggerOpenSettings();
        tray.TriggerPlayPause();
        tray.TriggerPrev();
        tray.TriggerNext();
        tray.TriggerExit();

        showTriggered.Should().BeTrue();
        toggleGlowTriggered.Should().BeTrue();
        toggleLyricsTriggered.Should().BeTrue();
        settingsTriggered.Should().BeTrue();
        playPauseTriggered.Should().BeTrue();
        prevTriggered.Should().BeTrue();
        nextTriggered.Should().BeTrue();
        exitTriggered.Should().BeTrue();
    }

    [Fact]
    public void WindowState_BoundsValidation_OnScreenBoundsPreserved()
    {
        var primaryScreen = new PixelRect(0, 0, 1920, 1080);
        var screens = new[] { primaryScreen };
        var candidate = new PixelRect(100, 100, 960, 720);

        var result = WindowStateManager.ValidateOrClampBounds(
            candidate,
            screens,
            primaryScreen,
            WindowStateManager.MinimumSize,
            WindowStateManager.DefaultNormalSize);

        result.X.Should().Be(100);
        result.Y.Should().Be(100);
        result.Width.Should().Be(960);
        result.Height.Should().Be(720);
    }

    [Fact]
    public void WindowState_BoundsValidation_MultiMonitorDisconnected_FallsBackToPrimaryCenter()
    {
        var primaryScreen = new PixelRect(0, 0, 1920, 1080);
        // Only primary screen is connected now (secondary monitor was unplugged)
        var screens = new[] { primaryScreen };

        // Candidate was previously placed on secondary monitor at X=2500, Y=300
        var offScreenCandidate = new PixelRect(2500, 300, 960, 720);

        var result = WindowStateManager.ValidateOrClampBounds(
            offScreenCandidate,
            screens,
            primaryScreen,
            WindowStateManager.MinimumSize,
            WindowStateManager.DefaultNormalSize);

        // Must be centered within primary screen
        int expectedX = (1920 - 960) / 2;
        int expectedY = (1080 - 720) / 2;

        result.X.Should().Be(expectedX);
        result.Y.Should().Be(expectedY);
        result.Width.Should().Be(960);
        result.Height.Should().Be(720);
    }

    [Fact]
    public void WindowState_BoundsValidation_ClampsMinimumDimensions()
    {
        var primaryScreen = new PixelRect(0, 0, 1920, 1080);
        var screens = new[] { primaryScreen };
        // User attempted or saved invalid 100x100 window size
        var tooSmallCandidate = new PixelRect(200, 200, 100, 100);

        var result = WindowStateManager.ValidateOrClampBounds(
            tooSmallCandidate,
            screens,
            primaryScreen,
            WindowStateManager.MinimumSize,
            WindowStateManager.DefaultNormalSize);

        result.Width.Should().BeGreaterThanOrEqualTo(WindowStateManager.MinimumSize.Width);
        result.Height.Should().BeGreaterThanOrEqualTo(WindowStateManager.MinimumSize.Height);
    }

    [Fact]
    public void NotificationPreferences_RoundtripAndDebounce()
    {
        var toastService = new ToastNotificationService();
        int receivedCount = 0;
        toastService.ToastReceived += (_, _) => receivedCount++;

        // Send two identical messages rapidly
        toastService.ShowInfo("Queen • Bohemian Rhapsody");
        toastService.ShowInfo("Queen • Bohemian Rhapsody");

        // Debounce drops duplicate within 1500ms
        receivedCount.Should().Be(1);

        // Different message passes
        toastService.ShowInfo("Pink Floyd • Comfortably Numb");
        receivedCount.Should().Be(2);
    }

    [Fact]
    public async Task Gsmtc_SyntheticMonitor_TrackTransitionAndCanSeek()
    {
        var monitor = new SyntheticMediaSessionMonitor();
        bool trackChangedFired = false;
        monitor.TrackChanged += (_, _) => trackChangedFired = true;

        await monitor.StartAsync();
        monitor.IsMonitoring.Should().BeTrue();

        monitor.SimulateTrack("Stairway to Heaven", "Led Zeppelin", "Led Zeppelin IV", TimeSpan.FromMinutes(8));
        trackChangedFired.Should().BeTrue();
        monitor.CurrentTrack.Title.Should().Be("Stairway to Heaven");
        monitor.CurrentTrack.Artist.Should().Be("Led Zeppelin");

        // Verify seeking
        bool seekSuccess = await monitor.TrySeekAsync(TimeSpan.FromSeconds(30));
        seekSuccess.Should().BeTrue();

        await monitor.StopAsync();
        monitor.IsMonitoring.Should().BeFalse();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("de")]
    public void Localization_Stage7WizardKeys_PresentInAllFiveLanguages(string cultureCode)
    {
        var loc = new LocalizationService();
        loc.SetLanguage(cultureCode);

        string[] stage7Keys =
        {
            "Wizard_Title", "Wizard_Slogan", "Wizard_Subtitle",
            "Wizard_GetStarted", "Wizard_Skip", "Wizard_Next", "Wizard_Back", "Wizard_Finish",
            "Wizard_Step1_Title", "Wizard_Step1_Desc", "Wizard_Step1_Theme", "Wizard_Step1_Animations",
            "Wizard_Step2_Title", "Wizard_Step2_Desc", "Wizard_Step2_SessionActive", "Wizard_Step2_SessionNone", "Wizard_Step2_Refresh",
            "Wizard_Step3_Title", "Wizard_Step3_Desc", "Wizard_Step3_Online", "Wizard_Step3_Offline",
            "Wizard_Step4_Title", "Wizard_Step4_Desc", "Wizard_Step4_Enable",
            "Wizard_Step5_Title", "Wizard_Step5_Desc",
            "Settings_RestartWizard", "Settings_RestartWizard_Desc",
            "General_Notifications", "General_NotifyOnTrackChange", "General_EnableAnimations"
        };

        foreach (var key in stage7Keys)
        {
            string text = loc.GetString(key);
            text.Should().NotBeNullOrWhiteSpace($"Key '{key}' must not be null or empty in {cultureCode}");
            text.Should().NotBe(key, $"Key '{key}' must have translation in {cultureCode}");
        }
    }

    [Fact]
    public async Task Settings_Stage7AllProperties_RoundTripPersist()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_stage7_settings_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new SettingsManager(tempFile);
            var s = LyrowSettings.CreateDefault();

            s.HasCompletedFirstRun = true;
            s.EnableAnimations = false;
            s.NotificationsEnabled = true;
            s.NotifyOnTrackChange = false;
            s.WindowX = 150;
            s.WindowY = 220;
            s.WindowWidth = 1024;
            s.WindowHeight = 768;
            s.IsCompactMode = true;
            s.IsMaximized = false;
            s.LastActivePage = "Lyrics";

            await manager.SaveAsync(s);

            var reloadManager = new SettingsManager(tempFile);
            var reloaded = await reloadManager.LoadAsync();

            reloaded.HasCompletedFirstRun.Should().BeTrue();
            reloaded.EnableAnimations.Should().BeFalse();
            reloaded.NotificationsEnabled.Should().BeTrue();
            reloaded.NotifyOnTrackChange.Should().BeFalse();
            reloaded.WindowX.Should().Be(150);
            reloaded.WindowY.Should().Be(220);
            reloaded.WindowWidth.Should().Be(1024);
            reloaded.WindowHeight.Should().Be(768);
            reloaded.IsCompactMode.Should().BeTrue();
            reloaded.IsMaximized.Should().BeFalse();
            reloaded.LastActivePage.Should().Be("Lyrics");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
