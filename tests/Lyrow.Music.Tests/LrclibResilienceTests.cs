using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class LrclibResilienceTests
{
    private const string SuccessLrcJson = """
        {
            "id": 101,
            "trackName": "Lucky",
            "artistName": "Britney Spears",
            "syncedLyrics": "[00:10.00]This is a story about a girl named Lucky"
        }
        """;

    [Fact]
    public async Task FetchLyricsAsync_When503OccursThenSucceeds_ShouldRetryAndReturnLyrics()
    {
        int attempt = 0;
        var handler = new DynamicMockHttpMessageHandler(req =>
        {
            attempt++;
            if (attempt == 1)
            {
                var resp503 = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"message\":\"The server is busy\",\"statusCode\":503}")
                };
                resp503.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(50));
                return resp503;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessLrcJson)
            };
        });

        var client = new LrclibClient(new HttpClient(handler));
        var result = await client.FetchLyricsAsync("Lucky", "Britney Spears");

        result.Found.Should().BeTrue();
        result.Lyrics.Should().NotBeNull();
        result.Lyrics!.IsSynced.Should().BeTrue();
        attempt.Should().Be(2); // Retried once and succeeded!
    }

    [Fact]
    public async Task FetchLyricsAsync_WhenPersistent503_ShouldFailGracefullyAfterMaxRetries()
    {
        int attempt = 0;
        var handler = new DynamicMockHttpMessageHandler(req =>
        {
            attempt++;
            var resp = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"message\":\"The server is busy\",\"statusCode\":503}")
            };
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return resp;
        });

        var client = new LrclibClient(new HttpClient(handler));
        var result = await client.FetchLyricsAsync("Some Track", "Some Artist");

        result.Found.Should().BeFalse();
        result.Error.Should().Contain("503");
        attempt.Should().Be(3); // 1 initial + 2 retries = 3 attempts
    }

    [Fact]
    public async Task FetchLyricsAsync_When404NotFound_ShouldNotRetryUnnecessarily()
    {
        int attempt = 0;
        var handler = new DynamicMockHttpMessageHandler(req =>
        {
            attempt++;
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"Track not found\"}")
            };
        });

        var client = new LrclibClient(new HttpClient(handler));
        var result = await client.FetchLyricsAsync("NonExistent", "UnknownArtist");

        result.Found.Should().BeFalse();
        // 404 fails fast on exact query; fallback 3 (search) also gets 404 and does not loop
        attempt.Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task FetchLyricsAsync_When429RateLimited_ShouldParseRetryAfterHeader()
    {
        var handler = new DynamicMockHttpMessageHandler(req =>
        {
            var resp = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("{\"message\":\"Rate limited\"}")
            };
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
            return resp;
        });

        var client = new LrclibClient(new HttpClient(handler));
        var result = await client.FetchLyricsAsync("RateLimitedTrack", "Artist");

        result.Found.Should().BeFalse();
        result.IsRateLimited.Should().BeTrue();
        result.RetryAfterSeconds.Should().Be(12);
    }

    [Fact]
    public async Task FetchLyricsAsync_SuccessCache_ShouldReturnCachedResultWithoutNetworkCall()
    {
        int callCount = 0;
        var handler = new DynamicMockHttpMessageHandler(req =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessLrcJson)
            };
        });

        var client = new LrclibClient(new HttpClient(handler));

        var res1 = await client.FetchLyricsAsync("Lucky", "Britney Spears");
        var res2 = await client.FetchLyricsAsync("Lucky", "Britney Spears");

        res1.Found.Should().BeTrue();
        res2.Found.Should().BeTrue();
        callCount.Should().Be(1); // Second call served directly from in-memory success cache!
    }

    [Fact]
    public async Task FetchLyricsAsync_NegativeCache_ShouldThrottleRepeatedCallsDuring503()
    {
        int callCount = 0;
        var handler = new DynamicMockHttpMessageHandler(req =>
        {
            callCount++;
            var resp = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"message\":\"Overloaded\"}")
            };
            resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
            return resp;
        });

        var client = new LrclibClient(new HttpClient(handler));

        var res1 = await client.FetchLyricsAsync("OverloadedTrack", "Artist");
        int callsAfterFirst = callCount;

        var res2 = await client.FetchLyricsAsync("OverloadedTrack", "Artist");

        res1.Found.Should().BeFalse();
        res2.Found.Should().BeFalse();
        callCount.Should().Be(callsAfterFirst); // Second call served from short-lived negative cache!
    }

    [Fact]
    public async Task FetchLyricsAsync_InFlightDeduplication_ShouldCoalesceConcurrentCalls()
    {
        int callCount = 0;
        var handler = new DynamicMockHttpMessageHandler(async req =>
        {
            callCount++;
            await Task.Delay(50); // Simulate network latency
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessLrcJson)
            };
        });

        var client = new LrclibClient(new HttpClient(handler));

        // Trigger 3 parallel calls for the exact same track concurrently
        var t1 = client.FetchLyricsAsync("Lucky", "Britney Spears");
        var t2 = client.FetchLyricsAsync("Lucky", "Britney Spears");
        var t3 = client.FetchLyricsAsync("Lucky", "Britney Spears");

        var results = await Task.WhenAll(t1, t2, t3);

        results.Should().AllSatisfy(r => r.Found.Should().BeTrue());
        callCount.Should().Be(1); // All 3 awaited the same in-flight task!
    }

    [Fact]
    public async Task FetchLyricsAsync_WhenCancelled_ShouldAbortPromptly()
    {
        var handler = new DynamicMockHttpMessageHandler(async req =>
        {
            await Task.Delay(5000);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new LrclibClient(new HttpClient(handler));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = async () => await client.FetchLyricsAsync("Track", "Artist", cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class DynamicMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handlerFunc;

        public DynamicMockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handlerFunc = req => Task.FromResult(handler(req));
        }

        public DynamicMockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncFunc)
        {
            _handlerFunc = asyncFunc;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handlerFunc(request);
        }
    }
}
