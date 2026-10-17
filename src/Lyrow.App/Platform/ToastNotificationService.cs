using System.Collections.Concurrent;

namespace Lyrow.App.Platform;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record ToastItem(
    Guid Id,
    string Message,
    ToastType Type,
    DateTime CreatedUtc);

public interface IToastNotificationService
{
    event EventHandler<ToastItem>? ToastReceived;
    void Show(string message, ToastType type = ToastType.Info);
    void ShowSuccess(string message);
    void ShowError(string message);
    void ShowWarning(string message);
    void ShowInfo(string message);
}

/// <summary>
/// Thread-safe toast notification service with anti-spam debounce
/// delivering modern, minimal feedback to the application interface.
/// </summary>
public sealed class ToastNotificationService : IToastNotificationService
{
    private readonly ConcurrentDictionary<string, DateTime> _recentMessages = new(StringComparer.Ordinal);
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(1500);

    public event EventHandler<ToastItem>? ToastReceived;

    public void Show(string message, ToastType type = ToastType.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var now = DateTime.UtcNow;

        // Anti-spam debounce: drop duplicate messages within debounce window
        if (_recentMessages.TryGetValue(message, out var lastTime))
        {
            if (now - lastTime < DebounceWindow)
            {
                return;
            }
        }

        _recentMessages[message] = now;

        var toast = new ToastItem(Guid.NewGuid(), message.Trim(), type, now);
        ToastReceived?.Invoke(this, toast);
    }

    public void ShowSuccess(string message) => Show(message, ToastType.Success);
    public void ShowError(string message) => Show(message, ToastType.Error);
    public void ShowWarning(string message) => Show(message, ToastType.Warning);
    public void ShowInfo(string message) => Show(message, ToastType.Info);
}
