using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Core.Modules;

/// <summary>
/// Core implementation of the Lumn module managing audio-reactive lighting and visualizations.
/// Operates completely independently from Verci and manages the audio capture & analysis lifecycle.
/// </summary>
public sealed class LumnModule : ILyrowModule
{
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private IAudioCaptureService? _audioCaptureService;
    private ISpectrumAnalyzer? _spectrumAnalyzer;
    private readonly IEventBus? _eventBus;
    private readonly ISettingsManager? _settingsManager;

    private CancellationTokenSource? _activeCts;
    private Task? _analysisLoopTask;
    private bool _isDisposed;

    public LumnModule(
        IAudioCaptureService? audioCaptureService = null,
        ISpectrumAnalyzer? spectrumAnalyzer = null,
        IEventBus? eventBus = null,
        ISettingsManager? settingsManager = null)
    {
        _audioCaptureService = audioCaptureService;
        _spectrumAnalyzer = spectrumAnalyzer;
        _eventBus = eventBus;
        _settingsManager = settingsManager;
    }

    public string ModuleId => "lumn";
    public string DisplayName => "Lumn (Audio Visualizer)";
    public ModuleState State { get; private set; } = ModuleState.Uninitialized;
    public Exception? LastFault { get; private set; }

    /// <summary>
    /// Gets the most recent frequency spectrum snapshot computed by the analysis loop.
    /// </summary>
    public FrequencySpectrum LatestSpectrum { get; private set; } = FrequencySpectrum.Empty;

    /// <summary>
    /// Gets the most recent audio power/levels snapshot.
    /// </summary>
    public AudioLevels LatestLevels => LatestSpectrum.Levels;

    /// <summary>
    /// Attaches or updates the audio capture and analysis services if not provided at construction.
    /// </summary>
    public void AttachServices(IAudioCaptureService audioCaptureService, ISpectrumAnalyzer spectrumAnalyzer)
    {
        _audioCaptureService = audioCaptureService;
        _spectrumAnalyzer = spectrumAnalyzer;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != ModuleState.Uninitialized) return;

            ApplySettings();
            State = ModuleState.Initialized;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == ModuleState.Running) return;

            State = ModuleState.Starting;
            _activeCts = new CancellationTokenSource();
            var ct = _activeCts.Token;

            ApplySettings();

            if (_audioCaptureService != null && _spectrumAnalyzer != null)
            {
                _audioCaptureService.DataAvailable += OnAudioDataAvailable;
                _audioCaptureService.CaptureFaulted += OnAudioCaptureFaulted;

                string? preferredDevice = _settingsManager?.Current.Lumn.PreferredDeviceId;
                await _audioCaptureService.StartAsync(preferredDevice, ct).ConfigureAwait(false);

                int fps = _settingsManager?.Current.Lumn.TargetFps ?? 60;
                fps = Math.Clamp(fps, 15, 144);
                var frameInterval = TimeSpan.FromMilliseconds(1000.0 / fps);

                _analysisLoopTask = Task.Run(() => AnalysisLoopAsync(frameInterval, ct), ct);
            }

            State = ModuleState.Running;
        }
        catch (Exception ex)
        {
            LastFault = ex;
            State = ModuleState.Faulted;
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State != ModuleState.Running && State != ModuleState.Starting) return;

            State = ModuleState.Stopping;

            _activeCts?.Cancel();
            if (_analysisLoopTask != null)
            {
                try
                {
                    await _analysisLoopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                _analysisLoopTask = null;
            }

            if (_audioCaptureService != null)
            {
                _audioCaptureService.DataAvailable -= OnAudioDataAvailable;
                _audioCaptureService.CaptureFaulted -= OnAudioCaptureFaulted;
                await _audioCaptureService.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            _spectrumAnalyzer?.Reset();
            LatestSpectrum = FrequencySpectrum.Empty;

            _activeCts?.Dispose();
            _activeCts = null;

            State = ModuleState.Stopped;
        }
        catch (Exception ex)
        {
            LastFault = ex;
            State = ModuleState.Faulted;
            throw;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void ApplySettings()
    {
        if (_spectrumAnalyzer == null || _settingsManager == null) return;

        var lumnSettings = _settingsManager.Current.Lumn;
        _spectrumAnalyzer.FftSize = lumnSettings.FftSize;
        _spectrumAnalyzer.BarCount = lumnSettings.BarCount;
        _spectrumAnalyzer.SmoothingFactor = lumnSettings.SmoothingFactor;
        _spectrumAnalyzer.Sensitivity = lumnSettings.Sensitivity;
    }

    private void OnAudioDataAvailable(object? sender, AudioDataAvailableEventArgs e)
    {
        if (_spectrumAnalyzer == null || State != ModuleState.Running) return;
        _spectrumAnalyzer.PushSamples(e.Samples.Span, e.Channels, e.SampleRate);
    }

    private void OnAudioCaptureFaulted(object? sender, Exception ex)
    {
        LastFault = ex;
        if (_eventBus != null)
        {
            _ = _eventBus.PublishAsync(new AudioCaptureFaultedEvent(ex));
        }
    }

    private async Task AnalysisLoopAsync(TimeSpan frameInterval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(frameInterval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)) break;
                if (_spectrumAnalyzer == null) continue;

                var spectrum = _spectrumAnalyzer.ComputeSpectrum();
                LatestSpectrum = spectrum;

                if (_eventBus != null)
                {
                    await _eventBus.PublishAsync(new AudioSpectrumUpdatedEvent(spectrum), ct).ConfigureAwait(false);
                    await _eventBus.PublishAsync(new AudioLevelUpdatedEvent(spectrum.Levels), ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LastFault = ex;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (State == ModuleState.Running)
        {
            await StopAsync().ConfigureAwait(false);
        }

        if (_audioCaptureService != null)
        {
            await _audioCaptureService.DisposeAsync().ConfigureAwait(false);
        }

        _spectrumAnalyzer?.Dispose();
        _stateLock.Dispose();
        _activeCts?.Dispose();
    }
}
