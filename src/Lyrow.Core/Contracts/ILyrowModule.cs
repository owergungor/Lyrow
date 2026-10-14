namespace Lyrow.Core.Contracts;

/// <summary>
/// Represents the execution states of a Lyrow module.
/// </summary>
public enum ModuleState
{
    Uninitialized,
    Initialized,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted
}

/// <summary>
/// Contract for all independent functional modules in Lyrow.
/// </summary>
public interface ILyrowModule : IAsyncDisposable
{
    /// <summary>Unique identifier for the module (e.g. "lumn", "verci").</summary>
    string ModuleId { get; }

    /// <summary>User-facing display name.</summary>
    string DisplayName { get; }

    /// <summary>Current lifecycle execution state.</summary>
    ModuleState State { get; }

    /// <summary>Last fault exception if state is Faulted, otherwise null.</summary>
    Exception? LastFault { get; }

    /// <summary>Prepares resources, configuration and adapters before running.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts background processing, event subscriptions and UI presentation.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Gracefully stops background tasks and releases active hardware/UI resources.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
