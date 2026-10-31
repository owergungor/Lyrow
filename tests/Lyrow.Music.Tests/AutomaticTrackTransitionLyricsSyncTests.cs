using FluentAssertions;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Xunit;

namespace Lyrow.Music.Tests;

public class AutomaticTrackTransitionLyricsSyncTests
{
    private sealed class ControlledMediaSessionMonitor : IMediaSessionMonitor
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

        public void FireTrackChanged(MediaTrackInfo track)
        {
            CurrentTrack = track;
            TrackChanged?.Invoke(this, track);
        }

        public void FireTimelineChanged(TimeSpan position, TimeSpan duration, MediaPlaybackStatus status = MediaPlaybackStatus.Playing)
        {
            CurrentTimeline = new MediaTimelineInfo(position, duration, status, DateTime.UtcNow);
            TimelineChanged?.Invoke(this, CurrentTimeline);
        }

        public Task<bool> TryPlayAsync() => Task.FromResult(true);
        public Task<bool> TryPauseAsync() => Task.FromResult(true);
        public Task<bool> TryTogglePlayPauseAsync() => Task.FromResult(true);
        public Task<bool> TrySkipNextAsync() => Task.FromResult(true);
        public Task<bool> TrySkipPreviousAsync() => Task.FromResult(true);
        public Task<bool> TrySeekAsync(TimeSpan position) => Task.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ConfigurableLyricsClient : ILyricsClient
    {
        private readonly Func<string, string, CancellationToken, Task<LyricsSearchResult>> _handler;

        public ConfigurableLyricsClient(Func<string, string, CancellationToken, Task<LyricsSearchResult>> handler)
        {
            _handler = handler;
        }

        public Task<LyricsSearchResult> FetchLyricsAsync(
            string trackName,
            string artistName,
            TimeSpan? duration = null,
            CancellationToken cancellationToken = default)
            => _handler(trackName, artistName, cancellationToken);
    }

    private sealed class EmptyLyricsCache : ILyricsCache
    {
        public Task<SyncedLyrics?> GetAsync(string trackName, string artistName, CancellationToken cancellationToken = default)
            => Task.FromResult<SyncedLyrics?>(null);

        public Task SaveAsync(string trackName, string artistName, SyncedLyrics lyrics, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task AutoTransition_TrackA_To_TrackB_ResetsPositionToZero_And_SyncsTrackBLyricsFromStart()
    {
        var monitor = new ControlledMediaSessionMonitor();
        var client = new ConfigurableLyricsClient((title, artist, ct) =>
        {
            var lines = new List<LyricLine>
            {
                new(TimeSpan.FromSeconds(1), $"{title} - Line 0", 0),
                new(TimeSpan.FromSeconds(60), $"{title} - Line 1", 1),
                new(TimeSpan.FromSeconds(150), $"{title} - Line 2", 2)
            };
            return Task.FromResult(new LyricsSearchResult(true, new SyncedLyrics(title, artist, true, lines), "LRCLIB"));
        });

        await using var module = new LyricsModule(monitor, client, new EmptyLyricsCache());
        await module.StartAsync();

        // 1. Track A plays near its end (179.8s of 180s)
        var trackA = new MediaTrackInfo("Track A", "Artist A", "Album A", TimeSpan.FromSeconds(180));
        monitor.FireTrackChanged(trackA);
        await Task.Delay(40);
        monitor.FireTimelineChanged(TimeSpan.FromSeconds(179.8), TimeSpan.FromSeconds(180));
        module.Synchronizer.EvaluatePosition(TimeSpan.FromSeconds(179.8));
        module.ActiveLineIndex.Should().Be(2);

        // 2. Track A finishes automatically and Track B (240s) starts
        var trackB = new MediaTrackInfo("Track B", "Artist B", "Album B", TimeSpan.FromSeconds(240));
        monitor.FireTrackChanged(trackB);

        // Immediately after Track B starts, position must be 0 and active line reset
        module.CurrentTrack.Title.Should().Be("Track B");
        module.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);
        module.CurrentTimeline.Duration.Should().Be(TimeSpan.FromSeconds(240));

        await Task.Delay(50);

        // Track B lyrics loaded; at 1.5s into Track B, Line 0 of Track B must be active (never Line 2 at 179.8s!)
        monitor.FireTimelineChanged(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(240));
        module.Synchronizer.EvaluatePosition(TimeSpan.FromSeconds(1.5));

        module.CurrentLyrics.Title.Should().Be("Track B");
        module.ActiveLineIndex.Should().Be(0);
    }

    [Fact]
    public async Task AutoTransition_StaleTimelineFromTrackA_IsRejected_WhenTrackBIsActive()
    {
        var monitor = new ControlledMediaSessionMonitor();
        var client = new ConfigurableLyricsClient((title, artist, ct) =>
        {
            var lines = new List<LyricLine>
            {
                new(TimeSpan.FromSeconds(2), $"{title} - Intro", 0),
                new(TimeSpan.FromSeconds(120), $"{title} - Chorus", 1)
            };
            return Task.FromResult(new LyricsSearchResult(true, new SyncedLyrics(title, artist, true, lines), "LRCLIB"));
        });

        await using var module = new LyricsModule(monitor, client, new EmptyLyricsCache());
        await module.StartAsync();

        // Track A (180s)
        var trackA = new MediaTrackInfo("Track A", "Artist A", "Album A", TimeSpan.FromSeconds(180));
        monitor.FireTrackChanged(trackA);
        await Task.Delay(30);
        monitor.FireTimelineChanged(TimeSpan.FromSeconds(179.5), TimeSpan.FromSeconds(180));

        // Automatic switch to Track B (240s)
        var trackB = new MediaTrackInfo("Track B", "Artist B", "Album B", TimeSpan.FromSeconds(240));
        monitor.FireTrackChanged(trackB);
        await Task.Delay(30);

        // Late/stale GSMTC timeline callback from Track A arrives carrying Track A's 180s duration & 179.9s position
        monitor.FireTimelineChanged(TimeSpan.FromSeconds(179.9), TimeSpan.FromSeconds(180));

        // Must be rejected! Track B timeline remains at 0s and 240s duration
        module.CurrentTimeline.Position.Should().Be(TimeSpan.Zero);
        module.CurrentTimeline.Duration.Should().Be(TimeSpan.FromSeconds(240));
        module.ActiveLineIndex.Should().Be(-1);
    }

    [Fact]
    public async Task AutoTransition_StaleLrclibResponseFromTrackA_IsDiscarded_NeverOverwritesTrackB()
    {
        var monitor = new ControlledMediaSessionMonitor();
        var tcsTrackA = new TaskCompletionSource<LyricsSearchResult>();

        var client = new ConfigurableLyricsClient(async (title, artist, ct) =>
        {
            if (title == "Track A")
            {
                return await tcsTrackA.Task.WaitAsync(ct);
            }

            var linesB = new List<LyricLine> { new(TimeSpan.FromSeconds(1), "Track B Lyric", 0) };
            return new LyricsSearchResult(true, new SyncedLyrics("Track B", artist, true, linesB), "LRCLIB");
        });

        await using var module = new LyricsModule(monitor, client, new EmptyLyricsCache());
        await module.StartAsync();

        // Start Track A (slow network request)
        monitor.FireTrackChanged(new MediaTrackInfo("Track A", "Artist A", "Album A", TimeSpan.FromSeconds(180)));
        long seqA = module.TrackSequence;

        // Track B starts before Track A's lyrics arrive
        monitor.FireTrackChanged(new MediaTrackInfo("Track B", "Artist B", "Album B", TimeSpan.FromSeconds(210)));
        long seqB = module.TrackSequence;
        seqB.Should().BeGreaterThan(seqA);

        await Task.Delay(40);

        // Unblock Track A response late
        var linesA = new List<LyricLine> { new(TimeSpan.FromSeconds(1), "Stale Track A Lyric", 0) };
        tcsTrackA.TrySetResult(new LyricsSearchResult(true, new SyncedLyrics("Track A", "Artist A", true, linesA), "LRCLIB"));
        await Task.Delay(40);

        // Current lyrics must strictly be Track B
        module.CurrentLyrics.Title.Should().Be("Track B");
        module.CurrentLyrics.Lines.Should().ContainSingle(l => l.Text == "Track B Lyric");
    }

    [Fact]
    public async Task AutoTransition_SeekPauseResume_OnTrackB_SynchronizesCorrectly()
    {
        var monitor = new ControlledMediaSessionMonitor();
        var client = new ConfigurableLyricsClient((title, artist, ct) =>
        {
            var lines = new List<LyricLine>
            {
                new(TimeSpan.FromSeconds(5), "Line 0", 0),
                new(TimeSpan.FromSeconds(15), "Line 1", 1),
                new(TimeSpan.FromSeconds(30), "Line 2", 2)
            };
            return Task.FromResult(new LyricsSearchResult(true, new SyncedLyrics(title, artist, true, lines), "LRCLIB"));
        });

        await using var module = new LyricsModule(monitor, client, new EmptyLyricsCache());
        await module.StartAsync();

        monitor.FireTrackChanged(new MediaTrackInfo("Track B", "Artist B", "Album B", TimeSpan.FromSeconds(200)));
        await Task.Delay(40);

        // Seek to 18s -> Line 1
        monitor.FireTimelineChanged(TimeSpan.FromSeconds(18), TimeSpan.FromSeconds(200), MediaPlaybackStatus.Playing);
        module.ActiveLineIndex.Should().Be(1);

        // Pause at 18s -> stays at Line 1
        monitor.FireTimelineChanged(TimeSpan.FromSeconds(18), TimeSpan.FromSeconds(200), MediaPlaybackStatus.Paused);
        module.CurrentTimeline.Status.Should().Be(MediaPlaybackStatus.Paused);
        module.ActiveLineIndex.Should().Be(1);

        // Seek to 35s -> Line 2
        monitor.FireTimelineChanged(TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(200), MediaPlaybackStatus.Playing);
        module.ActiveLineIndex.Should().Be(2);
    }
}
