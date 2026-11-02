using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Audio.Capture;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Audio.Tests;

public sealed class GlowAudioIntegrationTests
{
    [Fact]
    public async Task GlowModule_ShouldCaptureAudioAndPublishSpectrumEvents_IndependentlyFromLyrics()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var captureService = new SyntheticAudioCaptureService(sampleRate: 48000, channels: 2);
        captureService.PrimaryFrequencyHz = 440f;
        captureService.Amplitude = 0.8f;

        var analyzer = new SpectrumAnalyzer(fftSize: 1024, barCount: 16);
        var module = new GlowModule(captureService, analyzer, eventBus);

        var receivedSpectrums = new List<FrequencySpectrum>();
        var spectrumTcs = new TaskCompletionSource<FrequencySpectrum>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var sub = eventBus.Subscribe<AudioSpectrumUpdatedEvent>(e =>
        {
            lock (receivedSpectrums)
            {
                receivedSpectrums.Add(e.Spectrum);
            }

            // Deterministic trigger: signal when a non-silent spectrum frame with significant peak amplitude is received
            if (!e.Spectrum.Levels.IsSilent && e.Spectrum.Levels.Peak > 0.3f)
            {
                spectrumTcs.TrySetResult(e.Spectrum);
            }
        });

        // Act - Initialize & Start
        await module.InitializeAsync();
        module.State.Should().Be(ModuleState.Initialized);

        await module.StartAsync();
        module.State.Should().Be(ModuleState.Running);
        captureService.IsCapturing.Should().BeTrue();

        // Deterministic wait: complete as soon as valid audio spectrum is analyzed, or fail after generous timeout on busy CI
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
        var completedTask = await Task.WhenAny(spectrumTcs.Task, timeoutTask);
        completedTask.Should().Be(spectrumTcs.Task, "GlowModule must capture audio and publish an active spectrum event within timeout");

        var activeSpectrum = await spectrumTcs.Task;
        activeSpectrum.Levels.IsSilent.Should().BeFalse();
        activeSpectrum.Levels.Peak.Should().BeGreaterThan(0.3f);
        activeSpectrum.Bands.Length.Should().Be(16);

        lock (receivedSpectrums)
        {
            receivedSpectrums.Should().NotBeEmpty();
        }

        // Act - Stop
        await module.StopAsync();
        module.State.Should().Be(ModuleState.Stopped);
        captureService.IsCapturing.Should().BeFalse();

        // Clean up
        await module.DisposeAsync();
    }

    [Fact]
    public async Task GlowModule_ShouldHandleDeviceSwitching_Gracefully()
    {
        var captureService = new SyntheticAudioCaptureService();
        var analyzer = new SpectrumAnalyzer(fftSize: 512, barCount: 8);
        var module = new GlowModule(captureService, analyzer);

        await module.InitializeAsync();
        await module.StartAsync();

        var deviceChangedTcs = new TaskCompletionSource<AudioDeviceChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        captureService.DeviceChanged += (_, e) =>
        {
            deviceChangedTcs.TrySetResult(e);
        };

        captureService.SimulateDeviceChange(new AudioDeviceDescriptor("headphone-1", "New Headphones", true, 48000, 2));

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));
        var completedTask = await Task.WhenAny(deviceChangedTcs.Task, timeoutTask);
        completedTask.Should().Be(deviceChangedTcs.Task, "DeviceChanged event must fire upon simulation");

        var args = await deviceChangedTcs.Task;
        args.CurrentDevice.Name.Should().Be("New Headphones");

        await module.StopAsync();
        await module.DisposeAsync();
    }

    [Fact]
    public async Task GlowModule_ShouldReleaseResourcesAndStopCpuLoops_OnStop()
    {
        var captureService = new SyntheticAudioCaptureService();
        var analyzer = new SpectrumAnalyzer();
        var module = new GlowModule(captureService, analyzer);

        await module.InitializeAsync();
        await module.StartAsync();
        module.State.Should().Be(ModuleState.Running);
        captureService.IsCapturing.Should().BeTrue();

        await module.StopAsync();

        // Verification of clean state
        module.State.Should().Be(ModuleState.Stopped);
        module.LatestSpectrum.Should().BeSameAs(FrequencySpectrum.Empty);
        captureService.IsCapturing.Should().BeFalse();

        await module.DisposeAsync();
    }
}
