using FluentAssertions;
using Lyrow.Audio.Analysis;
using Lyrow.Audio.Capture;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Audio.Tests;

public sealed class LumnAudioIntegrationTests
{
    [Fact]
    public async Task LumnModule_ShouldCaptureAudioAndPublishSpectrumEvents_IndependentlyFromVerci()
    {
        // Arrange
        var eventBus = new InMemoryEventBus();
        var captureService = new SyntheticAudioCaptureService(sampleRate: 48000, channels: 2);
        captureService.PrimaryFrequencyHz = 440f;
        captureService.Amplitude = 0.8f;

        var analyzer = new SpectrumAnalyzer(fftSize: 1024, barCount: 16);
        var module = new LumnModule(captureService, analyzer, eventBus);

        var receivedSpectrums = new List<FrequencySpectrum>();
        using var sub = eventBus.Subscribe<AudioSpectrumUpdatedEvent>(e =>
        {
            lock (receivedSpectrums)
            {
                receivedSpectrums.Add(e.Spectrum);
            }
        });

        // Act - Initialize & Start
        await module.InitializeAsync();
        module.State.Should().Be(ModuleState.Initialized);

        await module.StartAsync();
        module.State.Should().Be(ModuleState.Running);
        captureService.IsCapturing.Should().BeTrue();

        // Wait for a few analysis frames to process
        await Task.Delay(200);

        // Assert - Events received and audio analyzed
        lock (receivedSpectrums)
        {
            receivedSpectrums.Should().NotBeEmpty();
            var lastSpectrum = receivedSpectrums.Last();
            lastSpectrum.Levels.IsSilent.Should().BeFalse();
            lastSpectrum.Levels.Peak.Should().BeGreaterThan(0.3f);
            lastSpectrum.Bands.Length.Should().Be(16);
        }

        // Act - Stop
        await module.StopAsync();
        module.State.Should().Be(ModuleState.Stopped);
        captureService.IsCapturing.Should().BeFalse();

        // Clean up
        await module.DisposeAsync();
    }

    [Fact]
    public async Task LumnModule_ShouldHandleDeviceSwitching_Gracefully()
    {
        var captureService = new SyntheticAudioCaptureService();
        var analyzer = new SpectrumAnalyzer(fftSize: 512, barCount: 8);
        var module = new LumnModule(captureService, analyzer);

        await module.InitializeAsync();
        await module.StartAsync();

        bool eventFired = false;
        captureService.DeviceChanged += (_, e) =>
        {
            eventFired = true;
            e.CurrentDevice.Name.Should().Be("New Headphones");
        };

        captureService.SimulateDeviceChange(new AudioDeviceDescriptor("headphone-1", "New Headphones", true, 48000, 2));

        eventFired.Should().BeTrue();

        await module.StopAsync();
        await module.DisposeAsync();
    }

    [Fact]
    public async Task LumnModule_ShouldReleaseResourcesAndStopCpuLoops_OnStop()
    {
        var captureService = new SyntheticAudioCaptureService();
        var analyzer = new SpectrumAnalyzer();
        var module = new LumnModule(captureService, analyzer);

        await module.InitializeAsync();
        await module.StartAsync();
        await Task.Delay(50);

        await module.StopAsync();

        // Verification of clean state
        module.State.Should().Be(ModuleState.Stopped);
        module.LatestSpectrum.Should().BeSameAs(FrequencySpectrum.Empty);
        captureService.IsCapturing.Should().BeFalse();

        await module.DisposeAsync();
    }
}
