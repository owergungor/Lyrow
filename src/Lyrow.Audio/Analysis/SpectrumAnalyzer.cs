using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Audio.Analysis;

/// <summary>
/// End-to-end zero-allocation audio spectrum analyzer coordinating ring buffering,
/// level measurement, windowed FFT and frequency band aggregation.
/// </summary>
public sealed class SpectrumAnalyzer : ISpectrumAnalyzer
{
    private readonly object _syncLock = new();
    private readonly FftAnalyzer _fft;
    private readonly FrequencyBandProcessor _bandProcessor;

    private float[] _ringBuffer;
    private float[] _fftInput;
    private float[] _bandsOutput;
    private float[] _rawMagnitudesOutput;

    private int _ringWritePos;
    private int _sampleRate = 48000;
    private float _smoothingFactor = 0.65f;
    private float _sensitivity = 1.0f;
    private float _silenceThresholdDb = -65.0f;
    private bool _isDisposed;

    public SpectrumAnalyzer(int fftSize = 2048, int barCount = 32)
    {
        _fft = new FftAnalyzer(fftSize);
        _bandProcessor = new FrequencyBandProcessor(barCount);

        _ringBuffer = new float[fftSize * 2];
        _fftInput = new float[fftSize];
        _bandsOutput = new float[barCount];
        _rawMagnitudesOutput = new float[fftSize / 2];
    }

    public int FftSize
    {
        get => _fft.FftSize;
        set
        {
            lock (_syncLock)
            {
                if (value == _fft.FftSize) return;
                _fft.SetSize(value);
                _ringBuffer = new float[value * 2];
                _fftInput = new float[value];
                _rawMagnitudesOutput = new float[value / 2];
                _ringWritePos = 0;
            }
        }
    }

    public int BarCount
    {
        get => _bandProcessor.BarCount;
        set
        {
            lock (_syncLock)
            {
                if (value == _bandProcessor.BarCount) return;
                _bandProcessor.SetBarCount(value);
                _bandsOutput = new float[value];
            }
        }
    }

    public float SmoothingFactor
    {
        get => _smoothingFactor;
        set => _smoothingFactor = Math.Clamp(value, 0f, 0.98f);
    }

    public float Sensitivity
    {
        get => _sensitivity;
        set => _sensitivity = Math.Clamp(value, 0.1f, 5.0f);
    }

    public float SilenceThresholdDb
    {
        get => _silenceThresholdDb;
        set => _silenceThresholdDb = value;
    }

    public void PushSamples(ReadOnlySpan<float> samples, int channels, int sampleRate)
    {
        if (samples.IsEmpty || _isDisposed) return;

        lock (_syncLock)
        {
            _sampleRate = sampleRate > 0 ? sampleRate : 48000;
            int ringLen = _ringBuffer.Length;
            int step = Math.Max(1, channels);

            // Convert multi-channel (e.g. stereo) interleaved float samples to mono
            for (int i = 0; i < samples.Length; i += step)
            {
                float mono;
                if (channels == 2 && i + 1 < samples.Length)
                {
                    mono = (samples[i] + samples[i + 1]) * 0.5f;
                }
                else
                {
                    mono = samples[i];
                }

                _ringBuffer[_ringWritePos] = mono;
                _ringWritePos = (_ringWritePos + 1) % ringLen;
            }
        }
    }

    public FrequencySpectrum ComputeSpectrum()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return FrequencySpectrum.Empty;

            int n = _fft.FftSize;
            int ringLen = _ringBuffer.Length;

            // Extract the latest N mono samples from ring buffer into contiguous array
            int readStart = (_ringWritePos - n + ringLen) % ringLen;
            for (int i = 0; i < n; i++)
            {
                int idx = (readStart + i) % ringLen;
                _fftInput[i] = _ringBuffer[idx];
            }

            // 1. Calculate audio power and levels
            var levels = AudioLevelAnalyzer.Calculate(_fftInput.AsSpan(), _silenceThresholdDb);

            // 2. If silent, skip FFT to conserve CPU and smoothly decay bands to zero
            if (levels.IsSilent)
            {
                _bandProcessor.Process(
                    ReadOnlySpan<float>.Empty,
                    _sampleRate,
                    _bandsOutput.AsSpan(),
                    out _,
                    out _,
                    out _,
                    out _,
                    smoothing: _smoothingFactor,
                    sensitivity: _sensitivity);

                return new FrequencySpectrum(
                    rawFrequencies: _rawMagnitudesOutput.ToArray(),
                    bands: _bandsOutput.ToArray(),
                    subBass: 0f,
                    bass: 0f,
                    mid: 0f,
                    treble: 0f,
                    levels: levels);
            }

            // 3. Perform windowed FFT
            var rawMagnitudes = _fft.Analyze(_fftInput.AsSpan());
            rawMagnitudes.CopyTo(_rawMagnitudesOutput.AsSpan());

            // 4. Map into perceptual logarithmic bands and 4 classic frequency ranges
            _bandProcessor.Process(
                rawMagnitudes,
                _sampleRate,
                _bandsOutput.AsSpan(),
                out float subBass,
                out float bass,
                out float mid,
                out float treble,
                smoothing: _smoothingFactor,
                sensitivity: _sensitivity);

            return new FrequencySpectrum(
                rawFrequencies: _rawMagnitudesOutput.ToArray(),
                bands: _bandsOutput.ToArray(),
                subBass: subBass,
                bass: bass,
                mid: mid,
                treble: treble,
                levels: levels);
        }
    }

    public void Reset()
    {
        lock (_syncLock)
        {
            Array.Clear(_ringBuffer);
            Array.Clear(_fftInput);
            Array.Clear(_bandsOutput);
            Array.Clear(_rawMagnitudesOutput);
            _ringWritePos = 0;
            _bandProcessor.Reset();
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Reset();
        }
    }
}
