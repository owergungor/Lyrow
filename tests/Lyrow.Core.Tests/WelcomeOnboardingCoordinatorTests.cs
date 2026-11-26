using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public sealed class WelcomeOnboardingCoordinatorTests
{
    private sealed class FakeAudioCaptureService : IAudioCaptureService
    {
        public bool IsCapturing { get; set; } = true;
        public AudioDeviceDescriptor? CurrentDevice { get; private set; }
        public IReadOnlyList<AudioDeviceDescriptor> Devices { get; set; } = new[]
        {
            new AudioDeviceDescriptor("dev-speakers", "Realtek Speakers", true, 48000, 2),
            new AudioDeviceDescriptor("dev-headphones", "Studio Headphones", false, 48000, 2)
        };
        public bool ThrowOnEnumerate { get; set; }
        public string? LastStartedDeviceId { get; private set; }

        public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable { add { } remove { } }
        public event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged { add { } remove { } }
        public event EventHandler<Exception>? CaptureFaulted { add { } remove { } }

        public IReadOnlyList<AudioDeviceDescriptor> GetAvailableDevices()
        {
            if (ThrowOnEnumerate) throw new InvalidOperationException("No audio endpoint subsystem");
            return Devices;
        }

        public Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default)
        {
            IsCapturing = true;
            LastStartedDeviceId = deviceId;
            CurrentDevice = Devices.FirstOrDefault(d => d.Id == deviceId);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsCapturing = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAudioRoutingService : IAudioRoutingService
    {
        public bool IsSupported => true;
        public string? LastRoutedDeviceId { get; private set; }
        public bool ThrowOnSet { get; set; }

        public bool SetDefaultPlaybackDevice(string deviceId)
        {
            if (ThrowOnSet) throw new InvalidOperationException("COM routing unavailable");
            LastRoutedDeviceId = deviceId;
            return true;
        }
    }

    private static string CreateTempSettingsPath()
        => Path.Combine(Path.GetTempPath(), $"lyrow_welcome_test_{Guid.NewGuid():N}", "settings.json");

    private static void CleanupTempPath(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Scenario1_NewUserWithoutSettingsFile_SeesWelcomeScreen()
    {
        string path = CreateTempSettingsPath();
        try
        {
            var manager = new SettingsManager(path);
            var settings = await manager.LoadAsync();

            Assert.False(settings.HasCompletedWelcome);
            Assert.True(WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(settings, isSmokeTestMode: false));
            Assert.False(WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(settings, isSmokeTestMode: true));
        }
        finally
        {
            CleanupTempPath(path);
        }
    }

    [Fact]
    public async Task Scenario2_ExistingV45UserWithSettingsFile_DoesNotSeeWelcome_AndKeepsAllSettings()
    {
        string path = CreateTempSettingsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Simulate a real v4.5 settings.json (which did NOT have HasCompletedWelcome)
            string v45Json = """
            {
              "Theme": 1,
              "Language": "tr",
              "GlowEnabled": true,
              "GlowIntensity": 90,
              "GlowThickness": 32,
              "VisualIntegrationEnabled": false
            }
            """;
            await File.WriteAllTextAsync(path, v45Json);

            var manager = new SettingsManager(path);
            var loaded = await manager.LoadAsync();

            Assert.True(loaded.HasCompletedWelcome, "Existing v4.5 user must be migrated to HasCompletedWelcome=true");
            Assert.False(WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(loaded));

            // Verify zero loss of existing user preferences
            Assert.Equal(ThemeMode.Dark, loaded.Theme);
            Assert.Equal("tr", loaded.Language);
            Assert.True(loaded.GlowEnabled);
            Assert.Equal(90, loaded.GlowIntensity);
            Assert.Equal(32, loaded.GlowThickness);
            Assert.False(loaded.VisualIntegrationEnabled);
        }
        finally
        {
            CleanupTempPath(path);
        }
    }

    [Fact]
    public async Task Scenario3And4_GetStartedContinueAndBackNavigation_PreservesUserSelections()
    {
        string path = CreateTempSettingsPath();
        try
        {
            var manager = new SettingsManager(path);
            await manager.LoadAsync();
            var coordinator = new WelcomeOnboardingCoordinator(manager);

            Assert.Equal(WelcomeStep.Welcome, coordinator.CurrentStep);
            Assert.False(coordinator.CanGoBack);

            // Step 1 -> Step 2 via GetStarted
            coordinator.GetStarted();
            Assert.Equal(WelcomeStep.Appearance, coordinator.CurrentStep);
            Assert.True(coordinator.CanGoBack);

            coordinator.SelectedTheme = ThemeMode.Light;
            coordinator.Continue();
            Assert.Equal(WelcomeStep.Experience, coordinator.CurrentStep);

            coordinator.EnableEdgeLight = true;
            coordinator.EnableDynamicBackground = false;
            coordinator.Continue();
            Assert.Equal(WelcomeStep.Audio, coordinator.CurrentStep);

            coordinator.SelectedAudioDeviceId = "dev-headphones";
            coordinator.Continue();
            Assert.Equal(WelcomeStep.Ready, coordinator.CurrentStep);
            Assert.True(coordinator.IsReadyStep);

            // Navigate Back all the way and verify every selection was preserved
            Assert.True(coordinator.Back());
            Assert.Equal(WelcomeStep.Audio, coordinator.CurrentStep);
            Assert.True(coordinator.Back());
            Assert.Equal(WelcomeStep.Experience, coordinator.CurrentStep);
            Assert.True(coordinator.Back());
            Assert.Equal(WelcomeStep.Appearance, coordinator.CurrentStep);

            Assert.Equal(ThemeMode.Light, coordinator.SelectedTheme);
            Assert.True(coordinator.EnableEdgeLight);
            Assert.False(coordinator.EnableDynamicBackground);
            Assert.Equal("dev-headphones", coordinator.SelectedAudioDeviceId);
        }
        finally
        {
            CleanupTempPath(path);
        }
    }

    [Fact]
    public async Task Scenario5_SkipSetup_DiscardsTemporarySelectionsKeepsDefaultsAndMarksCompleted()
    {
        string path = CreateTempSettingsPath();
        try
        {
            var manager = new SettingsManager(path);
            await manager.LoadAsync();
            var coordinator = new WelcomeOnboardingCoordinator(manager);

            coordinator.GetStarted();
            coordinator.SelectedLanguage = "tr";
            coordinator.SelectedTheme = ThemeMode.Dark;
            coordinator.EnableEdgeLight = true;
            coordinator.EnableDynamicBackground = false;

            WelcomeCompletionReason? reason = null;
            coordinator.OnboardingFinished += (_, r) => reason = r;

            bool ok = await coordinator.SkipAsync();

            Assert.True(ok);
            Assert.Equal(WelcomeCompletionReason.Skipped, reason);
            Assert.True(manager.Current.HasCompletedWelcome);
            Assert.Equal("en", manager.Current.Language); // Temporary edit discarded, default preserved
            Assert.Equal(ThemeMode.SystemDefault, manager.Current.Theme); // Temporary edit discarded, default preserved
            Assert.False(manager.Current.GlowEnabled); // Default preserved
            Assert.True(manager.Current.VisualIntegrationEnabled); // Default preserved
        }
        finally
        {
            CleanupTempPath(path);
        }
    }

    [Fact]
    public async Task Scenario6_8_9_10_11_StartListening_SavesThemeEdgeLightDynamicBgAndAudioSafely()
    {
        string path = CreateTempSettingsPath();
        try
        {
            var manager = new SettingsManager(path);
            await manager.LoadAsync();
            manager.Current.GlowThickness = 0; // Verify enabling Edge Light restores usable thickness

            var capture = new FakeAudioCaptureService();
            var routing = new FakeAudioRoutingService();
            var coordinator = new WelcomeOnboardingCoordinator(manager, capture, routing);

            await coordinator.RefreshAudioDevicesAsync();
            Assert.Equal(2, coordinator.AvailableAudioDevices.Count);

            coordinator.GetStarted();
            coordinator.SelectedTheme = ThemeMode.Dark;
            coordinator.Continue();
            coordinator.EnableEdgeLight = true;
            coordinator.EnableDynamicBackground = false;
            coordinator.Continue();
            coordinator.SelectedAudioDeviceId = "dev-headphones";
            coordinator.Continue();
            Assert.Equal(WelcomeStep.Ready, coordinator.CurrentStep);

            bool ok = await coordinator.CompleteAsync();
            await Task.Delay(60); // Allow non-blocking audio switch task to complete

            Assert.True(ok);
            Assert.True(manager.Current.HasCompletedWelcome);
            Assert.Equal(ThemeMode.Dark, manager.Current.Theme);
            Assert.True(manager.Current.GlowEnabled);
            Assert.Equal(24, manager.Current.GlowThickness);
            Assert.False(manager.Current.VisualIntegrationEnabled);
            Assert.Equal("dev-headphones", manager.Current.Glow.PreferredDeviceId);
            Assert.Equal("dev-headphones", routing.LastRoutedDeviceId);
            Assert.Equal("dev-headphones", capture.LastStartedDeviceId);
        }
        finally
        {
            CleanupTempPath(path);
        }
    }

    [Fact]
    public async Task Scenario7And14_WindowCloseDoesNotComplete_StartListeningPersistsAndNeverReshows()
    {
        string path = CreateTempSettingsPath();
        try
        {
            var firstRunManager = new SettingsManager(path);
            await firstRunManager.LoadAsync();
            var coordinator = new WelcomeOnboardingCoordinator(firstRunManager);
            coordinator.SelectedTheme = ThemeMode.Light;
            coordinator.SelectedLanguage = "tr";

            // Simulate user closing Welcome window via X / Escape without clicking Start Listening
            bool closedOk = await coordinator.CloseWindowAsync();
            Assert.True(closedOk);

            // Temporary edits must NOT be saved and Welcome must still show on next launch
            var afterCloseManager = new SettingsManager(path);
            var afterClose = await afterCloseManager.LoadAsync();
            Assert.False(afterClose.HasCompletedWelcome);
            Assert.True(WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(afterClose));
            Assert.Equal(ThemeMode.SystemDefault, afterClose.Theme);
            Assert.Equal("en", afterClose.Language);

            // Now user clicks Start Listening -> persists completion and never re-shows on restart
            var secondCoordinator = new WelcomeOnboardingCoordinator(afterCloseManager);
            Assert.True(await secondCoordinator.CompleteAsync());

            var restartedManager = new SettingsManager(path);
            var reloaded = await restartedManager.LoadAsync();
            Assert.True(reloaded.HasCompletedWelcome);
            Assert.False(WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(reloaded));
        }
        finally
        {
            CleanupTempPath(path);
        }
    }

    [Fact]
    public void Scenario12_TurkishAndEnglishWelcomeStrings_LoadCleanlyWithoutHardcoding()
    {
        var loc = LocalizationService.Instance;
        string originalLang = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("en");
            Assert.Equal("Welcome to Lyrow", loc["Welcome_Step1_Title"]);
            Assert.Equal("Your music. Your atmosphere.", loc["Welcome_Step1_Subtitle"]);
            Assert.Equal("Get Started", loc["Welcome_Btn_GetStarted"]);
            Assert.Equal("Make Lyrow Yours", loc["Welcome_Step2_Title"]);
            Assert.Equal("Shape Your Experience", loc["Welcome_Step3_Title"]);
            Assert.Equal("Audio Experience", loc["Welcome_Step4_Title"]);
            Assert.Equal("You're All Set!", loc["Welcome_Step5_Title"]);
            Assert.Equal("Start Listening", loc["Welcome_Btn_StartListening"]);
            Assert.Equal("Skip Setup", loc["Welcome_Btn_Skip"]);
            Assert.Equal("Show Welcome Screen", loc["Settings_ShowWelcome"]);

            loc.SetLanguage("tr");
            Assert.Equal("Lyrow'a Hoş Geldiniz", loc["Welcome_Step1_Title"]);
            Assert.Equal("Müziğin. Atmosferin.", loc["Welcome_Step1_Subtitle"]);
            Assert.Equal("Başlayalım", loc["Welcome_Btn_GetStarted"]);
            Assert.Equal("Lyrow'u Kişiselleştir", loc["Welcome_Step2_Title"]);
            Assert.Equal("Deneyimini Şekillendir", loc["Welcome_Step3_Title"]);
            Assert.Equal("Ses Deneyimi", loc["Welcome_Step4_Title"]);
            Assert.Equal("Her Şey Hazır!", loc["Welcome_Step5_Title"]);
            Assert.Equal("Dinlemeye Başla", loc["Welcome_Btn_StartListening"]);
            Assert.Equal("Kurulumu Atla", loc["Welcome_Btn_Skip"]);
            Assert.Equal("Karşılama Ekranını Göster", loc["Settings_ShowWelcome"]);
        }
        finally
        {
            loc.SetLanguage(originalLang);
        }
    }

    [Fact]
    public async Task Scenario13_ManualReopenFromSettings_PreselectsExistingPreferencesWithoutOverwriting()
    {
        string path = CreateTempSettingsPath();
        try
        {
            var manager = new SettingsManager(path);
            manager.Current.HasCompletedWelcome = true;
            manager.Current.Theme = ThemeMode.Light;
            manager.Current.GlowEnabled = true;
            manager.Current.VisualIntegrationEnabled = false;
            manager.Current.Glow.PreferredDeviceId = "dev-speakers";
            await manager.SaveAsync(manager.Current);

            var reopenCoordinator = new WelcomeOnboardingCoordinator(manager, isManualReopen: true);

            Assert.True(reopenCoordinator.IsManualReopen);
            Assert.Equal(ThemeMode.Light, reopenCoordinator.SelectedTheme);
            Assert.True(reopenCoordinator.EnableEdgeLight);
            Assert.False(reopenCoordinator.EnableDynamicBackground);
            Assert.Equal("dev-speakers", reopenCoordinator.SelectedAudioDeviceId);

            // Change one preference and complete
            reopenCoordinator.EnableDynamicBackground = true;
            await reopenCoordinator.CompleteAsync();

            Assert.Equal(ThemeMode.Light, manager.Current.Theme);
            Assert.True(manager.Current.GlowEnabled);
            Assert.True(manager.Current.VisualIntegrationEnabled);
        }
        finally
        {
            CleanupTempPath(path);
        }
    }

    [Fact]
    public async Task Scenario15_FaultyAudioSubsystemAndConcurrentCompletion_NeverDeadlocksOrThrows()
    {
        string path = CreateTempSettingsPath();
        try
        {
            var manager = new SettingsManager(path);
            await manager.LoadAsync();
            var faultyCapture = new FakeAudioCaptureService { ThrowOnEnumerate = true };
            var faultyRouting = new FakeAudioRoutingService { ThrowOnSet = true };
            var coordinator = new WelcomeOnboardingCoordinator(manager, faultyCapture, faultyRouting);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await coordinator.RefreshAudioDevicesAsync(cts.Token);
            Assert.Empty(coordinator.AvailableAudioDevices);
            Assert.False(coordinator.HasAudioDevices);

            coordinator.SelectedAudioDeviceId = "nonexistent-device";

            // Fire concurrent finalize calls — single-flight guard must resolve cleanly without deadlock
            var tasks = Enumerable.Range(0, 5).Select(_ => coordinator.CompleteAsync(cts.Token)).ToArray();
            bool[] results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(r => r));
            Assert.True(manager.Current.HasCompletedWelcome);
        }
        finally
        {
            CleanupTempPath(path);
        }
    }
}
