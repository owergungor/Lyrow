using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class WindowLifecycleTests
{
    private sealed record SampleLifecycleEvent(string Name, int Value);

    [Fact]
    public async Task EventBus_SafeUnsubscribe_DoesNotThrowOnNullOrMultipleUnsubscribes()
    {
        var eventBus = new InMemoryEventBus();
        int callCount = 0;

        var sub = eventBus.Subscribe<SampleLifecycleEvent>(_ => callCount++);

        await eventBus.PublishAsync(new SampleLifecycleEvent("Test", 1));
        callCount.Should().Be(1);

        // First disposal
        sub.Dispose();
        await eventBus.PublishAsync(new SampleLifecycleEvent("Test 2", 2));
        callCount.Should().Be(1);

        // Second disposal (idempotent)
        var action = () => sub.Dispose();
        action.Should().NotThrow();
    }

    [Fact]
    public async Task ModuleManager_LifecycleStateTransitions_StartAndStopCleanly()
    {
        var moduleManager = new ModuleManager();

        var mockModule = new MockLyrowModule("mock_lifecycle", "Mock Lifecycle Module");
        moduleManager.RegisterModule(mockModule);

        mockModule.State.Should().Be(ModuleState.Initialized);

        await moduleManager.SetModuleActiveAsync("mock_lifecycle", true);
        mockModule.State.Should().Be(ModuleState.Running);

        await moduleManager.SetModuleActiveAsync("mock_lifecycle", false);
        mockModule.State.Should().Be(ModuleState.Stopped);
    }

    private sealed class MockLyrowModule : ILyrowModule
    {
        public string ModuleId { get; }
        public string DisplayName { get; }
        public ModuleState State { get; private set; } = ModuleState.Initialized;
        public Exception? LastFault => null;

        public MockLyrowModule(string id, string name)
        {
            ModuleId = id;
            DisplayName = name;
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            State = ModuleState.Initialized;
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            State = ModuleState.Running;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            State = ModuleState.Stopped;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            State = ModuleState.Stopped;
            return ValueTask.CompletedTask;
        }
    }
}
