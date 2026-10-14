using FluentAssertions;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public sealed class EventBusTests
{
    private sealed record TestSampleEvent(string Message);

    [Fact]
    public async Task PublishAsync_ShouldDeliverEvent_ToAsyncSubscribers()
    {
        var bus = new InMemoryEventBus();
        var received = new List<string>();

        using var sub = bus.Subscribe<TestSampleEvent>((e, _) =>
        {
            received.Add(e.Message);
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new TestSampleEvent("Hello Lyrow"));

        received.Should().ContainSingle().Which.Should().Be("Hello Lyrow");
    }

    [Fact]
    public async Task PublishAsync_ShouldDeliverEvent_ToSyncSubscribers()
    {
        var bus = new InMemoryEventBus();
        var received = new List<string>();

        using var sub = bus.Subscribe<TestSampleEvent>(e => received.Add(e.Message));

        await bus.PublishAsync(new TestSampleEvent("Sync Message"));

        received.Should().ContainSingle().Which.Should().Be("Sync Message");
    }

    [Fact]
    public async Task DisposeSubscription_ShouldStopReceivingEvents()
    {
        var bus = new InMemoryEventBus();
        var count = 0;

        var sub = bus.Subscribe<TestSampleEvent>(_ => count++);

        await bus.PublishAsync(new TestSampleEvent("First"));
        count.Should().Be(1);

        sub.Dispose();

        await bus.PublishAsync(new TestSampleEvent("Second"));
        count.Should().Be(1);
    }

    [Fact]
    public async Task FailingSubscriber_ShouldNotPreventOtherSubscribersFromRunning()
    {
        var bus = new InMemoryEventBus();
        var successfulCount = 0;

        using var sub1 = bus.Subscribe<TestSampleEvent>(_ => throw new InvalidOperationException("Subscriber blew up"));
        using var sub2 = bus.Subscribe<TestSampleEvent>(_ => successfulCount++);

        var act = async () => await bus.PublishAsync(new TestSampleEvent("Resilient Event"));

        await act.Should().NotThrowAsync();
        successfulCount.Should().Be(1);
    }
}
