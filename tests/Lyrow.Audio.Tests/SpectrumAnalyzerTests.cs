using FluentAssertions;
using Lyrow.Audio.Analysis;
using Xunit;

namespace Lyrow.Audio.Tests;

public sealed class SpectrumAnalyzerTests
{
    [Fact]
    public void ComputeSpectrum_ShouldDetectSilenceAndSkipExpensiveFft()
    {
        using var analyzer = new SpectrumAnalyzer(fftSize: 1024, barCount: 16);
        float[] silentStereo = new float[2048]; // 1024 stereo samples

        analyzer.PushSamples(silentStereo, channels: 2, sampleRate: 48000);
        var spectrum = analyzer.ComputeSpectrum();

        spectrum.Levels.IsSilent.Should().BeTrue();
        spectrum.SubBass.Should().Be(0f);
        spectrum.Bass.Should().Be(0f);
        spectrum.Mid.Should().Be(0f);
        spectrum.Treble.Should().Be(0f);
    }

    [Fact]
    public void ComputeSpectrum_ShouldProcessStereoSignalCorrectly()
    {
        using var analyzer = new SpectrumAnalyzer(fftSize: 1024, barCount: 16);
        int sampleCount = 1024;
        float[] stereoSamples = new float[sampleCount * 2];

        // 440 Hz tone in left channel, right channel silent
        for (int i = 0; i < sampleCount; i++)
        {
            float s = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / 48000.0);
            stereoSamples[i * 2] = s;     // Left
            stereoSamples[i * 2 + 1] = 0f; // Right
        }

        analyzer.PushSamples(stereoSamples, channels: 2, sampleRate: 48000);
        var spectrum = analyzer.ComputeSpectrum();

        spectrum.Levels.IsSilent.Should().BeFalse();
        spectrum.Levels.Peak.Should().BeApproximately(0.5f, 0.05f); // Half because mono = (s + 0)/2
        spectrum.Bands.Length.Should().Be(16);
        spectrum.Mid.Should().BeGreaterThan(0.01f);
    }

    [Fact]
    public void Reset_ShouldClearAllBuffersAndState()
    {
        using var analyzer = new SpectrumAnalyzer(fftSize: 512, barCount: 8);
        float[] noise = new float[1024];
        Array.Fill(noise, 0.5f);

        analyzer.PushSamples(noise, channels: 1, sampleRate: 48000);
        analyzer.Reset();

        var spectrum = analyzer.ComputeSpectrum();
        spectrum.Levels.IsSilent.Should().BeTrue();
        spectrum.Levels.Rms.Should().Be(0f);
    }
}
