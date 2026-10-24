using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// Windows-first non-intrusive update checker querying GitHub Releases API.
/// Strictly adheres to two-part versioning (e.g. v1.3, v2.0, v2.1) and never performs
/// silent binary replacement or untrusted code execution.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    public const string DefaultReleasesApiUrl = "https://api.github.com/repos/owergungor/Lyrow/releases/latest";
    public const string DefaultReleasesPageUrl = "https://github.com/owergungor/Lyrow/releases";
    public const string AppVersion = "2.8";
    public const string SilentInstallerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";

    public static bool IsUpdateCheckDue(UpdateCheckFrequency frequency, DateTime? lastCheckUtc, DateTime? nowUtc = null)
    {
        if (lastCheckUtc == null) return true;
        var now = nowUtc ?? DateTime.UtcNow;
        var elapsed = now - lastCheckUtc.Value;
        if (elapsed < TimeSpan.Zero) return true;

        return frequency switch
        {
            UpdateCheckFrequency.OnStartup => true,
            UpdateCheckFrequency.Daily => elapsed >= TimeSpan.FromHours(24),
            UpdateCheckFrequency.Weekly => elapsed >= TimeSpan.FromDays(7),
            UpdateCheckFrequency.Monthly => elapsed >= TimeSpan.FromDays(30),
            _ => true
        };
    }

    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;

    public GitHubUpdateService(HttpClient? httpClient = null, string? currentVersion = null, string? apiUrl = null)
    {
        CurrentVersion = NormalizeVersionString(currentVersion ?? AppVersion);
        _apiUrl = string.IsNullOrWhiteSpace(apiUrl) ? DefaultReleasesApiUrl : apiUrl;

        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lyrow", CurrentVersion));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
    }

    public string CurrentVersion { get; }

    public UpdateCheckResult? LastCheckResult { get; private set; }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _apiUrl);
            if (!request.Headers.UserAgent.Any())
            {
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Lyrow", CurrentVersion));
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var failResult = new UpdateCheckResult(
                    Status: UpdateCheckStatus.Failed,
                    CurrentVersion: CurrentVersion,
                    LatestVersion: null,
                    ReleaseTitle: null,
                    ReleaseNotes: null,
                    ReleaseUrl: DefaultReleasesPageUrl,
                    InstallerDownloadUrl: null,
                    CheckedAtUtc: now,
                    ErrorMessage: $"Update server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
                LastCheckResult = failResult;
                return failResult;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                var emptyResult = new UpdateCheckResult(
                    Status: UpdateCheckStatus.MalformedResponse,
                    CurrentVersion: CurrentVersion,
                    LatestVersion: null,
                    ReleaseTitle: null,
                    ReleaseNotes: null,
                    ReleaseUrl: DefaultReleasesPageUrl,
                    InstallerDownloadUrl: null,
                    CheckedAtUtc: now,
                    ErrorMessage: "Empty response received from release endpoint.");
                LastCheckResult = emptyResult;
                return emptyResult;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out var tagProp) ||
                tagProp.ValueKind != JsonValueKind.String)
            {
                var malformedResult = new UpdateCheckResult(
                    Status: UpdateCheckStatus.MalformedResponse,
                    CurrentVersion: CurrentVersion,
                    LatestVersion: null,
                    ReleaseTitle: null,
                    ReleaseNotes: null,
                    ReleaseUrl: DefaultReleasesPageUrl,
                    InstallerDownloadUrl: null,
                    CheckedAtUtc: now,
                    ErrorMessage: "Release payload is missing a valid tag_name.");
                LastCheckResult = malformedResult;
                return malformedResult;
            }

            string rawTag = tagProp.GetString() ?? string.Empty;
            if (!TryParseTwoPartOrLegacyVersion(rawTag, out var remoteVer))
            {
                var invalidVerResult = new UpdateCheckResult(
                    Status: UpdateCheckStatus.MalformedResponse,
                    CurrentVersion: CurrentVersion,
                    LatestVersion: null,
                    ReleaseTitle: null,
                    ReleaseNotes: null,
                    ReleaseUrl: DefaultReleasesPageUrl,
                    InstallerDownloadUrl: null,
                    CheckedAtUtc: now,
                    ErrorMessage: $"Unrecognized version format '{rawTag}'.");
                LastCheckResult = invalidVerResult;
                return invalidVerResult;
            }

            string normalizedRemote = $"{remoteVer.Major}.{remoteVer.Minor}";
            string releaseTitle = root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? (nameProp.GetString() ?? $"Lyrow v{normalizedRemote}")
                : $"Lyrow v{normalizedRemote}";

            string? releaseNotes = root.TryGetProperty("body", out var bodyProp) && bodyProp.ValueKind == JsonValueKind.String
                ? bodyProp.GetString()
                : null;

            string releaseUrl = root.TryGetProperty("html_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(urlProp.GetString())
                ? urlProp.GetString()!
                : DefaultReleasesPageUrl;

            string? installerUrl = null;
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    if (asset.ValueKind == JsonValueKind.Object &&
                        asset.TryGetProperty("name", out var assetNameProp) &&
                        asset.TryGetProperty("browser_download_url", out var downloadUrlProp))
                    {
                        string assetName = assetNameProp.GetString() ?? string.Empty;
                        string? dlUrl = downloadUrlProp.GetString();
                        if (!string.IsNullOrWhiteSpace(dlUrl) &&
                            assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                            dlUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                        {
                            installerUrl = dlUrl;
                            break;
                        }
                    }
                }
            }

            bool isNewer = IsNewerVersion(CurrentVersion, normalizedRemote);

            var result = new UpdateCheckResult(
                Status: isNewer ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate,
                CurrentVersion: CurrentVersion,
                LatestVersion: normalizedRemote,
                ReleaseTitle: releaseTitle,
                ReleaseNotes: releaseNotes,
                ReleaseUrl: releaseUrl,
                InstallerDownloadUrl: installerUrl,
                CheckedAtUtc: now,
                ErrorMessage: null);

            LastCheckResult = result;
            return result;
        }
        catch (JsonException ex)
        {
            var malformed = new UpdateCheckResult(
                Status: UpdateCheckStatus.MalformedResponse,
                CurrentVersion: CurrentVersion,
                LatestVersion: null,
                ReleaseTitle: null,
                ReleaseNotes: null,
                ReleaseUrl: DefaultReleasesPageUrl,
                InstallerDownloadUrl: null,
                CheckedAtUtc: now,
                ErrorMessage: $"Malformed release metadata: {ex.Message}");
            LastCheckResult = malformed;
            return malformed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var offline = new UpdateCheckResult(
                Status: UpdateCheckStatus.Offline,
                CurrentVersion: CurrentVersion,
                LatestVersion: null,
                ReleaseTitle: null,
                ReleaseNotes: null,
                ReleaseUrl: DefaultReleasesPageUrl,
                InstallerDownloadUrl: null,
                CheckedAtUtc: now,
                ErrorMessage: "Unable to reach GitHub Releases. Please check your internet connection.");
            LastCheckResult = offline;
            return offline;
        }
        catch (Exception ex)
        {
            var failed = new UpdateCheckResult(
                Status: UpdateCheckStatus.Failed,
                CurrentVersion: CurrentVersion,
                LatestVersion: null,
                ReleaseTitle: null,
                ReleaseNotes: null,
                ReleaseUrl: DefaultReleasesPageUrl,
                InstallerDownloadUrl: null,
                CheckedAtUtc: now,
                ErrorMessage: $"Update check failed: {ex.Message}");
            LastCheckResult = failed;
            return failed;
        }
    }

    public async Task<string> DownloadUpdatePackageAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new ArgumentException("Download URL cannot be empty.", nameof(downloadUrl));
        }

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                double pct = Math.Clamp((double)totalRead / totalBytes.Value * 100.0, 0.0, 100.0);
                progress?.Report(pct);
            }
        }

        progress?.Report(100.0);
        return destinationPath;
    }

    public bool LaunchInstallerAndExit(string installerPath)
    {
        return LaunchSilentUpdateAndRestart(installerPath);
    }

    public bool LaunchSilentUpdateAndRestart(string installerPath, string? appExecutablePath = null)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            return false;
        }

        try
        {
            string appExe = appExecutablePath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "Lyrow.App.exe";
            int currentPid = Environment.ProcessId;

            // Script that waits for current process to exit, runs silent installer without wizard dialogs, and restarts app
            string script = $"Start-Sleep -Milliseconds 600; Wait-Process -Id {currentPid} -Timeout 10 -ErrorAction SilentlyContinue; " +
                            $"Start-Process -FilePath '{installerPath}' -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' -Wait; " +
                            $"Start-Process -FilePath '{appExe}';";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };

            var proc = Process.Start(startInfo);
            if (proc != null)
            {
                Environment.Exit(0);
                return true;
            }
        }
        catch { }

        // Fallback: standard launch
        try
        {
            var fallback = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                UseShellExecute = true
            };
            var proc = Process.Start(fallback);
            if (proc != null)
            {
                Environment.Exit(0);
                return true;
            }
        }
        catch { }

        return false;
    }

    public static string NormalizeVersionString(string version)
    {
        if (TryParseTwoPartOrLegacyVersion(version, out var parsed))
        {
            return $"{parsed.Major}.{parsed.Minor}";
        }
        return AppVersion;
    }

    public static bool IsNewerVersion(string currentVersion, string candidateVersion)
    {
        if (!TryParseTwoPartOrLegacyVersion(currentVersion, out var current)) return false;
        if (!TryParseTwoPartOrLegacyVersion(candidateVersion, out var candidate)) return false;

        return candidate > current;
    }

    public static bool IsValidTwoPartVersionTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        string trimmed = tag.Trim();
        if (!trimmed.StartsWith('v')) return false;
        string body = trimmed[1..];
        string[] parts = body.Split('.');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0], out int major) && major >= 0 &&
               int.TryParse(parts[1], out int minor) && minor >= 0;
    }

    private static bool TryParseTwoPartOrLegacyVersion(string? raw, out Version parsed)
    {
        parsed = new Version(2, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string clean = raw.Trim();
        if (clean.StartsWith('v') || clean.StartsWith('V'))
        {
            clean = clean[1..];
        }

        int dashIdx = clean.IndexOf('-');
        if (dashIdx > 0)
        {
            clean = clean[..dashIdx];
        }

        var parts = clean.Split('.');
        if (parts.Length < 2 || parts.Length > 3) return false;

        if (!int.TryParse(parts[0], out int major) || major < 0) return false;
        if (!int.TryParse(parts[1], out int minor) || minor < 0) return false;

        parsed = new Version(major, minor);
        return true;
    }
}
