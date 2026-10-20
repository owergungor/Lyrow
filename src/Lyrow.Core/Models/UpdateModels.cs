namespace Lyrow.Core.Models;

/// <summary>
/// Outcome status of an asynchronous update check.
/// </summary>
public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Offline,
    MalformedResponse,
    Failed
}

/// <summary>
/// Immutable result returned by <see cref="Contracts.IUpdateService.CheckForUpdatesAsync"/>.
/// </summary>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseTitle,
    string? ReleaseNotes,
    string? ReleaseUrl,
    string? InstallerDownloadUrl,
    DateTime CheckedAtUtc,
    string? ErrorMessage = null)
{
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable && !string.IsNullOrWhiteSpace(LatestVersion);
}
