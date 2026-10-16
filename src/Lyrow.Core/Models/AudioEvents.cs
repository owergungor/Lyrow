namespace Lyrow.Core.Models;

/// <summary>
/// Event broadcast when a new frequency spectrum calculation is available.
/// </summary>
public sealed record AudioSpectrumUpdatedEvent(FrequencySpectrum Spectrum);

/// <summary>
/// Event broadcast when audio power and peak levels are calculated.
/// </summary>
public sealed record AudioLevelUpdatedEvent(AudioLevels Levels);

/// <summary>
/// Event broadcast when the active audio endpoint has been switched.
/// </summary>
public sealed record AudioDeviceSwitchedEvent(AudioDeviceDescriptor? PreviousDevice, AudioDeviceDescriptor NewDevice);

/// <summary>
/// Event broadcast when an unrecoverable or transient hardware fault occurs in audio capture.
/// </summary>
public sealed record AudioCaptureFaultedEvent(Exception Exception);
