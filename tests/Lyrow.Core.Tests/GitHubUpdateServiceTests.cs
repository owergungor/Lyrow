using System.Net;
using System.Text;
using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public class GitHubUpdateServiceTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenNewerVersionExists_ReturnsUpdateAvailable()
    {
        string json = """
        {
          "tag_name": "v2.1",
          "name": "Lyrow v2.1 — Ambient Update",
          "body": "Improved 4-edge Glow and Lyrics engine.",
          "html_url": "https://github.com/owergungor/Lyrow/releases/tag/v2.1",
          "published_at": "2026-10-20T12:00:00Z",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "Lyrow-2.1-Setup-x64.exe",
              "browser_download_url": "https://github.com/owergungor/Lyrow/releases/download/v2.1/Lyrow-2.1-Setup-x64.exe"
            }
          ]
        }
        """;

        using var httpClient = new HttpClient(new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            })));

        var service = new GitHubUpdateService(httpClient, "2.0");
        var result = await service.CheckForUpdatesAsync();

        result.Status.Should().Be(UpdateCheckStatus.UpdateAvailable);
        result.IsUpdateAvailable.Should().BeTrue();
        result.CurrentVersion.Should().Be("2.0");
        result.LatestVersion.Should().Be("2.1");
        result.ReleaseUrl.Should().Contain("v2.1");
        result.InstallerDownloadUrl.Should().Contain("Lyrow-2.1-Setup-x64.exe");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenSameVersion_ReturnsUpToDate()
    {
        string json = """
        {
          "tag_name": "v2.0",
          "name": "Lyrow v2.0",
          "body": "Major v2.0 release.",
          "html_url": "https://github.com/owergungor/Lyrow/releases/tag/v2.0",
          "draft": false,
          "prerelease": false
        }
        """;

        using var httpClient = new HttpClient(new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            })));

        var service = new GitHubUpdateService(httpClient, "2.0");
        var result = await service.CheckForUpdatesAsync();

        result.Status.Should().Be(UpdateCheckStatus.UpToDate);
        result.IsUpdateAvailable.Should().BeFalse();
        result.CurrentVersion.Should().Be("2.0");
        result.LatestVersion.Should().Be("2.0");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenOffline_ReturnsOfflineSafelyWithoutThrowing()
    {
        using var httpClient = new HttpClient(new MockHttpMessageHandler((_, _) =>
            throw new HttpRequestException("No internet connection")));

        var service = new GitHubUpdateService(httpClient, "2.0");
        var result = await service.CheckForUpdatesAsync();

        result.Status.Should().Be(UpdateCheckStatus.Offline);
        result.IsUpdateAvailable.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenMalformedJson_ReturnsMalformedResponseSafely()
    {
        using var httpClient = new HttpClient(new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ invalid json [", Encoding.UTF8, "application/json")
            })));

        var service = new GitHubUpdateService(httpClient, "2.0");
        var result = await service.CheckForUpdatesAsync();

        result.Status.Should().Be(UpdateCheckStatus.MalformedResponse);
        result.IsUpdateAvailable.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Malformed");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenServerReturnsErrorStatusCode_ReturnsFailedSafely()
    {
        using var httpClient = new HttpClient(new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));

        var service = new GitHubUpdateService(httpClient, "2.0");
        var result = await service.CheckForUpdatesAsync();

        result.Status.Should().Be(UpdateCheckStatus.Failed);
        result.IsUpdateAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData("v2.0", "2.0", true)]
    [InlineData("v1.3", "1.3", true)]
    [InlineData("v2.0.0", "2.0", false)]
    [InlineData("v1.3.1", "1.3", false)]
    public void NormalizeAndValidateTwoPartVersion_EnforcesStrictTwoPartVersioning(string raw, string expectedNormalized, bool expectedValidTag)
    {
        string normalized = GitHubUpdateService.NormalizeVersionString(raw);
        normalized.Should().Be(expectedNormalized);
        normalized.Split('.').Should().HaveCount(2);

        GitHubUpdateService.IsValidTwoPartVersionTag(raw).Should().Be(expectedValidTag);
    }
}
