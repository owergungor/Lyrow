namespace Lyrow.Audio.Analysis;

/// <summary>
/// Aggregates linear FFT frequency bins into perceptually-spaced logarithmic bands
/// and classic musical frequency ranges (Sub-bass, Bass, Mid, Treble).
/// </summary>
public sealed class FrequencyBandProcessor
{
    private float[] _smoothedBands = [];
    private float[] _prevMagnitudes = [];
    private int _barCount;

    public FrequencyBandProcessor(int barCount = 32)
    {
        SetBarCount(barCount);
    }

    public int BarCount => _barCount;
    public float LatestLowMid { get; private set; }
    public float LatestHighMid { get; private set; }
    public float LatestSpectralFlux { get; private set; }
    public float LatestBassFlux { get; private set; }
    public float LatestMidFlux { get; private set; }
    public float LatestTrebleFlux { get; private set; }

    public void SetBarCount(int barCount)
    {
        if (barCount <= 0) throw new ArgumentOutOfRangeException(nameof(barCount), "Bar count must be positive.");
        if (_barCount == barCount && _smoothedBands.Length == barCount) return;

        _barCount = barCount;
        _smoothedBands = new float[barCount];
    }

    /// <summary>
    /// Processes FFT magnitudes into logarithmic bars and 5 musical frequency ranges with spectral flux and smoothing.
    /// </summary>
    public void Process(
        ReadOnlySpan<float> magnitudes,
        int sampleRate,
        Span<float> targetBands,
        out float subBass,
        out float bass,
        out float mid,
        out float treble,
        float smoothing = 0.65f,
        float sensitivity = 1.0f)
    {
        if (targetBands.Length < _barCount)
        {
            throw new ArgumentException($"Target bands span length ({targetBands.Length}) must be at least {_barCount}.", nameof(targetBands));
        }

        int binCount = magnitudes.Length;
        if (binCount == 0 || sampleRate <= 0)
        {
            subBass = bass = mid = treble = 0f;
            LatestLowMid = LatestHighMid = LatestSpectralFlux = LatestBassFlux = LatestMidFlux = LatestTrebleFlux = 0f;
            for (int i = 0; i < _barCount; i++)
            {
                _smoothedBands[i] *= smoothing;
                targetBands[i] = _smoothedBands[i];
            }
            if (_prevMagnitudes.Length > 0)
            {
                Array.Clear(_prevMagnitudes);
            }
            return;
        }

        if (_prevMagnitudes.Length != binCount)
        {
            _prevMagnitudes = new float[binCount];
        }

        float binWidth = (sampleRate / 2.0f) / binCount;

        // 1. Calculate 5 musical frequency bands (Sub/Bass: 20-160Hz, Low-Mid: 160-500Hz, Mid: 500-2000Hz, High-Mid: 2000-6000Hz, Treble: 6000-16000Hz)
        // Also retain standard SubBass/Bass/Mid/Treble outputs for compatibility
        subBass = CalculateRangeEnergy(magnitudes, binWidth, 20f, 65f) * sensitivity * 1.5f;
        bass = CalculateRangeEnergy(magnitudes, binWidth, 55f, 220f) * sensitivity * 1.35f;
        LatestLowMid = CalculateRangeEnergy(magnitudes, binWidth, 160f, 500f) * sensitivity * 1.2f;
        mid = CalculateRangeEnergy(magnitudes, binWidth, 250f, 3500f) * sensitivity;
        LatestHighMid = CalculateRangeEnergy(magnitudes, binWidth, 2000f, 6000f) * sensitivity * 1.15f;
        treble = CalculateRangeEnergy(magnitudes, binWidth, 4000f, 18000f) * sensitivity * 1.25f;

        // 2. Calculate half-wave rectified Spectral Flux: SF(t) = sum(max(0, X(t,k) - X(t-1,k)))
        float totalFlux = 0f;
        float bassFlux = 0f;
        float midFlux = 0f;
        float trebleFlux = 0f;
        int bassEndBin = Math.Clamp((int)MathF.Ceiling(200f / binWidth), 1, binCount);
        int midEndBin = Math.Clamp((int)MathF.Ceiling(3500f / binWidth), bassEndBin + 1, binCount);
        int trebleEndBin = Math.Clamp((int)MathF.Ceiling(16000f / binWidth), midEndBin + 1, binCount);

        for (int k = 1; k < trebleEndBin; k++)
        {
            float curr = magnitudes[k];
            float prev = _prevMagnitudes[k];
            float diff = curr - prev;
            _prevMagnitudes[k] = curr;

            if (diff > 0f)
            {
                totalFlux += diff;
                if (k < bassEndBin)
                {
                    bassFlux += diff * 1.8f; // Strong weight on kick/bass onsets
                }
                else if (k < midEndBin)
                {
                    midFlux += diff * 1.2f; // Snare/clap/vocal transients
                }
                else
                {
                    trebleFlux += diff * 1.4f; // Hi-hat/cymbal transients
                }
            }
        }

        // Copy remaining bins if any
        for (int k = trebleEndBin; k < binCount; k++)
        {
            _prevMagnitudes[k] = magnitudes[k];
        }

        LatestSpectralFlux = Math.Clamp(totalFlux * sensitivity * 0.45f, 0f, 4.0f);
        LatestBassFlux = Math.Clamp(bassFlux * sensitivity * 0.65f, 0f, 4.0f);
        LatestMidFlux = Math.Clamp(midFlux * sensitivity * 0.45f, 0f, 4.0f);
        LatestTrebleFlux = Math.Clamp(trebleFlux * sensitivity * 0.55f, 0f, 4.0f);

        // 2. Map to logarithmic visualization bars (from minFreq = 20Hz to maxFreq = min(sampleRate/2, 20000Hz))
        float minFreq = 25f;
        float maxFreq = MathF.Min(sampleRate / 2.0f, 18000f);
        float logMin = MathF.Log10(minFreq);
        float logMax = MathF.Log10(maxFreq);
        float logRange = (logMax - logMin) / _barCount;

        for (int i = 0; i < _barCount; i++)
        {
            float fStart = MathF.Pow(10f, logMin + i * logRange);
            float fEnd = MathF.Pow(10f, logMin + (i + 1) * logRange);

            int startBin = Math.Clamp((int)(fStart / binWidth), 0, binCount - 1);
            int endBin = Math.Clamp((int)MathF.Ceiling(fEnd / binWidth), startBin + 1, binCount);

            float sum = 0f;
            int count = 0;
            for (int b = startBin; b < endBin; b++)
            {
                sum += magnitudes[b];
                count++;
            }

            float rawBarValue = count > 0 ? (sum / count) * sensitivity : 0f;

            // Apply logarithmic perceptual boosting for higher frequencies
            float freqBoost = 1.0f + (i / (float)_barCount) * 1.2f;
            rawBarValue *= freqBoost;

            // Normalize and smooth falloff
            float currentSmoothed = _smoothedBands[i];
            float targetValue = Math.Clamp(rawBarValue, 0f, 1f);

            if (targetValue >= currentSmoothed)
            {
                // Quick rise
                _smoothedBands[i] = targetValue;
            }
            else
            {
                // Smooth exponential falloff
                _smoothedBands[i] = (currentSmoothed * smoothing) + (targetValue * (1.0f - smoothing));
            }

            targetBands[i] = Math.Clamp(_smoothedBands[i], 0f, 1f);
        }
    }

    private static float CalculateRangeEnergy(ReadOnlySpan<float> magnitudes, float binWidth, float minHz, float maxHz)
    {
        int startBin = Math.Clamp((int)(minHz / binWidth), 0, magnitudes.Length - 1);
        int endBin = Math.Clamp((int)MathF.Ceiling(maxHz / binWidth), startBin + 1, magnitudes.Length);

        float sum = 0f;
        float max = 0f;
        int count = 0;
        for (int i = startBin; i < endBin; i++)
        {
            float m = magnitudes[i];
            if (m > max) max = m;
            sum += m;
            count++;
        }

        if (count == 0) return 0f;
        float avg = sum / count;
        return Math.Clamp(max * 0.7f + avg * 2.0f, 0f, 1f);
    }

    public void Reset()
    {
        Array.Clear(_smoothedBands);
        Array.Clear(_prevMagnitudes);
        LatestLowMid = LatestHighMid = LatestSpectralFlux = LatestBassFlux = LatestMidFlux = LatestTrebleFlux = 0f;
    }
}
