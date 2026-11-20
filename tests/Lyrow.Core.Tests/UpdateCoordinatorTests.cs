using System.Net;
using System.Text;
using Lyrow.Core.Contracts;
using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public sealed class UpdateCoordinatorTests
{
    private sealed class FakeSettingsManager : ISettingsManager
    {
        public LyrowSettings Current { get; private set; } = new()
        {
            AutoUpdateEnabled = true,
            UpdateFrequency = UpdateCheckFrequency.OnStartup,
            LastUpdateCheckUtc = null
        };
        public event EventHandler<LyrowSettings>? SettingsChanged;
        public int SaveCount { get; private set; }

        public Task<LyrowSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(LyrowSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            SaveCount++;
            SettingsChanged?.Invoke(this, Current);
            return Task.CompletedTask;
        }
        public Task UpdateAsync(Action<LyrowSettings> configure, CancellationToken cancellationToken = default)
        {
            configure(Current);
            return SaveAsync(Current, cancellationToken);
        }
    }

    private sealed class StubUpdateService : IUpdateService
    {
        public string CurrentVersion { get; init; } = "4.4";
        public UpdateCheckResult? LastCheckResult { get; private set; }
        public UpdateCheckResult NextCheckResult { get; set; } = new(
            Status: UpdateCheckStatus.UpToDate,
            CurrentVersion: "4.4",
            LatestVersion: "4.4",
            ReleaseTitle: "Lyrow v4.4",
            ReleaseNotes: "Already current",
            ReleaseUrl: "https://github.com/owergungor/Lyrow/releases/tag/v4.4",
            InstallerDownloadUrl: null,
            CheckedAtUtc: DateTime.UtcNow);

        public Func<string, string, IProgress<UpdateDownloadProgress>?, CancellationToken, Task<string>>? OnDownload { get; set; }
        public bool VerifyResult { get; set; } = true;
        public bool LaunchResult { get; set; } = true;
        public int DownloadCallCount { get; private set; }
        public int VerifyCallCount { get; private set; }
        public int LaunchCallCount { get; private set; }
        public string? LastVerifiedSha256 { get; private set; }

        public Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            LastCheckResult = NextCheckResult;
            return Task.FromResult(NextCheckResult);
        }

        public Task<string> DownloadUpdatePackageAsync(string downloadUrl, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => DownloadUpdatePackageAsync(downloadUrl, destinationPath, (IProgress<UpdateDownloadProgress>?)null, cancellationToken);

        public async Task<string> DownloadUpdatePackageAsync(string downloadUrl, string destinationPath, IProgress<UpdateDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadCallCount++;
            if (OnDownload != null)
            {
                return await OnDownload(downloadUrl, destinationPath, progress, cancellationToken);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, new byte[] { 0x4D, 0x5A, 0x90, 0x00 }.Concat(new byte[128]).ToArray(), cancellationToken);
            progress?.Report(new UpdateDownloadProgress(50.0, 512 * 1024, 1024 * 1024));
            progress?.Report(new UpdateDownloadProgress(100.0, 1024 * 1024, 1024 * 1024));
            return destinationPath;
        }

        public bool LaunchInstallerAndExit(string installerPath) => LaunchSilentUpdateAndRestart(installerPath);

        public bool LaunchSilentUpdateAndRestart(string installerPath, string? appExecutablePath = null, Action? onBeforeExit = null)
            => LaunchSilentUpdateAndRestart(installerPath, appExecutablePath, onBeforeExit, null);

        public bool LaunchSilentUpdateAndRestart(string installerPath, string? appExecutablePath, Action? onBeforeExit, string? expectedSha256)
        {
            LaunchCallCount++;
            if (LaunchResult)
            {
                onBeforeExit?.Invoke();
            }
            return LaunchResult;
        }

        public bool VerifyDownloadedPackage(string packagePath) => VerifyDownloadedPackage(packagePath, null);

        public bool VerifyDownloadedPackage(string packagePath, string? expectedSha256)
        {
            VerifyCallCount++;
            LastVerifiedSha256 = expectedSha256;
            return VerifyResult;
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;
        public StubHttpMessageHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }

    private static UpdateCheckResult MakeAvailableUpdate(string version = "4.5") => new(
        Status: UpdateCheckStatus.UpdateAvailable,
        CurrentVersion: "4.4",
        LatestVersion: version,
        ReleaseTitle: $"Lyrow v{version}",
        ReleaseNotes: "# Lyrow v4.5\n- Smooth Fluent update toast\n- Improved audio sync",
        ReleaseUrl: $"https://github.com/owergungor/Lyrow/releases/tag/v{version}",
        InstallerDownloadUrl: $"https://github.com/owergungor/Lyrow/releases/download/v{version}/Lyrow-{version}-Setup-x64.exe",
        CheckedAtUtc: DateTime.UtcNow,
        ErrorMessage: null,
        Sha256Checksum: "0BF9DE457792F95422D6F81FBE0A979ABBFDCFA7C37E1A5F9B386CF079E37E54",
        PublishedAtUtc: new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Scenario1_NewVersionFound_RaisesToastOncePerSession()
    {
        var svc = new StubUpdateService { NextCheckResult = MakeAvailableUpdate("4.5") };
        var settings = new FakeSettingsManager();
        var coordinator = new UpdateCoordinator(svc, settings, () => AppDistributionKind.InstalledSetup);

        int toastCount = 0;
        UpdateCheckResult? toasted = null;
        coordinator.UpdateAvailableToastRequested += (_, res) =>
        {
            toastCount++;
            toasted = res;
        };

        await coordinator.CheckForUpdatesAsync(isManual: false);
        await coordinator.CheckForUpdatesAsync(isManual: false);

        Assert.Equal(1, toastCount);
        Assert.NotNull(toasted);
        Assert.Equal("4.5", toasted!.LatestVersion);
        Assert.Equal(UpdateWorkflowState.UpdateAvailable, coordinator.CurrentState.WorkflowState);
        Assert.True(coordinator.CurrentState.CanInstall);
    }

    [Fact]
    public async Task Scenario2_InstallNowFromToast_DownloadsVerifiesSavesSettingsAndLaunches()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "LyrowTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new StubUpdateService { NextCheckResult = MakeAvailableUpdate("4.5") };
            var settings = new FakeSettingsManager();
            var coordinator = new UpdateCoordinator(svc, settings, () => AppDistributionKind.InstalledSetup, () => tempDir);

            await coordinator.CheckForUpdatesAsync(isManual: false);
            int savesBeforeInstall = settings.SaveCount;
            bool exitCallbackInvoked = false;

            bool ok = await coordinator.StartInstallAsync(onBeforeExit: () => exitCallbackInvoked = true);

            Assert.True(ok);
            Assert.Equal(1, svc.DownloadCallCount);
            Assert.Equal(1, svc.VerifyCallCount);
            Assert.Equal("0BF9DE457792F95422D6F81FBE0A979ABBFDCFA7C37E1A5F9B386CF079E37E54", svc.LastVerifiedSha256);
            Assert.Equal(1, svc.LaunchCallCount);
            Assert.True(exitCallbackInvoked);
            Assert.True(settings.SaveCount > savesBeforeInstall, "Settings must be flushed safely before exit");
            Assert.Equal(UpdateWorkflowState.Completed, coordinator.CurrentState.WorkflowState);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Scenario3And4_LaterDismissesToastWithoutClearingMetadata_AndSettingsCanStillInstall()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "LyrowTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new StubUpdateService { NextCheckResult = MakeAvailableUpdate("4.5") };
            var settings = new FakeSettingsManager();
            var coordinator = new UpdateCoordinator(svc, settings, () => AppDistributionKind.InstalledSetup, () => tempDir);

            int toastCount = 0;
            coordinator.UpdateAvailableToastRequested += (_, _) => toastCount++;

            await coordinator.CheckForUpdatesAsync(isManual: false);
            Assert.Equal(1, toastCount);

            // User clicks "Later" / "Sonra"
            coordinator.DismissToastForSession();
            Assert.True(coordinator.CurrentState.ToastDismissedForSession);
            Assert.True(coordinator.CurrentState.HasAvailableUpdate);
            Assert.True(coordinator.CurrentState.CanInstall);

            // Subsequent auto-check in same session must NOT re-show toast
            await coordinator.CheckForUpdatesAsync(isManual: false);
            Assert.Equal(1, toastCount);

            // User opens Settings > Updates and clicks "Install New Version"
            bool installed = await coordinator.StartInstallAsync();
            Assert.True(installed);
            Assert.Equal(1, svc.LaunchCallCount);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Scenario5_UpToDate_DoesNotRaiseToast_AndCannotInstall()
    {
        var svc = new StubUpdateService();
        var settings = new FakeSettingsManager();
        var coordinator = new UpdateCoordinator(svc, settings, () => AppDistributionKind.InstalledSetup);

        int toastCount = 0;
        coordinator.UpdateAvailableToastRequested += (_, _) => toastCount++;

        await coordinator.CheckForUpdatesAsync(isManual: false);

        Assert.Equal(0, toastCount);
        Assert.Equal(UpdateWorkflowState.UpToDate, coordinator.CurrentState.WorkflowState);
        Assert.False(coordinator.CurrentState.HasAvailableUpdate);
        Assert.False(coordinator.CurrentState.CanInstall);
    }

    [Fact]
    public async Task Scenario6_CheckError_SetsFailedStateWithRetryAndNeverCrashes()
    {
        var svc = new StubUpdateService
        {
            NextCheckResult = new UpdateCheckResult(
                Status: UpdateCheckStatus.Offline,
                CurrentVersion: "4.4",
                LatestVersion: null,
                ReleaseTitle: null,
                ReleaseNotes: null,
                ReleaseUrl: GitHubUpdateService.DefaultReleasesPageUrl,
                InstallerDownloadUrl: null,
                CheckedAtUtc: DateTime.UtcNow,
                ErrorMessage: "Unable to reach GitHub Releases.")
        };
        var settings = new FakeSettingsManager();
        var coordinator = new UpdateCoordinator(svc, settings, () => AppDistributionKind.InstalledSetup);

        int toastCount = 0;
        coordinator.UpdateAvailableToastRequested += (_, _) => toastCount++;

        await coordinator.CheckForUpdatesAsync(isManual: true);

        Assert.Equal(0, toastCount);
        Assert.Equal(UpdateWorkflowState.Failed, coordinator.CurrentState.WorkflowState);
        Assert.Contains("GitHub", coordinator.CurrentState.ErrorMessage);
        Assert.True(coordinator.CurrentState.CanRetry);
        Assert.False(coordinator.CurrentState.CanInstall);
    }

    [Fact]
    public async Task Scenario7_ConcurrentInstallAttempts_StrictlyBlockedToSingleDownload()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "LyrowTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var downloadGate = new TaskCompletionSource<bool>();
            var svc = new StubUpdateService
            {
                NextCheckResult = MakeAvailableUpdate("4.5"),
                OnDownload = async (_, dest, progress, ct) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    await File.WriteAllBytesAsync(dest, new byte[] { 0x4D, 0x5A, 0x90, 0x00 }.Concat(new byte[128]).ToArray(), ct);
                    progress?.Report(new UpdateDownloadProgress(40.0, 400_000, 1_000_000));
                    await downloadGate.Task;
                    return dest;
                }
            };
            var settings = new FakeSettingsManager();
            var coordinator = new UpdateCoordinator(svc, settings, () => AppDistributionKind.InstalledSetup, () => tempDir);

            await coordinator.CheckForUpdatesAsync(isManual: true);

            var firstTask = coordinator.StartInstallAsync();
            // Second concurrent attempt from Settings or Toast while first is downloading
            bool secondResult = await coordinator.StartInstallAsync();

            Assert.False(secondResult);
            Assert.True(coordinator.CurrentState.IsBusy);
            Assert.False(coordinator.CurrentState.CanInstall);

            downloadGate.SetResult(true);
            bool firstResult = await firstTask;

            Assert.True(firstResult);
            Assert.Equal(1, svc.DownloadCallCount);
            Assert.Equal(1, svc.LaunchCallCount);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task VerificationFailure_BlocksLaunchAndAllowsRetry()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "LyrowTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var svc = new StubUpdateService
            {
                NextCheckResult = MakeAvailableUpdate("4.5"),
                VerifyResult = false
            };
            var settings = new FakeSettingsManager();
            var coordinator = new UpdateCoordinator(svc, settings, () => AppDistributionKind.InstalledSetup, () => tempDir);

            await coordinator.CheckForUpdatesAsync(isManual: true);
            bool ok = await coordinator.StartInstallAsync();

            Assert.False(ok);
            Assert.Equal(0, svc.LaunchCallCount);
            Assert.Equal(UpdateWorkflowState.Failed, coordinator.CurrentState.WorkflowState);
            Assert.True(coordinator.CurrentState.CanRetry);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData(AppDistributionKind.MsixPackaged)]
    [InlineData(AppDistributionKind.Portable)]
    public async Task UnsupportedDistributions_DoNotLaunchSetupExe_AndReportClearMessage(AppDistributionKind kind)
    {
        var svc = new StubUpdateService { NextCheckResult = MakeAvailableUpdate("4.5") };
        var settings = new FakeSettingsManager();
        var coordinator = new UpdateCoordinator(svc, settings, () => kind);

        await coordinator.CheckForUpdatesAsync(isManual: true);
        Assert.False(coordinator.CurrentState.CanInstall);

        bool ok = await coordinator.StartInstallAsync();
        Assert.False(ok);
        Assert.Equal(0, svc.DownloadCallCount);
        Assert.Equal(0, svc.LaunchCallCount);
        Assert.Equal(UpdateWorkflowState.UnsupportedDistribution, coordinator.CurrentState.WorkflowState);
        Assert.False(string.IsNullOrWhiteSpace(coordinator.CurrentState.ErrorMessage));
    }

    [Fact]
    public async Task GitHubUpdateService_ParsesPublishedAtAndAssetSha256Digest()
    {
        string payload = """
        {
          "tag_name": "v4.5",
          "name": "Lyrow v4.5",
          "published_at": "2026-11-20T15:30:00Z",
          "html_url": "https://github.com/owergungor/Lyrow/releases/tag/v4.5",
          "body": "# Lyrow v4.5\n- Fluent update toast",
          "assets": [
            {
              "name": "Lyrow-4.5-Setup-x64.exe",
              "browser_download_url": "https://github.com/owergungor/Lyrow/releases/download/v4.5/Lyrow-4.5-Setup-x64.exe",
              "digest": "sha256:0bf9de457792f95422d6f81fbe0a979abbfdcfa7c37e1a5f9b386cf079e37e54"
            }
          ]
        }
        """;

        using var client = new HttpClient(new StubHttpMessageHandler(payload));
        var svc = new GitHubUpdateService(client, currentVersion: "4.4");

        var result = await svc.CheckForUpdatesAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("4.5", result.LatestVersion);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 15, 30, 0, TimeSpan.Zero), result.PublishedAtUtc);
        Assert.Equal("0bf9de457792f95422d6f81fbe0a979abbfdcfa7c37e1a5f9b386cf079e37e54", result.Sha256Checksum);
    }

    [Fact]
    public void Localization_TurkishAndEnglishUpdateKeysPresent()
    {
        var loc = LocalizationService.Instance;
        string original = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("tr");
            Assert.Equal("Şimdi Kur", loc["Updates_InstallNow"]);
            Assert.Equal("Yeni Sürümü Kur", loc["Updates_InstallNewVersion"]);
            Assert.Equal("Sonra", loc["Updates_Later"]);
            Assert.False(string.IsNullOrWhiteSpace(loc["Updates_UnsupportedMsix"]));
            Assert.False(string.IsNullOrWhiteSpace(loc["Updates_UnsupportedPortable"]));

            loc.SetLanguage("en");
            Assert.Equal("Install Now", loc["Updates_InstallNow"]);
            Assert.Equal("Install New Version", loc["Updates_InstallNewVersion"]);
            Assert.Equal("Later", loc["Updates_Later"]);
            Assert.False(string.IsNullOrWhiteSpace(loc["Updates_UnsupportedMsix"]));
            Assert.False(string.IsNullOrWhiteSpace(loc["Updates_UnsupportedPortable"]));
        }
        finally
        {
            loc.SetLanguage(original);
        }
    }
}
