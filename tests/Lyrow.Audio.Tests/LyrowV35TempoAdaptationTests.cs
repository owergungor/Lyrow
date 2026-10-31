using System;
using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.Audio.Tests;

public class LyrowV35TempoAdaptationTests
{
    private const int SampleRate = 48000;
    private const int FftSize = 2048;
    private const double FrameDeltaMs = 16.6667;
    private static readonly int SamplesPerFrame = (int)(SampleRate * (FrameDeltaMs / 1000.0));

    private static float[] GenerateSine(int count, float freqHz, float amplitude, ref double phase)
    {
        var samples = new float[count];
        double step = 2.0 * Math.PI * freqHz / SampleRate;
        for (int i = 0; i < count; i++)
        {
            samples[i] = (float)(Math.Sin(phase) * amplitude);
            phase += step;
        }
        return samples;
    }

    [Theory]
    [InlineData(100, 130)]
    [InlineData(120, 90)]
    [InlineData(80, 120)]
    public void AdaptiveTempoTracking_ConvergesQuicklyOnSuddenTempoShift(int startBpm, int targetBpm)
    {
        using var analyzer = new SpectrumAnalyzer(FftSize, 32);
        var engine = new GlowBeatEngine();
        double phase = 0;

        // Phase 1: 5 seconds at startBpm
        int framesPhase1 = (int)(5000.0 / FrameDeltaMs);
        for (int f = 0; f < framesPhase1; f++)
        {
            double curMs = f * FrameDeltaMs;
            double beatPeriod = 60000.0 / startBpm;
            float amp = (curMs % beatPeriod < 45.0) ? 0.88f : 0.03f;
            analyzer.PushSamples(GenerateSine(SamplesPerFrame, 75f, amp, ref phase), 1, SampleRate);
            engine.Process(analyzer.ComputeSpectrum(), 1.2f, FrameDeltaMs);
        }

        engine.EstimatedBpm.Should().BeInRange(startBpm - 3f, startBpm + 3f);

        // Phase 2: 7.5 seconds at targetBpm
        int framesPhase2 = (int)(7500.0 / FrameDeltaMs);
        for (int f = 0; f < framesPhase2; f++)
        {
            double curMs = f * FrameDeltaMs;
            double beatPeriod = 60000.0 / targetBpm;
            float amp = (curMs % beatPeriod < 45.0) ? 0.88f : 0.03f;
            analyzer.PushSamples(GenerateSine(SamplesPerFrame, 75f, amp, ref phase), 1, SampleRate);
            engine.Process(analyzer.ComputeSpectrum(), 1.2f, FrameDeltaMs);
        }

        // Must converge to within 3% of targetBpm
        engine.EstimatedBpm.Should().BeInRange(targetBpm * 0.97f, targetBpm * 1.03f);
        engine.TempoConfidence.Should().BeGreaterThan(0.20f);
    }
}
