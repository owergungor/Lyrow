using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class RateLimitUxTests
{
    [Fact]
    public async Task VerciModule_HandlesRateLimitCooldown_AndBroadcastsFailureEvent()
    {
        var eventBus = new InMemoryEventBus();
        var client = new MockRateLimitedLyricsClient();
        var verci = new VerciModule(
            mediaSessionMonitor: null,
            lyricsClient: client,
            lyricsCache: null,
            eventBus: eventBus,
            settingsManager: null);

        var events = new List<LyricsFetchFailedEvent>();
        using var sub = eventBus.Subscribe<LyricsFetchFailedEvent>(e => events.Add(e));

        var track1 = new MediaTrackInfo("Song 1", "Artist 1", "Album 1", TimeSpan.FromMinutes(3));
        await verci.HandleTrackChangedAsync(track1);

        events.Should().HaveCount(1);
        events[0].IsRateLimited.Should().BeTrue();
        events[0].RetryAfterSeconds.Should().Be(15);
        client.CallCount.Should().Be(1);

        // Second track during active cooldown should not storm the API
        var track2 = new MediaTrackInfo("Song 2", "Artist 2", "Album 2", TimeSpan.FromMinutes(4));
        await verci.HandleTrackChangedAsync(track2);

        events.Should().HaveCount(2);
        events[1].IsRateLimited.Should().BeTrue();
        client.CallCount.Should().Be(1); // API was NOT called again during cooldown!
    }

    private sealed class MockRateLimitedLyricsClient : ILyricsClient
    {
        public int CallCount { get; private set; }

        public Task<LyricsSearchResult> FetchLyricsAsync(
            string trackName,
            string artistName,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var res = new LyricsSearchResult(
                Found: false,
                Lyrics: null,
                Source: "LRCLIB",
                Error: "Rate limited by LRCLIB API. 15 saniye sonra tekrar denenecek.",
                IsRateLimited: true,
                RetryAfterSeconds: 15);

            return Task.FromResult(res);
        }
    }
}
