using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Audio.Capture;

/// <summary>
/// In-memory synthetic audio capture service for automated tests and CI environments.
/// Can generate configurable sine tones, chords or silence without physical audio hardware.
/// </summary>
public sealed class SyntheticAudioCaptureService : IAudioCaptureService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _activeCts;
    private Task? _generatorTask;
    private bool _isDisposed;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly float[] _buffer;

    public SyntheticAudioCaptureService(int sampleRate = 48000, int channels = 2, int bufferSize = 960)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _buffer = new float[bufferSize * channels];
        CurrentDevice = new AudioDeviceDescriptor("synthetic-loopback", "Synthetic Audio Loopback", true, sampleRate, channels);
    }

    public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;
    public event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;
    public event EventHandler<Exception>? CaptureFaulted;

    public bool IsCapturing { get; private set; }
    public AudioDeviceDescriptor? CurrentDevice { get; private set; }

    /// <summary>Frequency of primary synthesized tone (e.g. 440 Hz for concert A).</summary>
    public float PrimaryFrequencyHz { get; set; } = 440f;

    /// <summary>Amplitude of synthesized signal [0.0, 1.0]. 0.0 creates silence.</summary>
    public float Amplitude { get; set; } = 0.5f;

    public IReadOnlyList<AudioDeviceDescriptor> GetAvailableDevices()
    {
        return
        [
            new AudioDeviceDescriptor("synthetic-loopback", "Synthetic Audio Loopback", true, _sampleRate, _channels),
            new AudioDeviceDescriptor("synthetic-secondary", "Synthetic Secondary Endpoint", false, _sampleRate, _channels)
        ];
    }

    public async Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsCapturing) return;

            var chosen = GetAvailableDevices().FirstOrDefault(d => d.Id == deviceId) ?? GetAvailableDevices()[0];
            CurrentDevice = chosen;

            _activeCts = new CancellationTokenSource();
            IsCapturing = true;
            _generatorTask = Task.Run(() => GenerateLoopAsync(_activeCts.Token));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCapturing) return;

            IsCapturing = false;
            _activeCts?.Cancel();
            if (_generatorTask != null)
            {
                try
                {
                    await _generatorTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                _generatorTask = null;
            }

            _activeCts?.Dispose();
            _activeCts = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void SimulateDeviceChange(AudioDeviceDescriptor newDevice)
    {
        var old = CurrentDevice;
        CurrentDevice = newDevice;
        DeviceChanged?.Invoke(this, new AudioDeviceChangedEventArgs(old, newDevice));
    }

    public void SimulateFault(Exception ex)
    {
        CaptureFaulted?.Invoke(this, ex);
    }

    private async Task GenerateLoopAsync(CancellationToken ct)
    {
        int frames = _buffer.Length / _channels;
        double phase = 0.0;
        double phaseStep = (2.0 * Math.PI * PrimaryFrequencyHz) / _sampleRate;
        var interval = TimeSpan.FromMilliseconds((frames * 1000.0) / _sampleRate);

        using var timer = new PeriodicTimer(interval > TimeSpan.Zero ? interval : TimeSpan.FromMilliseconds(20));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)) break;

                float amp = Amplitude;
                int idx = 0;
                for (int f = 0; f < frames; f++)
                {
                    float sample = amp > 0f ? (float)(Math.Sin(phase) * amp) : 0f;
                    phase += phaseStep;
                    if (phase >= 2.0 * Math.PI) phase -= 2.0 * Math.PI;

                    for (int c = 0; c < _channels; c++)
                    {
                        _buffer[idx++] = sample;
                    }
                }

                DataAvailable?.Invoke(this, new AudioDataAvailableEventArgs(_buffer, _channels, _sampleRate));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                CaptureFaulted?.Invoke(this, ex);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await StopAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
