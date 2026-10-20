namespace Lyrow.Core.Services;

/// <summary>
/// Debounces and deduplicates rapid notification messages to prevent toast spam
/// while maintaining responsive feedback for user interactions.
/// </summary>
public sealed class ToastDebouncer
{
    private readonly TimeSpan _debounceWindow;
    private readonly Dictionary<string, (DateTime lastSeen, int count)> _recentMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public ToastDebouncer(TimeSpan? debounceWindow = null)
    {
        _debounceWindow = debounceWindow ?? TimeSpan.FromSeconds(2.5);
    }

    /// <summary>
    /// Evaluates whether a toast notification with the given message should be displayed.
    /// Returns (shouldShow: true, isDuplicateUpdate: false) for new messages,
    /// (shouldShow: true, isDuplicateUpdate: true) if suppressed as an in-place pulse update,
    /// or (shouldShow: false, ...) if completely throttled.
    /// </summary>
    public (bool shouldShow, bool isDuplicateUpdate, int duplicateCount) RegisterToast(string message, DateTime? currentUtc = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return (false, false, 0);
        }

        var now = currentUtc ?? DateTime.UtcNow;
        string key = message.Trim();

        lock (_lock)
        {
            // Clean up stale entries older than 30 seconds
            var staleKeys = _recentMessages
                .Where(kvp => (now - kvp.Value.lastSeen) > TimeSpan.FromSeconds(30))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var stale in staleKeys)
            {
                _recentMessages.Remove(stale);
            }

            if (_recentMessages.TryGetValue(key, out var entry))
            {
                var elapsed = now - entry.lastSeen;
                if (elapsed < _debounceWindow)
                {
                    // Debounce: update existing entry without spawning a new visual toast card
                    int nextCount = entry.count + 1;
                    _recentMessages[key] = (now, nextCount);
                    return (false, true, nextCount);
                }
            }

            _recentMessages[key] = (now, 1);
            return (true, false, 1);
        }
    }

    /// <summary>Clears all cached message timestamps.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _recentMessages.Clear();
        }
    }
}
