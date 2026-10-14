namespace Lyrow.Core.Contracts;

/// <summary>
/// Lightweight, decoupled in-process event bus for communication between modules.
/// </summary>
public interface IEventBus
{
    /// <summary>Publishes an event to all registered asynchronous subscribers.</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class;

    /// <summary>Registers an asynchronous handler for a specific event type.</summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class;

    /// <summary>Registers a synchronous handler for a specific event type.</summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}
