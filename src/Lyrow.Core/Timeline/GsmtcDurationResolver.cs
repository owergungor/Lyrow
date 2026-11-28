namespace Lyrow.Core.Timeline;

/// <summary>
/// Pure, deterministic resolver for Windows GSMTC timeline properties
/// (<c>StartTime</c>, <c>EndTime</c>, <c>MinSeekTime</c>, <c>MaxSeekTime</c>, <c>Position</c>).
/// Computes accurate track-relative position and duration while rejecting invalid,
/// negative, live-stream, or non-authoritative sentinel values.
/// </summary>
public static class GsmtcDurationResolver
{
    /// <summary>
    /// Upper sanity bound for a single media track duration (rejects sentinel values like TimeSpan.MaxValue).
    /// </summary>
    public static readonly TimeSpan MaxSaneTrackDuration = TimeSpan.FromHours(24);

    /// <summary>
    /// Minimum duration required before accepting <c>MaxSeekTime</c> as a fallback end time.
    /// </summary>
    public static readonly TimeSpan MinValidFallbackDuration = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Resolves track-relative <c>Position</c> and <c>Duration</c> from raw GSMTC timeline fields.
    /// When <c>StartTime &gt; 0</c>, both duration (<c>EndTime - StartTime</c>) and position
    /// (<c>Position - StartTime</c>) are normalized relative to <c>StartTime</c>.
    /// <c>MaxSeekTime</c> is only used as a fallback when <c>EndTime</c> is missing/invalid AND
    /// <c>MaxSeekTime</c> represents a genuine non-degenerate seekable track window.
    /// </summary>
    public static (TimeSpan Position, TimeSpan Duration) Resolve(
        TimeSpan startTime,
        TimeSpan endTime,
        TimeSpan minSeekTime,
        TimeSpan maxSeekTime,
        TimeSpan rawPosition)
    {
        TimeSpan origin = (startTime > TimeSpan.Zero && startTime <= MaxSaneTrackDuration)
            ? startTime
            : TimeSpan.Zero;

        TimeSpan duration = TimeSpan.Zero;

        // 1. Authoritative EndTime (must be strictly greater than origin and within sane bounds)
        if (endTime > origin && endTime <= MaxSaneTrackDuration)
        {
            var candidate = endTime - origin;
            if (candidate > TimeSpan.Zero && candidate <= MaxSaneTrackDuration)
            {
                duration = candidate;
            }
        }
        // 2. Validated MaxSeekTime fallback ONLY when EndTime is absent/invalid
        else if (IsValidMaxSeekFallback(origin, minSeekTime, maxSeekTime, rawPosition))
        {
            duration = maxSeekTime - origin;
        }

        // 3. Track-relative position (never negative; clamped to duration when duration is known)
        TimeSpan pos;
        if (rawPosition < TimeSpan.Zero || rawPosition > MaxSaneTrackDuration)
        {
            pos = TimeSpan.Zero;
        }
        else if (origin > TimeSpan.Zero)
        {
            pos = rawPosition >= origin ? (rawPosition - origin) : TimeSpan.Zero;
        }
        else
        {
            pos = rawPosition;
        }

        if (duration > TimeSpan.Zero && pos > duration)
        {
            pos = duration;
        }

        return (pos, duration);
    }

    private static bool IsValidMaxSeekFallback(
        TimeSpan origin,
        TimeSpan minSeekTime,
        TimeSpan maxSeekTime,
        TimeSpan rawPosition)
    {
        if (maxSeekTime <= origin || maxSeekTime > MaxSaneTrackDuration)
        {
            return false;
        }

        // Degenerate seek range (e.g., live stream / radio where MinSeekTime == MaxSeekTime or MinSeekTime > MaxSeekTime)
        if (minSeekTime >= maxSeekTime)
        {
            return false;
        }

        var candidateDuration = maxSeekTime - origin;
        if (candidateDuration < MinValidFallbackDuration || candidateDuration > MaxSaneTrackDuration)
        {
            return false;
        }

        // If rawPosition is far beyond maxSeekTime, maxSeekTime is not a true track end
        if (rawPosition > TimeSpan.Zero && rawPosition <= MaxSaneTrackDuration &&
            rawPosition > maxSeekTime + TimeSpan.FromSeconds(5))
        {
            return false;
        }

        return true;
    }
}
