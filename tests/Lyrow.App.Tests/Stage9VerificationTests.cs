using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

/// <summary>
/// Comprehensive verification test suite for Stage 9: Release Candidate & End-to-End Distribution Verification.
/// </summary>
public class Stage9VerificationTests
{
    [Fact]
    public void AutoUpdater_ValidSha256_VerifiesAndPasses()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"updater_test_valid_{Guid.NewGuid():N}.exe");
        try
        {
            byte[] mockPe = new byte[256];
            mockPe[0] = 0x4D; // 'M'
            mockPe[1] = 0x5A; // 'Z'
            File.WriteAllBytes(tempFile, mockPe);

            string expectedSha = GitHubUpdateService.ComputeSha256(tempFile);
            var service = new GitHubUpdateService();

            service.VerifyDownloadedPackage(tempFile, expectedSha).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AutoUpdater_InvalidSha256_FailsVerification()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"updater_test_invalid_sha_{Guid.NewGuid():N}.exe");
        try
        {
            byte[] mockPe = new byte[256];
            mockPe[0] = 0x4D;
            mockPe[1] = 0x5A;
            File.WriteAllBytes(tempFile, mockPe);

            var service = new GitHubUpdateService();
            service.VerifyDownloadedPackage(tempFile, "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855").Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AutoUpdater_CorruptedPe_FailsVerification()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"updater_test_corrupt_{Guid.NewGuid():N}.exe");
        try
        {
            // Fewer than 64 bytes is considered corrupted/incomplete PE
            File.WriteAllBytes(tempFile, new byte[32]);

            var service = new GitHubUpdateService();
            service.VerifyDownloadedPackage(tempFile).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public int RequestCount { get; private set; }

        public MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responseFactory(request));
        }
    }

    [Fact]
    public async Task AutoUpdater_TransientHttp500_RetriesAndRecovers()
    {
        int attempt = 0;
        byte[] payload = Encoding.UTF8.GetBytes("PE_SIMULATED_CONTENT_FOR_RETRY");

        var handler = new MockHttpHandler(_ =>
        {
            attempt++;
            if (attempt == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            var ok = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
            ok.Content.Headers.ContentLength = payload.Length;
            return ok;
        });

        using var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client);
        string dest = Path.Combine(Path.GetTempPath(), $"retry_500_{Guid.NewGuid():N}.exe");

        try
        {
            string downloaded = await service.DownloadUpdatePackageAsync("https://example.com/update.exe", dest);
            downloaded.Should().Be(dest);
            File.Exists(dest).Should().BeTrue();
            File.ReadAllBytes(dest).Should().Equal(payload);
            handler.RequestCount.Should().Be(2);
        }
        finally
        {
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    [Fact]
    public async Task AutoUpdater_PersistentFailure_CleansUpDownloadArtifacts()
    {
        var handler = new MockHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client);

        string dest = Path.Combine(Path.GetTempPath(), $"fail_clean_{Guid.NewGuid():N}.exe");
        string tempDownload = dest + ".download";

        try
        {
            var act = async () => await service.DownloadUpdatePackageAsync("https://example.com/fail.exe", dest);
            await act.Should().ThrowAsync<HttpRequestException>();

            File.Exists(dest).Should().BeFalse("Target file must not be created on failed download");
            File.Exists(tempDownload).Should().BeFalse("Temporary .download file must be cleaned up on failure");
        }
        finally
        {
            if (File.Exists(dest)) File.Delete(dest);
            if (File.Exists(tempDownload)) File.Delete(tempDownload);
        }
    }

    [Fact]
    public async Task AutoUpdater_Cancellation_CleansUpTempFile()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-canceled token

        var service = new GitHubUpdateService();
        string dest = Path.Combine(Path.GetTempPath(), $"cancel_clean_{Guid.NewGuid():N}.exe");
        string tempDownload = dest + ".download";

        try
        {
            var act = async () => await service.DownloadUpdatePackageAsync("https://example.com/cancel.exe", dest, cancellationToken: cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();

            File.Exists(dest).Should().BeFalse();
            File.Exists(tempDownload).Should().BeFalse("Canceled download must not leave orphaned temp files");
        }
        finally
        {
            if (File.Exists(dest)) File.Delete(dest);
            if (File.Exists(tempDownload)) File.Delete(tempDownload);
        }
    }

    [Fact]
    public async Task AutoUpdater_MissingContentLength_HandlesIndeterminateDownload()
    {
        byte[] payload = Encoding.UTF8.GetBytes("CHUNKED_STREAM_PAYLOAD");
        var handler = new MockHttpHandler(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
            // Remove Content-Length to simulate chunked/streaming HTTP
            resp.Content.Headers.ContentLength = null;
            return resp;
        });

        using var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client);
        string dest = Path.Combine(Path.GetTempPath(), $"chunked_{Guid.NewGuid():N}.exe");

        bool reportedIndeterminate = false;
        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            if (p.IsIndeterminate) reportedIndeterminate = true;
        });

        try
        {
            string downloaded = await service.DownloadUpdatePackageAsync("https://example.com/chunked.exe", dest, progress);
            downloaded.Should().Be(dest);
            File.Exists(dest).Should().BeTrue();
            File.ReadAllBytes(dest).Should().Equal(payload);
            reportedIndeterminate.Should().BeTrue("Should report indeterminate progress when Content-Length is absent");
        }
        finally
        {
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    [Fact]
    public void InnoSetup_DirectivesForIsolatedTestingAndRemoval_Exist()
    {
        string issPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "installer/setup.iss"));
        if (!File.Exists(issPath))
        {
            issPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "installer/setup.iss"));
        }

        File.Exists(issPath).Should().BeTrue("setup.iss must exist");
        string content = File.ReadAllText(issPath);

        content.Should().Contain("HasParam('/REMOVEUSERDATA')");
        content.Should().Contain("HasParam('/KEEPUSERDATA')");
        content.Should().Contain("/DATADIR=");
        content.Should().Contain("WizardSilent");
    }

    [Fact]
    public void RollbackMechanism_HonestEvaluation_DocumentsStagingAndAtomicBehavior()
    {
        // Architectural boundary documentation verification:
        // 1. Download uses atomic .download staging file.
        // 2. Failure or cancellation deletes the .download staging file (fail-safe staging).
        // 3. Existing application binary is untouched if download or PE verification fails.
        // 4. Note: Automated side-by-side historical version snapshot directory is NOT implemented;
        //    if an Inno Setup installer fails during file extraction, Inno Setup's native rollback
        //    handles file restoration, but Lyrow does not keep its own previous version backup folder.

        string tempFile = Path.Combine(Path.GetTempPath(), $"rollback_eval_{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(tempFile, new byte[64]);
            var service = new GitHubUpdateService();

            // Calling silent update with an invalid (non-PE) file must immediately return false and abort
            bool launched = service.LaunchSilentUpdateAndRestart(tempFile);
            launched.Should().BeFalse("Must abort and protect existing process if package is unverified");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
