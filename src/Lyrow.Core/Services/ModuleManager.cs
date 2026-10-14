using System.Collections.Concurrent;
using Lyrow.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyrow.Core.Services;

/// <summary>
/// Thread-safe module lifecycle coordinator managing the 4 discrete execution states.
/// </summary>
public sealed class ModuleManager : IModuleManager
{
    private readonly ConcurrentDictionary<string, ILyrowModule> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly ILogger<ModuleManager> _logger;

    public event EventHandler<ModuleStateChangedEventArgs>? ModuleStateChanged;

    public IReadOnlyCollection<ILyrowModule> RegisteredModules => _modules.Values.ToList();

    public ModuleExecutionMode CurrentExecutionMode
    {
        get
        {
            var lumnActive = IsModuleActive("lumn");
            var verciActive = IsModuleActive("verci");

            return (lumnActive, verciActive) switch
            {
                (false, false) => ModuleExecutionMode.BothDisabled,
                (true, false) => ModuleExecutionMode.LumnOnly,
                (false, true) => ModuleExecutionMode.VerciOnly,
                (true, true) => ModuleExecutionMode.BothEnabled
            };
        }
    }

    public ModuleManager(ILogger<ModuleManager>? logger = null)
    {
        _logger = logger ?? NullLogger<ModuleManager>.Instance;
    }

    public void RegisterModule(ILyrowModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!_modules.TryAdd(module.ModuleId, module))
        {
            throw new InvalidOperationException($"Module with ID '{module.ModuleId}' is already registered.");
        }
        _logger.LogInformation("Module '{ModuleId}' ({DisplayName}) registered.", module.ModuleId, module.DisplayName);
    }

    public ILyrowModule? GetModule(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        _modules.TryGetValue(moduleId, out var module);
        return module;
    }

    public async Task SetModuleActiveAsync(string moduleId, bool active, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_modules.TryGetValue(moduleId, out var module))
            {
                throw new KeyNotFoundException($"Module '{moduleId}' not found in registry.");
            }

            if (active)
            {
                await StartSingleModuleAsync(module, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await StopSingleModuleAsync(module, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task ApplyExecutionModeAsync(ModuleExecutionMode mode, CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Applying execution mode: {Mode}", mode);

            var (lumnTarget, verciTarget) = mode switch
            {
                ModuleExecutionMode.BothDisabled => (false, false),
                ModuleExecutionMode.LumnOnly => (true, false),
                ModuleExecutionMode.VerciOnly => (false, true),
                ModuleExecutionMode.BothEnabled => (true, true),
                _ => (false, false)
            };

            if (_modules.TryGetValue("lumn", out var lumn))
            {
                if (lumnTarget) await StartSingleModuleAsync(lumn, cancellationToken).ConfigureAwait(false);
                else await StopSingleModuleAsync(lumn, cancellationToken).ConfigureAwait(false);
            }

            if (_modules.TryGetValue("verci", out var verci))
            {
                if (verciTarget) await StartSingleModuleAsync(verci, cancellationToken).ConfigureAwait(false);
                else await StopSingleModuleAsync(verci, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    public async Task ShutdownAllAsync(CancellationToken cancellationToken = default)
    {
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Shutting down all active modules...");
            foreach (var module in _modules.Values)
            {
                await StopSingleModuleAsync(module, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private async Task StartSingleModuleAsync(ILyrowModule module, CancellationToken cancellationToken)
    {
        if (module.State == ModuleState.Running || module.State == ModuleState.Starting)
        {
            return;
        }

        var prev = module.State;
        try
        {
            if (module.State == ModuleState.Uninitialized)
            {
                await module.InitializeAsync(cancellationToken).ConfigureAwait(false);
                NotifyStateChanged(module.ModuleId, prev, module.State);
                prev = module.State;
            }

            await module.StartAsync(cancellationToken).ConfigureAwait(false);
            NotifyStateChanged(module.ModuleId, prev, module.State);
            _logger.LogInformation("Module '{ModuleId}' started successfully.", module.ModuleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start module '{ModuleId}'. Module transitioned to Faulted.", module.ModuleId);
            NotifyStateChanged(module.ModuleId, prev, ModuleState.Faulted, ex);
        }
    }

    private async Task StopSingleModuleAsync(ILyrowModule module, CancellationToken cancellationToken)
    {
        if (module.State == ModuleState.Stopped || module.State == ModuleState.Uninitialized)
        {
            return;
        }

        var prev = module.State;
        try
        {
            await module.StopAsync(cancellationToken).ConfigureAwait(false);
            NotifyStateChanged(module.ModuleId, prev, module.State);
            _logger.LogInformation("Module '{ModuleId}' stopped successfully.", module.ModuleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop module '{ModuleId}' cleanly.", module.ModuleId);
            NotifyStateChanged(module.ModuleId, prev, ModuleState.Faulted, ex);
        }
    }

    private bool IsModuleActive(string moduleId)
    {
        return _modules.TryGetValue(moduleId, out var m) && m.State == ModuleState.Running;
    }

    private void NotifyStateChanged(string moduleId, ModuleState prev, ModuleState current, Exception? fault = null)
    {
        ModuleStateChanged?.Invoke(this, new ModuleStateChangedEventArgs(moduleId, prev, current, fault));
    }
}
