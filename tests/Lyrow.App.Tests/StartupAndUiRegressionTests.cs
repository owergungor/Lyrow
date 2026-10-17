using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class StartupAndUiRegressionTests
{
    [Fact]
    public void LocalDiagnosticLogger_WritesLogsToLocalAppdata_WithoutCrashing()
    {
        LocalDiagnosticLogger.LogInfo("Test info log entry");
        LocalDiagnosticLogger.LogWarning("Test warning log entry");
        LocalDiagnosticLogger.LogError("Test error log entry", new InvalidOperationException("Test inner exception"));

        File.Exists(LocalDiagnosticLogger.LogFilePath).Should().BeTrue();
        string content = File.ReadAllText(LocalDiagnosticLogger.LogFilePath);
        content.Should().Contain("Test info log entry");
        content.Should().Contain("Test error log entry");
        content.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public void SettingsManager_LoadsDefaultSettings_CleanStartup()
    {
        var manager = new SettingsManager();
        var settings = manager.Current;

        settings.Should().NotBeNull();
        settings.LumnEnabled.Should().BeTrue();
        settings.VerciEnabled.Should().BeTrue();
        settings.MinimizeToTray.Should().BeTrue();
        settings.Hotkeys.ToggleLumn.Should().Be("Ctrl+Shift+L");
        settings.Hotkeys.ToggleVerci.Should().Be("Ctrl+Shift+V");
        settings.Hotkeys.ToggleClickThrough.Should().Be("Ctrl+Shift+C");
    }

    [Fact]
    public async Task SettingsManager_PersistsCustomPreferences_AcrossRestarts()
    {
        var manager = new SettingsManager();
        await manager.UpdateAsync(s =>
        {
            s.Theme = ThemeMode.Dark;
            s.Verci.SyncOffsetMs = 250;
            s.Lumn.Sensitivity = 1.8f;
        });

        manager.Current.Theme.Should().Be(ThemeMode.Dark);
        manager.Current.Verci.SyncOffsetMs.Should().Be(250);
        manager.Current.Lumn.Sensitivity.Should().Be(1.8f);
    }

    [Fact]
    public void ModuleManager_TerminologyVerification_GlowAndLyrics()
    {
        var moduleManager = new ModuleManager();
        var lumn = new LumnModule();
        var verci = new VerciModule();

        moduleManager.RegisterModule(lumn);
        moduleManager.RegisterModule(verci);

        verci.DisplayName.Should().Contain("Lyrics");
        verci.ModuleId.Should().Be("verci");
        lumn.ModuleId.Should().Be("lumn");
    }
}
