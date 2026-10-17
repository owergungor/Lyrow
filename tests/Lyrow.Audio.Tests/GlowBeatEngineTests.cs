using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.Audio.Tests;

public class GlowBeatEngineTests
{
    private static FrequencySpectrum CreateSpectrum(
        float rms,
        float bass,
        float subBass,
        float mid,
        float treble,
        bool isSilent = false)
    {
        var levels = new AudioLevels(
            Rms: rms,
            Peak: rms * 1.4f,
            Decibels: isSilent ? -90f : -10f,
            IsSilent: isSilent);

        return new FrequencySpectrum(
            rawFrequencies: Array.Empty<float>(),
            bands: Array.Empty<float>(),
            subBass: subBass,
            bass: bass,
            mid: mid,
            treble: treble,
            levels: levels);
    }

    [Fact]
    public void Silence_DecaysGlowIntensitySmoothlyToZero()
    {
        var engine = new GlowBeatEngine();

        // Feed some active energy first
        var active = CreateSpectrum(rms: 0.6f, bass: 0.8f, subBass: 0.7f, mid: 0.5f, treble: 0.3f);
        for (int i = 0; i < 10; i++)
        {
            engine.Process(active);
        }

        engine.OverallGlowIntensity.Should().BeGreaterThan(0.2f);

        // Now feed silence
        var silent = CreateSpectrum(rms: 0f, bass: 0f, subBass: 0f, mid: 0f, treble: 0f, isSilent: true);
        float prevIntensity = engine.OverallGlowIntensity;

        for (int i = 0; i < 20; i++)
        {
            engine.Process(silent);
            engine.OverallGlowIntensity.Should().BeLessThanOrEqualTo(prevIntensity);
            prevIntensity = engine.OverallGlowIntensity;
        }

        engine.OverallGlowIntensity.Should().BeLessThan(0.05f);
        engine.TransientEnergy.Should().BeLessThan(0.05f);
    }

    [Fact]
    public void LowEnergy_ProducesSubtleGlow()
    {
        var engine = new GlowBeatEngine();
        var lowEnergy = CreateSpectrum(rms: 0.08f, bass: 0.1f, subBass: 0.05f, mid: 0.1f, treble: 0.05f);

        for (int i = 0; i < 15; i++)
        {
            engine.Process(lowEnergy);
        }

        engine.OverallGlowIntensity.Should().BeGreaterThan(0.02f);
        engine.OverallGlowIntensity.Should().BeLessThan(0.35f);
    }

    [Fact]
    public void HighEnergy_ProducesStrongGlow()
    {
        var engine = new GlowBeatEngine();
        var highEnergy = CreateSpectrum(rms: 0.85f, bass: 0.95f, subBass: 0.9f, mid: 0.8f, treble: 0.7f);

        for (int i = 0; i < 15; i++)
        {
            engine.Process(highEnergy);
        }

        engine.OverallGlowIntensity.Should().BeGreaterThan(0.65f);
        engine.BassIntensity.Should().BeGreaterThan(0.60f);
    }

    [Fact]
    public void TransientSpike_TriggersBeatDetection()
    {
        var engine = new GlowBeatEngine();

        // 1. Establish low baseline energy over 30 frames at 60fps (500ms)
        var baseline = CreateSpectrum(rms: 0.1f, bass: 0.15f, subBass: 0.1f, mid: 0.1f, treble: 0.1f);
        for (int i = 0; i < 30; i++)
        {
            engine.Process(baseline, frameDeltaMs: 16.6);
        }

        engine.IsBeatDetected.Should().BeFalse();
        float transientBefore = engine.TransientEnergy;

        // 2. Deliver a sudden, sharp bass beat transient spike
        var beatSpike = CreateSpectrum(rms: 0.95f, bass: 1.0f, subBass: 1.0f, mid: 0.7f, treble: 0.5f);
        engine.Process(beatSpike, frameDeltaMs: 16.6);

        engine.IsBeatDetected.Should().BeTrue();
        engine.TransientEnergy.Should().BeGreaterThan(transientBefore);
    }

    [Fact]
    public void BandSmoothing_MaintainsIndependentBandSeparation()
    {
        var engine = new GlowBeatEngine();

        // Bass-heavy audio (Sub/Bass = 0.9, Treble = 0.05)
        var bassHeavy = CreateSpectrum(rms: 0.5f, bass: 0.9f, subBass: 0.85f, mid: 0.2f, treble: 0.05f);
        for (int i = 0; i < 10; i++)
        {
            engine.Process(bassHeavy);
        }

        engine.BassIntensity.Should().BeGreaterThan(engine.TrebleIntensity);
        engine.BassIntensity.Should().BeGreaterThan(0.5f);
    }

    [Fact]
    public void Reset_RestoresZeroState()
    {
        var engine = new GlowBeatEngine();
        var active = CreateSpectrum(rms: 0.9f, bass: 0.9f, subBass: 0.9f, mid: 0.9f, treble: 0.9f);
        for (int i = 0; i < 10; i++) engine.Process(active);

        engine.OverallGlowIntensity.Should().BeGreaterThan(0.5f);

        engine.Reset();

        engine.OverallGlowIntensity.Should().Be(0f);
        engine.TransientEnergy.Should().Be(0f);
        engine.BassIntensity.Should().Be(0f);
        engine.IsBeatDetected.Should().BeFalse();
    }
}
