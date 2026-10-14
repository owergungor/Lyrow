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
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<IModuleManager, ModuleManager>();
        services.AddSingleton<LumnModule>();
        services.AddSingleton<VerciModule>();
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