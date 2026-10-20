using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Audio.Control;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Audio.Tests;

public class AudioVolumeAndFourEdgeGlowTests
{
    [Theory]
    [InlineData(-15, 0)]
    [InlineData(0, 0)]
    [InlineData(35, 35)]
    [InlineData(75, 75)]
    [InlineData(100, 100)]
    [InlineData(125, 100)]
    public void AudioVolumeService_ClampsAndSetsVolumePercentInZeroToHundredRange(int inputPercent, int expectedPercent)
    {
        using var service = new SyntheticAudioVolumeService();
        int? notifiedPercent = null;
        service.VolumeChanged += (_, p) => notifiedPercent = p;

        bool result = service.SetVolumePercent(inputPercent);

        result.Should().BeTrue();
        service.GetVolumePercent().Should().Be(expectedPercent);
        notifiedPercent.Should().Be(expectedPercent);

        string formatted = SyntheticAudioVolumeService.FormatVolumePercentage(service.GetVolumePercent());
        formatted.Should().Be($"{expectedPercent}%");
        formatted.Should().NotContain("dB");
    }

    [Fact]
    public void AudioVolumeService_MuteAndUnmute_TogglesCleanly()
    {
        using var service = new SyntheticAudioVolumeService();
        service.SetVolumePercent(80);
        service.IsMuted.Should().BeFalse();

        // Setting volume > 0 while muted should unmute automatically
        service.SetMute(true).Should().BeTrue();
        service.IsMuted.Should().BeTrue();

        service.SetVolumePercent(65);
        service.IsMuted.Should().BeFalse();
        service.GetVolumePercent().Should().Be(65);

        // ToggleMute flips state cleanly
        service.ToggleMute().Should().BeTrue();
        service.IsMuted.Should().BeTrue();
        service.ToggleMute().Should().BeFalse();
        service.IsMuted.Should().BeFalse();
    }

    [Fact]
    public void AudioVolumeService_SessionSwitchAndReconnect_MaintainsWorkingVolumeControl()
    {
        using var service = new SyntheticAudioVolumeService();
        service.SetVolumePercent(70);

        service.SetPreferredAppId("Spotify.exe");
        service.ActiveTargetName.Should().Be("Spotify.exe");
        service.SetVolumePercent(55);
        service.GetVolumePercent().Should().Be(55);

        service.SetPreferredAppId("AppleMusic.exe");
        service.ActiveTargetName.Should().Be("AppleMusic.exe");
        service.SetVolumePercent(90);
        service.GetVolumePercent().Should().Be(90);

        // Switching back to Spotify restores Spotify's session volume
        service.SetPreferredAppId("Spotify.exe");
        service.GetVolumePercent().Should().Be(55);
    }

    [Fact]
    public void GlowBeatEngine_FourEdges_ComputeIndependentEnergiesFromFrequencyBands()
    {
        var engine = new GlowBeatEngine();

        // Feed bass-dominant audio (drives Bottom edge strongest)
        var bassHeavy = new FrequencySpectrum(
            rawFrequencies: Array.Empty<float>(),
            bands: Array.Empty<float>(),
            subBass: 0.95f,
            bass: 0.95f,
            mid: 0.15f,
            treble: 0.05f,
            levels: new AudioLevels(Rms: 0.65f, Peak: 0.9f, Decibels: -6f, IsSilent: false));

        for (int i = 0; i < 12; i++)
        {
            engine.Process(bassHeavy, sensitivity: 1.0f, frameDeltaMs: 16.6);
        }

        engine.BottomEdgeIntensity.Should().BeGreaterThan(0.55f);
        engine.TopEdgeIntensity.Should().BeGreaterThan(0.05f);
        engine.LeftEdgeIntensity.Should().BeGreaterThan(0.10f);
        engine.RightEdgeIntensity.Should().BeGreaterThan(0.10f);
        engine.BottomEdgeIntensity.Should().BeGreaterThan(engine.TopEdgeIntensity);
    }

    [Fact]
    public void GlowBeatEngine_AttackHoldDecay_FollowsSmoothEnvelopeOnBeat()
    {
        var engine = new GlowBeatEngine();

        // 1. Baseline low energy
        var baseline = new FrequencySpectrum(
            Array.Empty<float>(), Array.Empty<float>(),
            subBass: 0.08f, bass: 0.10f, mid: 0.08f, treble: 0.05f,
            new AudioLevels(Rms: 0.08f, Peak: 0.12f, Decibels: -25f, IsSilent: false));

        for (int i = 0; i < 20; i++)
        {
            engine.Process(baseline, 1.0f, 16.6);
        }

        float beforeBeat = engine.OverallGlowIntensity;

        // 2. Sudden beat (Attack phase + Hold triggered)
        var beat = new FrequencySpectrum(
            Array.Empty<float>(), Array.Empty<float>(),
            subBass: 0.95f, bass: 1.0f, mid: 0.85f, treble: 0.70f,
            new AudioLevels(Rms: 0.90f, Peak: 1.0f, Decibels: -1f, IsSilent: false));

        engine.Process(beat, 1.0f, 16.6);

        engine.IsBeatDetected.Should().BeTrue();
        engine.IsInHoldPhase.Should().BeTrue();
        float peakAttack = engine.OverallGlowIntensity;
        peakAttack.Should().BeGreaterThan(beforeBeat + 0.30f);

        // 3. Immediately drop input back to baseline: during Hold phase (~45ms), intensity does not abruptly drop to zero
        engine.Process(baseline, 1.0f, 16.6);
        engine.OverallGlowIntensity.Should().BeGreaterThan(peakAttack * 0.85f);

        // 4. Advance past Hold phase into smooth Decay phase
        for (int i = 0; i < 15; i++)
        {
            engine.Process(baseline, 1.0f, 16.6);
        }

        engine.IsInHoldPhase.Should().BeFalse();
        engine.OverallGlowIntensity.Should().BeLessThan(peakAttack);
        engine.OverallGlowIntensity.Should().BeGreaterThan(0.05f); // Still smoothly lit, never hard zero
    }

    [Fact]
    public void ArtworkColorExtractor_ExtractPaletteFromRgbaPixels_ExtractsVibrantMultiColorPalette()
    {
        // Create 16x16 RGBA pixel buffer with vibrant purple, magenta, and cyan-blue pixels
        byte[] rgba = new byte[16 * 16 * 4];
        for (int i = 0; i < 16 * 16; i++)
        {
            int idx = i * 4;
            if (i < 120)
            {
                // Vibrant purple
                rgba[idx] = 148;
                rgba[idx + 1] = 42;
                rgba[idx + 2] = 235;
                rgba[idx + 3] = 255;
            }
            else if (i < 200)
            {
                // Vibrant pink/red
                rgba[idx] = 245;
                rgba[idx + 1] = 50;
                rgba[idx + 2] = 135;
                rgba[idx + 3] = 255;
            }
            else
            {
                // Vibrant cyan/blue
                rgba[idx] = 30;
                rgba[idx + 1] = 185;
                rgba[idx + 2] = 250;
                rgba[idx + 3] = 255;
            }
        }

        var palette = ArtworkColorExtractor.ExtractPaletteFromRgbaPixels(rgba);

        palette.BottomColor.Should().NotBe(GlowPalette.Default.BottomColor);
        palette.TopColor.Should().NotBe(palette.BottomColor);

        // Ensure colors are vibrant (not plain white/grey)
        int maxDiff = Math.Max(
            Math.Abs(palette.TopColor.R - palette.TopColor.G),
            Math.Abs(palette.TopColor.G - palette.TopColor.B));
        maxDiff.Should().BeGreaterThan(40);
    }

    [Fact]
    public void GlowPalette_Lerp_SmoothlyCrossfadesBetweenOldAndNewTrackPalettes()
    {
        var paletteA = new GlowPalette(
            top: GlowColor.FromRgb(220, 40, 60),
            bottom: GlowColor.FromRgb(255, 80, 40),
            left: GlowColor.FromRgb(180, 20, 90),
            right: GlowColor.FromRgb(240, 120, 50),
            accent: GlowColor.FromRgb(255, 60, 80));

        var paletteB = new GlowPalette(
            top: GlowColor.FromRgb(30, 120, 240),
            bottom: GlowColor.FromRgb(40, 200, 255),
            left: GlowColor.FromRgb(80, 60, 220),
            right: GlowColor.FromRgb(20, 220, 180),
            accent: GlowColor.FromRgb(60, 180, 255));

        var midPalette = GlowPalette.Lerp(paletteA, paletteB, 0.5f);

        midPalette.TopColor.R.Should().BeInRange(120, 130);
        midPalette.TopColor.B.Should().BeInRange(145, 155);
        midPalette.BottomColor.G.Should().BeInRange(135, 145);
    }
}
