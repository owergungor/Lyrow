using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lyrow.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? MainWindowInstance { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            LocalDiagnosticLogger.LogInfo("Initializing Lyrow application framework and DI container...");
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            // Initialize settings and apply saved theme
            var settingsManager = Services.GetRequiredService<ISettingsManager>();
            var settings = settingsManager.LoadAsync().GetAwaiter().GetResult();
            ApplyTheme(settings.Theme);

            // Register core modules into module manager
            var moduleManager = Services.GetRequiredService<IModuleManager>();
            var lumn = Services.GetRequiredService<LumnModule>();
            var verci = Services.GetRequiredService<VerciModule>();

            moduleManager.RegisterModule(lumn);
            moduleManager.RegisterModule(verci);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

                var platformAdapter = Services.GetRequiredService<IWindowPlatformAdapter>();
                var startupManager = Services.GetRequiredService<IWindowsStartupManager>();
                var hotkeyManager = Services.GetRequiredService<IGlobalHotkeyManager>();
                var trayIconManager = Services.GetRequiredService<Lyrow.App.Platform.LyrowTrayIconManager>();

                var mainWindow = new MainWindow(
                    moduleManager,
                    settingsManager,
                    platformAdapter,
                    startupManager,
                    hotkeyManager,
                    trayIconManager);

                desktop.MainWindow = mainWindow;
                MainWindowInstance = mainWindow;
                desktop.Exit += OnAppExit;

                if (settings.SystemTrayEnabled)
                {
                    try
                    {
                        trayIconManager.Initialize(
                            onShowMainWindow: () => mainWindow.ShowAndActivate(),
                            onToggleLumn: () => mainWindow.ToggleLumnFromExternal(),
                            onToggleVerci: () => mainWindow.ToggleVerciFromExternal(),
                            onToggleClickThrough: () => mainWindow.ToggleClickThroughFromExternal(),
                            onOpenSettings: () => mainWindow.ShowAndActivate(),
                            onExit: () => mainWindow.RequestExit());
                    }
                    catch (Exception ex)
                    {
                        LocalDiagnosticLogger.LogError("Tray initialization non-fatal error", ex);
                    }
                }

                // Explicitly show and activate main window on application startup
                mainWindow.Show();
                mainWindow.Activate();
                LocalDiagnosticLogger.LogInfo("MainWindow created and displayed successfully.");

                // Start modules asynchronously in the background so UI never stalls or fails to open
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var startupMode = (settings.LumnEnabled, settings.VerciEnabled) switch
                        {
                            (false, false) => ModuleExecutionMode.BothDisabled,
                            (true, false) => ModuleExecutionMode.LumnOnly,
                            (false, true) => ModuleExecutionMode.VerciOnly,
                            (true, true) => ModuleExecutionMode.BothEnabled
                        };

                        LocalDiagnosticLogger.LogInfo($"Applying startup execution mode: {startupMode}");
                        await moduleManager.ApplyExecutionModeAsync(startupMode).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LocalDiagnosticLogger.LogError("Module startup background error", ex);
                    }
                });
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Fatal error in OnFrameworkInitializationCompleted", ex);
            throw;
        }
    }

    private static void OnAppExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            LocalDiagnosticLogger.LogInfo("Application exit requested, performing cleanup...");
            var hotkeys = Services?.GetService<IGlobalHotkeyManager>();
            hotkeys?.Dispose();

            var tray = Services?.GetService<Lyrow.App.Platform.LyrowTrayIconManager>();
            tray?.Dispose();

            var moduleManager = Services?.GetService<IModuleManager>();
            if (moduleManager != null)
            {
                moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothDisabled).GetAwaiter().GetResult();
            }

            var lumn = Services?.GetService<LumnModule>();
            lumn?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            var verci = Services?.GetService<VerciModule>();
            verci?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            var wasapi = Services?.GetService<IAudioCaptureService>();
            wasapi?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            var gsmtc = Services?.GetService<IMediaSessionMonitor>();
            gsmtc?.DisposeAsync().AsTask().GetAwaiter().GetResult();

            (Services as IDisposable)?.Dispose();
            LocalDiagnosticLogger.LogInfo("Application cleanup complete.");
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

        // Audio pipeline services
        services.AddSingleton<IAudioCaptureService>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new Lyrow.Audio.Capture.WasapiAudioCaptureService();
            }
            return new Lyrow.Audio.Capture.SyntheticAudioCaptureService();
        });
        services.AddSingleton<ISpectrumAnalyzer>(sp => new Lyrow.Audio.Analysis.SpectrumAnalyzer(fftSize: 2048, barCount: 32));

        services.AddSingleton<LumnModule>(sp => new LumnModule(
            sp.GetRequiredService<IAudioCaptureService>(),
            sp.GetRequiredService<ISpectrumAnalyzer>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISettingsManager>()));

        // Music & Lyrics pipeline services
        services.AddSingleton<IMediaSessionMonitor>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new Lyrow.Music.Session.WindowsGsmtcMediaSessionMonitor();
            }
            return new Lyrow.Music.Session.SyntheticMediaSessionMonitor();
        });
        services.AddSingleton<ILyricsClient, Lyrow.Music.Lyrics.LrclibClient>();
        services.AddSingleton<ILyricsCache, Lyrow.Music.Lyrics.LocalFileLyricsCache>();

        services.AddSingleton<VerciModule>(sp => new VerciModule(
            sp.GetRequiredService<IMediaSessionMonitor>(),
            sp.GetRequiredService<ILyricsClient>(),
            sp.GetRequiredService<ILyricsCache>(),
            sp.GetRequiredService<IEventBus>(),
            sp.GetRequiredService<ISettingsManager>()));

        services.AddSingleton<IWindowPlatformAdapter>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new Lyrow.App.Platform.WindowsWindowPlatformAdapter();
            }
            return new Lyrow.App.Platform.SyntheticWindowPlatformAdapter();
        });

        services.AddSingleton<IWindowsStartupManager>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new Lyrow.App.Platform.WindowsStartupManager();
            }
            return new Lyrow.App.Platform.SyntheticStartupManager();
        });

        services.AddSingleton<IGlobalHotkeyManager>(sp =>
        {
            if (OperatingSystem.IsWindows())
            {
                return new Lyrow.App.Platform.WindowsGlobalHotkeyManager();
            }
            return new Lyrow.App.Platform.SyntheticGlobalHotkeyManager();
        });

        services.AddSingleton<Lyrow.App.Platform.LyrowTrayIconManager>();
    }

    public static void ApplyTheme(ThemeMode theme)
    {
        if (Current == null) return;

        Current.RequestedThemeVariant = theme switch
        {
            ThemeMode.Dark => ThemeVariant.Dark,
            ThemeMode.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }
}