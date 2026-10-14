using Lyrow.Core.Contracts;

namespace Lyrow.Core.Modules;

/// <summary>
/// Core implementation of the Verci module managing synchronized lyrics and music metadata overlay.
/// </summary>
public sealed class VerciModule : ILyrowModule
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private CancellationTokenSource? _activeCts;

    public string ModuleId => "verci";
    public string DisplayName => "Verci (Synced Lyrics & Metadata)";
    public ModuleState State { get; private set; } = ModuleState.Uninitialized;
    public Exception? LastFault { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != ModuleState.Uninitialized) return;

            // Prepare metadata monitor and lyrics cache in Stage 4
            State = ModuleState.Initialized;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == ModuleState.Running) return;

            State = ModuleState.Starting;
            _activeCts = new CancellationTokenSource();

            // Active lyrics synchronization loop will be attached in Stage 4
            State = ModuleState.Running;
        }
        catch (Exception ex)
        {
            LastFault = ex;
            State = ModuleState.Faulted;
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != ModuleState.Running && State != ModuleState.Starting) return;

            State = ModuleState.Stopping;
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = null;

            State = ModuleState.Stopped;
        }
        catch (Exception ex)
        {
            LastFault = ex;
            State = ModuleState.Faulted;
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (State == ModuleState.Running)
        {
            await StopAsync().ConfigureAwait(false);
        }
        _stateLock.Dispose();
        _activeCts?.Dispose();
    }
}
