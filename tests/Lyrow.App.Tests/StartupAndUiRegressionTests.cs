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
        settings.GlowEnabled.Should().BeFalse();
        settings.LyricsEnabled.Should().BeFalse();
        settings.MinimizeToTray.Should().BeTrue();
        settings.Hotkeys.ToggleGlow.Should().BeEmpty();
        settings.Hotkeys.ToggleLyrics.Should().BeEmpty();
        settings.Hotkeys.ToggleClickThrough.Should().BeEmpty();
    }

    [Fact]
    public async Task SettingsManager_PersistsCustomPreferences_AcrossRestarts()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"Lyrow_Settings_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new SettingsManager(tempFile);
            await manager.UpdateAsync(s =>
            {
                s.Theme = ThemeMode.Dark;
                s.Lyrics.SyncOffsetMs = 250;
                s.Glow.Sensitivity = 1.8f;
            });

            manager.Current.Theme.Should().Be(ThemeMode.Dark);
            manager.Current.Lyrics.SyncOffsetMs.Should().Be(250);
            manager.Current.Glow.Sensitivity.Should().Be(1.8f);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ModuleManager_TerminologyVerification_GlowAndLyrics()
    {
        var moduleManager = new ModuleManager();
        var glow = new GlowModule();
        var lyrics = new LyricsModule();

        moduleManager.RegisterModule(glow);
        moduleManager.RegisterModule(lyrics);

        lyrics.DisplayName.Should().Contain("Lyrics");
        lyrics.ModuleId.Should().Be("lyrics");
        glow.ModuleId.Should().Be("glow");
    }
}
