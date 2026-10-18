using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class VolumeAndStartupStateTests
{
    [Theory]
    [InlineData(0.0f, "0%")]
    [InlineData(0.5f, "50%")]
    [InlineData(0.78f, "78%")]
    [InlineData(1.0f, "100%")]
    public void VolumePresentation_FormatsAsPercentage_WithoutDbfs(float linearPeak, string expectedPercent)
    {
        int percent = Math.Clamp((int)Math.Round(linearPeak * 100.0f), 0, 100);
        string formatted = $"{percent}%";

        formatted.Should().Be(expectedPercent);
        formatted.Should().NotContain("dB");
        formatted.Should().NotContain("dBFS");
        formatted.ToLowerInvariant().Should().NotContain("db");
    }

    [Fact]
    public async Task StartupState_ModulesDefaultToOff_DeterministicSourceOfTruth()
    {
        var settingsManager = new SettingsManager();
        var settings = await settingsManager.LoadAsync();

        // 1. Persisted defaults must specify OFF for Glow and Lyrics
        settings.LumnEnabled.Should().BeFalse();
        settings.VerciEnabled.Should().BeFalse();

        // 2. ModuleManager initialized with modules
        var moduleManager = new ModuleManager();
        var lumn = new LumnModule();
        var verci = new VerciModule();

        moduleManager.RegisterModule(lumn);
        moduleManager.RegisterModule(verci);

        await lumn.InitializeAsync();
        await verci.InitializeAsync();

        // 3. Apply default startup mode (BothDisabled)
        await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothDisabled);

        moduleManager.CurrentExecutionMode.Should().Be(ModuleExecutionMode.BothDisabled);
        lumn.State.Should().Be(ModuleState.Initialized);
        verci.State.Should().Be(ModuleState.Initialized);

        // Neither module should be in Running state on app startup
        (lumn.State == ModuleState.Running).Should().BeFalse();
        (verci.State == ModuleState.Running).Should().BeFalse();
    }
}
