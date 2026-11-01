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
/// User-selected schedule frequency for checking GitHub Releases.
/// </summary>
public enum UpdateCheckFrequency
{
    OnStartup = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
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
    string? ErrorMessage = null,
    string? Sha256Checksum = null)
{
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable && !string.IsNullOrWhiteSpace(LatestVersion);
}

/// <summary>
/// Real-time progress snapshot for downloading self-update installer packages.
/// Contains bounded percentage [0..100] (or negative if indeterminate), actual bytes downloaded, and total file length if known.
/// </summary>
public readonly record struct UpdateDownloadProgress(double Percent, long BytesRead, long? TotalBytes)
{
    public bool IsIndeterminate => Percent < 0.0 || !TotalBytes.HasValue || TotalBytes.Value <= 0;
    public double MegabytesRead => BytesRead / (1024.0 * 1024.0);
    public double? TotalMegabytes => TotalBytes.HasValue ? TotalBytes.Value / (1024.0 * 1024.0) : null;
}
