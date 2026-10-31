namespace Lyrow.Core.Models;

/// <summary>
/// Represents audio power and level measurements calculated from raw PCM samples.
/// </summary>
public readonly record struct AudioLevels(
    float Rms,
    float Peak,
    float Decibels,
    bool IsSilent)
{
    public static AudioLevels Zero => new(0f, 0f, -90f, true);
}

/// <summary>
/// Represents an analyzed frequency spectrum snapshot.
/// </summary>
public sealed class FrequencySpectrum
{
    public static readonly FrequencySpectrum Empty = new(
        rawFrequencies: Array.Empty<float>(),
        bands: Array.Empty<float>(),
        subBass: 0f,
        bass: 0f,
        mid: 0f,
        treble: 0f,
        levels: AudioLevels.Zero);

    public FrequencySpectrum(
        ReadOnlyMemory<float> rawFrequencies,
        ReadOnlyMemory<float> bands,
        float subBass,
        float bass,
        float mid,
        float treble,
        AudioLevels levels,
        float lowMid = 0f,
        float highMid = 0f,
        float spectralFlux = 0f,
        float bassFlux = 0f,
        float midFlux = 0f,
        float trebleFlux = 0f)
    {
        RawFrequencies = rawFrequencies;
        Bands = bands;
        SubBass = Math.Clamp(subBass, 0f, 1f);
        Bass = Math.Clamp(bass, 0f, 1f);
        LowMid = Math.Clamp(lowMid > 0f ? lowMid : (bass * 0.5f + mid * 0.5f), 0f, 1f);
        Mid = Math.Clamp(mid, 0f, 1f);
        HighMid = Math.Clamp(highMid > 0f ? highMid : (mid * 0.5f + treble * 0.5f), 0f, 1f);
        Treble = Math.Clamp(treble, 0f, 1f);
        SpectralFlux = Math.Max(0f, spectralFlux);
        BassFlux = Math.Max(0f, bassFlux);
        MidFlux = Math.Max(0f, midFlux);
        TrebleFlux = Math.Max(0f, trebleFlux);
        Levels = levels;
    }

    public ReadOnlyMemory<float> RawFrequencies { get; }
    public ReadOnlyMemory<float> Bands { get; }
    public float SubBass { get; }
    public float Bass { get; }
    public float LowMid { get; }
    public float Mid { get; }
    public float HighMid { get; }
    public float Treble { get; }
    public float SpectralFlux { get; }
    public float BassFlux { get; }
    public float MidFlux { get; }
    public float TrebleFlux { get; }
    public AudioLevels Levels { get; }
}

/// <summary>
/// Metadata describing an audio capture or playback endpoint.
/// </summary>
public sealed record AudioDeviceDescriptor(
    string Id,
    string Name,
    bool IsDefault,
    int SampleRate,
    int Channels);

/// <summary>
/// Event arguments carrying fresh interleaved floating-point PCM audio samples.
/// </summary>
public sealed class AudioDataAvailableEventArgs : EventArgs
{
    public AudioDataAvailableEventArgs(ReadOnlyMemory<float> samples, int channels, int sampleRate)
    {
        Samples = samples;
        Channels = channels;
        SampleRate = sampleRate;
    }

    public ReadOnlyMemory<float> Samples { get; }
    public int Channels { get; }
    public int SampleRate { get; }
}

/// <summary>
/// High-level categorization of audio output endpoint types for UI icon binding.
/// </summary>
public enum AudioDeviceCategory
{
    Speakers,
    Headphones,
    Bluetooth,
    Other
}

/// <summary>
/// Event arguments when the active playback audio endpoint has changed.
/// </summary>
public sealed class AudioDeviceChangedEventArgs : EventArgs
{
    public AudioDeviceChangedEventArgs(AudioDeviceDescriptor? previousDevice, AudioDeviceDescriptor currentDevice)
    {
        PreviousDevice = previousDevice;
        CurrentDevice = currentDevice;
    }

    public AudioDeviceDescriptor? PreviousDevice { get; }
    public AudioDeviceDescriptor CurrentDevice { get; }
}
