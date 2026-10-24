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
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? MainWindowInstance { get; private set; }
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
            var settings = settingsManager.LoadAsync().GetAwaiter().GetResult();

            // Register core modules into module manager
            var moduleManager = Services.GetRequiredService<IModuleManager>();
            var glow = Services.GetRequiredService<GlowModule>();
            var lyrics = Services.GetRequiredService<LyricsModule>();
            lyrics.KeepMediaMonitorAliveWhenStopped = true;

            moduleManager.RegisterModule(glow);
            moduleManager.RegisterModule(lyrics);

            // Deterministic default startup: Modules initialize into Stopped/OFF state
            LocalDiagnosticLogger.LogInfo("Deterministic startup: Glow and Lyrics modules default to OFF");
            moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothDisabled).GetAwaiter().GetResult();

            var platformAdapter = Services.GetRequiredService<IWindowPlatformAdapter>();
            var startupManager = Services.GetRequiredService<IWindowsStartupManager>();
            var hotkeyManager = Services.GetRequiredService<IGlobalHotkeyManager>();
            var trayIconManager = Services.GetRequiredService<LyrowTrayIconManager>();
            var glowOverlayManager = Services.GetRequiredService<GlowOverlayManager>();
            var artworkBitmapManager = Services.GetRequiredService<ArtworkBitmapManager>();
            var toastService = Services.GetRequiredService<IToastNotificationService>();
            var volumeService = Services.GetRequiredService<IAudioVolumeService>();
            var updateService = Services.GetRequiredService<IUpdateService>();

            var mainWindow = new MainWindow(
                moduleManager,
                settingsManager,
                platformAdapter,
                startupManager,
                hotkeyManager,
                trayIconManager,
                glowOverlayManager,
                artworkBitmapManager,
                toastService,
                volumeService,
                updateService);

            MainWindowInstance = mainWindow;

            if (settings.SystemTrayEnabled)
            {
                try
                {
                    trayIconManager.Initialize(
                        onShowMainWindow: () => mainWindow.ShowAndActivate(),
                        onToggleGlow: () => mainWindow.ToggleGlowFromExternal(),
                        onToggleLyrics: () => mainWindow.ToggleLyricsFromExternal(),
                        onToggleClickThrough: () => { },
                        onOpenSettings: () => mainWindow.ShowAndActivate(),
                        onExit: () => mainWindow.RequestExit());
                }
                catch (Exception ex)
                {
                    LocalDiagnosticLogger.LogError("Tray initialization non-fatal error", ex);
                }
            }

            mainWindow.ApplyTheme(settings.Theme);
            mainWindow.Activate();
            LocalDiagnosticLogger.LogInfo("MainWindow created and displayed successfully.");
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Fatal error in WinUI OnLaunched", ex);
            throw;
        }
    }

    public static void ShutdownApp()
    {
        try
        {
            LocalDiagnosticLogger.LogInfo("Application shutdown requested, performing cleanup...");
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

            (Services as IDisposable)?.Dispose();
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
        services.AddSingleton<IUpdateService>(sp => new GitHubUpdateService());

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

        services.AddSingleton<GlowModule>(sp => new GlowModule(
            sp.GetRequiredService<IAudioCaptureService>(),
            sp.GetRequiredService<ISpectrumAnalyzer>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISettingsManager>()));

        // Music & Lyrics pipeline services
        services.AddSingleton<IMediaSessionMonitor>(sp =>
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041, 0))
            {
                return new Lyrow.Music.Session.WindowsGsmtcMediaSessionMonitor();
            }
            return new Lyrow.Music.Session.SyntheticMediaSessionMonitor();
        });
        services.AddSingleton<ILyricsClient, Lyrow.Music.Lyrics.LrclibClient>();
        services.AddSingleton<ILyricsCache, Lyrow.Music.Lyrics.LocalFileLyricsCache>();
        services.AddSingleton<IArtworkService, ArtworkService>();
        services.AddSingleton<ArtworkBitmapManager>();
        services.AddSingleton<IToastNotificationService, ToastNotificationService>();

        services.AddSingleton<LyricsModule>(sp => new LyricsModule(
            sp.GetRequiredService<IMediaSessionMonitor>(),
            sp.GetRequiredService<ILyricsClient>(),
            sp.GetRequiredService<ILyricsCache>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISettingsManager>()));

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
                        LocalDiagnosticLogger.LogInfo("Wake signal received from secondary launch. Restoring main window.");
                        MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                        {
                            MainWindowInstance?.ShowAndActivate();
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
