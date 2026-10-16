using Lyrow.Core.Models;

namespace Lyrow.Core.Contracts;

/// <summary>
/// High-performance audio analysis contract producing level calculations and frequency spectra.
/// </summary>
public interface ISpectrumAnalyzer : IDisposable
{
    /// <summary>
    /// Gets or sets the target FFT size (must be a power of two).
    /// </summary>
    int FftSize { get; set; }

    /// <summary>
    /// Gets or sets the visual bar count.
    /// </summary>
    int BarCount { get; set; }

    /// <summary>
    /// Gets or sets the smoothing factor for visual falloff [0.0, 0.95].
    /// </summary>
    float SmoothingFactor { get; set; }

    /// <summary>
    /// Gets or sets sensitivity multiplier.
    /// </summary>
    float Sensitivity { get; set; }

    /// <summary>
    /// Feeds fresh interleaved PCM audio samples into the analysis pipeline.
    /// </summary>
    void PushSamples(ReadOnlySpan<float> samples, int channels, int sampleRate);

    /// <summary>
    /// Computes the current frequency spectrum and level snapshot without allocations.
    /// </summary>
    FrequencySpectrum ComputeSpectrum();

    /// <summary>
    /// Resets internal buffers and smoothing state to zero.
    /// </summary>
    void Reset();
}
