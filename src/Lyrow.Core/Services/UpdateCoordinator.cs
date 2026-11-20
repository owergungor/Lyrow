using System.Text.RegularExpressions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// Shared, thread-safe update orchestrator used by both the top-right update toast
/// and Settings > Updates. Prevents duplicate concurrent downloads/installations,
/// enforces session-scoped toast dismissal without discarding update metadata,
/// verifies package integrity before launch, and respects distribution format boundaries.
/// </summary>
public sealed class UpdateCoordinator
{
    private readonly IUpdateService _updateService;
    private readonly ISettingsManager _settingsManager;
    private readonly Func<AppDistributionKind> _distributionDetector;
    private readonly Func<string> _downloadDirectoryProvider;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly object _stateLock = new();
    private int _installInFlight;

    private UpdateStateSnapshot _currentState;

    public UpdateCoordinator(
        IUpdateService updateService,
        ISettingsManager settingsManager,
        Func<AppDistributionKind>? distributionDetector = null,
        Func<string>? downloadDirectoryProvider = null)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _distributionDetector = distributionDetector ?? (() => DetectCurrentDistributionKind());
        _downloadDirectoryProvider = downloadDirectoryProvider ?? (() => Path.Combine(Path.GetTempPath(), "Lyrow", "Updates"));

        _currentState = new UpdateStateSnapshot(
            WorkflowState: UpdateWorkflowState.Idle,
            LastResult: _updateService.LastCheckResult,
            Progress: null,
            StatusMessage: null,
            ErrorMessage: null,
            DistributionKind: _distributionDetector(),
            ToastDismissedForSession: false,
            NotifiedVersion: null);
    }

    public string CurrentVersion => _updateService.CurrentVersion;

    public UpdateStateSnapshot CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    /// <summary>Raised whenever update workflow state, progress, or metadata changes.</summary>
    public event EventHandler<UpdateStateSnapshot>? StateChanged;

    /// <summary>Raised once per session per version when a newer release should be presented in the top-right toast.</summary>
    public event EventHandler<UpdateCheckResult>? UpdateAvailableToastRequested;

    /// <summary>
    /// Checks GitHub Releases for a newer version.
    /// Automatic checks respect AutoUpdateEnabled and UpdateFrequency; manual checks always run.
    /// </summary>
    public async Task<UpdateCheckResult?> CheckForUpdatesAsync(bool isManual = false, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _installInFlight) == 1)
        {
            return CurrentState.LastResult;
        }

        if (!isManual)
        {
            var settings = _settingsManager.Current;
            if (!settings.AutoUpdateEnabled)
            {
                return CurrentState.LastResult;
            }

            if (!GitHubUpdateService.IsUpdateCheckDue(settings.UpdateFrequency, settings.LastUpdateCheckUtc))
            {
                return CurrentState.LastResult;
            }
        }

        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return CurrentState.LastResult;
        }

        try
        {
            var loc = LocalizationService.Instance;
            UpdateState(s => s with
            {
                WorkflowState = UpdateWorkflowState.Checking,
                Progress = null,
                StatusMessage = loc["Updates_Checking"],
                ErrorMessage = null
            });

            var result = await _updateService.CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                _settingsManager.Current.LastUpdateCheckUtc = result.CheckedAtUtc;
                await _settingsManager.SaveAsync(_settingsManager.Current).ConfigureAwait(false);
            }
            catch
            {
                // Non-fatal: never block update check flow if settings persistence fails
            }

            bool shouldNotifyToast = false;

            if (result.IsUpdateAvailable)
            {
                var dist = _distributionDetector();
                UpdateState(s =>
                {
                    bool sameVersionDismissed = s.ToastDismissedForSession &&
                        string.Equals(s.NotifiedVersion, result.LatestVersion, StringComparison.OrdinalIgnoreCase);

                    bool alreadyNotifiedThisSession = string.Equals(s.NotifiedVersion, result.LatestVersion, StringComparison.OrdinalIgnoreCase);

                    // Show top-right toast on automatic check once per session per version (never spam).
                    if (!isManual && !sameVersionDismissed && !alreadyNotifiedThisSession)
                    {
                        shouldNotifyToast = true;
                    }

                    return s with
                    {
                        WorkflowState = UpdateWorkflowState.UpdateAvailable,
                        LastResult = result,
                        Progress = null,
                        StatusMessage = $"{loc["Updates_Available"]}: v{result.LatestVersion}",
                        ErrorMessage = null,
                        DistributionKind = dist,
                        ToastDismissedForSession = sameVersionDismissed,
                        NotifiedVersion = shouldNotifyToast ? result.LatestVersion : s.NotifiedVersion
                    };
                });

                if (shouldNotifyToast)
                {
                    UpdateAvailableToastRequested?.Invoke(this, result);
                }
            }
            else if (result.Status == UpdateCheckStatus.UpToDate)
            {
                UpdateState(s => s with
                {
                    WorkflowState = UpdateWorkflowState.UpToDate,
                    LastResult = result,
                    Progress = null,
                    StatusMessage = loc["Updates_UpToDate"],
                    ErrorMessage = null
                });
            }
            else
            {
                string err = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.ErrorMessage
                    : loc["Updates_Failed"];
                UpdateState(s => s with
                {
                    WorkflowState = UpdateWorkflowState.Failed,
                    LastResult = result,
                    Progress = null,
                    StatusMessage = loc["Updates_Failed"],
                    ErrorMessage = err
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            var loc = LocalizationService.Instance;
            UpdateState(s => s with
            {
                WorkflowState = UpdateWorkflowState.Failed,
                Progress = null,
                StatusMessage = loc["Updates_Failed"],
                ErrorMessage = ex.Message
            });
            return null;
        }
        finally
        {
            _checkGate.Release();
        }
    }

    /// <summary>
    /// Dismisses the top-right update toast for the current session without clearing
    /// the discovered update metadata so Settings > Updates remains actionable.
    /// </summary>
    public void DismissToastForSession()
    {
        UpdateState(s => s with
        {
            ToastDismissedForSession = true,
            NotifiedVersion = s.LastResult?.LatestVersion ?? s.NotifiedVersion
        });
    }

    /// <summary>
    /// Shared download, verification, and silent installation entry point used by both
    /// the top-right toast ("Install Now") and Settings > Updates ("Install New Version").
    /// Guarantees single-flight execution so concurrent installations cannot start.
    /// </summary>
    public async Task<bool> StartInstallAsync(Action? onBeforeExit = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _installInFlight, 1, 0) != 0)
        {
            return false;
        }

        string? downloadedFilePath = null;
        try
        {
            var loc = LocalizationService.Instance;
            var snapshot = CurrentState;
            var result = snapshot.LastResult;

            if (result == null || !result.IsUpdateAvailable)
            {
                return false;
            }

            var dist = _distributionDetector();
            if (dist == AppDistributionKind.MsixPackaged)
            {
                UpdateState(s => s with
                {
                    WorkflowState = UpdateWorkflowState.UnsupportedDistribution,
                    DistributionKind = dist,
                    Progress = null,
                    StatusMessage = loc["Updates_UnsupportedMsix"],
                    ErrorMessage = loc["Updates_UnsupportedMsix"]
                });
                return false;
            }

            if (dist == AppDistributionKind.Portable)
            {
                UpdateState(s => s with
                {
                    WorkflowState = UpdateWorkflowState.UnsupportedDistribution,
                    DistributionKind = dist,
                    Progress = null,
                    StatusMessage = loc["Updates_UnsupportedPortable"],
                    ErrorMessage = loc["Updates_UnsupportedPortable"]
                });
                return false;
            }

            if (string.IsNullOrWhiteSpace(result.InstallerDownloadUrl))
            {
                UpdateState(s => s with
                {
                    WorkflowState = UpdateWorkflowState.Failed,
                    Progress = null,
                    StatusMessage = loc["Updates_Failed"],
                    ErrorMessage = loc["Updates_NoInstallerAsset"]
                });
                return false;
            }

            UpdateState(s => s with
            {
                WorkflowState = UpdateWorkflowState.Downloading,
                Progress = new UpdateDownloadProgress(0, 0, null),
                StatusMessage = $"{loc["Updates_Downloading"]} 0%",
                ErrorMessage = null
            });

            string downloadDir = _downloadDirectoryProvider();
            Directory.CreateDirectory(downloadDir);
            string safeVersion = Regex.Replace(result.LatestVersion ?? "latest", @"[^0-9A-Za-z._-]", "");
            downloadedFilePath = Path.Combine(downloadDir, $"Lyrow-{safeVersion}-Setup-x64.exe");

            var progressReporter = new Progress<UpdateDownloadProgress>(p =>
            {
                string statusText = p.IsIndeterminate
                    ? $"{loc["Updates_Downloading"]} ({p.MegabytesRead:0.0} MB)"
                    : $"{loc["Updates_Downloading"]} {(int)Math.Round(p.Percent)}%";

                UpdateState(s => s with
                {
                    WorkflowState = UpdateWorkflowState.Downloading,
                    Progress = p,
                    StatusMessage = statusText,
                    ErrorMessage = null
                });
            });

            string savedPath = await _updateService.DownloadUpdatePackageAsync(
                result.InstallerDownloadUrl,
                downloadedFilePath,
                progressReporter,
                cancellationToken).ConfigureAwait(false);

            UpdateState(s => s with
            {
                WorkflowState = UpdateWorkflowState.Verifying,
                Progress = new UpdateDownloadProgress(100, s.Progress?.BytesRead ?? 0, s.Progress?.TotalBytes),
                StatusMessage = loc["Updates_Verifying"],
                ErrorMessage = null
            });

            bool verified = _updateService.VerifyDownloadedPackage(savedPath, result.Sha256Checksum);
            if (!verified)
            {
                TryDeleteFile(savedPath);
                UpdateState(s => s with
                {
                    WorkflowState = UpdateWorkflowState.Failed,
                    Progress = null,
                    StatusMessage = loc["Updates_Failed"],
                    ErrorMessage = loc["Updates_VerifyFailed"]
                });
                return false;
            }

            // Safely flush user settings before handing off to installer & restart
            try
            {
                await _settingsManager.SaveAsync(_settingsManager.Current).ConfigureAwait(false);
            }
            catch
            {
                // Non-fatal: continue install
            }

            UpdateState(s => s with
            {
                WorkflowState = UpdateWorkflowState.Installing,
                Progress = new UpdateDownloadProgress(100, s.Progress?.BytesRead ?? 0, s.Progress?.TotalBytes),
                StatusMessage = loc["Updates_StartingInstall"],
                ErrorMessage = null
            });

            bool launched = _updateService.LaunchSilentUpdateAndRestart(
                savedPath,
                appExecutablePath: null,
                onBeforeExit: onBeforeExit,
                expectedSha256: result.Sha256Checksum);

            if (!launched)
            {
                UpdateState(s => s with
                {
                    WorkflowState = UpdateWorkflowState.Failed,
                    Progress = null,
                    StatusMessage = loc["Updates_Failed"],
                    ErrorMessage = loc["Updates_Failed"]
                });
                return false;
            }

            UpdateState(s => s with
            {
                WorkflowState = UpdateWorkflowState.Completed,
                StatusMessage = loc["Updates_Completed"],
                ErrorMessage = null
            });

            return true;
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(downloadedFilePath);
            var loc = LocalizationService.Instance;
            UpdateState(s => s with
            {
                WorkflowState = UpdateWorkflowState.UpdateAvailable,
                Progress = null,
                StatusMessage = $"{loc["Updates_Available"]}: v{s.LastResult?.LatestVersion}",
                ErrorMessage = null
            });
            return false;
        }
        catch (Exception ex)
        {
            TryDeleteFile(downloadedFilePath);
            var loc = LocalizationService.Instance;
            UpdateState(s => s with
            {
                WorkflowState = UpdateWorkflowState.Failed,
                Progress = null,
                StatusMessage = loc["Updates_Failed"],
                ErrorMessage = ex.Message
            });
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _installInFlight, 0);
        }
    }

    /// <summary>
    /// Detects whether the current process is running as an Inno Setup install, Portable ZIP, or MSIX package.
    /// </summary>
    public static AppDistributionKind DetectCurrentDistributionKind(string? executablePath = null)
    {
        try
        {
            string exePath = !string.IsNullOrWhiteSpace(executablePath)
                ? executablePath
                : (Environment.ProcessPath ?? AppContext.BaseDirectory);

            if (string.IsNullOrWhiteSpace(exePath))
            {
                return AppDistributionKind.InstalledSetup;
            }

            if (exePath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
            {
                return AppDistributionKind.MsixPackaged;
            }

            string? dir = Directory.Exists(exePath) ? exePath : Path.GetDirectoryName(exePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "unins000.exe")))
                {
                    return AppDistributionKind.InstalledSetup;
                }

                string localProgramsLyrow = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "Lyrow");

                if (dir.StartsWith(localProgramsLyrow, StringComparison.OrdinalIgnoreCase) ||
                    dir.Contains(@"\Program Files\Lyrow", StringComparison.OrdinalIgnoreCase) ||
                    dir.Contains(@"\Program Files (x86)\Lyrow", StringComparison.OrdinalIgnoreCase))
                {
                    return AppDistributionKind.InstalledSetup;
                }
            }

            return AppDistributionKind.Portable;
        }
        catch
        {
            return AppDistributionKind.InstalledSetup;
        }
    }

    /// <summary>
    /// Extracts a clean, concise plain-text summary from GitHub Release markdown notes.
    /// </summary>
    public static string FormatReleaseSummary(string? releaseNotes, int maxLength = 140)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes))
        {
            return string.Empty;
        }

        var lines = releaseNotes.Replace("\r\n", "\n").Split('\n');
        var cleanLines = new List<string>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith('#') || line.StartsWith('|') || line.StartsWith("---") || line.StartsWith("> ")) continue;

            // Strip common markdown bullets and bold/code markers
            line = Regex.Replace(line, @"^[-*•]\s+", string.Empty);
            line = line.Replace("**", string.Empty).Replace("`", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Contains("SHA-256", StringComparison.OrdinalIgnoreCase)) continue;

            cleanLines.Add(line);
            if (cleanLines.Count >= 2) break;
        }

        string combined = string.Join(" ", cleanLines).Trim();
        if (combined.Length > maxLength)
        {
            return combined[..(maxLength - 1)].TrimEnd() + "…";
        }
        return combined;
    }

    private void UpdateState(Func<UpdateStateSnapshot, UpdateStateSnapshot> mutator)
    {
        UpdateStateSnapshot next;
        lock (_stateLock)
        {
            next = mutator(_currentState);
            _currentState = next;
        }
        StateChanged?.Invoke(this, next);
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
