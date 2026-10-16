using FluentAssertions;
using Lyrow.Audio.Analysis;
using Xunit;

namespace Lyrow.Audio.Tests;

public sealed class AudioLevelAnalyzerTests
{
    [Fact]
    public void Calculate_ShouldReturnTheoreticalRmsAndPeak_ForFullScaleSineWave()
    {
        // For a normalized sine wave of amplitude 1.0, theoretical RMS is 1/sqrt(2) ≈ 0.7071
        int sampleCount = 4800;
        float[] sineWave = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            sineWave[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0);
        }

        var levels = AudioLevelAnalyzer.Calculate(sineWave);

        levels.Peak.Should().BeApproximately(1.0f, 0.01f);
        levels.Rms.Should().BeApproximately(0.7071f, 0.02f);
        levels.Decibels.Should().BeApproximately(-3.01f, 0.5f); // 20 * log10(0.7071) ≈ -3 dBFS
        levels.IsSilent.Should().BeFalse();
    }

    [Fact]
    public void Calculate_ShouldDetectSilence_WhenAllSamplesAreZero()
    {
        float[] silence = new float[1024];

        var levels = AudioLevelAnalyzer.Calculate(silence);

        levels.Rms.Should().Be(0f);
        levels.Peak.Should().Be(0f);
        levels.Decibels.Should().Be(-90.0f);
        levels.IsSilent.Should().BeTrue();
    }

    [Fact]
    public void Calculate_ShouldReturnZeroLevels_WhenInputIsEmpty()
    {
        var levels = AudioLevelAnalyzer.Calculate(ReadOnlySpan<float>.Empty);

        levels.Rms.Should().Be(0f);
        levels.Peak.Should().Be(0f);
        levels.IsSilent.Should().BeTrue();
    }
}
