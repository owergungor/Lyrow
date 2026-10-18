using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class GlowAmbientLightingTests
{
    [Fact]
    public void GlowBeatEngine_Silence_DecaysSmoothlyTowardsZero()
    {
        var engine = new GlowBeatEngine();

        // 1. Send high energy frame
        var loudSpectrum = CreateMockSpectrum(rms: 0.8f, bass: 0.9f);
        for (int i = 0; i < 5; i++)
        {
            engine.Process(loudSpectrum, 1.0f, 16.6);
        }

        engine.OverallGlowIntensity.Should().BeGreaterThan(0.3f);

        // 2. Send silence
        float previous = engine.OverallGlowIntensity;
        for (int i = 0; i < 30; i++)
        {
            engine.Process(FrequencySpectrum.Empty, 1.0f, 16.6);
            engine.OverallGlowIntensity.Should().BeLessThanOrEqualTo(previous + 0.001f);
            previous = engine.OverallGlowIntensity;
        }

        // Must decay close to 0
        engine.OverallGlowIntensity.Should().BeLessThan(0.05f);
        engine.TransientEnergy.Should().BeLessThan(0.05f);
    }

    [Fact]
    public void GlowBeatEngine_TransientOnset_TriggersBeatSpike()
    {
        var engine = new GlowBeatEngine();

        // Prime with low baseline (30 frames at 60fps)
        var baseline = CreateMockSpectrum(rms: 0.1f, bass: 0.15f);
        for (int i = 0; i < 30; i++)
        {
            engine.Process(baseline, 1.0f, 16.6);
        }

        // Sudden transient bass punch
        var beatSpectrum = CreateMockSpectrum(rms: 0.95f, bass: 1.0f);
        engine.Process(beatSpectrum, 1.0f, 16.6);

        engine.IsBeatDetected.Should().BeTrue();
        engine.TransientEnergy.Should().BeGreaterThan(0.4f);
        engine.BassIntensity.Should().BeGreaterThan(0.5f);
    }

    [Fact]
    public void ArtworkColorExtractor_ValidImage_ExtractsDominantAndAccentColors()
    {
        // Synthesize test image data with dominant red/orange tones
        byte[] fakeJpeg = new byte[256];
        fakeJpeg[0] = 0xFF; fakeJpeg[1] = 0xD8; fakeJpeg[2] = 0xFF; // JPEG magic header
        for (int i = 4; i < fakeJpeg.Length; i += 3)
        {
            fakeJpeg[i] = 255;   // Red
            fakeJpeg[i + 1] = 80; // Orange-tinted green
            fakeJpeg[i + 2] = 20; // Low blue
        }

        var palette = ArtworkColorExtractor.ExtractPalette(fakeJpeg);

        palette.Should().NotBeNull();
        palette.BottomColor.R.Should().BeGreaterThan(150);
    }

    [Fact]
    public void GlowPalette_Lerp_PerformsSmoothCrossFadeTransition()
    {
        var oldPalette = GlowPalette.Default;
        var newPalette = GlowPalette.Aurora;

        var start = GlowPalette.Lerp(oldPalette, newPalette, 0.0f);
        start.TopColor.Should().Be(oldPalette.TopColor);

        var mid = GlowPalette.Lerp(oldPalette, newPalette, 0.5f);
        mid.TopColor.R.Should().Be((byte)((oldPalette.TopColor.R + newPalette.TopColor.R) / 2));

        var end = GlowPalette.Lerp(oldPalette, newPalette, 1.0f);
        end.TopColor.Should().Be(newPalette.TopColor);
    }

    private static FrequencySpectrum CreateMockSpectrum(float rms, float bass)
    {
        var levels = new AudioLevels(rms, rms, -10f, false);
        var bands = new float[32];
        for (int i = 0; i < 8; i++) bands[i] = bass;
        return new FrequencySpectrum(new float[32], bands, bass * 0.8f, bass, 0.3f, 0.2f, levels);
    }
}
