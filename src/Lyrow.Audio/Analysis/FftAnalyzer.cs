namespace Lyrow.Audio.Analysis;

/// <summary>
/// High-performance zero-allocation Fast Fourier Transform (FFT) analyzer.
/// Implements Radix-2 Cooley-Tukey algorithm with precomputed Hanning window and twiddle factors.
/// </summary>
public sealed class FftAnalyzer
{
    private int _fftSize;
    private float[] _real = [];
    private float[] _imag = [];
    private float[] _window = [];
    private float[] _magnitudes = [];
    private float[] _twiddleCos = [];
    private float[] _twiddleSin = [];
    private int[] _bitReversal = [];

    public FftAnalyzer(int fftSize = 2048)
    {
        SetSize(fftSize);
    }

    public int FftSize => _fftSize;

    /// <summary>
    /// Reconfigures FFT size and precomputes trigonometry and bit-reversal tables.
    /// </summary>
    public void SetSize(int fftSize)
    {
        if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a positive power of two (e.g. 512, 1024, 2048).", nameof(fftSize));
        }

        if (_fftSize == fftSize && _real.Length == fftSize)
        {
            return;
        }

        _fftSize = fftSize;
        _real = new float[fftSize];
        _imag = new float[fftSize];
        _window = new float[fftSize];
        _magnitudes = new float[fftSize / 2];
        _bitReversal = new int[fftSize];

        // Precompute Hanning window: w[n] = 0.5 * (1 - cos(2*pi*n / (N - 1)))
        for (int i = 0; i < fftSize; i++)
        {
            _window[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1))));
        }

        // Precompute bit-reversal lookup
        int bits = (int)Math.Round(Math.Log2(fftSize));
        for (int i = 0; i < fftSize; i++)
        {
            int reversed = 0;
            for (int b = 0; b < bits; b++)
            {
                if ((i & (1 << b)) != 0)
                {
                    reversed |= (1 << (bits - 1 - b));
                }
            }
            _bitReversal[i] = reversed;
        }

        // Precompute twiddle factors (half of FFT size is enough)
        int halfSize = fftSize / 2;
        _twiddleCos = new float[halfSize];
        _twiddleSin = new float[halfSize];
        for (int i = 0; i < halfSize; i++)
        {
            double angle = -2.0 * Math.PI * i / fftSize;
            _twiddleCos[i] = (float)Math.Cos(angle);
            _twiddleSin[i] = (float)Math.Sin(angle);
        }
    }

    /// <summary>
    /// Performs in-place windowed FFT on the provided input samples and writes normalized magnitudes.
    /// Returns a ReadOnlySpan of positive frequency magnitudes (length: FftSize / 2).
    /// </summary>
    public ReadOnlySpan<float> Analyze(ReadOnlySpan<float> inputSamples)
    {
        int n = _fftSize;
        int inputLen = Math.Min(n, inputSamples.Length);

        // Apply Hanning window and bit-reversal ordering
        for (int i = 0; i < inputLen; i++)
        {
            int targetIdx = _bitReversal[i];
            _real[targetIdx] = inputSamples[i] * _window[i];
            _imag[targetIdx] = 0f;
        }

        // Zero-pad remainder if input was shorter than FFT size
        for (int i = inputLen; i < n; i++)
        {
            int targetIdx = _bitReversal[i];
            _real[targetIdx] = 0f;
            _imag[targetIdx] = 0f;
        }

        // Cooley-Tukey Radix-2 decimation-in-time
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1;
            int step = n / len;

            for (int i = 0; i < n; i += len)
            {
                for (int j = 0; j < half; j++)
                {
                    int twiddleIdx = j * step;
                    float c = _twiddleCos[twiddleIdx];
                    float s = _twiddleSin[twiddleIdx];

                    int uIdx = i + j;
                    int vIdx = i + j + half;

                    float vReal = _real[vIdx];
                    float vImag = _imag[vIdx];

                    // Complex multiply: (vReal + i*vImag) * (c + i*s)
                    float tReal = vReal * c - vImag * s;
                    float tImag = vReal * s + vImag * c;

                    float uReal = _real[uIdx];
                    float uImag = _imag[uIdx];

                    _real[uIdx] = uReal + tReal;
                    _imag[uIdx] = uImag + tImag;

                    _real[vIdx] = uReal - tReal;
                    _imag[vIdx] = uImag - tImag;
                }
            }
        }

        // Calculate normalized positive frequency magnitudes: [0 .. N/2 - 1]
        int halfLen = n / 2;
        float normFactor = 2.0f / n;

        for (int i = 0; i < halfLen; i++)
        {
            float r = _real[i];
            float im = _imag[i];
            _magnitudes[i] = MathF.Sqrt(r * r + im * im) * normFactor;
        }

        return _magnitudes.AsSpan(0, halfLen);
    }
}
