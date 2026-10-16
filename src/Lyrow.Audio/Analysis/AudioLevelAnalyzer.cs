using Lyrow.Core.Models;

namespace Lyrow.Audio.Analysis;

/// <summary>
/// Calculates power, peak amplitude and loudness decibels from audio sample blocks.
/// </summary>
public static class AudioLevelAnalyzer
{
    private const float Epsilon = 1e-7f;

    /// <summary>
    /// Computes RMS, peak and decibels from a float PCM sample block.
    /// </summary>
    /// <param name="samples">PCM float samples in range [-1.0, 1.0].</param>
    /// <param name="silenceThresholdDb">dB threshold below which audio is marked silent (e.g. -65 dB).</param>
    public static AudioLevels Calculate(ReadOnlySpan<float> samples, float silenceThresholdDb = -65.0f)
    {
        if (samples.IsEmpty)
        {
            return AudioLevels.Zero;
        }

        float sumSquares = 0f;
        float peak = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float s = samples[i];
            float abs = MathF.Abs(s);
            if (abs > peak)
            {
                peak = abs;
            }
            sumSquares += s * s;
        }

        float rms = MathF.Sqrt(sumSquares / samples.Length);
        float safeRms = Math.Max(rms, Epsilon);

        // dBFS formula: 20 * log10(RMS)
        float decibels = 20.0f * MathF.Log10(safeRms);
        if (float.IsInfinity(decibels) || float.IsNaN(decibels) || decibels < -90.0f)
        {
            decibels = -90.0f;
        }

        bool isSilent = decibels < silenceThresholdDb || rms < 0.0002f;

        return new AudioLevels(
            Rms: Math.Clamp(rms, 0f, 1f),
            Peak: Math.Clamp(peak, 0f, 1f),
            Decibels: Math.Clamp(decibels, -90f, 0f),
            IsSilent: isSilent);
    }
}
