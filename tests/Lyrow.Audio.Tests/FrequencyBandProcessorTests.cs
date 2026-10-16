using FluentAssertions;
using Lyrow.Audio.Analysis;
using Xunit;

namespace Lyrow.Audio.Tests;

public sealed class FrequencyBandProcessorTests
{
    [Fact]
    public void Process_ShouldRouteSubBassEnergy_WhenGivenLowFrequencyMagnitudes()
    {
        var processor = new FrequencyBandProcessor(barCount: 32);
        int binCount = 1024;
        float[] magnitudes = new float[binCount];
        float[] bands = new float[32];

        // Place energy squarely in 40 Hz bin (48000 / 2048 ≈ 23.43 Hz/bin, so bin 2 ≈ 46.8 Hz)
        magnitudes[2] = 1.0f;

        processor.Process(
            magnitudes,
            sampleRate: 48000,
            bands,
            out float subBass,
            out float bass,
            out float mid,
            out float treble);

        subBass.Should().BeGreaterThan(0.01f);
        mid.Should().Be(0f);
        treble.Should().Be(0f);
    }

    [Fact]
    public void Process_ShouldSmoothFallingPeaks_WhenDecaying()
    {
        var processor = new FrequencyBandProcessor(barCount: 8);
        int binCount = 256;
        float[] loudMagnitudes = new float[binCount];
        Array.Fill(loudMagnitudes, 0.8f);

        float[] bandsFrame1 = new float[8];
        float[] bandsFrame2 = new float[8];

        // Frame 1: Loud signal
        processor.Process(
            loudMagnitudes,
            sampleRate: 48000,
            bandsFrame1,
            out _, out _, out _, out _,
            smoothing: 0.8f);

        // Frame 2: Instant silence
        processor.Process(
            ReadOnlySpan<float>.Empty,
            sampleRate: 48000,
            bandsFrame2,
            out _, out _, out _, out _,
            smoothing: 0.8f);

        // Frame 2 bands should have decayed smoothly, not dropped instantly to zero
        for (int i = 0; i < 8; i++)
        {
            bandsFrame2[i].Should().BeGreaterThan(0f);
            bandsFrame2[i].Should().BeLessThan(bandsFrame1[i]);
        }
    }
}
