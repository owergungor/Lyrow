using FluentAssertions;
using Lyrow.Audio.Analysis;
using Xunit;

namespace Lyrow.Audio.Tests;

public sealed class FftAnalyzerTests
{
    [Fact]
    public void SetSize_ShouldThrowArgumentException_WhenSizeIsNotPowerOfTwo()
    {
        var act = () => new FftAnalyzer(1000);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*power of two*");
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void SetSize_ShouldAcceptValidPowersOfTwo(int validSize)
    {
        var analyzer = new FftAnalyzer(validSize);
        analyzer.FftSize.Should().Be(validSize);
    }

    [Fact]
    public void Analyze_ShouldDetectAccuratePeakFrequency_For440HzConcertA()
    {
        // 440 Hz sine wave sampled at 48000 Hz, FFT size 2048
        int sampleRate = 48000;
        int fftSize = 2048;
        float targetFrequency = 440.0f;

        var analyzer = new FftAnalyzer(fftSize);
        float[] samples = new float[fftSize];

        for (int i = 0; i < fftSize; i++)
        {
            double t = (double)i / sampleRate;
            samples[i] = (float)Math.Sin(2.0 * Math.PI * targetFrequency * t);
        }

        var magnitudes = analyzer.Analyze(samples);

        // Calculate expected bin: f = bin * (sampleRate / fftSize)
        float binWidth = (float)sampleRate / fftSize; // 48000 / 2048 ≈ 23.4375 Hz
        int expectedBin = (int)Math.Round(targetFrequency / binWidth); // ≈ 19

        // Find the bin with the maximum magnitude
        int peakBin = 0;
        float maxMag = 0f;
        for (int i = 0; i < magnitudes.Length; i++)
        {
            if (magnitudes[i] > maxMag)
            {
                maxMag = magnitudes[i];
                peakBin = i;
            }
        }

        // Peak bin must match expected bin within +/- 1 bin tolerance
        Math.Abs(peakBin - expectedBin).Should().BeLessThanOrEqualTo(1,
            $"Peak bin {peakBin} ({peakBin * binWidth} Hz) should match target 440 Hz bin {expectedBin}");
        maxMag.Should().BeGreaterThan(0.3f);
    }

    [Fact]
    public void Analyze_ShouldDetectAccuratePeakFrequency_For100HzBass()
    {
        int sampleRate = 48000;
        int fftSize = 2048;
        float targetFrequency = 100.0f;

        var analyzer = new FftAnalyzer(fftSize);
        float[] samples = new float[fftSize];

        for (int i = 0; i < fftSize; i++)
        {
            double t = (double)i / sampleRate;
            samples[i] = (float)Math.Sin(2.0 * Math.PI * targetFrequency * t);
        }

        var magnitudes = analyzer.Analyze(samples);

        float binWidth = (float)sampleRate / fftSize;
        int expectedBin = (int)Math.Round(targetFrequency / binWidth); // 100 / 23.4375 ≈ 4

        int peakBin = 0;
        float maxMag = 0f;
        for (int i = 0; i < magnitudes.Length; i++)
        {
            if (magnitudes[i] > maxMag)
            {
                maxMag = magnitudes[i];
                peakBin = i;
            }
        }

        Math.Abs(peakBin - expectedBin).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Analyze_ShouldReturnZeroMagnitudes_WhenInputIsSilent()
    {
        var analyzer = new FftAnalyzer(1024);
        float[] silentSamples = new float[1024];

        var magnitudes = analyzer.Analyze(silentSamples);

        magnitudes.Length.Should().Be(512);
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i].Should().Be(0f);
        }
    }
}
