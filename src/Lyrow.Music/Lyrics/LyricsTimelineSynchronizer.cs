using Lyrow.Core.Contracts;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;

namespace Lyrow.Music.Lyrics;

/// <summary>
/// Backward-compatible wrapper delegating to Lyrow.Core.Lyrics.LyricsTimelineSynchronizer.
/// </summary>
public sealed class LyricsTimelineSynchronizer : IDisposable
{
    private readonly Lyrow.Core.Lyrics.LyricsTimelineSynchronizer _inner;

    public LyricsTimelineSynchronizer(IEventBus? eventBus = null)
    {
        _inner = new Lyrow.Core.Lyrics.LyricsTimelineSynchronizer(eventBus);
        _inner.ActiveLineChanged += (s, e) => ActiveLineChanged?.Invoke(this, e);
        _inner.PositionTicked += (s, pos) => PositionTicked?.Invoke(this, pos);
    }

    public event EventHandler<ActiveLyricLineChangedEventArgs>? ActiveLineChanged;
    public event EventHandler<TimeSpan>? PositionTicked;

    public int CurrentActiveIndex => _inner.CurrentActiveIndex;
    public SyncedLyrics CurrentLyrics => _inner.CurrentLyrics;

    public int SyncOffsetMs
    {
        get => _inner.SyncOffsetMs;
        set => _inner.SyncOffsetMs = value;
    }

    public int TargetFps
    {
        get => _inner.TargetFps;
        set => _inner.TargetFps = value;
    }

    public void SetLyrics(SyncedLyrics lyrics) => _inner.SetLyrics(lyrics);
    public void UpdateTimeline(MediaTimelineInfo timeline) => _inner.UpdateTimeline(timeline);
    public void EvaluatePosition(TimeSpan rawPosition) => _inner.EvaluatePosition(rawPosition);
    public void Reset() => _inner.Reset();
    public void Dispose() => _inner.Dispose();
}
