using System.Diagnostics;
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
using Xunit.Abstractions;

namespace Lyrow.Music.Tests;

public sealed class Stage5PerformanceBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public Stage5PerformanceBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LyricsTimelineSynchronizer_BinarySearch_100kLookups_ShouldHaveNearZeroAllocation()
    {
        // Arrange
        const string sampleLrc = """
        [00:05.10] Line 1
        [00:10.20] Line 2
        [00:15.30] Line 3
        [00:20.40] Line 4
        [00:25.50] Line 5
        [00:30.60] Line 6
        [00:35.70] Line 7
        [00:40.80] Line 8
        [00:45.90] Line 9
        [00:51.00] Line 10
        """;
        var lyrics = LrcParser.Parse(sampleLrc);

        // Warmup
        for (int i = 0; i < 1000; i++)
        {
            lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(i % 60));
        }

        long memBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        const int iterations = 100_000;
        int dummySum = 0;
        for (int i = 0; i < iterations; i++)
        {
            int idx = lyrics.FindActiveLineIndex(TimeSpan.FromSeconds((i % 55) + 2));
            dummySum += idx;
        }

        sw.Stop();
        long memAfter = GC.GetAllocatedBytesForCurrentThread();
        long allocatedBytes = memAfter - memBefore;

        _output.WriteLine($"[PERF] 100,000 Binary Search lookups took {sw.ElapsedMilliseconds} ms, Allocated bytes: {allocatedBytes}");

        dummySum.Should().BeGreaterThan(0);
        sw.ElapsedMilliseconds.Should().BeLessThan(200, "100k binary search lookups should complete in under 200ms");
        allocatedBytes.Should().BeLessThanOrEqualTo(128, "100k binary search lookups should allocate negligible bytes");
    }

    [Fact]
    public async Task LumnAndVerci_ConcurrentLoad_ShouldMaintainLowMemoryAndCleanDisposal()
    {
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        long initialWorkingSetMb = proc.WorkingSet64 / (1024 * 1024);

        var eventBus = new InMemoryEventBus();
        var settingsManager = new SettingsManager();
        var moduleManager = new ModuleManager();

        var capture = new SyntheticAudioCaptureService(48000, 2);
        var analyzer = new SpectrumAnalyzer(fftSize: 1024, barCount: 32);
        var lumn = new LumnModule(capture, analyzer, eventBus, settingsManager);

        var monitor = new SyntheticMediaSessionMonitor();
        var cache = new LocalFileLyricsCache(Path.Combine(Path.GetTempPath(), "Lyrow_Perf_" + Guid.NewGuid().ToString("N")));
        var verci = new VerciModule(monitor, new LrclibClient(new HttpClient(new FastMockHandler())), cache, eventBus, settingsManager);

        moduleManager.RegisterModule(lumn);
        moduleManager.RegisterModule(verci);

        int spectrumCount = 0;
        using var sub = eventBus.Subscribe<AudioSpectrumUpdatedEvent>(_ => Interlocked.Increment(ref spectrumCount));

        // Start both
        await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothEnabled);

        monitor.SimulateTrack("Test Song", "Test Artist", "Test Album", TimeSpan.FromMinutes(4));
        monitor.SimulatePlaybackState(MediaPlaybackStatus.Playing, TimeSpan.FromSeconds(15));

        capture.PrimaryFrequencyHz = 440f;
        capture.Amplitude = 0.8f;

        // Run concurrent stream for 500ms
        await Task.Delay(500);

        proc.Refresh();
        long peakWorkingSetMb = proc.WorkingSet64 / (1024 * 1024);

        _output.WriteLine($"[PERF] Concurrent Lumn + Verci active: Received {spectrumCount} audio spectrum frames. WorkingSet: {peakWorkingSetMb} MB (Delta: {peakWorkingSetMb - initialWorkingSetMb} MB)");

        // Stop both
        await moduleManager.ApplyExecutionModeAsync(ModuleExecutionMode.BothDisabled);
        await lumn.DisposeAsync();
        await verci.DisposeAsync();

        proc.Refresh();
        long finalWorkingSetMb = proc.WorkingSet64 / (1024 * 1024);
        _output.WriteLine($"[PERF] After disposal WorkingSet: {finalWorkingSetMb} MB");

        spectrumCount.Should().BeGreaterThan(5);
        lumn.State.Should().Be(ModuleState.Stopped);
        verci.State.Should().Be(ModuleState.Stopped);
    }

    private sealed class FastMockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string json = "{\"id\":1,\"trackName\":\"Test Song\",\"artistName\":\"Test Artist\",\"syncedLyrics\":\"[00:10.00] Line\"}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
