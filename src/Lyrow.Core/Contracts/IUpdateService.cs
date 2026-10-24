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

    /// <summary>Downloads the update installer asset with progress reporting without opening a browser.</summary>
    Task<string> DownloadUpdatePackageAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Launches the downloaded installer and terminates the application cleanly.</summary>
    bool LaunchInstallerAndExit(string installerPath);

    /// <summary>Applies update silently in-app without wizard prompts, replaces binaries, and restarts the application.</summary>
    bool LaunchSilentUpdateAndRestart(string installerPath, string? appExecutablePath = null);
}
