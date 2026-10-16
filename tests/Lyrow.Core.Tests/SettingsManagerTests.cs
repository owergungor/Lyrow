using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public sealed class SettingsManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempFile;

    public SettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Lyrow_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempFile = Path.Combine(_tempDir, "settings.json");
    }

    [Fact]
    public async Task LoadAsync_ShouldReturnDefault_WhenFileDoesNotExist()
    {
        var manager = new SettingsManager(_tempFile);

        var settings = await manager.LoadAsync();

        settings.Should().NotBeNull();
        settings.Theme.Should().Be(ThemeMode.SystemDefault);
        settings.LumnEnabled.Should().BeTrue();
        settings.VerciEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_ShouldPersistSettingsAtomically_AndFireEvent()
    {
        var manager = new SettingsManager(_tempFile);
        var settingsChangedFired = false;
        manager.SettingsChanged += (_, _) => settingsChangedFired = true;

        var newSettings = new LyrowSettings
        {
            Theme = ThemeMode.Dark,
            LumnEnabled = false,
            VerciEnabled = true,
            MasterOpacity = 0.85,
            AccentColor = "#FF5500"
        };

        await manager.SaveAsync(newSettings);

        File.Exists(_tempFile).Should().BeTrue();
        settingsChangedFired.Should().BeTrue();

        // Reload using a fresh manager instance to verify round-trip persistence
        var reloadedManager = new SettingsManager(_tempFile);
        var reloaded = await reloadedManager.LoadAsync();

        reloaded.Theme.Should().Be(ThemeMode.Dark);
        reloaded.LumnEnabled.Should().BeFalse();
        reloaded.VerciEnabled.Should().BeTrue();
        reloaded.MasterOpacity.Should().Be(0.85);
        reloaded.AccentColor.Should().Be("#FF5500");
    }

    [Fact]
    public async Task LoadAsync_ShouldGracefullyFallbackToDefault_WhenJsonCorrupted()
    {
        await File.WriteAllTextAsync(_tempFile, "{ invalid json @@ ## broken");

        var manager = new SettingsManager(_tempFile);
        var loaded = await manager.LoadAsync();

        loaded.Should().NotBeNull();
        loaded.Theme.Should().Be(ThemeMode.SystemDefault);
    }

    [Fact]
    public async Task UpdateAsync_ShouldModifyAndPersistImmediately()
    {
        var manager = new SettingsManager(_tempFile);
        await manager.LoadAsync();

        await manager.UpdateAsync(s =>
        {
            s.Theme = ThemeMode.Light;
            s.ClickThroughEnabled = true;
        });

        manager.Current.Theme.Should().Be(ThemeMode.Light);
        manager.Current.ClickThroughEnabled.Should().BeTrue();

        var freshManager = new SettingsManager(_tempFile);
        var verified = await freshManager.LoadAsync();
        verified.Theme.Should().Be(ThemeMode.Light);
        verified.ClickThroughEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Settings_ShouldPersistNewStage6Properties()
    {
        var manager = new SettingsManager(_tempFile);
        await manager.LoadAsync();

        await manager.UpdateAsync(s =>
        {
            s.OverlayPosition = OverlayPosition.TopRight;
            s.TargetMonitorIndex = 1;
            s.AutoFetchLyrics = false;
        });

        var freshManager = new SettingsManager(_tempFile);
        var verified = await freshManager.LoadAsync();

        verified.OverlayPosition.Should().Be(OverlayPosition.TopRight);
        verified.TargetMonitorIndex.Should().Be(1);
        verified.AutoFetchLyrics.Should().BeFalse();
    }

    [Fact]
    public async Task Settings_ShouldMigrateLegacyJsonWithoutStage6Properties()
    {
        // Legacy JSON format without OverlayPosition, TargetMonitorIndex, AutoFetchLyrics
        string legacyJson = """
        {
            "Theme": 1,
            "LumnEnabled": true,
            "VerciEnabled": true,
            "ClickThroughEnabled": false,
            "MasterOpacity": 1.0,
            "AccentColor": "#4A90E2"
        }
        """;
        await File.WriteAllTextAsync(_tempFile, legacyJson);

        var manager = new SettingsManager(_tempFile);
        var loaded = await manager.LoadAsync();

        loaded.Theme.Should().Be(ThemeMode.Dark);
        // Default fallbacks should apply gracefully
        loaded.OverlayPosition.Should().Be(OverlayPosition.Center);
        loaded.TargetMonitorIndex.Should().Be(0);
        loaded.AutoFetchLyrics.Should().BeTrue();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }
}
