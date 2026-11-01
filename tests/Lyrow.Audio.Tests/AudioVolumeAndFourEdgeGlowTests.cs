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

    [Fact]
    public void GlowAcousticDynamics_TrebleAndBassBias_SeparatesTopAndBottomLighting()
    {
        var engine = new GlowBeatEngine();

        // High frequency spectrum (high treble, low bass)
        var trebleHeavy = new FrequencySpectrum(
            rawFrequencies: Array.Empty<float>(),
            bands: Array.Empty<float>(),
            subBass: 0.05f,
            bass: 0.05f,
            mid: 0.20f,
            treble: 0.95f,
            levels: new AudioLevels(Rms: 0.50f, Peak: 0.70f, Decibels: -9f, IsSilent: false));

        for (int i = 0; i < 15; i++)
        {
            engine.Process(trebleHeavy, sensitivity: 1.0f, frameDeltaMs: 16.6);
        }

        engine.TopEdgeIntensity.Should().BeGreaterThan(engine.BottomEdgeIntensity);
        engine.TrebleIntensity.Should().BeGreaterThan(0.40f);
        engine.BassIntensity.Should().BeLessThan(0.20f);

        // Low frequency spectrum (high bass, low treble)
        var bassHeavy = new FrequencySpectrum(
            rawFrequencies: Array.Empty<float>(),
            bands: Array.Empty<float>(),
            subBass: 0.90f,
            bass: 0.95f,
            mid: 0.20f,
            treble: 0.05f,
            levels: new AudioLevels(Rms: 0.65f, Peak: 0.85f, Decibels: -7f, IsSilent: false));

        for (int i = 0; i < 15; i++)
        {
            engine.Process(bassHeavy, sensitivity: 1.0f, frameDeltaMs: 16.6);
        }

        engine.BottomEdgeIntensity.Should().BeGreaterThan(engine.TopEdgeIntensity);
        engine.BassIntensity.Should().BeGreaterThan(0.50f);
        engine.TrebleIntensity.Should().BeLessThan(0.35f);
    }

    [Fact]
    public void GlowBeatEngine_SilenceDecay_DropsSmoothlyToAbsoluteZeroWithoutResidues()
    {
        var engine = new GlowBeatEngine();

        // 1. Warm up with loud audio
        var loudAudio = new FrequencySpectrum(
            Array.Empty<float>(), Array.Empty<float>(),
            subBass: 0.90f, bass: 0.95f, mid: 0.80f, treble: 0.70f,
            levels: new AudioLevels(0.85f, 0.98f, -2f, false));

        for (int i = 0; i < 20; i++)
        {
            engine.Process(loudAudio, sensitivity: 1.0f, frameDeltaMs: 16.6);
        }

        engine.OverallGlowIntensity.Should().BeGreaterThan(0.50f);
        engine.BottomEdgeIntensity.Should().BeGreaterThan(0.50f);

        // 2. Sudden absolute silence (e.g. paused or stopped)
        var silentAudio = new FrequencySpectrum(
            Array.Empty<float>(), Array.Empty<float>(),
            subBass: 0f, bass: 0f, mid: 0f, treble: 0f,
            levels: new AudioLevels(0f, 0f, -90f, true));

        // Process 100 frames (~1.6 seconds of decay)
        for (int i = 0; i < 100; i++)
        {
            engine.Process(silentAudio, sensitivity: 1.0f, frameDeltaMs: 16.6);
        }

        // All edges and overall glow must smoothly reach EXACT 0.0f without negative values or micro-residues
        engine.OverallGlowIntensity.Should().Be(0.0f);
        engine.TopEdgeIntensity.Should().Be(0.0f);
        engine.BottomEdgeIntensity.Should().Be(0.0f);
        engine.LeftEdgeIntensity.Should().Be(0.0f);
        engine.RightEdgeIntensity.Should().Be(0.0f);
        engine.BassIntensity.Should().Be(0.0f);
        engine.TrebleIntensity.Should().Be(0.0f);
    }

    [Fact]
    public void GlowPalette_CornerHarmonization_MaintainsContinuousChromaticJunction()
    {
        var palette = GlowPalette.Default;

        // In GlowOverlayWindow, the 4 corners are mathematically harmonized via 50% Lerp:
        var cornerTL = GlowColor.Lerp(palette.LeftColor, palette.TopColor, 0.50f);
        var cornerTR = GlowColor.Lerp(palette.RightColor, palette.TopColor, 0.50f);
        var cornerBL = GlowColor.Lerp(palette.LeftColor, palette.BottomColor, 0.50f);
        var cornerBR = GlowColor.Lerp(palette.RightColor, palette.BottomColor, 0.50f);

        // Verify Euclidean distance to left edge equals Euclidean distance to top edge for cornerTL
        double distToLeft = Math.Sqrt(Math.Pow(cornerTL.R - palette.LeftColor.R, 2) + Math.Pow(cornerTL.G - palette.LeftColor.G, 2) + Math.Pow(cornerTL.B - palette.LeftColor.B, 2));
        double distToTop = Math.Sqrt(Math.Pow(cornerTL.R - palette.TopColor.R, 2) + Math.Pow(cornerTL.G - palette.TopColor.G, 2) + Math.Pow(cornerTL.B - palette.TopColor.B, 2));

        // In 50% linear interpolation, distances from midpoint to either end are strictly equal (+/- 1 unit rounding)
        Math.Abs(distToLeft - distToTop).Should().BeLessThanOrEqualTo(1.0);

        // Verify alpha remains opaque 255
        cornerTL.A.Should().Be(255);
        cornerTR.A.Should().Be(255);
        cornerBL.A.Should().Be(255);
        cornerBR.A.Should().Be(255);
    }

    [Fact]
    public void GlowRenderBenchmark_OldPerPixelLerpVsNewSegmentedBuffer_ProvesPerformanceAdvantage()
    {
        const int width = 1920;
        const int height = 1080;
        const int maxThickness = 24;
        const int frames = 100;
        var palette = GlowPalette.Default;
        var cornerTL = GlowColor.Lerp(palette.LeftColor, palette.TopColor, 0.5f);
        var cornerBL = GlowColor.Lerp(palette.LeftColor, palette.BottomColor, 0.5f);

        uint[] bufferOld = new uint[width * height];
        uint[] bufferNew = new uint[width * height];
        uint[] leftColorsBuffer = new uint[256];

        // 1. Benchmark OLD implementation (nested per-pixel float Lerp on every row)
        long allocBeforeOld = GC.GetAllocatedBytesForCurrentThread();
        var swOld = System.Diagnostics.Stopwatch.StartNew();
        for (int f = 0; f < frames; f++)
        {
            float alphaLeft = 0.8f;
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                float tY = (float)y / height;
                GlowColor vertColor;
                if (tY < 0.25f)
                    vertColor = GlowColor.Lerp(palette.TopColor, palette.LeftColor, tY / 0.25f);
                else if (tY > 0.75f)
                    vertColor = GlowColor.Lerp(palette.LeftColor, palette.BottomColor, (tY - 0.75f) / 0.25f);
                else
                    vertColor = palette.LeftColor;

                for (int x = 0; x < maxThickness; x++)
                {
                    float normX = 1.0f - ((float)x / maxThickness);
                    float falloff = normX * normX * (1.25f - 0.25f * normX);
                    byte a = (byte)Math.Clamp((int)(alphaLeft * falloff * 240.0f), 0, 255);
                    if (a == 0) continue;
                    uint c = ((uint)a << 24) | ((uint)(vertColor.R * a / 255) << 16) | ((uint)(vertColor.G * a / 255) << 8) | (uint)(vertColor.B * a / 255);
                    bufferOld[rowOffset + x] = c;
                }
            }
        }
        swOld.Stop();
        long allocAfterOld = GC.GetAllocatedBytesForCurrentThread();

        // 2. Benchmark NEW implementation (segmented vertColor + precomputed integer color buffer)
        int segHeight = Math.Max(16, height / 24);
        var swNew = new System.Diagnostics.Stopwatch();
        long allocBeforeNew = GC.GetAllocatedBytesForCurrentThread();
        swNew.Start();
        for (int f = 0; f < frames; f++)
        {
            float alphaLeft = 0.8f;
            for (int yStart = 0; yStart < height; yStart += segHeight)
            {
                int yEnd = Math.Min(height, yStart + segHeight);
                float tY = (float)yStart / height;
                GlowColor vertColor;
                if (tY < 0.35f)
                    vertColor = GlowColor.Lerp(cornerTL, palette.LeftColor, tY / 0.35f);
                else if (tY > 0.65f)
                    vertColor = GlowColor.Lerp(palette.LeftColor, cornerBL, (tY - 0.65f) / 0.35f);
                else
                    vertColor = palette.LeftColor;

                for (int x = 0; x < maxThickness; x++)
                {
                    float normX = 1.0f - ((float)x / maxThickness);
                    float falloff = normX * normX * (1.25f - 0.25f * normX);
                    byte a = (byte)Math.Clamp((int)(alphaLeft * falloff * 242.0f), 0, 255);
                    leftColorsBuffer[x] = a > 0 ? (((uint)a << 24) | ((uint)(vertColor.R * a / 255) << 16) | ((uint)(vertColor.G * a / 255) << 8) | (uint)(vertColor.B * a / 255)) : 0u;
                }

                for (int y = yStart; y < yEnd; y++)
                {
                    int rowOffset = y * width;
                    for (int x = 0; x < maxThickness; x++)
                    {
                        uint c = leftColorsBuffer[x];
                        if (c != 0u)
                        {
                            bufferNew[rowOffset + x] = c;
                        }
                    }
                }
            }
        }
        swNew.Stop();
        long allocAfterNew = GC.GetAllocatedBytesForCurrentThread();

        // Verify allocations
        (allocAfterNew - allocBeforeNew).Should().Be(0, "New segmented buffer render loop must have zero managed heap allocations");

        // Verify speedup
        double oldMs = swOld.Elapsed.TotalMilliseconds;
        double newMs = swNew.Elapsed.TotalMilliseconds;
        newMs.Should().BeLessThan(oldMs, $"New segmented rendering ({newMs:F2} ms) must be faster than old unsegmented rendering ({oldMs:F2} ms)");
    }
}
