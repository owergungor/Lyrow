using Lyrow.Core.Models;

namespace Lyrow.Audio.Analysis;

/// <summary>
/// Audio dynamics processor tracking composite spectral energy, frequency sub-bands,
/// and beat/transient spikes with zero allocation per frame.
/// Modulates ambient screen edge lighting with responsive attack and smooth decay.
/// </summary>
public sealed class GlowBeatEngine
{
    private float _energyEma = 0.05f;
    private float _transientEnergy = 0f;
    private float _prevEnergy = 0f;
    private float _smoothedBass = 0f;
    private float _smoothedMid = 0f;
    private float _smoothedTreble = 0f;
    private float _overallGlowIntensity = 0f;
    private DateTime _lastBeatUtc = DateTime.MinValue;
    private double _virtualClockMs = 0.0;
    private double _lastBeatVirtualMs = -1000.0;

    public float OverallGlowIntensity => _overallGlowIntensity;
    public float TransientEnergy => _transientEnergy;
    public float BassIntensity => _smoothedBass;
    public float MidIntensity => _smoothedMid;
    public float TrebleIntensity => _smoothedTreble;
    public bool IsBeatDetected { get; private set; }

    /// <summary>
    /// Updates energy and transient tracking using the latest analyzed frequency spectrum.
    /// In real-time audio, frameDeltaMs is negative to use system clock; in tests or fixed timesteps, pass the frame delta (e.g. 16.6ms).
    /// </summary>
    public void Process(FrequencySpectrum spectrum, float sensitivity = 1.0f, double frameDeltaMs = -1.0)
    {
        IsBeatDetected = false;

        if (spectrum.Levels.IsSilent)
        {
            // Smoothly decay to zero when silent to prevent abrupt shutoff
            _overallGlowIntensity *= 0.85f;
            _transientEnergy *= 0.80f;
            _prevEnergy *= 0.85f;
            _smoothedBass *= 0.85f;
            _smoothedMid *= 0.85f;
            _smoothedTreble *= 0.85f;
            _energyEma *= 0.95f;
            return;
        }

        float sens = Math.Clamp(sensitivity, 0.2f, 3.0f);

        // 1. Calculate isolated sub-band energies
        float instantBass = Math.Clamp((spectrum.Bass * 1.6f + spectrum.SubBass * 1.2f) * sens, 0f, 1.5f);
        float instantMid = Math.Clamp(spectrum.Mid * 1.4f * sens, 0f, 1.5f);
        float instantTreble = Math.Clamp(spectrum.Treble * 1.2f * sens, 0f, 1.5f);

        // 2. Composite RMS and spectral energy
        float instantEnergy = Math.Clamp(
            (spectrum.Levels.Rms * 2.2f + instantBass * 0.7f + instantMid * 0.4f + instantTreble * 0.2f) * sens,
            0f, 2.0f);

        // 3. Transient / Beat Detection using dynamic threshold spike against rolling EMA + positive onset delta
        var now = DateTime.UtcNow;
        double msSinceLastBeat;
        if (frameDeltaMs > 0.0)
        {
            _virtualClockMs += frameDeltaMs;
            msSinceLastBeat = _virtualClockMs - _lastBeatVirtualMs;
        }
        else
        {
            msSinceLastBeat = (now - _lastBeatUtc).TotalMilliseconds;
        }

        float energyDelta = instantEnergy - _prevEnergy;
        float beatThreshold = Math.Max(0.10f, _energyEma * 1.30f);
        bool isOnset = energyDelta > 0.12f * sens;

        if (instantEnergy > beatThreshold && isOnset && msSinceLastBeat > 120.0)
        {
            IsBeatDetected = true;
            _lastBeatUtc = now;
            _lastBeatVirtualMs = _virtualClockMs;
            // Transient spike proportional to energy surge
            float surge = Math.Clamp((instantEnergy - _energyEma) * 1.5f, 0.4f, 1.0f);
            _transientEnergy = Math.Min(1.0f, _transientEnergy + surge);
        }
        else
        {
            // Exponential transient decay (~150ms punch duration)
            _transientEnergy *= 0.82f;
        }

        _prevEnergy = instantEnergy;

        // Update rolling energy moving average
        _energyEma += (instantEnergy - _energyEma) * 0.05f;

        // 4. Band Smoothing (Fast attack, natural decay)
        _smoothedBass = instantBass > _smoothedBass
            ? _smoothedBass + (instantBass - _smoothedBass) * 0.70f
            : _smoothedBass * 0.88f;

        _smoothedMid = instantMid > _smoothedMid
            ? _smoothedMid + (instantMid - _smoothedMid) * 0.65f
            : _smoothedMid * 0.86f;

        _smoothedTreble = instantTreble > _smoothedTreble
            ? _smoothedTreble + (instantTreble - _smoothedTreble) * 0.60f
            : _smoothedTreble * 0.85f;

        // 5. Overall Glow Intensity Envelope
        float targetGlow = Math.Clamp(instantEnergy * 0.60f + _transientEnergy * 0.45f, 0.03f, 1.0f);

        if (targetGlow > _overallGlowIntensity)
        {
            _overallGlowIntensity += (targetGlow - _overallGlowIntensity) * 0.65f; // Fast attack
        }
        else
        {
            _overallGlowIntensity += (targetGlow - _overallGlowIntensity) * 0.16f; // Smooth decay
        }
    }

    /// <summary>
    /// Resets all internal energy and transient states.
    /// </summary>
    public void Reset()
    {
        _energyEma = 0.05f;
        _transientEnergy = 0f;
        _prevEnergy = 0f;
        _smoothedBass = 0f;
        _smoothedMid = 0f;
        _smoothedTreble = 0f;
        _overallGlowIntensity = 0f;
        _lastBeatUtc = DateTime.MinValue;
        _virtualClockMs = 0.0;
        _lastBeatVirtualMs = -1000.0;
        IsBeatDetected = false;
    }
}
