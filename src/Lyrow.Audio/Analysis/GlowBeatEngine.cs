using Lyrow.Core.Models;

namespace Lyrow.Audio.Analysis;

/// <summary>
/// Audio dynamics processor tracking composite spectral energy, 5 frequency sub-bands,
/// spectral flux onset detection, short-term tempo &amp; beat-phase tracking, and fast-attack transients
/// with zero allocation per frame.
/// Implements ambient pipeline: Audio Energy -> Spectral Flux &amp; Tempo Tracker -> Attack -> Hold -> Decay -> Rendered 4-Edge Intensity.
/// </summary>
public sealed class GlowBeatEngine
{
    private const int FluxHistorySize = 48;
    private const int MaxBeatIntervals = 16;

    private readonly float[] _fluxHistory = new float[FluxHistorySize];
    private readonly double[] _beatIntervalsMs = new double[MaxBeatIntervals];
    private int _fluxHistoryIndex = 0;
    private int _fluxHistoryCount = 0;
    private int _beatIntervalIndex = 0;
    private int _beatIntervalCount = 0;

    private float _energyEma = 0.05f;
    private float _fluxEma = 0.02f;
    private float _transientEnergy = 0f;
    private float _prevEnergy = 0f;
    private float _prevBass = 0f;
    private float _prevMid = 0f;
    private float _prevTreble = 0f;
    private float _smoothedBass = 0f;
    private float _smoothedLowMid = 0f;
    private float _smoothedMid = 0f;
    private float _smoothedHighMid = 0f;
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

    // Tempo & Beat-Phase State
    private float _estimatedBpm = 0f;
    private float _tempoConfidence = 0f;
    private float _beatPhase = 0f;

    private DateTime _lastBeatUtc = DateTime.MinValue;
    private DateTime _lastTransientUtc = DateTime.MinValue;
    private DateTime _lastProcessUtc = DateTime.MinValue;
    private double _virtualClockMs = 0.0;
    private double _lastBeatVirtualMs = -1000.0;
    private double _lastTransientVirtualMs = -1000.0;

    public float OverallGlowIntensity => _overallGlowIntensity;
    public float TransientEnergy => _transientEnergy;
    public float BassIntensity => _smoothedBass;
    public float LowMidIntensity => _smoothedLowMid;
    public float MidIntensity => _smoothedMid;
    public float HighMidIntensity => _smoothedHighMid;
    public float TrebleIntensity => _smoothedTreble;

    public float TopEdgeIntensity => _topEdgeIntensity;
    public float BottomEdgeIntensity => _bottomEdgeIntensity;
    public float LeftEdgeIntensity => _leftEdgeIntensity;
    public float RightEdgeIntensity => _rightEdgeIntensity;

    public bool IsBeatDetected { get; private set; }
    public bool IsTransientDetected { get; private set; }
    public bool IsInHoldPhase => _overallHoldRemainingMs > 0.0;
    public float EstimatedBpm => _estimatedBpm;
    public float TempoConfidence => _tempoConfidence;
    public float BeatPhase => _beatPhase;

    /// <summary>
    /// Updates energy, 4-edge envelopes, spectral flux onset detection, and tempo/transient tracking
    /// using the latest analyzed frequency spectrum.
    /// In real-time audio, frameDeltaMs is negative to use system clock; in tests or fixed timesteps, pass the frame delta (e.g. 16.6ms).
    /// </summary>
    public void Process(FrequencySpectrum spectrum, float sensitivity = 1.0f, double frameDeltaMs = -1.0)
    {
        IsBeatDetected = false;
        IsTransientDetected = false;

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
            _prevBass *= 0.85f;
            _prevMid *= 0.85f;
            _prevTreble *= 0.85f;
            _smoothedBass *= 0.85f;
            _smoothedLowMid *= 0.85f;
            _smoothedMid *= 0.85f;
            _smoothedHighMid *= 0.85f;
            _smoothedTreble *= 0.85f;
            _topEdgeIntensity *= 0.86f;
            _bottomEdgeIntensity *= 0.86f;
            _leftEdgeIntensity *= 0.86f;
            _rightEdgeIntensity *= 0.86f;
            _energyEma *= 0.95f;
            _fluxEma *= 0.92f;
            _tempoConfidence *= 0.96f;

            if (_overallGlowIntensity < 0.002f)
            {
                _overallGlowIntensity = 0f;
                _transientEnergy = 0f;
                _topEdgeIntensity = 0f;
                _bottomEdgeIntensity = 0f;
                _leftEdgeIntensity = 0f;
                _rightEdgeIntensity = 0f;
            }
            return;
        }

        // High reactivity support: [0.0, 5.0]. Normalizes and soft-clamps with hyperbolic tonemapping at high values to prevent strobing
        float sens = Math.Clamp(sensitivity, 0.0f, 5.0f);
        float toneMappedSens = sens <= 1.0f ? sens : 1.0f + MathF.Tanh((sens - 1.0f) * 0.75f) * 1.5f;

        // 1. Calculate isolated 5-band energies (Sub/Bass, Low-Mid, Mid, High-Mid, Treble)
        float instantBass = Math.Clamp((spectrum.Bass * 1.6f + spectrum.SubBass * 1.2f) * toneMappedSens, 0f, 1.5f);
        float instantLowMid = Math.Clamp(spectrum.LowMid * 1.35f * toneMappedSens, 0f, 1.5f);
        float instantMid = Math.Clamp(spectrum.Mid * 1.4f * toneMappedSens, 0f, 1.5f);
        float instantHighMid = Math.Clamp(spectrum.HighMid * 1.3f * toneMappedSens, 0f, 1.5f);
        float instantTreble = Math.Clamp(spectrum.Treble * 1.2f * toneMappedSens, 0f, 1.5f);

        // 2. Composite RMS and spectral energy
        float instantEnergy = Math.Clamp(
            (spectrum.Levels.Rms * 2.2f + instantBass * 0.7f + instantMid * 0.4f + instantTreble * 0.2f) * toneMappedSens,
            0f, 2.0f);

        // 3. Spectral Flux & Positive Sub-Band Onset Calculation
        float bassDelta = Math.Max(0f, instantBass - _prevBass);
        float midDelta = Math.Max(0f, instantMid - _prevMid);
        float trebleDelta = Math.Max(0f, instantTreble - _prevTreble);
        float energyDelta = instantEnergy - _prevEnergy;

        float compositeFlux = spectrum.SpectralFlux > 0f
            ? (spectrum.SpectralFlux * 0.55f + spectrum.BassFlux * 0.45f + bassDelta * 0.65f + Math.Max(0f, energyDelta) * 0.55f)
            : (bassDelta * 0.85f + midDelta * 0.45f + trebleDelta * 0.30f + Math.Max(0f, energyDelta) * 0.75f);

        // Push to circular flux history for adaptive mean + variance thresholding
        _fluxHistory[_fluxHistoryIndex] = compositeFlux;
        _fluxHistoryIndex = (_fluxHistoryIndex + 1) % FluxHistorySize;
        if (_fluxHistoryCount < FluxHistorySize) _fluxHistoryCount++;

        float meanFlux = 0f;
        for (int i = 0; i < _fluxHistoryCount; i++) meanFlux += _fluxHistory[i];
        meanFlux /= Math.Max(1, _fluxHistoryCount);

        float varFlux = 0f;
        for (int i = 0; i < _fluxHistoryCount; i++)
        {
            float d = _fluxHistory[i] - meanFlux;
            varFlux += d * d;
        }
        float stdFlux = MathF.Sqrt(varFlux / Math.Max(1, _fluxHistoryCount));
        float adaptiveFluxThreshold = Math.Max(0.06f, meanFlux + 1.15f * stdFlux);

        double msSinceLastBeat;
        double msSinceLastTransient;
        if (frameDeltaMs > 0.0)
        {
            _virtualClockMs += frameDeltaMs;
            msSinceLastBeat = _virtualClockMs - _lastBeatVirtualMs;
            msSinceLastTransient = _virtualClockMs - _lastTransientVirtualMs;
        }
        else
        {
            msSinceLastBeat = (now - _lastBeatUtc).TotalMilliseconds;
            msSinceLastTransient = (now - _lastTransientUtc).TotalMilliseconds;
        }

        // Advance beat phase if tempo is tracked
        if (_estimatedBpm >= 55f && _estimatedBpm <= 200f)
        {
            double beatPeriodMs = 60000.0 / _estimatedBpm;
            if (beatPeriodMs > 100.0)
            {
                _beatPhase = (float)((msSinceLastBeat % beatPeriodMs) / beatPeriodMs);
            }
        }

        float beatThreshold = Math.Max(0.08f, _energyEma * 1.24f);
        bool isEnergyOnset = energyDelta > 0.095f * Math.Min(sens, 1.2f);
        bool isFluxOnset = compositeFlux > adaptiveFluxThreshold && (bassDelta > 0.08f || energyDelta > 0.07f);

        // Guard against flat broadband noise (where stdFlux is tiny and bassDelta is near zero after initial frame)
        bool isBroadbandSteady = _fluxHistoryCount >= 10 && stdFlux < 0.025f && Math.Abs(energyDelta) < 0.04f;

        if (!isBroadbandSteady &&
            ((instantEnergy > beatThreshold && isEnergyOnset) || (instantEnergy > beatThreshold * 0.90f && isFluxOnset)) &&
            msSinceLastBeat > 115.0)
        {
            IsBeatDetected = true;
            IsTransientDetected = true;

            if (msSinceLastBeat >= 260.0 && msSinceLastBeat <= 1200.0)
            {
                RecordBeatInterval(msSinceLastBeat);
            }

            _lastBeatUtc = now;
            _lastTransientUtc = now;
            _lastBeatVirtualMs = _virtualClockMs;
            _lastTransientVirtualMs = _virtualClockMs;
            _beatPhase = 0f;

            // Transient spike proportional to energy & flux surge
            float surge = Math.Clamp((instantEnergy - _energyEma) * 1.55f + compositeFlux * 0.35f, 0.45f, 1.0f);
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
            // Independent fast-attack mid/high transient detection (snare, clap, hi-hat, percussive accent)
            float hiTransientFlux = Math.Max(midDelta, trebleDelta) + spectrum.MidFlux * 0.4f + spectrum.TrebleFlux * 0.5f;
            if (!isBroadbandSteady && hiTransientFlux > Math.Max(0.14f, _fluxEma * 1.45f) && msSinceLastTransient > 65.0)
            {
                IsTransientDetected = true;
                _lastTransientUtc = now;
                _lastTransientVirtualMs = _virtualClockMs;
                float miniSurge = Math.Clamp(hiTransientFlux * 0.75f, 0.20f, 0.65f);
                _transientEnergy = Math.Min(1.0f, _transientEnergy + miniSurge);
                _topHoldRemainingMs = Math.Max(_topHoldRemainingMs, 30.0);
                _leftHoldRemainingMs = Math.Max(_leftHoldRemainingMs, 25.0);
                _rightHoldRemainingMs = Math.Max(_rightHoldRemainingMs, 25.0);
            }
            else
            {
                // Exponential transient decay (~150ms punch duration)
                _transientEnergy *= 0.84f;
            }
        }

        _prevEnergy = instantEnergy;
        _prevBass = instantBass;
        _prevMid = instantMid;
        _prevTreble = instantTreble;

        // Update rolling EMAs
        _energyEma += (instantEnergy - _energyEma) * 0.05f;
        _fluxEma += (compositeFlux - _fluxEma) * 0.08f;

        // 4. Band Smoothing (Fast attack, natural decay across all 5 bands)
        _smoothedBass = instantBass > _smoothedBass
            ? _smoothedBass + (instantBass - _smoothedBass) * 0.72f
            : _smoothedBass * 0.88f;

        _smoothedLowMid = instantLowMid > _smoothedLowMid
            ? _smoothedLowMid + (instantLowMid - _smoothedLowMid) * 0.70f
            : _smoothedLowMid * 0.87f;

        _smoothedMid = instantMid > _smoothedMid
            ? _smoothedMid + (instantMid - _smoothedMid) * 0.68f
            : _smoothedMid * 0.86f;

        _smoothedHighMid = instantHighMid > _smoothedHighMid
            ? _smoothedHighMid + (instantHighMid - _smoothedHighMid) * 0.66f
            : _smoothedHighMid * 0.85f;

        _smoothedTreble = instantTreble > _smoothedTreble
            ? _smoothedTreble + (instantTreble - _smoothedTreble) * 0.64f
            : _smoothedTreble * 0.85f;

        // 5. Overall Glow Intensity Envelope (Attack -> Hold -> Decay)
        float phasePulse = (_tempoConfidence > 0.45f && _beatPhase < 0.18f)
            ? (1.0f - (_beatPhase / 0.18f)) * 0.08f * _tempoConfidence
            : 0f;
        float targetGlow = Math.Clamp(instantEnergy * 0.52f + _transientEnergy * 0.45f + phasePulse, 0.03f, 1.0f);

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
        float perceptualRms = MathF.Pow(Math.Clamp(spectrum.Levels.Rms * 2.5f * sens, 0f, 1f), 0.65f);
        float perceptualBass = MathF.Pow(Math.Clamp(_smoothedBass, 0f, 1f), 0.65f);
        float perceptualLowMid = MathF.Pow(Math.Clamp(_smoothedLowMid, 0f, 1f), 0.65f);
        float perceptualMid = MathF.Pow(Math.Clamp(_smoothedMid, 0f, 1f), 0.65f);
        float perceptualHighMid = MathF.Pow(Math.Clamp(_smoothedHighMid, 0f, 1f), 0.65f);
        float perceptualTreble = MathF.Pow(Math.Clamp(_smoothedTreble, 0f, 1f), 0.65f);

        float targetBottom = Math.Clamp(perceptualBass * 0.65f + perceptualLowMid * 0.15f + perceptualRms * 0.20f + _transientEnergy * 0.45f, 0.05f, 1.0f);
        float targetTop = Math.Clamp(perceptualTreble * 0.50f + perceptualHighMid * 0.30f + perceptualRms * 0.20f + _transientEnergy * 0.35f, 0.05f, 1.0f);
        float targetLeft = Math.Clamp(perceptualMid * 0.45f + perceptualLowMid * 0.30f + perceptualRms * 0.20f + _transientEnergy * 0.30f, 0.05f, 1.0f);
        float targetRight = Math.Clamp(perceptualMid * 0.40f + perceptualHighMid * 0.35f + perceptualRms * 0.20f + _transientEnergy * 0.30f, 0.05f, 1.0f);

        _bottomEdgeIntensity = ApplyEnvelope(_bottomEdgeIntensity, targetBottom, ref _bottomHoldRemainingMs, dtMs, attackRate: 0.72f, decayRate: 0.14f);
        _topEdgeIntensity = ApplyEnvelope(_topEdgeIntensity, targetTop, ref _topHoldRemainingMs, dtMs, attackRate: 0.68f, decayRate: 0.15f);
        _leftEdgeIntensity = ApplyEnvelope(_leftEdgeIntensity, targetLeft, ref _leftHoldRemainingMs, dtMs, attackRate: 0.66f, decayRate: 0.15f);
        _rightEdgeIntensity = ApplyEnvelope(_rightEdgeIntensity, targetRight, ref _rightHoldRemainingMs, dtMs, attackRate: 0.66f, decayRate: 0.15f);
    }

    private void RecordBeatInterval(double intervalMs)
    {
        // Normalize octave multiples into standard [333ms..1000ms] (60..180 BPM) range
        double normalizedMs = intervalMs;
        while (normalizedMs > 1000.0) normalizedMs *= 0.5;
        while (normalizedMs < 333.3 && normalizedMs > 100.0) normalizedMs *= 2.0;

        _beatIntervalsMs[_beatIntervalIndex] = normalizedMs;
        _beatIntervalIndex = (_beatIntervalIndex + 1) % MaxBeatIntervals;
        if (_beatIntervalCount < MaxBeatIntervals) _beatIntervalCount++;

        if (_beatIntervalCount < 2)
        {
            _estimatedBpm = (float)(60000.0 / normalizedMs);
            _tempoConfidence = 0.25f;
            return;
        }

        // Recency-weighted mean & tempo shift detection for agile adaptation without jitter
        int newestIdx = (_beatIntervalIndex - 1 + MaxBeatIntervals) % MaxBeatIntervals;
        int prevIdx = (_beatIntervalIndex - 2 + MaxBeatIntervals) % MaxBeatIntervals;
        double recentDiff = Math.Abs(_beatIntervalsMs[newestIdx] - _beatIntervalsMs[prevIdx]);
        bool isTempoShift = _beatIntervalCount >= 3 &&
                            (recentDiff / _beatIntervalsMs[newestIdx]) < 0.12 &&
                            Math.Abs(_beatIntervalsMs[newestIdx] - (60000.0 / Math.Max(1f, _estimatedBpm))) > 35.0;

        double weightedSum = 0.0;
        double totalWeight = 0.0;
        double decayFactor = isTempoShift ? 0.50 : 0.82;

        for (int k = 0; k < _beatIntervalCount; k++)
        {
            int bufIdx = (_beatIntervalIndex - 1 - k + MaxBeatIntervals) % MaxBeatIntervals;
            double w = Math.Pow(decayFactor, k);
            weightedSum += _beatIntervalsMs[bufIdx] * w;
            totalWeight += w;
        }

        double meanMs = weightedSum / totalWeight;

        double devSum = 0.0;
        for (int i = 0; i < _beatIntervalCount; i++)
        {
            devSum += Math.Abs(_beatIntervalsMs[i] - meanMs);
        }
        double meanDev = devSum / _beatIntervalCount;

        float candidateBpm = (float)Math.Clamp(60000.0 / meanMs, 60.0, 180.0);
        if (_estimatedBpm <= 0f)
        {
            _estimatedBpm = candidateBpm;
        }
        else
        {
            // Adaptive smoothing: adapt faster (0.75) during detected tempo transitions, smooth gently (0.35) during steady state
            float adaptRate = isTempoShift ? 0.75f : 0.35f;
            _estimatedBpm += (candidateBpm - _estimatedBpm) * adaptRate;
        }

        float stability = (float)Math.Clamp(1.0 - (meanDev / Math.Max(1.0, meanMs * 0.25)), 0.0, 1.0);
        float sampleWeight = Math.Min(1.0f, _beatIntervalCount / 6.0f);
        _tempoConfidence = Math.Clamp(stability * sampleWeight, 0.0f, 1.0f);
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
    /// Resets all internal energy, spectral flux, tempo, and transient states (e.g., on track change).
    /// </summary>
    public void Reset()
    {
        Array.Clear(_fluxHistory);
        Array.Clear(_beatIntervalsMs);
        _fluxHistoryIndex = 0;
        _fluxHistoryCount = 0;
        _beatIntervalIndex = 0;
        _beatIntervalCount = 0;
        _energyEma = 0.05f;
        _fluxEma = 0.02f;
        _transientEnergy = 0f;
        _prevEnergy = 0f;
        _prevBass = 0f;
        _prevMid = 0f;
        _prevTreble = 0f;
        _smoothedBass = 0f;
        _smoothedLowMid = 0f;
        _smoothedMid = 0f;
        _smoothedHighMid = 0f;
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
        _estimatedBpm = 0f;
        _tempoConfidence = 0f;
        _beatPhase = 0f;
        _lastBeatUtc = DateTime.MinValue;
        _lastTransientUtc = DateTime.MinValue;
        _lastProcessUtc = DateTime.MinValue;
        _virtualClockMs = 0.0;
        _lastBeatVirtualMs = -1000.0;
        _lastTransientVirtualMs = -1000.0;
        IsBeatDetected = false;
        IsTransientDetected = false;
    }
}
