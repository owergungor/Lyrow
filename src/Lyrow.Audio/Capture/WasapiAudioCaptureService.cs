using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Lyrow.Audio.Capture;

/// <summary>
/// Hardware WASAPI Loopback audio capture service for Windows endpoints.
/// Captures mixed system output directly from the soundcard driver, handles dynamic
/// device switching, and automatically recovers from AUDCLNT_E_DEVICE_INVALIDATED.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiAudioCaptureService : IAudioCaptureService
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private MMDeviceEnumerator? _deviceEnumerator;
    private WasapiLoopbackCapture? _capture;
    private string? _currentDeviceId;
    private bool _isCapturing;
    private bool _isDisposed;

    private float[] _floatConversionBuffer = [];

    public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;
    public event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;
    public event EventHandler<Exception>? CaptureFaulted;

    public bool IsCapturing => _isCapturing;
    public AudioDeviceDescriptor? CurrentDevice { get; private set; }

    public IReadOnlyList<AudioDeviceDescriptor> GetAvailableDevices()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<AudioDeviceDescriptor>();
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var results = new List<AudioDeviceDescriptor>(endpoints.Count);

            MMDevice? defaultDevice = null;
            try
            {
                defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch
            {
                // No default playback endpoint active
            }

            foreach (var ep in endpoints)
            {
                bool isDefault = defaultDevice != null && ep.ID.Equals(defaultDevice.ID, StringComparison.OrdinalIgnoreCase);
                int sampleRate = ep.AudioClient?.MixFormat?.SampleRate ?? 48000;
                int channels = ep.AudioClient?.MixFormat?.Channels ?? 2;

                results.Add(new AudioDeviceDescriptor(
                    Id: ep.ID,
                    Name: ep.FriendlyName,
                    IsDefault: isDefault,
                    SampleRate: sampleRate,
                    Channels: channels));
            }

            return results;
        }
        catch (Exception ex)
        {
            CaptureFaulted?.Invoke(this, ex);
            return Array.Empty<AudioDeviceDescriptor>();
        }
    }

    public async Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WASAPI audio capture is only supported on Windows.");
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isCapturing) return;

            _currentDeviceId = deviceId;
            _deviceEnumerator ??= new MMDeviceEnumerator();

            await InitializeAndStartCaptureInternalAsync(deviceId).ConfigureAwait(false);
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
            if (!_isCapturing) return;

            StopCaptureInternal();
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task InitializeAndStartCaptureInternalAsync(string? deviceId)
    {
        StopCaptureInternal();

        try
        {
            _deviceEnumerator ??= new MMDeviceEnumerator();
            MMDevice targetDevice;

            if (string.IsNullOrEmpty(deviceId))
            {
                targetDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            else
            {
                targetDevice = _deviceEnumerator.GetDevice(deviceId);
            }

            _capture = new WasapiLoopbackCapture(targetDevice);
            _capture.DataAvailable += OnWasapiDataAvailable;
            _capture.RecordingStopped += OnWasapiRecordingStopped;

            var waveFormat = _capture.WaveFormat;
            CurrentDevice = new AudioDeviceDescriptor(
                Id: targetDevice.ID,
                Name: targetDevice.FriendlyName,
                IsDefault: string.IsNullOrEmpty(deviceId),
                SampleRate: waveFormat.SampleRate,
                Channels: waveFormat.Channels);

            _capture.StartRecording();
            _isCapturing = true;

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _isCapturing = false;
            CaptureFaulted?.Invoke(this, ex);
            throw;
        }
    }

    private void StopCaptureInternal()
    {
        _isCapturing = false;
        if (_capture != null)
        {
            try
            {
                _capture.DataAvailable -= OnWasapiDataAvailable;
                _capture.RecordingStopped -= OnWasapiRecordingStopped;
                _capture.StopRecording();
            }
            catch
            {
                // Ignore errors during hardware stop
            }
            finally
            {
                _capture.Dispose();
                _capture = null;
            }
        }
    }

    private void OnWasapiDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || !_isCapturing || _capture == null) return;

        try
        {
            var format = _capture.WaveFormat;
            int channels = format.Channels;
            int sampleRate = format.SampleRate;

            ReadOnlySpan<byte> rawBytes = e.Buffer.AsSpan(0, e.BytesRecorded);

            // WASAPI loopback is typically IEEE 32-bit Float PCM
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                ReadOnlySpan<float> floatSpan = MemoryMarshal.Cast<byte, float>(rawBytes);
                DataAvailable?.Invoke(this, new AudioDataAvailableEventArgs(floatSpan.ToArray(), channels, sampleRate));
            }
            else if (format.BitsPerSample == 16)
            {
                ReadOnlySpan<short> shortSpan = MemoryMarshal.Cast<byte, short>(rawBytes);
                if (_floatConversionBuffer.Length < shortSpan.Length)
                {
                    _floatConversionBuffer = new float[shortSpan.Length];
                }

                for (int i = 0; i < shortSpan.Length; i++)
                {
                    _floatConversionBuffer[i] = shortSpan[i] / 32768.0f;
                }

                DataAvailable?.Invoke(this, new AudioDataAvailableEventArgs(
                    _floatConversionBuffer.AsMemory(0, shortSpan.Length),
                    channels,
                    sampleRate));
            }
        }
        catch (Exception ex)
        {
            CaptureFaulted?.Invoke(this, ex);
        }
    }

    private void OnWasapiRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null && _isCapturing)
        {
            CaptureFaulted?.Invoke(this, e.Exception);

            // Handle device invalidation (e.g. unplugged headphones or default device switch)
            // COM HRESULT 0x88890004 = AUDCLNT_E_DEVICE_INVALIDATED
            const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
            if (e.Exception.HResult == AUDCLNT_E_DEVICE_INVALIDATED)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(500).ConfigureAwait(false);
                        if (_isCapturing)
                        {
                            await StartAsync(_currentDeviceId).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        CaptureFaulted?.Invoke(this, ex);
                    }
                });
            }
        }
    }

    /// <summary>
    /// Switches the active capture endpoint to another device, or to system default if deviceId is null.
    /// </summary>
    public async Task SwitchDeviceAsync(string? deviceId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_isCapturing)
            {
                _currentDeviceId = deviceId;
                return;
            }

            var oldDevice = CurrentDevice;
            _currentDeviceId = deviceId;
            await InitializeAndStartCaptureInternalAsync(deviceId).ConfigureAwait(false);

            if (CurrentDevice != null)
            {
                DeviceChanged?.Invoke(this, new AudioDeviceChangedEventArgs(oldDevice, CurrentDevice));
            }
        }
        catch (Exception ex)
        {
            CaptureFaulted?.Invoke(this, ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await StopAsync().ConfigureAwait(false);

        _deviceEnumerator?.Dispose();
        _deviceEnumerator = null;
        _lock.Dispose();
    }
}
