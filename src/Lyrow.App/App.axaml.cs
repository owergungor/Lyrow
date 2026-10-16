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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Initialize settings and apply saved theme
        var settingsManager = Services.GetRequiredService<ISettingsManager>();
        var settings = await settingsManager.LoadAsync();
        ApplyTheme(settings.Theme);

        // Register core modules into module manager
        var moduleManager = Services.GetRequiredService<IModuleManager>();
        var lumn = Services.GetRequiredService<LumnModule>();
        var verci = Services.GetRequiredService<VerciModule>();

        moduleManager.RegisterModule(lumn);
        moduleManager.RegisterModule(verci);

        // Apply saved startup execution mode
        var startupMode = (settings.LumnEnabled, settings.VerciEnabled) switch
        {
            (false, false) => ModuleExecutionMode.BothDisabled,
            (true, false) => ModuleExecutionMode.LumnOnly,
            (false, true) => ModuleExecutionMode.VerciOnly,
            (true, true) => ModuleExecutionMode.BothEnabled
        };
        await moduleManager.ApplyExecutionModeAsync(startupMode);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(moduleManager, settingsManager);
            desktop.Exit += OnAppExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnAppExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
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
        }
        catch
        {
            // Suppress teardown exceptions during process exit
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