using System.Collections.Concurrent;
using Lyrow.Core.Contracts;

namespace Lyrow.Core.Services;

/// <summary>
/// Thread-safe in-memory event bus implementation for decoupled communication.
/// </summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, ConcurrentBag<Delegate>> _subscriptions = new();

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (!_subscriptions.TryGetValue(typeof(TEvent), out var handlers) || handlers.IsEmpty)
        {
            return Task.CompletedTask;
        }

        var tasks = new List<Task>();
        foreach (var handler in handlers)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            switch (handler)
            {
                case Func<TEvent, CancellationToken, Task> asyncFunc:
                    tasks.Add(SafeExecuteAsync(asyncFunc, @event, cancellationToken));
                    break;
                case Action<TEvent> syncAction:
                    SafeExecuteSync(syncAction, @event);
                    break;
            }
        }

        return tasks.Count > 0 ? Task.WhenAll(tasks) : Task.CompletedTask;
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        var bag = _subscriptions.GetOrAdd(typeof(TEvent), _ => new ConcurrentBag<Delegate>());
        bag.Add(handler);
        return new SubscriptionToken(() => RemoveHandler(typeof(TEvent), handler));
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        var bag = _subscriptions.GetOrAdd(typeof(TEvent), _ => new ConcurrentBag<Delegate>());
        bag.Add(handler);
        return new SubscriptionToken(() => RemoveHandler(typeof(TEvent), handler));
    }

    private void RemoveHandler(Type eventType, Delegate handler)
    {
        if (_subscriptions.TryGetValue(eventType, out var bag))
        {
            var remaining = bag.Where(d => !ReferenceEquals(d, handler)).ToList();
            _subscriptions[eventType] = new ConcurrentBag<Delegate>(remaining);
        }
    }

    private static async Task SafeExecuteAsync<TEvent>(Func<TEvent, CancellationToken, Task> func, TEvent evt, CancellationToken ct)
    {
        try
        {
            await func(evt, ct).ConfigureAwait(false);
        }
        catch
        {
            // Event bus handlers must not crash the event bus dispatch
        }
    }

    private static void SafeExecuteSync<TEvent>(Action<TEvent> action, TEvent evt)
    {
        try
        {
            action(evt);
        }
        catch
        {
            // Event bus handlers must not crash the event bus dispatch
        }
    }

    private sealed class SubscriptionToken(Action unsubscribeAction) : IDisposable
    {
        private Action? _action = unsubscribeAction;

        public void Dispose()
        {
            Interlocked.Exchange(ref _action, null)?.Invoke();
        }
    }
}
