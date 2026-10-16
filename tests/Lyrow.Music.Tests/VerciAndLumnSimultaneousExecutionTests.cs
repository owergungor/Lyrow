using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Audio.Capture;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Lyrow.Music.Lyrics;
using Lyrow.Music.Session;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class VerciAndLumnSimultaneousExecutionTests
{
    [Fact]
    public async Task LumnAndVerci_SimultaneousExecution_ShouldRunConcurrently_WithIsolatedEventContracts()
    {
        // 1. Shared Infrastructure
        var eventBus = new InMemoryEventBus();
        var settingsManager = new SettingsManager();
        var moduleManager = new ModuleManager();

        // 2. Lumn setup (Synthetic for deterministic isolation testing)
        var audioCapture = new SyntheticAudioCaptureService(48000, 2);
        var spectrumAnalyzer = new SpectrumAnalyzer(fftSize: 1024, barCount: 32);
        var lumnModule = new LumnModule(audioCapture, spectrumAnalyzer, eventBus, settingsManager);

        // 3. Verci setup (Synthetic for deterministic isolation testing)
        var mediaMonitor = new SyntheticMediaSessionMonitor();
        var lyricsCache = new LocalFileLyricsCache(Path.Combine(Path.GetTempPath(), "Lyrow_Simultaneous_" + Guid.NewGuid().ToString("N")));
        
        var mockHttpClient = new HttpClient(new MockHttpHandler());
        var lyricsClient = new LrclibClient(mockHttpClient);
        var verciModule = new VerciModule(mediaMonitor, lyricsClient, lyricsCache, eventBus, settingsManager);

        // 4. Register both in ModuleManager
        moduleManager.RegisterModule(lumnModule);
        moduleManager.RegisterModule(verciModule);

        // Event collectors
        var audioEvents = new List<AudioSpectrumUpdatedEvent>();
        var trackEvents = new List<MediaTrackChangedEvent>();
        var lyricsEvents = new List<LyricsLoadedEvent>();

        using var sub1 = eventBus.Subscribe<AudioSpectrumUpdatedEvent>(e => { lock (audioEvents) audioEvents.Add(e); });
        using var sub2 = eventBus.Subscribe<MediaTrackChangedEvent>(e => { lock (trackEvents) trackEvents.Add(e); });
        using var sub3 = eventBus.Subscribe<LyricsLoadedEvent>(e => { lock (lyricsEvents) lyricsEvents.Add(e); });

        // Act 1: Apply BothEnabled
        await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothEnabled);
        moduleManager.CurrentExecutionMode.Should().Be(ModuleExecutionMode.BothEnabled);
        lumnModule.State.Should().Be(ModuleState.Running);
        verciModule.State.Should().Be(ModuleState.Running);

        // Act 2: Simulate real media playback in Verci
        mediaMonitor.SimulateTrack("Bohemian Rhapsody", "Queen", "A Night at the Opera", TimeSpan.FromSeconds(354));
        mediaMonitor.SimulatePlaybackState(MediaPlaybackStatus.Playing, TimeSpan.FromSeconds(30));

        // Act 3: Feed audio into Lumn
        audioCapture.PrimaryFrequencyHz = 220f;
        audioCapture.Amplitude = 0.9f;

        // Allow concurrent pipeline processing
        await Task.Delay(300);

        // Assert 1: Both received events independently
        lock (audioEvents)
        {
            audioEvents.Should().NotBeEmpty("Lumn must emit spectrum events via event bus");
            audioEvents.Last().Spectrum.Levels.IsSilent.Should().BeFalse();
        }

        lock (trackEvents)
        {
            trackEvents.Should().NotBeEmpty("Verci must emit track events via event bus");
            trackEvents.Last().Track.Title.Should().Be("Bohemian Rhapsody");
        }

        lock (lyricsEvents)
        {
            lyricsEvents.Should().NotBeEmpty("Verci must load and emit lyrics event");
        }

        // Act 4: Mode Switch to VerciOnly
        await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.VerciOnly);
        lumnModule.State.Should().Be(ModuleState.Stopped);
        verciModule.State.Should().Be(ModuleState.Running);
        audioCapture.IsCapturing.Should().BeFalse();
        mediaMonitor.IsMonitoring.Should().BeTrue();

        // Act 5: Mode Switch to LumnOnly
        await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.LumnOnly);
        lumnModule.State.Should().Be(ModuleState.Running);
        verciModule.State.Should().Be(ModuleState.Stopped);
        audioCapture.IsCapturing.Should().BeTrue();
        mediaMonitor.IsMonitoring.Should().BeFalse();

        // Act 6: Mode Switch to BothDisabled
        await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothDisabled);
        lumnModule.State.Should().Be(ModuleState.Stopped);
        verciModule.State.Should().Be(ModuleState.Stopped);
        audioCapture.IsCapturing.Should().BeFalse();
        mediaMonitor.IsMonitoring.Should().BeFalse();

        // Clean teardown
        await lumnModule.DisposeAsync();
        await verciModule.DisposeAsync();
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string json = """
            {
                "id": 42,
                "trackName": "Bohemian Rhapsody",
                "artistName": "Queen",
                "syncedLyrics": "[00:00.50] Is this the real life?\n[00:05.20] Is this just fantasy?"
            }
            """;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
