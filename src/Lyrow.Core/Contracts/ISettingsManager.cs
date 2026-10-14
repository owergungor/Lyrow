using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// Service contract for loading, persisting and observing user settings.
/// </summary>
public interface ISettingsManager
{
    /// <summary>Gets the current in-memory settings snapshot.</summary>
    LyrowSettings Current { get; }

    /// <summary>Occurs when settings have been updated and persisted.</summary>
    event EventHandler<LyrowSettings>? SettingsChanged;

    /// <summary>Loads settings from persistent storage or returns default if not found.</summary>
    Task<LyrowSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the given settings atomically to disk.</summary>
    Task SaveAsync(LyrowSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Updates in-memory settings via an action and immediately persists.</summary>
    Task UpdateAsync(Action<LyrowSettings> configure, CancellationToken cancellationToken = default);
}
