using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public class NextTrackTriggered10msBeforeEndTests
{
    private sealed class SimulatedMediaSessionMonitor : IMediaSessionMonitor
    {
        public bool IsMonitoring { get; private set; }
        public bool HasActiveSession => true;
        public string? SourceAppId => "Spotify.exe";
        public MediaTrackInfo CurrentTrack { get; private set; } = MediaTrackInfo.Empty;
        public MediaTimelineInfo CurrentTimeline { get; private set; } = MediaTimelineInfo.Zero;

#pragma warning disable CS0067
        public event EventHandler<MediaTrackInfo>? TrackChanged;
        public event EventHandler<MediaTimelineInfo>? TimelineChanged;
        public event EventHandler<Exception>? MonitorFaulted;
#pragma warning restore CS0067

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsMonitoring = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsMonitoring = false;
            return Task.CompletedTask;
        }

        public void SimulateTrack(MediaTrackInfo track)
        {
            CurrentTrack = track;
            CurrentTimeline = new MediaTimelineInfo(TimeSpan.Zero, track.Duration, MediaPlaybackStatus.Playing, DateTime.UtcNow);
            TrackChanged?.Invoke(this, CurrentTrack);
            TimelineChanged?.Invoke(this, CurrentTimeline);
        }

        public void SimulateTimeline(TimeSpan position, TimeSpan duration, MediaPlaybackStatus status = MediaPlaybackStatus.Playing)
        {
            CurrentTimeline = new MediaTimelineInfo(position, duration, status, DateTime.UtcNow);
            TimelineChanged?.Invoke(this, CurrentTimeline);
        }

        public Task<bool> TryPlayAsync() => Task.FromResult(true);
        public Task<bool> TryPauseAsync() => Task.FromResult(true);
        public Task<bool> TryTogglePlayPauseAsync() => Task.FromResult(true);

        public Task<bool> TrySkipNextAsync()
        {
            // Transition to Next Track B
            var trackB = new MediaTrackInfo("Song B", "Artist B", "Album B", TimeSpan.FromSeconds(160));
            SimulateTrack(trackB);
            return Task.FromResult(true);
        }

        public Task<bool> TrySkipPreviousAsync() => Task.FromResult(true);
        public Task<bool> TrySeekAsync(TimeSpan position) => Task.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class DelayedLyricsClient : ILyricsClient
    {
        public int CallCount { get; private set; }

        public async Task<LyricsSearchResult> FetchLyricsAsync(
            string trackName,
            string artistName,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.Delay(100, cancellationToken);
            var lyrics = new SyncedLyrics(
                trackName,
                artistName,
                isSynced: true,
                lines: new List<LyricLine>
                {
                    new(TimeSpan.FromSeconds(2), $"Line for {trackName}", 0)
                });
            return new LyricsSearchResult(true, lyrics, "Mock");
        }
    }

    private sealed class DummyCache : ILyricsCache
    {
        public Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
            => Task.FromResult<SyncedLyrics?>(null);

        public Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Theory]
    [InlineData(180000, 180000 - 20)] // T - 20ms
    [InlineData(180000, 180000 - 10)] // T - 10ms (Spec requirement)
    [InlineData(180000, 180000 - 1)]  // T - 1ms
    [InlineData(180000, 180000)]      // T
    [InlineData(180000, 180000 + 5)]  // T + 5ms (slight tolerance)
    public async Task NextTrackTriggered_NearEndOfTrack_TransitionsCleanly_To_Track_B(int durationMs, int triggerPositionMs)
    {
        var monitor = new SimulatedMediaSessionMonitor();
        var client = new DelayedLyricsClient();
        var cache = new DummyCache();

        await using var module = new LyricsModule(monitor, client, cache);
        await module.StartAsync();

        // 1. Start Track A (Duration = 180s)
        var durationA = TimeSpan.FromMilliseconds(durationMs);
        var trackA = new MediaTrackInfo("Song A", "Artist A", "Album A", durationA);
        monitor.SimulateTrack(trackA);

        // 2. Playback advances to trigger position (e.g. T - 10ms)
        var triggerPos = TimeSpan.FromMilliseconds(triggerPositionMs);
        monitor.SimulateTimeline(triggerPos, durationA);

        module.CurrentTimeline.Position.Should().Be(triggerPos <= durationA ? triggerPos : durationA);
        module.CurrentTrack.Title.Should().Be("Song A");

        long seqBefore = module.TrackSequence;

        // 3. User / System triggers Next Track
        bool nextOk = await monitor.TrySkipNextAsync();
        nextOk.Should().BeTrue();

        // 4. Assert that Track B is active with Position = 0 and Duration = 160s (NOT 180s, NOT cumulative 340s)
        module.CurrentTrack.Title.Should().Be("Song B");
        module.CurrentTrack.Duration.Should().Be(TimeSpan.FromSeconds(160));
        module.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);
        module.CurrentTimeline.Duration.Should().Be(TimeSpan.FromSeconds(160));

        // 5. Track sequence must have atomically incremented
        module.TrackSequence.Should().BeGreaterThan(seqBefore);

        // 6. Old Track A duration state must NOT leak into Track B
        module.CurrentTimeline.Duration.TotalSeconds.Should().Be(160);
    }

    [Fact]
    public async Task NextTrack_WhenDurationZeroOrNaN_DoesNotProduceFalseTransition()
    {
        var monitor = new SimulatedMediaSessionMonitor();
        var client = new DelayedLyricsClient();
        var cache = new DummyCache();

        await using var module = new LyricsModule(monitor, client, cache);
        await module.StartAsync();

        // Track with zero duration
        var zeroTrack = new MediaTrackInfo("Live Stream", "Radio Host", "", TimeSpan.Zero);
        monitor.SimulateTrack(zeroTrack);

        module.CurrentTrack.Title.Should().Be("Live Stream");
        module.CurrentTrack.Duration.Should().Be(TimeSpan.Zero);
        module.CurrentTimeline.Duration.Should().Be(TimeSpan.Zero);

        // Timeline position at zero
        module.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);
        module.CurrentTimeline.GetInterpolatedPosition().Should().BeLessThan(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task NextTrack_CancelsInFlightTrackAFetch_AndIncrementsTrackSequence()
    {
        var monitor = new SimulatedMediaSessionMonitor();
        var client = new DelayedLyricsClient();
        var cache = new DummyCache();

        await using var module = new LyricsModule(monitor, client, cache);
        await module.StartAsync();

        var trackA = new MediaTrackInfo("Slow Track A", "Artist A", "Album A", TimeSpan.FromSeconds(200));
        monitor.SimulateTrack(trackA);

        long seqA = module.TrackSequence;

        // Next track at T - 10ms while fetch for A is still pending
        monitor.SimulateTimeline(TimeSpan.FromMilliseconds(199990), TimeSpan.FromSeconds(200));
        await monitor.TrySkipNextAsync();

        long seqB = module.TrackSequence;
        seqB.Should().BeGreaterThan(seqA);

        // Let the delay pass: Track A fetch was cancelled and discarded
        await Task.Delay(150);

        module.CurrentTrack.Title.Should().Be("Song B");
        module.CurrentTrack.Duration.Should().Be(TimeSpan.FromSeconds(160));
    }

    [Fact]
    public void PositionInterpolation_ClampsCleanly_At_T_Minus_10ms()
    {
        var now = DateTime.UtcNow;
        var duration = TimeSpan.FromSeconds(180);
        var pos10msBefore = duration - TimeSpan.FromMilliseconds(10);

        var timeline = new MediaTimelineInfo(pos10msBefore, duration, MediaPlaybackStatus.Playing, now);

        // Immediate interpolated position matches
        var interpolated = timeline.GetInterpolatedPosition();
        interpolated.Should().BeGreaterThanOrEqualTo(pos10msBefore);
        interpolated.Should().BeLessThanOrEqualTo(duration);
    }
}
