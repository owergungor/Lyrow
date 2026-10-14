namespace Lyrow.Core.Contracts;

/// <summary>
/// The 4 fundamental execution configurations of the Lyrow product.
/// </summary>
public enum ModuleExecutionMode
{
    BothDisabled = 0,
    LumnOnly = 1,
    VerciOnly = 2,
    BothEnabled = 3
}

/// <summary>
/// Manages the registration, lifecycle and health of all Lyrow modules.
/// </summary>
public interface IModuleManager
{
    /// <summary>Gets all registered modules.</summary>
    IReadOnlyCollection<ILyrowModule> RegisteredModules { get; }

    /// <summary>Current combined execution mode of Lumn and Verci.</summary>
    ModuleExecutionMode CurrentExecutionMode { get; }

    /// <summary>Fired whenever any module changes its lifecycle state.</summary>
    event EventHandler<ModuleStateChangedEventArgs>? ModuleStateChanged;

    /// <summary>Registers a module with the manager.</summary>
    void RegisterModule(ILyrowModule module);

    /// <summary>Gets a registered module by its ID.</summary>
    ILyrowModule? GetModule(string moduleId);

    /// <summary>Enables or disables a specific module by ID.</summary>
    Task SetModuleActiveAsync(string moduleId, bool active, CancellationToken cancellationToken = default);

    /// <summary>Applies a specific 4-state execution mode.</summary>
    Task ApplyExecutionModeAsync(ModuleExecutionMode mode, CancellationToken cancellationToken = default);

    /// <summary>Gracefully shuts down all active modules.</summary>
    Task ShutdownAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Event arguments for module state changes.
/// </summary>
public sealed class ModuleStateChangedEventArgs(string moduleId, ModuleState previousState, ModuleState newState, Exception? fault = null) : EventArgs
{
    public string ModuleId { get; } = moduleId;
    public ModuleState PreviousState { get; } = previousState;
    public ModuleState NewState { get; } = newState;
    public Exception? Fault { get; } = fault;
}
