namespace Lyrow.Core.Models;

/// <summary>
/// Configuration specific to the Lumn audio-reactive visualization module.
/// </summary>
public sealed class LumnSettings
{
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// FFT sample window size (must be a power of two, e.g. 512, 1024, 2048, 4096).
    /// </summary>
    public int FftSize { get; set; } = 2048;

    /// <summary>
    /// Target visualization rendering frame rate (e.g. 30, 60, 120).
    /// </summary>
    public int TargetFps { get; set; } = 60;

    /// <summary>
    /// Sensitivity multiplier applied to audio amplitudes [0.1, 5.0].
    /// </summary>
    public float Sensitivity { get; set; } = 1.0f;

    /// <summary>
    /// Falloff smoothing multiplier applied between frames [0.0, 0.95].
    /// Higher values produce smoother, slower-falling visualizer bars.
    /// </summary>
    public float SmoothingFactor { get; set; } = 0.65f;

    /// <summary>
    /// Number of discrete frequency bars displayed in the visualizer.
    /// </summary>
    public int BarCount { get; set; } = 32;

    /// <summary>
    /// Mode of visualization: SpectrumBars, Waveform, AmbientGlow.
    /// </summary>
    public string VisualizerMode { get; set; } = "SpectrumBars";

    /// <summary>
    /// Device ID of preferred audio output endpoint, or null for default loopback.
    /// </summary>
    public string? PreferredDeviceId { get; set; }

    /// <summary>
    /// Whether the dedicated visualizer window is open.
    /// </summary>
    public bool ShowVisualizerWindow { get; set; } = true;

    /// <summary>
    /// Silence detection threshold in decibels below which analysis enters power-saving idle.
    /// </summary>
    public float SilenceThresholdDb { get; set; } = -65.0f;
}
