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
    string? Sha256Checksum = null,
    DateTimeOffset? PublishedAtUtc = null)
{
    public bool IsUpdateAvailable => Status == UpdateCheckStatus.UpdateAvailable && !string.IsNullOrWhiteSpace(LatestVersion);
}

/// <summary>
/// Detected Windows distribution format of the running Lyrow instance.
/// </summary>
public enum AppDistributionKind
{
    InstalledSetup = 0,
    Portable = 1,
    MsixPackaged = 2
}

/// <summary>
/// Unified lifecycle state shared between the top-right update toast and Settings > Updates.
/// </summary>
public enum UpdateWorkflowState
{
    Idle = 0,
    Checking = 1,
    UpToDate = 2,
    UpdateAvailable = 3,
    Downloading = 4,
    Verifying = 5,
    Installing = 6,
    Completed = 7,
    UnsupportedDistribution = 8,
    Failed = 9
}

/// <summary>
/// Immutable UI-bindable snapshot of the shared update workflow.
/// </summary>
public sealed record UpdateStateSnapshot(
    UpdateWorkflowState WorkflowState,
    UpdateCheckResult? LastResult,
    UpdateDownloadProgress? Progress,
    string? StatusMessage,
    string? ErrorMessage,
    AppDistributionKind DistributionKind,
    bool ToastDismissedForSession,
    string? NotifiedVersion)
{
    public bool IsBusy => WorkflowState is UpdateWorkflowState.Checking
        or UpdateWorkflowState.Downloading
        or UpdateWorkflowState.Verifying
        or UpdateWorkflowState.Installing;

    public bool HasAvailableUpdate => LastResult?.IsUpdateAvailable == true;

    public bool CanInstall => HasAvailableUpdate
        && !IsBusy
        && DistributionKind == AppDistributionKind.InstalledSetup
        && !string.IsNullOrWhiteSpace(LastResult?.InstallerDownloadUrl);

    public bool CanRetry => WorkflowState == UpdateWorkflowState.Failed && !IsBusy;
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
