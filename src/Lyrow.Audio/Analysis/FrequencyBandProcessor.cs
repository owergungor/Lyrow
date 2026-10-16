namespace Lyrow.Audio.Analysis;

/// <summary>
/// Aggregates linear FFT frequency bins into perceptually-spaced logarithmic bands
/// and classic musical frequency ranges (Sub-bass, Bass, Mid, Treble).
/// </summary>
public sealed class FrequencyBandProcessor
{
    private float[] _smoothedBands = [];
    private int _barCount;

    public FrequencyBandProcessor(int barCount = 32)
    {
        SetBarCount(barCount);
    }

    public int BarCount => _barCount;

    public void SetBarCount(int barCount)
    {
        if (barCount <= 0) throw new ArgumentOutOfRangeException(nameof(barCount), "Bar count must be positive.");
        if (_barCount == barCount && _smoothedBands.Length == barCount) return;

        _barCount = barCount;
        _smoothedBands = new float[barCount];
    }

    /// <summary>
    /// Processes FFT magnitudes into logarithmic bars and 4 main frequency ranges with smoothing.
    /// </summary>
    /// <param name="magnitudes">Positive frequency bin magnitudes from FFT [0 .. FftSize/2 - 1].</param>
    /// <param name="sampleRate">Audio sampling rate (e.g. 44100, 48000 Hz).</param>
    /// <param name="targetBands">Destination buffer of length BarCount.</param>
    /// <param name="subBass">Out: 20 - 60 Hz normalized energy.</param>
    /// <param name="bass">Out: 60 - 250 Hz normalized energy.</param>
    /// <param name="mid">Out: 250 - 4000 Hz normalized energy.</param>
    /// <param name="treble">Out: 4000 - 20000 Hz normalized energy.</param>
    /// <param name="smoothing">Smoothing decay factor [0.0 - 0.95].</param>
    /// <param name="sensitivity">Sensitivity boost multiplier.</param>
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
            for (int i = 0; i < _barCount; i++)
            {
                _smoothedBands[i] *= smoothing;
                targetBands[i] = _smoothedBands[i];
            }
            return;
        }

        float binWidth = (sampleRate / 2.0f) / binCount;

        // 1. Calculate specific standard audio frequency ranges
        subBass = CalculateRangeEnergy(magnitudes, binWidth, 20f, 60f) * sensitivity * 1.5f;
        bass = CalculateRangeEnergy(magnitudes, binWidth, 60f, 250f) * sensitivity * 1.3f;
        mid = CalculateRangeEnergy(magnitudes, binWidth, 250f, 4000f) * sensitivity;
        treble = CalculateRangeEnergy(magnitudes, binWidth, 4000f, 20000f) * sensitivity * 1.2f;

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
    }
}
