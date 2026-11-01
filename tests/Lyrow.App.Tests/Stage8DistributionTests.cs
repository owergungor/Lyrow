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
/// Comprehensive verification test suite for Stage 8: Premium Product Polish & Distribution Readiness.
/// </summary>
public class Stage8DistributionTests
{
    [Theory]
    [InlineData("installer/Lyrow.ico")]
    [InlineData("src/Lyrow.App/Assets/lyrow.ico")]
    [InlineData("src/Lyrow.App/Assets/lyrow-logo.ico")]
    public void MultiResolutionIco_HasStandardSizesAndSquareAspectRatio(string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", relativePath));
        if (!File.Exists(fullPath))
        {
            fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativePath));
        }

        File.Exists(fullPath).Should().BeTrue($"ICO file must exist at {fullPath}");

        using var fs = File.OpenRead(fullPath);
        using var reader = new BinaryReader(fs);

        ushort reserved = reader.ReadUInt16();
        ushort type = reader.ReadUInt16();
        ushort count = reader.ReadUInt16();

        reserved.Should().Be(0);
        type.Should().Be(1, "Icon type must be 1 (ICO)");
        count.Should().Be(7, "Must contain all 7 Windows standard resolutions (16, 24, 32, 48, 64, 128, 256)");

        int[] expectedSizes = [16, 24, 32, 48, 64, 128, 256];
        for (int i = 0; i < count; i++)
        {
            byte widthByte = reader.ReadByte();
            byte heightByte = reader.ReadByte();
            byte colorCount = reader.ReadByte();
            byte reservedByte = reader.ReadByte();
            ushort planes = reader.ReadUInt16();
            ushort bpp = reader.ReadUInt16();
            uint bytesInRes = reader.ReadUInt32();
            uint imageOffset = reader.ReadUInt32();

            int width = widthByte == 0 ? 256 : widthByte;
            int height = heightByte == 0 ? 256 : heightByte;

            width.Should().Be(height, $"Icon layer {i} must be square");
            width.Should().Be(expectedSizes[i], $"Icon layer {i} width must match expected resolution");
            bytesInRes.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void InnoSetup_ScriptContainsRequiredDistributionDirectives()
    {
        string issPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..", "installer/setup.iss"));
        if (!File.Exists(issPath))
        {
            issPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "installer/setup.iss"));
        }

        File.Exists(issPath).Should().BeTrue("setup.iss must exist");
        string content = File.ReadAllText(issPath);

        // AppMutex & single instance protection
        content.Should().Contain("Lyrow_SingleInstance_f84b12c9-94b1-4c4f-9e63-47a61d15c89f");
        content.Should().Contain("AppMutex=");
        content.Should().Contain("CloseApplications=yes");

        // Multilingual support
        content.Should().Contain("Name: \"english\"");
        content.Should().Contain("Name: \"turkish\"");
        content.Should().Contain("Name: \"french\"");
        content.Should().Contain("Name: \"spanish\"");
        content.Should().Contain("Name: \"german\"");

        // Settings preservation / removal prompt in uninstaller
        content.Should().Contain("CurUninstallStepChanged");
        content.Should().Contain("{userappdata}\\Lyrow");
        content.Should().Contain("KeepSettingsPrompt");
    }

    [Fact]
    public void ComputeSha256_ComputesCorrectChecksum()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"sha256_test_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllBytes(tempFile, Encoding.UTF8.GetBytes("Lyrow Stage 8 Verification"));
            string hash = GitHubUpdateService.ComputeSha256(tempFile);

            hash.Should().NotBeNullOrWhiteSpace();
            hash.Length.Should().Be(64, "SHA-256 hexadecimal output is exactly 64 characters");
            hash.Should().MatchRegex("^[0-9A-F]{64}$");

            // Recalculating should be deterministic
            string hash2 = GitHubUpdateService.ComputeSha256(tempFile);
            hash2.Should().Be(hash);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyDownloadedPackage_WithExpectedSha256_ValidatesCorrectly()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"pe_test_{Guid.NewGuid():N}.exe");
        try
        {
            // Create a fake PE file: 'M', 'Z' header + at least 64 bytes
            byte[] mockPe = new byte[128];
            mockPe[0] = 0x4D; // 'M'
            mockPe[1] = 0x5A; // 'Z'
            File.WriteAllBytes(tempFile, mockPe);

            var service = new GitHubUpdateService();

            // Valid PE header without SHA verification
            service.VerifyDownloadedPackage(tempFile).Should().BeTrue();

            // Valid PE header with matching SHA-256
            string correctHash = GitHubUpdateService.ComputeSha256(tempFile);
            service.VerifyDownloadedPackage(tempFile, correctHash).Should().BeTrue();
            service.VerifyDownloadedPackage(tempFile, correctHash.ToLowerInvariant()).Should().BeTrue("Comparison must be case-insensitive");

            // Valid PE header with incorrect SHA-256
            service.VerifyDownloadedPackage(tempFile, "0000000000000000000000000000000000000000000000000000000000000000").Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VerifyDownloadedPackage_NonPeFile_FailsValidation()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"not_pe_{Guid.NewGuid():N}.exe");
        try
        {
            byte[] notPe = new byte[128];
            notPe[0] = 0x7F;
            notPe[1] = 0x45; // ELF header, not Windows PE
            File.WriteAllBytes(tempFile, notPe);

            var service = new GitHubUpdateService();
            service.VerifyDownloadedPackage(tempFile).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsPackageAuthenticodeSigned_SafeOnUnsignedOrMissingFile()
    {
        GitHubUpdateService.IsPackageAuthenticodeSigned("nonexistent.exe").Should().BeFalse();

        string tempFile = Path.Combine(Path.GetTempPath(), $"unsigned_{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllBytes(tempFile, new byte[128]);
            GitHubUpdateService.IsPackageAuthenticodeSigned(tempFile).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DiagnosticLogger_Sanitize_RedactsUserDirectoryNames()
    {
        string raw = @"Error loading asset from C:\Users\JohnDoe\AppData\Roaming\Lyrow\settings.json";
        string sanitized = LocalDiagnosticLogger.Sanitize(raw);

        sanitized.Should().NotContain("JohnDoe");
        sanitized.Should().Contain(@"C:\Users\***\AppData\Roaming\Lyrow\settings.json");
    }

    [Fact]
    public void SettingsManager_PortableModeDetection()
    {
        // When custom path is not passed, default path should be resolved
        var manager = new SettingsManager();
        manager.FilePath.Should().NotBeNullOrWhiteSpace();
        manager.FilePath.Should().EndWith("settings.json");
    }

    private class TransientMockHttpMessageHandler : HttpMessageHandler
    {
        private int _attempts = 0;
        private readonly byte[] _content;

        public int AttemptCount => _attempts;

        public TransientMockHttpMessageHandler(byte[] content)
        {
            _content = content;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _attempts++;
            if (_attempts == 1)
            {
                // First attempt fails with transient error
                throw new HttpRequestException("Simulated network blip on attempt 1");
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            };
            response.Content.Headers.ContentLength = _content.Length;
            return await Task.FromResult(response);
        }
    }

    [Fact]
    public async Task DownloadUpdatePackageAsync_RetriesOnTransientFailureAndSucceeds()
    {
        byte[] payload = Encoding.UTF8.GetBytes("Valid installer payload for retry verification");
        var handler = new TransientMockHttpMessageHandler(payload);
        using var httpClient = new HttpClient(handler);

        var service = new GitHubUpdateService(httpClient);
        string destPath = Path.Combine(Path.GetTempPath(), $"retry_test_{Guid.NewGuid():N}.exe");

        try
        {
            string result = await service.DownloadUpdatePackageAsync("https://example.com/download.exe", destPath);

            result.Should().Be(destPath);
            File.Exists(destPath).Should().BeTrue();
            File.ReadAllBytes(destPath).Should().Equal(payload);
            handler.AttemptCount.Should().Be(2, "Should have succeeded on retry attempt 2");
        }
        finally
        {
            if (File.Exists(destPath)) File.Delete(destPath);
        }
    }
}
