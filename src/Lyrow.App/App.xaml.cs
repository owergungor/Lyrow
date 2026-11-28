using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;

namespace Lyrow.App;

/// <summary>
/// Root WinUI 3 Application entry coordinating DI, module initialization,
/// and deterministic startup with Glow and Lyrics OFF by default.
/// </summary>
public partial class App : Application
{
    public const string CurrentReleaseVersion = "5.0";
    public static IServiceProvider Services { get; private set; } = null!;
    public static LyricsWindow? PrimaryWindowInstance { get; private set; }
    public static WelcomeWindow? ActiveWelcomeWindowInstance { get; internal set; }
    public static bool IsSmokeTestMode { get; set; }

    private const string AppGuid = "Lyrow_SingleInstance_f84b12c9-94b1-4c4f-9e63-47a61d15c89f";
    private const string WakeEventName = "Lyrow_WakeEvent_f84b12c9-94b1-4c4f-9e63-47a61d15c89f";
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _wakeEvent;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            IsSmokeTestMode = (!string.IsNullOrEmpty(args.Arguments) && args.Arguments.Contains("--smoke-test", StringComparison.OrdinalIgnoreCase))
                || Environment.GetCommandLineArgs().Any(a => a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase))
                || Environment.GetEnvironmentVariable("LYROW_SMOKE_TEST") == "1";
            bool isFirstInstance;
            try
            {
                _instanceMutex = new Mutex(true, AppGuid, out isFirstInstance);
            }
            catch
            {
                isFirstInstance = true;
            }

            if (!isFirstInstance)
            {
                try
                {
                    if (EventWaitHandle.TryOpenExisting(WakeEventName, out var existingWakeEvent))
                    {
                        existingWakeEvent.Set();
                        existingWakeEvent.Dispose();
                    }
                }
                catch { }
                Environment.Exit(0);
                return;
            }

            try
            {
                _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
                StartWakeListenerThread(_wakeEvent);
            }
            catch { }

            LocalDiagnosticLogger.LogInfo($"OnLaunched called. args.Arguments='{args.Arguments}', CmdLine='{string.Join(' ', Environment.GetCommandLineArgs())}', IsSmokeTestMode={IsSmokeTestMode}");
            LocalDiagnosticLogger.LogInfo("Initializing Lyrow v2.0 WinUI 3 application and DI container...");
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            var settingsManager = Services.GetRequiredService<ISettingsManager>();
            // Execute on thread-pool to avoid capturing WinUI DispatcherQueueSynchronizationContext
            var settings = Task.Run(() => settingsManager.LoadAsync()).GetAwaiter().GetResult();
            Lyrow.Core.Localization.LocalizationService.Instance.SetLanguage(settings.Language);

            // Register core modules into module manager
            var moduleManager = Services.GetRequiredService<IModuleManager>();
            var glow = Services.GetRequiredService<GlowModule>();
            var lyrics = Services.GetRequiredService<LyricsModule>();
            lyrics.KeepMediaMonitorAliveWhenStopped = true;

            moduleManager.RegisterModule(glow);
            moduleManager.RegisterModule(lyrics);

            var trayIconManager = Services.GetRequiredService<LyrowTrayIconManager>();
            var artworkBitmapManager = Services.GetRequiredService<ArtworkBitmapManager>();
            var mediaMonitor = Services.GetRequiredService<IMediaSessionMonitor>();
            var volumeService = Services.GetRequiredService<IAudioVolumeService>();
            var audioCaptureService = Services.GetRequiredService<IAudioCaptureService>();
            var audioRoutingService = Services.GetRequiredService<IAudioRoutingService>();
            var timelineEngine = Services.GetRequiredService<IPlaybackTimelineEngine>();

            if (PrimaryWindowInstance == null)
            {
                PrimaryWindowInstance = new LyricsWindow(
                    lyrics,
                    settingsManager,
                    artworkBitmapManager,
                    mediaMonitor,
                    volumeService,
                    toastManager: null,
                    audioCaptureService,
                    audioRoutingService,
                    timelineEngine);
            }

            var lyricsWindow = PrimaryWindowInstance;

            if (settings.SystemTrayEnabled)
            {
                try
                {
                    trayIconManager.Initialize(
                        onShowMainWindow: () => lyricsWindow.ShowAndActivate(),
                        onToggleGlow: () => lyricsWindow.ToggleEdgeGlowFromExternal(),
                        onToggleLyrics: () => lyricsWindow.ToggleLyricsPanelFromExternal(),
                        onToggleClickThrough: () => { },
                        onOpenSettings: () => lyricsWindow.DispatcherQueue.TryEnqueue(() =>
                        {
                            lyricsWindow.ShowAndActivate();
                            lyricsWindow.OpenSettingsModal();
                        }),
                        onExit: () => lyricsWindow.DispatcherQueue.TryEnqueue(() => lyricsWindow.RequestExit()));
                }
                catch (Exception ex)
                {
                    LocalDiagnosticLogger.LogError("Tray initialization non-fatal error", ex);
                }
            }

            lyricsWindow.ApplyTheme(settings.Theme);

            if (WelcomeOnboardingCoordinator.ShouldShowWelcomeOnStartup(settings, IsSmokeTestMode))
            {
                LocalDiagnosticLogger.LogInfo("First-run detected (HasCompletedWelcome=false): presenting Windows 11 Fluent WelcomeWindow.");
                var welcomeWindow = new WelcomeWindow(
                    settingsManager,
                    audioCaptureService,
                    audioRoutingService,
                    isManualReopen: false,
                    onFinishedCallback: reason =>
                    {
                        LocalDiagnosticLogger.LogInfo($"Welcome onboarding finished ({reason}); activating primary LyricsWindow.");
                        ActiveWelcomeWindowInstance = null;
                        lyricsWindow.DispatcherQueue.TryEnqueue(async () =>
                        {
                            await lyricsWindow.ApplyOnboardingPreferencesAsync();
                            lyricsWindow.ShowAndActivate();
                        });

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var mode = settingsManager.Current.GlowEnabled
                                    ? ModuleExecutionMode.BothEnabled
                                    : ModuleExecutionMode.LyricsOnly;
                                await moduleManager.ApplyExecutionModeAsync(mode).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                LocalDiagnosticLogger.LogError("Non-fatal error activating execution mode after Welcome", ex);
                            }
                        });
                    });

                ActiveWelcomeWindowInstance = welcomeWindow;
                welcomeWindow.Activate();
            }
            else
            {
                lyricsWindow.Activate();
                LocalDiagnosticLogger.LogInfo("Primary LyricsWindow created and displayed successfully.");

                // Start execution mode asynchronously off the WinUI UI thread to prevent WinRT/GSMTC UI-thread deadlock
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (settings.GlowEnabled)
                        {
                            LocalDiagnosticLogger.LogInfo("Startup: Activating BothEnabled mode asynchronously.");
                            await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothEnabled).ConfigureAwait(false);
                            lyricsWindow.DispatcherQueue.TryEnqueue(async () => await lyricsWindow.SetEdgeGlowStateAsync(true));
                        }
                        else
                        {
                            LocalDiagnosticLogger.LogInfo("Startup: Activating LyricsOnly mode asynchronously.");
                            await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.LyricsOnly).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        LocalDiagnosticLogger.LogError("Non-fatal error activating execution mode on startup", ex);
                    }
                });
            }

            if (IsSmokeTestMode)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1200).ConfigureAwait(false);
                    LocalDiagnosticLogger.LogInfo("Smoke test verified LyricsWindow startup; requesting clean exit (0).");
                    lyricsWindow.DispatcherQueue.TryEnqueue(() => lyricsWindow.RequestExit());
                });
            }
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Fatal error in WinUI OnLaunched", ex);
            throw;
        }
    }

    public static void ReleaseSingleInstanceLock()
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch { }
        try
        {
            _instanceMutex?.Dispose();
            _instanceMutex = null;
        }
        catch { }
        try
        {
            _wakeEvent?.Dispose();
            _wakeEvent = null;
        }
        catch { }
    }

    public static void ShutdownApp()
    {
        try
        {
            LocalDiagnosticLogger.LogInfo("Application shutdown requested, performing cleanup...");
            ReleaseSingleInstanceLock();
            var glowOverlay = Services?.GetService<GlowOverlayManager>();
            glowOverlay?.Dispose();

            var hotkeys = Services?.GetService<IGlobalHotkeyManager>();
            hotkeys?.Dispose();

            var tray = Services?.GetService<LyrowTrayIconManager>();
            tray?.Dispose();

            var volume = Services?.GetService<IAudioVolumeService>();
            volume?.Dispose();

            try
            {
                Task.Run(async () =>
                {
                    var moduleManager = Services?.GetService<IModuleManager>();
                    if (moduleManager != null)
                    {
                        await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothDisabled).ConfigureAwait(false);
                    }

                    var glow = Services?.GetService<GlowModule>();
                    if (glow != null) await glow.DisposeAsync().ConfigureAwait(false);

                    var lyrics = Services?.GetService<LyricsModule>();
                    if (lyrics != null) await lyrics.DisposeAsync().ConfigureAwait(false);

                    var wasapi = Services?.GetService<IAudioCaptureService>();
                    if (wasapi != null) await wasapi.DisposeAsync().ConfigureAwait(false);

                    var gsmtc = Services?.GetService<IMediaSessionMonitor>();
                    if (gsmtc != null) await gsmtc.DisposeAsync().ConfigureAwait(false);
                }).Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            try { if (Services is IAsyncDisposable ad) ad.DisposeAsync().AsTask().Wait(500); else (Services as IDisposable)?.Dispose(); } catch { }
            LocalDiagnosticLogger.LogInfo("Application cleanup complete.");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Error during application exit cleanup", ex);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<IModuleManager, ModuleManager>();
        services.AddSingleton<IUpdateService>(sp => new GitHubUpdateService(currentVersion: CurrentReleaseVersion));
        services.AddSingleton<UpdateCoordinator>(sp => new UpdateCoordinator(
            sp.GetRequiredService<IUpdateService>(),
            sp.GetRequiredService<ISettingsManager>()));
        services.AddTransient<WelcomeOnboardingCoordinator>(sp => new WelcomeOnboardingCoordinator(
            sp.GetRequiredService<ISettingsManager>(),
            sp.GetService<IAudioCaptureService>(),
            sp.GetService<IAudioRoutingService>()));

        // Audio capture & real Windows volume control services
        services.AddSingleton<IAudioCaptureService>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new Lyrow.Audio.Capture.WasapiAudioCaptureService();
            }
            return new Lyrow.Audio.Capture.SyntheticAudioCaptureService();
        });

        services.AddSingleton<IAudioVolumeService>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new Lyrow.Audio.Control.WindowsAudioVolumeService();
            }
            return new Lyrow.Audio.Control.SyntheticAudioVolumeService();
        });

        services.AddSingleton<ISpectrumAnalyzer>(sp => new Lyrow.Audio.Analysis.SpectrumAnalyzer(fftSize: 2048, barCount: 32));

        services.AddSingleton<IAudioRoutingService>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new Lyrow.Audio.Routing.WindowsAudioRoutingService();
            }
            return new Lyrow.Audio.Routing.SyntheticAudioRoutingService();
        });

        services.AddSingleton<GlowModule>(sp => new GlowModule(
            sp.GetRequiredService<IAudioCaptureService>(),
            sp.GetRequiredService<ISpectrumAnalyzer>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISettingsManager>()));

        // Music & Lyrics pipeline services
        services.AddSingleton<Lyrow.Core.Contracts.IPlaybackTimelineEngine, Lyrow.Core.Timeline.PlaybackTimelineEngine>();
        services.AddSingleton<IMediaMetadataNormalizer, Lyrow.Music.Metadata.MediaMetadataNormalizer>();
        services.AddSingleton<IMediaSessionMonitor>(sp =>
        {
            var normalizer = sp.GetRequiredService<IMediaMetadataNormalizer>();
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041, 0))
            {
                var timelineEngine = sp.GetRequiredService<Lyrow.Core.Contracts.IPlaybackTimelineEngine>();
                return new Lyrow.Music.Session.WindowsGsmtcMediaSessionMonitor(normalizer, timelineEngine);
            }
            return new Lyrow.Music.Session.SyntheticMediaSessionMonitor();
        });
        services.AddSingleton<Lyrow.Music.Lyrics.LrclibClient>(sp => new Lyrow.Music.Lyrics.LrclibClient(
            normalizer: sp.GetRequiredService<IMediaMetadataNormalizer>()));
        services.AddSingleton<Lyrow.Core.Lyrics.LyricsProviderPriorityResolver>();
        services.AddSingleton<Lyrow.Music.Lyrics.MultiProviderLyricsOrchestrator>(sp =>
        {
            var normalizer = sp.GetRequiredService<IMediaMetadataNormalizer>();
            var lrclib = sp.GetRequiredService<Lyrow.Music.Lyrics.LrclibClient>();
            var providers = new ILyricsSyncProvider[]
            {
                new Lyrow.Music.Lyrics.BetterLyricsClient(normalizer: normalizer),
                new Lyrow.Music.Lyrics.BetterLyricsUnisonClient(normalizer: normalizer),
                new Lyrow.Music.Lyrics.UnconfiguredSyncProvider(Lyrow.Core.Lyrics.LyricsProviderId.BiniLyrics),
                new Lyrow.Music.Lyrics.LrclibSyncProvider(lrclib),
                new Lyrow.Music.Lyrics.UnconfiguredSyncProvider(Lyrow.Core.Lyrics.LyricsProviderId.BetterLyricsPortato),
                new Lyrow.Music.Lyrics.UnconfiguredSyncProvider(Lyrow.Core.Lyrics.LyricsProviderId.BetterLyricsLegato)
            };
            var multiTrackProviders = new IMultiTrackLyricsProvider[]
            {
                new Lyrow.Music.Lyrics.LiriQoClient(normalizer: normalizer)
            };
            return new Lyrow.Music.Lyrics.MultiProviderLyricsOrchestrator(
                providers,
                sp.GetRequiredService<Lyrow.Core.Lyrics.LyricsProviderPriorityResolver>(),
                sp.GetRequiredService<ISettingsManager>(),
                multiTrackProviders: multiTrackProviders);
        });
        services.AddSingleton<IMultiProviderLyricsService>(sp => sp.GetRequiredService<Lyrow.Music.Lyrics.MultiProviderLyricsOrchestrator>());
        services.AddSingleton<ILyricsClient>(sp => sp.GetRequiredService<Lyrow.Music.Lyrics.MultiProviderLyricsOrchestrator>());
        services.AddSingleton<ILyricsCache, Lyrow.Music.Lyrics.LocalFileLyricsCache>();
        services.AddSingleton<IArtworkService, ArtworkService>();
        services.AddSingleton<ArtworkBitmapManager>();
        services.AddSingleton<IToastNotificationService, ToastNotificationService>();

        services.AddSingleton<LyricsModule>(sp => new LyricsModule(
            sp.GetRequiredService<IMediaSessionMonitor>(),
            sp.GetRequiredService<ILyricsClient>(),
            sp.GetRequiredService<ILyricsCache>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISettingsManager>(),
            sp.GetRequiredService<Lyrow.Core.Contracts.IPlaybackTimelineEngine>(),
            fallbackLyricsClient: sp.GetRequiredService<Lyrow.Music.Lyrics.LrclibClient>()));

        services.AddSingleton<IWindowPlatformAdapter>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsWindowPlatformAdapter();
            }
            return new SyntheticWindowPlatformAdapter();
        });

        services.AddSingleton<IWindowsStartupManager>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsStartupManager();
            }
            return new SyntheticStartupManager();
        });

        services.AddSingleton<IGlobalHotkeyManager>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsGlobalHotkeyManager();
            }
            return new SyntheticGlobalHotkeyManager();
        });

        services.AddSingleton<LyrowTrayIconManager>();

        services.AddSingleton<GlowOverlayManager>(sp => new GlowOverlayManager(
            sp.GetRequiredService<GlowModule>(),
            sp.GetRequiredService<ISettingsManager>()));
    }

    private static void StartWakeListenerThread(EventWaitHandle wakeEvent)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (wakeEvent.WaitOne())
                    {
                        LocalDiagnosticLogger.LogInfo("Wake signal received from secondary launch. Restoring primary LyricsWindow.");
                        PrimaryWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                        {
                            PrimaryWindowInstance?.ShowAndActivate();
                        });
                    }
                }
                catch
                {
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "LyrowSingleInstanceWakeListener"
        };
        thread.Start();
    }
}
