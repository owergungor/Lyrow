using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.Core.Lyrics;

/// <summary>
/// High-efficiency timeline synchronization engine calculating active lyric line
/// and progress interpolation without allocations.
/// Responds immediately to seek operations and pauses CPU timer when playback stops.
/// </summary>
public sealed class LyricsTimelineSynchronizer : IDisposable
{
    private readonly object _lock = new();
    private readonly IEventBus? _eventBus;
    private SyncedLyrics _currentLyrics = SyncedLyrics.Empty;
    private MediaTimelineInfo _currentTimeline = MediaTimelineInfo.Zero;

    private CancellationTokenSource? _activeCts;
    private Task? _syncLoopTask;
    private int _lastActiveIndex = -2;
    private int _syncOffsetMs = 0;
    private int _targetFps = 30;
    private bool _isDisposed;

    public LyricsTimelineSynchronizer(IEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    /// <summary>Fired when the current active lyric line advances or jumps.</summary>
    public event EventHandler<ActiveLyricLineChangedEventArgs>? ActiveLineChanged;

    /// <summary>Fired on every tick with current interpolated playback position.</summary>
    public event EventHandler<TimeSpan>? PositionTicked;

    public int CurrentActiveIndex => _lastActiveIndex;
    public SyncedLyrics CurrentLyrics => _currentLyrics;

    public int SyncOffsetMs
    {
        get => _syncOffsetMs;
        set
        {
            lock (_lock)
            {
                _syncOffsetMs = value;
                EvaluatePosition(_currentTimeline.GetInterpolatedPosition());
            }
        }
    }

    public int TargetFps
    {
        get => _targetFps;
        set => _targetFps = Math.Clamp(value, 10, 60);
    }

    public void SetLyrics(SyncedLyrics lyrics)
    {
        lock (_lock)
        {
            _currentLyrics = lyrics ?? SyncedLyrics.Empty;
            _lastActiveIndex = -2;
            EvaluatePosition(_currentTimeline.GetInterpolatedPosition());
        }
    }

    public void UpdateTimeline(MediaTimelineInfo timeline)
    {
        lock (_lock)
        {
            _currentTimeline = timeline;
            EvaluatePosition(timeline.GetInterpolatedPosition());

            if (timeline.Status == MediaPlaybackStatus.Playing && _syncLoopTask == null)
            {
                StartLoop();
            }
            else if (timeline.Status != MediaPlaybackStatus.Playing && _syncLoopTask != null)
            {
                StopLoop();
            }
        }
    }

    private void StartLoop()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _activeCts = new CancellationTokenSource();
        var ct = _activeCts.Token;

        var interval = TimeSpan.FromMilliseconds(1000.0 / _targetFps);
        _syncLoopTask = Task.Run(() => SyncLoopAsync(interval, ct), ct);
    }

    private void StopLoop()
    {
        _activeCts?.Cancel();
        _activeCts = null;
        _syncLoopTask = null;
    }

    private async Task SyncLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)) break;

                TimeSpan pos;
                lock (_lock)
                {
                    pos = _currentTimeline.GetInterpolatedPosition();
                    EvaluatePosition(pos);
                }

                PositionTicked?.Invoke(this, pos);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Prevent background loop crash
            }
        }
    }

    public void EvaluatePosition(TimeSpan rawPosition)
    {
        var offsetPosition = rawPosition + TimeSpan.FromMilliseconds(_syncOffsetMs);
        if (offsetPosition < TimeSpan.Zero) offsetPosition = TimeSpan.Zero;

        int activeIndex = _currentLyrics.FindActiveLineIndex(offsetPosition);

        if (activeIndex != _lastActiveIndex)
        {
            _lastActiveIndex = activeIndex;
            LyricLine? line = (activeIndex >= 0 && activeIndex < _currentLyrics.Lines.Count)
                ? _currentLyrics.Lines[activeIndex]
                : null;

            ActiveLineChanged?.Invoke(this, new ActiveLyricLineChangedEventArgs(activeIndex, line));

            if (_eventBus != null)
            {
                _ = _eventBus.PublishAsync(new ActiveLyricLineChangedEvent(activeIndex, line));
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            StopLoop();
            _currentLyrics = SyncedLyrics.Empty;
            _currentTimeline = MediaTimelineInfo.Zero;
            _lastActiveIndex = -2;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Reset();
    }
}

public sealed class ActiveLyricLineChangedEventArgs : EventArgs
{
    public ActiveLyricLineChangedEventArgs(int lineIndex, LyricLine? line)
    {
        LineIndex = lineIndex;
        Line = line;
    }

    public int LineIndex { get; }
    public LyricLine? Line { get; }
}
