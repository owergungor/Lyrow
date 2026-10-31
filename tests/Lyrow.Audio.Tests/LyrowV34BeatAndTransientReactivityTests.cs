using System;
using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.Audio.Tests;

public class LyrowV34BeatAndTransientReactivityTests
{
    private const int SampleRate = 48000;
    private const int FftSize = 2048;

    private static float[] GenerateSineBurstFrame(int frameSamples, float freqHz, float amplitude, ref double phase)
    {
        var samples = new float[frameSamples];
        double step = 2.0 * Math.PI * freqHz / SampleRate;
        for (int i = 0; i < frameSamples; i++)
        {
            samples[i] = (float)(Math.Sin(phase) * amplitude);
            phase += step;
        }
        return samples;
    }

    [Theory]
    [InlineData(100)]
    [InlineData(120)]
    [InlineData(140)]
    public void BeatAndTransientReactivityTests_DetectsPeriodicKickPulsesAndTracksTempo(int targetBpm)
    {
        using var analyzer = new SpectrumAnalyzer(FftSize, 32);
        var engine = new GlowBeatEngine();

        double frameDurationMs = 16.6667; // 60 FPS
        int samplesPerFrame = (int)(SampleRate * (frameDurationMs / 1000.0)); // 800 mono samples per frame
        double beatPeriodMs = 60000.0 / targetBpm;

        double phase = 0.0;
        int totalFrames = 360; // 6 seconds at 60 FPS
        int detectedBeats = 0;

        for (int f = 0; f < totalFrames; f++)
        {
            double currentMs = f * frameDurationMs;
            double posInBeatMs = currentMs % beatPeriodMs;

            // 60ms punchy 80Hz kick pulse on each beat, low ambient floor between beats
            float amp = posInBeatMs < 55.0 ? 0.90f : 0.04f;
            var pcm = GenerateSineBurstFrame(samplesPerFrame, 80f, amp, ref phase);

            analyzer.PushSamples(pcm, channels: 1, sampleRate: SampleRate);
            var spectrum = analyzer.ComputeSpectrum();
            engine.Process(spectrum, sensitivity: 1.2f, frameDeltaMs: frameDurationMs);

            if (engine.IsBeatDetected)
            {
                detectedBeats++;
            }
        }

        // Over 6 seconds, 100 BPM = 10 beats, 120 BPM = 12 beats, 140 BPM = 14 beats
        detectedBeats.Should().BeGreaterThanOrEqualTo(6);
        engine.EstimatedBpm.Should().BeInRange(targetBpm - 22f, targetBpm + 22f);
        engine.TempoConfidence.Should().BeGreaterThan(0.25f);
    }

    [Fact]
    public void BeatAndTransientReactivityTests_HiHatAndSnareTriggerFastTransientWithoutFalseSilenceBeat()
    {
        using var analyzer = new SpectrumAnalyzer(FftSize, 32);
        var engine = new GlowBeatEngine();
        double phase = 0.0;
        int samplesPerFrame = 1024;

        // 1. Feed low steady pad
        for (int i = 0; i < 20; i++)
        {
            var pad = GenerateSineBurstFrame(samplesPerFrame, 300f, 0.05f, ref phase);
            analyzer.PushSamples(pad, 1, SampleRate);
            engine.Process(analyzer.ComputeSpectrum(), 1.0f, 16.6);
        }

        // 2. Sudden high-frequency snare/hi-hat burst (5000 Hz)
        bool transientTriggered = false;
        for (int i = 0; i < 3; i++)
        {
            var hihat = GenerateSineBurstFrame(samplesPerFrame, 5500f, 0.85f, ref phase);
            analyzer.PushSamples(hihat, 1, SampleRate);
            var spec = analyzer.ComputeSpectrum();
            engine.Process(spec, 1.2f, 16.6);
            if (engine.IsTransientDetected) transientTriggered = true;
        }

        transientTriggered.Should().BeTrue();
        engine.TopEdgeIntensity.Should().BeGreaterThan(0.10f);

        // 3. Silence must never produce false beats
        var silentSamples = new float[samplesPerFrame];
        for (int i = 0; i < 30; i++)
        {
            analyzer.PushSamples(silentSamples, 1, SampleRate);
            var spec = analyzer.ComputeSpectrum();
            engine.Process(spec, 1.2f, 16.6);
            engine.IsBeatDetected.Should().BeFalse();
        }
    }
}
