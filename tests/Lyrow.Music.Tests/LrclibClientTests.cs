using System.Net;
using FluentAssertions;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class LrclibClientTests
{
    [Fact]
    public async Task FetchLyricsAsync_ShouldReturnSyncedLyrics_OnSuccessfulResponse()
    {
        string json = """
            {
                "id": 12345,
                "trackName": "Hotel California",
                "artistName": "Eagles",
                "syncedLyrics": "[00:10.00]On a dark desert highway\n[00:15.00]Cool wind in my hair"
            }
            """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new LrclibClient(new HttpClient(handler));

        var result = await client.FetchLyricsAsync("Hotel California", "Eagles");

        result.Found.Should().BeTrue();
        result.Source.Should().Be("LRCLIB");
        result.Lyrics.Should().NotBeNull();
        result.Lyrics!.IsSynced.Should().BeTrue();
        result.Lyrics.Lines.Should().HaveCount(2);
        result.Lyrics.Lines[0].Text.Should().Be("On a dark desert highway");
    }

    [Fact]
    public async Task FetchLyricsAsync_ShouldFallbackToPlainLyrics_WhenOnlyPlainAvailable()
    {
        string json = """
            {
                "id": 6789,
                "trackName": "Instrumental Song",
                "artistName": "Artist",
                "plainLyrics": "First plain line\nSecond plain line"
            }
            """;

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new LrclibClient(new HttpClient(handler));

        var result = await client.FetchLyricsAsync("Instrumental Song", "Artist");

        result.Found.Should().BeTrue();
        result.Lyrics.Should().NotBeNull();
        result.Lyrics!.IsSynced.Should().BeFalse();
        result.Lyrics.PlainLyrics.Should().Contain("First plain line");
    }

    [Fact]
    public async Task FetchLyricsAsync_ShouldHandle404NotFoundGracefully()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound, "{\"message\":\"Not found\"}");
        var client = new LrclibClient(new HttpClient(handler));

        var result = await client.FetchLyricsAsync("Nonexistent", "Unknown");

        result.Found.Should().BeFalse();
        result.Lyrics.Should().BeNull();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task FetchLyricsAsync_ShouldHandle429RateLimitGracefully()
    {
        var handler = new MockHttpMessageHandler((HttpStatusCode)429, "Rate limit exceeded");
        var client = new LrclibClient(new HttpClient(handler));

        var result = await client.FetchLyricsAsync("Some Track", "Some Artist");

        result.Found.Should().BeFalse();
        result.Error.Should().Contain("Rate limited");
    }

    [Fact]
    public async Task FetchLyricsAsync_ShouldHandleEmptyInputs_WithoutNetworkCall()
    {
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, "{}");
        var client = new LrclibClient(new HttpClient(handler));

        var result = await client.FetchLyricsAsync("", "");

        result.Found.Should().BeFalse();
        result.Error.Should().Contain("empty");
        handler.CallCount.Should().Be(0);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseContent;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string responseContent)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
