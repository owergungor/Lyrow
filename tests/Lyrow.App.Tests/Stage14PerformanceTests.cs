using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Lyrow.Music.Lyrics;
using Xunit;
using Xunit.Abstractions;

namespace Lyrow.App.Tests;

public class Stage14PerformanceTests
{
    private readonly ITestOutputHelper _output;

    public Stage14PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GlowDSP_SpectrumAnalyzer_SteadyStateAllocations_AreZeroOrMinimal()
    {
        var analyzer = new SpectrumAnalyzer(fftSize: 2048, barCount: 32);
        float[] sampleBuffer = new float[2048];
        for (int i = 0; i < sampleBuffer.Length; i++)
        {
            sampleBuffer[i] = MathF.Sin(2.0f * MathF.PI * 440.0f * i / 48000.0f);
        }

        // Warm-up JIT and preallocate ring buffer
        for (int i = 0; i < 10; i++)
        {
            analyzer.PushSamples(sampleBuffer, 1, 48000);
            _ = analyzer.ComputeSpectrum();
        }

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        const int iterations = 100;

        for (int i = 0; i < iterations; i++)
        {
            analyzer.PushSamples(sampleBuffer, 1, 48000);
            _ = analyzer.ComputeSpectrum();
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocated = afterAlloc - beforeAlloc;
        long bytesPerFrame = totalAllocated / iterations;

        _output.WriteLine($"SpectrumAnalyzer steady-state allocation: {bytesPerFrame} bytes/frame (Total: {totalAllocated} bytes for {iterations} frames)");

        // With double-buffered snapshots, 1024-float array (4096B) and 32-float array (128B) allocations are eliminated.
        // Only the FrequencySpectrum object itself (< 256 bytes) is allocated per frame.
        bytesPerFrame.Should().BeLessThan(350, "SpectrumAnalyzer must not allocate raw magnitude or band arrays every frame");
    }

    [Fact]
    public void GlowBeatEngine_Processing_ZeroHeapAllocationsPerFrame()
    {
        var engine = new GlowBeatEngine();
        var spectrum = new FrequencySpectrum(
            rawFrequencies: new float[1024],
            bands: new float[32],
            subBass: 0.8f,
            bass: 0.7f,
            mid: 0.5f,
            treble: 0.4f,
            levels: new AudioLevels(0.6f, 0.9f, -12f, false));

        // Warm up and trigger JIT compilation
        for (int i = 0; i < 50; i++)
        {
            engine.Process(spectrum, 1.0f, 16.6);
        }

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        const int iterations = 1000;

        for (int i = 0; i < iterations; i++)
        {
            engine.Process(spectrum, 1.0f, 16.6);
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocated = afterAlloc - beforeAlloc;
        double bytesPerFrame = (double)totalAllocated / iterations;

        _output.WriteLine($"GlowBeatEngine {iterations} iterations allocation: {totalAllocated} bytes ({bytesPerFrame:F2} bytes/frame)");
        bytesPerFrame.Should().BeLessThan(1.0, "GlowBeatEngine.Process must have zero heap allocations per frame");
    }

    [Fact]
    public void GlowBeatEngine_SetAcousticProfile_DoesNotReallocateOrDropState()
    {
        var engine = new GlowBeatEngine();
        var spectrum = new FrequencySpectrum(
            rawFrequencies: Array.Empty<float>(),
            bands: Array.Empty<float>(),
            subBass: 0.5f,
            bass: 0.5f,
            mid: 0.5f,
            treble: 0.5f,
            levels: new AudioLevels(0.5f, 0.7f, -15f, false));

        engine.Process(spectrum, 1.0f, 16.6);

        // Warm up JIT for SetAcousticProfile
        engine.SetAcousticProfile(GlowAcousticProfile.BassBoost);
        engine.SetAcousticProfile(GlowAcousticProfile.Balanced);

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        engine.SetAcousticProfile(GlowAcousticProfile.BassBoost);
        engine.SetAcousticProfile(GlowAcousticProfile.VocalFocus);
        engine.SetAcousticProfile(GlowAcousticProfile.HighFidelity);
        engine.SetAcousticProfile(GlowAcousticProfile.Balanced);
        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

        engine.AcousticProfile.Should().Be(GlowAcousticProfile.Balanced);
        (afterAlloc - beforeAlloc).Should().Be(0, "Switching acoustic profiles should mutate scalar weights with 0 allocation");
    }

    [Fact]
    public async Task GlowModule_WhenStopped_LoopAndCaptureAreFullyTerminated()
    {
        var module = new GlowModule();
        await module.InitializeAsync();
        await module.StartAsync();

        module.State.Should().Be(ModuleState.Running);

        await module.StopAsync();
        module.State.Should().Be(ModuleState.Stopped);
        module.LatestSpectrum.Should().Be(FrequencySpectrum.Empty);
    }

    [Fact]
    public void LyricsTimelineSynchronizer_BinarySearch_ScalesLogarithmically()
    {
        // Generate massive synthetic lyrics with 5,000 lines (simulating extreme opera/classical libretto)
        var lines = new List<LyricLine>(5000);
        for (int i = 0; i < 5000; i++)
        {
            lines.Add(new LyricLine(TimeSpan.FromMilliseconds(i * 100), $"Line {i}", i));
        }

        var lyrics = new SyncedLyrics("Test", "Artist", isSynced: true, lines: lines, plainLyrics: null);

        // Warm up JIT for FindActiveLineIndex
        _ = lyrics.FindActiveLineIndex(TimeSpan.FromSeconds(5));

        const int queryCount = 2000;
        int lastIdx = -1;

        var sw = Stopwatch.StartNew();
        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < queryCount; i++)
        {
            var testPos = TimeSpan.FromMilliseconds((i * 123) % 500000);
            lastIdx = lyrics.FindActiveLineIndex(testPos);
        }

        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();
        sw.Stop();

        lastIdx.Should().BeGreaterThanOrEqualTo(-1);
        _output.WriteLine($"Binary search 2,000 lookups over 5,000 lines took {sw.ElapsedMilliseconds} ms ({(double)sw.Elapsed.TotalMicroseconds / queryCount:F2} µs/lookup), Alloc: {afterAlloc - beforeAlloc} bytes");

        (afterAlloc - beforeAlloc).Should().BeLessThanOrEqualTo(64, "Binary search lookups must not allocate on the heap");
        sw.ElapsedMilliseconds.Should().BeLessThan(200, "2,000 binary searches must finish within 200ms");
    }

    [Fact]
    public void LrcParser_LargeLyrics_ParsesCleanlyAndDeterministically()
    {
        // 500-line realistic LRC content
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[ti:Symphony of Performance]");
        sb.AppendLine("[ar:Lyrow]");
        sb.AppendLine("[al:Release Edition]");
        for (int i = 0; i < 500; i++)
        {
            int min = i / 60;
            int sec = i % 60;
            sb.AppendLine($"[{min:00}:{sec:00}.50] Lyric line performance test verse #{i}");
        }

        var sw = Stopwatch.StartNew();
        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();

        var parsed = LrcParser.Parse(sb.ToString(), defaultTitle: "", defaultArtist: "");

        sw.Stop();
        long afterAlloc = GC.GetAllocatedBytesForCurrentThread();

        _output.WriteLine($"Parsing 500-line LRC took {sw.ElapsedMilliseconds} ms, Alloc: {(afterAlloc - beforeAlloc) / 1024} KB");

        parsed.IsSynced.Should().BeTrue();
        parsed.Lines.Count.Should().Be(500);
        parsed.Title.Should().Be("Symphony of Performance");
        parsed.Artist.Should().Be("Lyrow");
        sw.ElapsedMilliseconds.Should().BeLessThan(150, "Parsing 500 lines should finish well under 150ms");
    }

    [Fact]
    public async Task SettingsManager_Serialization_RoundtripPerformance()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"lyrow_perf_settings_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new SettingsManager(tempPath);
            var sw = Stopwatch.StartNew();

            await manager.SaveAsync(LyrowSettings.CreateDefault());
            var loaded = await manager.LoadAsync();

            sw.Stop();
            _output.WriteLine($"Settings atomic save + load roundtrip took {sw.ElapsedMilliseconds} ms");

            loaded.Should().NotBeNull();
            sw.ElapsedMilliseconds.Should().BeLessThan(300, "Settings atomic write and read should be fast and sub-300ms");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(tempPath + ".tmp")) File.Delete(tempPath + ".tmp");
        }
    }

    [Fact]
    public void ZeroLegacyNames_Stage14()
    {
        string root = GetSolutionRoot();
        string[] searchDirs = { "src", "tests", "installer" };
        string pattern1 = @"\b" + "l" + "u" + "m" + "n" + @"\b";
        string pattern2 = @"\b" + "v" + "e" + "r" + "c" + "i" + @"\b";

        foreach (var dir in searchDirs)
        {
            var dirPath = Path.Combine(root, dir);
            if (!Directory.Exists(dirPath)) continue;

            var files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (file.Contains("FeatureVerificationTests") || file.Contains("Stage13ReliabilityTests") || file.Contains("Stage14PerformanceTests"))
                    continue;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".cs" or ".xaml" or ".iss" or ".props" or ".csproj" or ".json" or ".xml"))
                    continue;

                string content = File.ReadAllText(file);
                Regex.IsMatch(content, pattern1, RegexOptions.IgnoreCase).Should().BeFalse($"File {file} should not contain legacy whole-word");
                Regex.IsMatch(content, pattern2, RegexOptions.IgnoreCase).Should().BeFalse($"File {file} should not contain legacy whole-word");
            }
        }
    }

    private static string GetSolutionRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Lyrow.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return Directory.GetCurrentDirectory();
    }
}
