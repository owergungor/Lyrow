using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// Asynchronous non-blocking service for checking GitHub Releases for newer Lyrow versions.
/// Never auto-installs or closes the application without explicit user consent.
/// </summary>
public interface IUpdateService
{
    /// <summary>Gets the running two-part version string (e.g. "2.0").</summary>
    string CurrentVersion { get; }

    /// <summary>Gets the last update check result, if any.</summary>
    UpdateCheckResult? LastCheckResult { get; }

    /// <summary>Checks GitHub Releases for a newer version asynchronously without blocking UI.</summary>
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
