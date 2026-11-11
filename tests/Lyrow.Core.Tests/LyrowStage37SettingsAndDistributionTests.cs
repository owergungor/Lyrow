using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public sealed class LyrowStage37SettingsAndDistributionTests : IDisposable
{
    private readonly string _tempRoot;

    public LyrowStage37SettingsAndDistributionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Lyrow_Stage37_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void IsDirectoryWritable_ShouldReturnTrue_ForWritableDirectory()
    {
        bool writable = SettingsManager.IsDirectoryWritable(_tempRoot);
        writable.Should().BeTrue();
    }

    [Fact]
    public void IsDirectoryWritable_ShouldReturnFalse_ForNonExistentDirectory()
    {
        string fakeDir = Path.Combine(_tempRoot, "DoesNotExist_" + Guid.NewGuid().ToString("N"));
        bool writable = SettingsManager.IsDirectoryWritable(fakeDir);
        writable.Should().BeFalse();
    }

    [Fact]
    public void CreateDefaultSettings_ShouldHaveSafeDefaults_AndNotCarryDeveloperGuid()
    {
        var settings = LyrowSettings.CreateDefault();
        settings.Should().NotBeNull();
        settings.Theme.Should().Be(ThemeMode.SystemDefault);
        settings.GlowEnabled.Should().BeFalse();
        settings.LyricsEnabled.Should().BeFalse();
        settings.Glow.PreferredDeviceId.Should().BeNullOrEmpty("Clean defaults should not contain developer audio device GUID");
        settings.HasCompletedFirstRun.Should().BeFalse("Fresh installation must require first-run setup");
    }

    [Fact]
    public async Task SaveAsync_ShouldSaveCleanly_InWritableDirectory()
    {
        string settingsFile = Path.Combine(_tempRoot, "settings.json");
        var manager = new SettingsManager(settingsFile);

        var settings = LyrowSettings.CreateDefault();
        settings.Theme = ThemeMode.Dark;
        await manager.SaveAsync(settings);

        File.Exists(settingsFile).Should().BeTrue();
        var reloaded = await manager.LoadAsync();
        reloaded.Theme.Should().Be(ThemeMode.Dark);
    }
}
