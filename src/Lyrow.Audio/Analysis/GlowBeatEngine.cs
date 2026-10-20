using Lyrow.Core.Models;

namespace Lyrow.Audio.Analysis;

/// <summary>
/// Audio dynamics processor tracking composite spectral energy, frequency sub-bands,
/// and beat/transient spikes with zero allocation per frame.
/// Implements Lumn-style pipeline: Audio Energy -> Smoothing -> Attack -> Hold -> Decay -> Rendered 4-Edge Intensity.
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

    // 4-edge independent envelope states (Top, Bottom, Left, Right)
    private float _topEdgeIntensity = 0f;
    private float _bottomEdgeIntensity = 0f;
    private float _leftEdgeIntensity = 0f;
    private float _rightEdgeIntensity = 0f;

    // Hold timers (in milliseconds) for Attack -> Hold -> Decay behavior
    private double _overallHoldRemainingMs = 0.0;
    private double _topHoldRemainingMs = 0.0;
    private double _bottomHoldRemainingMs = 0.0;
    private double _leftHoldRemainingMs = 0.0;
    private double _rightHoldRemainingMs = 0.0;

    private DateTime _lastBeatUtc = DateTime.MinValue;
    private DateTime _lastProcessUtc = DateTime.MinValue;
    private double _virtualClockMs = 0.0;
    private double _lastBeatVirtualMs = -1000.0;

    public float OverallGlowIntensity => _overallGlowIntensity;
    public float TransientEnergy => _transientEnergy;
    public float BassIntensity => _smoothedBass;
    public float MidIntensity => _smoothedMid;
    public float TrebleIntensity => _smoothedTreble;

    public float TopEdgeIntensity => _topEdgeIntensity;
    public float BottomEdgeIntensity => _bottomEdgeIntensity;
    public float LeftEdgeIntensity => _leftEdgeIntensity;
    public float RightEdgeIntensity => _rightEdgeIntensity;

    public bool IsBeatDetected { get; private set; }
    public bool IsInHoldPhase => _overallHoldRemainingMs > 0.0;

    /// <summary>
    /// Updates energy, 4-edge envelopes, and transient tracking using the latest analyzed frequency spectrum.
    /// In real-time audio, frameDeltaMs is negative to use system clock; in tests or fixed timesteps, pass the frame delta (e.g. 16.6ms).
    /// </summary>
    public void Process(FrequencySpectrum spectrum, float sensitivity = 1.0f, double frameDeltaMs = -1.0)
    {
        IsBeatDetected = false;

        var now = DateTime.UtcNow;
        double dtMs;
        if (frameDeltaMs > 0.0)
        {
            dtMs = frameDeltaMs;
        }
        else if (_lastProcessUtc != DateTime.MinValue)
        {
            double elapsed = (now - _lastProcessUtc).TotalMilliseconds;
            dtMs = elapsed < 2.0 ? 16.6 : Math.Clamp(elapsed, 4.0, 100.0);
        }
        else
        {
            dtMs = 16.6;
        }
        _lastProcessUtc = now;

        if (spectrum.Levels.IsSilent)
        {
            // Smoothly decay to zero when silent to prevent abrupt shutoff
            _overallHoldRemainingMs = 0.0;
            _topHoldRemainingMs = 0.0;
            _bottomHoldRemainingMs = 0.0;
            _leftHoldRemainingMs = 0.0;
            _rightHoldRemainingMs = 0.0;

            _overallGlowIntensity *= 0.85f;
            _transientEnergy *= 0.80f;
            _prevEnergy *= 0.85f;
            _smoothedBass *= 0.85f;
            _smoothedMid *= 0.85f;
            _smoothedTreble *= 0.85f;
            _topEdgeIntensity *= 0.86f;
            _bottomEdgeIntensity *= 0.86f;
            _leftEdgeIntensity *= 0.86f;
            _rightEdgeIntensity *= 0.86f;
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
        float beatThreshold = Math.Max(0.08f, _energyEma * 1.28f);
        bool isOnset = energyDelta > 0.10f * Math.Min(sens, 1.2f);

        if (instantEnergy > beatThreshold && isOnset && msSinceLastBeat > 115.0)
        {
            IsBeatDetected = true;
            _lastBeatUtc = now;
            _lastBeatVirtualMs = _virtualClockMs;

            // Transient spike proportional to energy surge
            float surge = Math.Clamp((instantEnergy - _energyEma) * 1.6f, 0.45f, 1.0f);
            _transientEnergy = Math.Min(1.0f, _transientEnergy + surge);

            // Trigger short Hold phase (~45ms) so beat punch holds before smooth decay
            _overallHoldRemainingMs = 45.0;
            _bottomHoldRemainingMs = 55.0;
            _topHoldRemainingMs = 40.0;
            _leftHoldRemainingMs = 45.0;
            _rightHoldRemainingMs = 45.0;
        }
        else
        {
            // Exponential transient decay (~150ms punch duration)
            _transientEnergy *= 0.84f;
        }

        _prevEnergy = instantEnergy;

        // Update rolling energy moving average
        _energyEma += (instantEnergy - _energyEma) * 0.05f;

        // 4. Band Smoothing (Fast attack, natural decay)
        _smoothedBass = instantBass > _smoothedBass
            ? _smoothedBass + (instantBass - _smoothedBass) * 0.72f
            : _smoothedBass * 0.88f;

        _smoothedMid = instantMid > _smoothedMid
            ? _smoothedMid + (instantMid - _smoothedMid) * 0.68f
            : _smoothedMid * 0.86f;

        _smoothedTreble = instantTreble > _smoothedTreble
            ? _smoothedTreble + (instantTreble - _smoothedTreble) * 0.64f
            : _smoothedTreble * 0.85f;

        // 5. Overall Glow Intensity Envelope (Attack -> Hold -> Decay)
        float targetGlow = Math.Clamp(instantEnergy * 0.52f + _transientEnergy * 0.45f, 0.03f, 1.0f);

        if (targetGlow >= _overallGlowIntensity)
        {
            _overallGlowIntensity += (targetGlow - _overallGlowIntensity) * 0.68f; // Fast attack
        }
        else if (_overallHoldRemainingMs > 0.0)
        {
            _overallHoldRemainingMs = Math.Max(0.0, _overallHoldRemainingMs - dtMs);
            // Gentle sustain during hold phase
            _overallGlowIntensity += (targetGlow - _overallGlowIntensity) * 0.04f;
        }
        else
        {
            _overallGlowIntensity += (targetGlow - _overallGlowIntensity) * 0.16f; // Smooth decay
        }

        // 6. Perceptual 4-Edge Independent Intensities (Top, Bottom, Left, Right)
        // Use perceptual compression (pow 0.65) so real-world music at moderate volume drives rich edge movement
        float perceptualRms = MathF.Pow(Math.Clamp(spectrum.Levels.Rms * 2.5f * sens, 0f, 1f), 0.65f);
        float perceptualBass = MathF.Pow(Math.Clamp(_smoothedBass, 0f, 1f), 0.65f);
        float perceptualMid = MathF.Pow(Math.Clamp(_smoothedMid, 0f, 1f), 0.65f);
        float perceptualTreble = MathF.Pow(Math.Clamp(_smoothedTreble, 0f, 1f), 0.65f);

        float targetBottom = Math.Clamp(perceptualBass * 0.65f + perceptualRms * 0.25f + _transientEnergy * 0.45f, 0.05f, 1.0f);
        float targetTop = Math.Clamp(perceptualTreble * 0.55f + perceptualMid * 0.25f + perceptualRms * 0.20f + _transientEnergy * 0.35f, 0.05f, 1.0f);
        float targetLeft = Math.Clamp(perceptualMid * 0.55f + perceptualBass * 0.25f + perceptualRms * 0.20f + _transientEnergy * 0.30f, 0.05f, 1.0f);
        float targetRight = Math.Clamp(perceptualMid * 0.50f + perceptualTreble * 0.30f + perceptualRms * 0.20f + _transientEnergy * 0.30f, 0.05f, 1.0f);

        _bottomEdgeIntensity = ApplyEnvelope(_bottomEdgeIntensity, targetBottom, ref _bottomHoldRemainingMs, dtMs, attackRate: 0.72f, decayRate: 0.14f);
        _topEdgeIntensity = ApplyEnvelope(_topEdgeIntensity, targetTop, ref _topHoldRemainingMs, dtMs, attackRate: 0.68f, decayRate: 0.15f);
        _leftEdgeIntensity = ApplyEnvelope(_leftEdgeIntensity, targetLeft, ref _leftHoldRemainingMs, dtMs, attackRate: 0.66f, decayRate: 0.15f);
        _rightEdgeIntensity = ApplyEnvelope(_rightEdgeIntensity, targetRight, ref _rightHoldRemainingMs, dtMs, attackRate: 0.66f, decayRate: 0.15f);
    }

    private static float ApplyEnvelope(
        float current,
        float target,
        ref double holdRemainingMs,
        double dtMs,
        float attackRate,
        float decayRate)
    {
        if (target >= current)
        {
            return current + (target - current) * attackRate;
        }

        if (holdRemainingMs > 0.0)
        {
            holdRemainingMs = Math.Max(0.0, holdRemainingMs - dtMs);
            return current + (target - current) * 0.03f;
        }

        return current + (target - current) * decayRate;
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
        _topEdgeIntensity = 0f;
        _bottomEdgeIntensity = 0f;
        _leftEdgeIntensity = 0f;
        _rightEdgeIntensity = 0f;
        _overallHoldRemainingMs = 0.0;
        _topHoldRemainingMs = 0.0;
        _bottomHoldRemainingMs = 0.0;
        _leftHoldRemainingMs = 0.0;
        _rightHoldRemainingMs = 0.0;
        _lastBeatUtc = DateTime.MinValue;
        _lastProcessUtc = DateTime.MinValue;
        _virtualClockMs = 0.0;
        _lastBeatVirtualMs = -1000.0;
        IsBeatDetected = false;
    }
}
