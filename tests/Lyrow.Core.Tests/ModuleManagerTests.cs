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
        var lumn = new LumnModule();

        manager.RegisterModule(lumn);

        manager.RegisteredModules.Should().ContainSingle(m => m.ModuleId == "lumn");
        manager.GetModule("lumn").Should().BeSameAs(lumn);
    }

    [Fact]
    public void RegisterModule_ShouldThrow_WhenDuplicateId()
    {
        var manager = new ModuleManager();
        manager.RegisterModule(new LumnModule());

        var act = () => manager.RegisterModule(new LumnModule());

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ModuleExecutionMode.BothDisabled, false, false)]
    [InlineData(ModuleExecutionMode.LumnOnly, true, false)]
    [InlineData(ModuleExecutionMode.VerciOnly, false, true)]
    [InlineData(ModuleExecutionMode.BothEnabled, true, true)]
    public async Task ApplyExecutionMode_ShouldCorrectlyActivateModules_ForFourDiscreteModes(
        ModuleExecutionMode targetMode,
        bool expectLumnRunning,
        bool expectVerciRunning)
    {
        var manager = new ModuleManager();
        var lumn = new LumnModule();
        var verci = new VerciModule();

        manager.RegisterModule(lumn);
        manager.RegisterModule(verci);

        await manager.ApplyExecutionModeAsync(targetMode);

        manager.CurrentExecutionMode.Should().Be(targetMode);
        (lumn.State == ModuleState.Running).Should().Be(expectLumnRunning);
        (verci.State == ModuleState.Running).Should().Be(expectVerciRunning);
    }

    [Fact]
    public async Task ModuleStateChanged_EventShouldFire_WhenStateTransitions()
    {
        var manager = new ModuleManager();
        var lumn = new LumnModule();
        manager.RegisterModule(lumn);

        var transitionEvents = new List<ModuleStateChangedEventArgs>();
        manager.ModuleStateChanged += (_, args) => transitionEvents.Add(args);

        await manager.SetModuleActiveAsync("lumn", true);

        transitionEvents.Should().NotBeEmpty();
        transitionEvents.Last().NewState.Should().Be(ModuleState.Running);

        await manager.SetModuleActiveAsync("lumn", false);
        transitionEvents.Last().NewState.Should().Be(ModuleState.Stopped);
    }

    [Fact]
    public async Task ShutdownAllAsync_ShouldStopAllActiveModules()
    {
        var manager = new ModuleManager();
        var lumn = new LumnModule();
        var verci = new VerciModule();
        manager.RegisterModule(lumn);
        manager.RegisterModule(verci);

        await manager.ApplyExecutionModeAsync(ModuleExecutionMode.BothEnabled);
        manager.CurrentExecutionMode.Should().Be(ModuleExecutionMode.BothEnabled);

        await manager.ShutdownAllAsync();

        manager.CurrentExecutionMode.Should().Be(ModuleExecutionMode.BothDisabled);
        lumn.State.Should().Be(ModuleState.Stopped);
        verci.State.Should().Be(ModuleState.Stopped);
    }

    [Fact]
    public async Task FaultedModule_ShouldNotHaltOtherModules()
    {
        var manager = new ModuleManager();
        var failingModule = new FaultyMockModule("faulty", "Failing Module");
        var healthyModule = new LumnModule();

        manager.RegisterModule(failingModule);
        manager.RegisterModule(healthyModule);

        var stateChanges = new List<ModuleStateChangedEventArgs>();
        manager.ModuleStateChanged += (_, args) => stateChanges.Add(args);

        // Act - Start failing module, then healthy module
        await manager.SetModuleActiveAsync("faulty", true);
        await manager.SetModuleActiveAsync("lumn", true);

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
