using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public sealed class ModuleManagerTests
{
    [Fact]
    public void RegisterModule_ShouldAddModule_WhenUnique()
    {
        var manager = new ModuleManager();
        var glow = new GlowModule();

        manager.RegisterModule(glow);

        manager.RegisteredModules.Should().ContainSingle(m => m.ModuleId == "glow");
        manager.GetModule("glow").Should().BeSameAs(glow);
    }

    [Fact]
    public void RegisterModule_ShouldThrow_WhenDuplicateId()
    {
        var manager = new ModuleManager();
        manager.RegisterModule(new GlowModule());

        var act = () => manager.RegisterModule(new GlowModule());

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ModuleExecutionMode.BothDisabled, false, false)]
    [InlineData(ModuleExecutionMode.GlowOnly, true, false)]
    [InlineData(ModuleExecutionMode.LyricsOnly, false, true)]
    [InlineData(ModuleExecutionMode.BothEnabled, true, true)]
    public async Task ApplyExecutionMode_ShouldCorrectlyActivateModules_ForFourDiscreteModes(
        ModuleExecutionMode targetMode,
        bool expectGlowRunning,
        bool expectLyricsRunning)
    {
        var manager = new ModuleManager();
        var glow = new GlowModule();
        var lyrics = new LyricsModule();

        manager.RegisterModule(glow);
        manager.RegisterModule(lyrics);

        await manager.ApplyExecutionModeAsync(targetMode);

        manager.CurrentExecutionMode.Should().Be(targetMode);
        (glow.State == ModuleState.Running).Should().Be(expectGlowRunning);
        (lyrics.State == ModuleState.Running).Should().Be(expectLyricsRunning);
    }

    [Fact]
    public async Task ModuleStateChanged_EventShouldFire_WhenStateTransitions()
    {
        var manager = new ModuleManager();
        var glow = new GlowModule();
        manager.RegisterModule(glow);

        var transitionEvents = new List<ModuleStateChangedEventArgs>();
        manager.ModuleStateChanged += (_, args) => transitionEvents.Add(args);

        await manager.SetModuleActiveAsync("glow", true);

        transitionEvents.Should().NotBeEmpty();
        transitionEvents.Last().NewState.Should().Be(ModuleState.Running);

        await manager.SetModuleActiveAsync("glow", false);
        transitionEvents.Last().NewState.Should().Be(ModuleState.Stopped);
    }

    [Fact]
    public async Task ShutdownAllAsync_ShouldStopAllActiveModules()
    {
        var manager = new ModuleManager();
        var glow = new GlowModule();
        var lyrics = new LyricsModule();
        manager.RegisterModule(glow);
        manager.RegisterModule(lyrics);

        await manager.ApplyExecutionModeAsync(ModuleExecutionMode.BothEnabled);
        manager.CurrentExecutionMode.Should().Be(ModuleExecutionMode.BothEnabled);

        await manager.ShutdownAllAsync();

        manager.CurrentExecutionMode.Should().Be(ModuleExecutionMode.BothDisabled);
        glow.State.Should().Be(ModuleState.Stopped);
        lyrics.State.Should().Be(ModuleState.Stopped);
    }

    [Fact]
    public async Task FaultedModule_ShouldNotHaltOtherModules()
    {
        var manager = new ModuleManager();
        var failingModule = new FaultyMockModule("faulty", "Failing Module");
        var healthyModule = new GlowModule();

        manager.RegisterModule(failingModule);
        manager.RegisterModule(healthyModule);

        var stateChanges = new List<ModuleStateChangedEventArgs>();
        manager.ModuleStateChanged += (_, args) => stateChanges.Add(args);

        // Act - Start failing module, then healthy module
        await manager.SetModuleActiveAsync("faulty", true);
        await manager.SetModuleActiveAsync("glow", true);

        // Assert
        failingModule.State.Should().Be(ModuleState.Faulted);
        healthyModule.State.Should().Be(ModuleState.Running);
        stateChanges.Should().Contain(e => e.ModuleId == "faulty" && e.NewState == ModuleState.Faulted);
    }

    private sealed class FaultyMockModule(string id, string name) : ILyrowModule
    {
        public string ModuleId { get; } = id;
        public string DisplayName { get; } = name;
        public ModuleState State { get; private set; } = ModuleState.Uninitialized;
        public Exception? LastFault { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            State = ModuleState.Initialized;
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            var ex = new InvalidOperationException("Simulated hardware capture crash.");
            LastFault = ex;
            State = ModuleState.Faulted;
            throw ex;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            State = ModuleState.Stopped;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
