using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Lyrow.Core.Services;

namespace Lyrow.App.Controls;

public enum UpdateToastState
{
    Available,
    Downloading,
    Downloaded,
    Installing,
    Completed,
    DownloadFailed,
    InstallFailed
}

/// <summary>
/// Active controller handle for interactive progress toasts (such as in-app self-update downloading).
/// Allows continuous progress updates, state transitions, retry triggers, and keeps toast visible throughout operations.
/// </summary>
public sealed class ProgressToastHandle
{
    private readonly Action<double, string?> _updateProgress;
    private readonly Action<UpdateToastState, string?, Action?> _updateState;
    private readonly Action _dismiss;

    public ProgressToastHandle(
        Action<double, string?> updateProgress,
        Action<UpdateToastState, string?, Action?> updateState,
        Action dismiss)
    {
        _updateProgress = updateProgress;
        _updateState = updateState;
        _dismiss = dismiss;
    }

    public void ReportProgress(double percent, string? statusText = null) => _updateProgress(percent, statusText);
    public void SetState(UpdateToastState state, string? message = null, Action? retryAction = null) => _updateState(state, message, retryAction);
    public void Dismiss() => _dismiss();
}

/// <summary>
/// Manages elegant Mac-style floating toast notifications in the top-right safe area of the application.
/// Provides smooth slide/fade animations, automatic debounce/deduplication, stacking (up to 3 cards),
/// action button support, and auto-dismissal without shifting the main layout.
/// Also hosts persistent progress-driven update cards with full state machine support.
/// </summary>
public sealed class ToastNotificationManager
{
    private readonly StackPanel _container;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ToastDebouncer _debouncer = new(TimeSpan.FromSeconds(2.5));
    private readonly List<ToastCardHandle> _activeCards = new();
    private readonly object _lock = new();

    private sealed class ToastCardHandle
    {
        public required string Message { get; init; }
        public required Border CardBorder { get; init; }
        public required DispatcherTimer AutoDismissTimer { get; init; }
        public TextBlock? MessageTextBlock { get; set; }
        public bool IsPersistent { get; set; }
    }

    public ToastNotificationManager(StackPanel container, DispatcherQueue dispatcherQueue)
    {
        _container = container;
        _dispatcherQueue = dispatcherQueue;
    }

    public void ShowToast(
        string message,
        string? title = null,
        InfoBarSeverity severity = InfoBarSeverity.Informational,
        string? actionLabel = null,
        Action? onAction = null)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            var (shouldShow, isDuplicate, count) = _debouncer.RegisterToast(message);

            if (!shouldShow && isDuplicate)
            {
                // Pulse existing card without creating layout disruption
                var existing = _activeCards.FirstOrDefault(c => string.Equals(c.Message, message.Trim(), StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (!existing.IsPersistent)
                    {
                        existing.AutoDismissTimer.Stop();
                        existing.AutoDismissTimer.Start();
                    }
                    if (existing.MessageTextBlock != null && count > 1)
                    {
                        existing.MessageTextBlock.Text = $"{message} (x{count})";
                    }
                }
                return;
            }

            if (!shouldShow) return;

            CreateAndDisplayToastCard(message.Trim(), title, severity, actionLabel, onAction);
        });
    }

    /// <summary>
    /// Creates a dedicated persistent progress toast for downloading updates.
    /// Does not auto-dismiss during downloading and renders an Apple-inspired compact progress bar and status text.
    /// </summary>
    public ProgressToastHandle ShowUpdateDownloadToast(
        string title,
        string initialMessage,
        Action onStartDownload)
    {
        ProgressToastHandle? handle = null;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_activeCards.Count >= 3)
            {
                var nonPersistent = _activeCards.FirstOrDefault(c => !c.IsPersistent);
                if (nonPersistent != null) DismissCard(nonPersistent);
            }

            var rootGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                }
            };

            var iconBorder = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new FontIcon
                {
                    Glyph = "\uE895", // Sync / Update Download glyph
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(iconBorder, 0);
            rootGrid.Children.Add(iconBorder);

            var textStack = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleBlock = new TextBlock
            {
                Text = title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            textStack.Children.Add(titleBlock);

            var msgTextBlock = new TextBlock
            {
                Text = initialMessage,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            };
            textStack.Children.Add(msgTextBlock);

            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Height = 4,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            textStack.Children.Add(progressBar);

            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };

            var retryBtn = new Button
            {
                Content = "Retry",
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11,
                CornerRadius = new CornerRadius(6)
            };
            actionRow.Children.Add(retryBtn);

            var dismissBtn = new Button
            {
                Content = "Dismiss",
                Height = 26,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 11,
                CornerRadius = new CornerRadius(6)
            };
            actionRow.Children.Add(dismissBtn);

            textStack.Children.Add(actionRow);

            Grid.SetColumn(textStack, 1);
            rootGrid.Children.Add(textStack);

            bool isLight = Application.Current.RequestedTheme == ApplicationTheme.Light;
            var solidCardBg = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF2, 0xF2, 0xF7))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1C, 0x1C, 0x1E));
            var solidCardStroke = isLight
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xD1, 0xD1, 0xD6))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x38, 0x3A));

            var transform = new TranslateTransform { X = 40 };
            var cardBorder = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 11, 14, 11),
                Background = solidCardBg,
                BorderBrush = solidCardStroke,
                BorderThickness = new Thickness(1),
                Opacity = 0.0,
                RenderTransform = transform,
                Child = rootGrid
            };

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };

            var cardHandle = new ToastCardHandle
            {
                Message = initialMessage,
                CardBorder = cardBorder,
                AutoDismissTimer = timer,
                MessageTextBlock = msgTextBlock,
                IsPersistent = true
            };

            dismissBtn.Click += (_, _) => DismissCard(cardHandle);

            _activeCards.Add(cardHandle);
            _container.Children.Add(cardBorder);

            AnimateEntry(cardBorder, transform);

            // Trigger the download callback now that the toast UI is safely mounted
            onStartDownload();

            handle = new ProgressToastHandle(
                updateProgress: (pct, statusText) =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        progressBar.Value = pct;
                        if (!string.IsNullOrWhiteSpace(statusText))
                        {
                            msgTextBlock.Text = statusText;
                        }
                    });
                },
                updateState: (state, message, retryAction) =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        switch (state)
                        {
                            case UpdateToastState.Downloading:
                                progressBar.Visibility = Visibility.Visible;
                                actionRow.Visibility = Visibility.Collapsed;
                                if (!string.IsNullOrWhiteSpace(message)) msgTextBlock.Text = message;
                                break;

                            case UpdateToastState.Downloaded:
                            case UpdateToastState.Installing:
                                progressBar.Value = 100;
                                if (!string.IsNullOrWhiteSpace(message)) msgTextBlock.Text = message;
                                break;

                            case UpdateToastState.Completed:
                                progressBar.Visibility = Visibility.Collapsed;
                                if (!string.IsNullOrWhiteSpace(message)) msgTextBlock.Text = message;
                                cardHandle.IsPersistent = false;
                                cardHandle.AutoDismissTimer.Interval = TimeSpan.FromSeconds(3.5);
                                cardHandle.AutoDismissTimer.Start();
                                break;

                            case UpdateToastState.DownloadFailed:
                            case UpdateToastState.InstallFailed:
                                progressBar.Visibility = Visibility.Collapsed;
                                if (!string.IsNullOrWhiteSpace(message)) msgTextBlock.Text = message;
                                actionRow.Visibility = Visibility.Visible;
                                if (retryAction != null)
                                {
                                    retryBtn.Click += (_, _) =>
                                    {
                                        actionRow.Visibility = Visibility.Collapsed;
                                        progressBar.Visibility = Visibility.Visible;
                                        retryAction();
                                    };
                                }
                                break;
                        }
                    });
                },
                dismiss: () =>
                {
                    _dispatcherQueue.TryEnqueue(() => DismissCard(cardHandle));
                });
        });

        return handle ?? new ProgressToastHandle((_, _) => { }, (_, _, _) => { }, () => { });
    }

    private void CreateAndDisplayToastCard(
        string message,
        string? title,
        InfoBarSeverity severity,
        string? actionLabel,
        Action? onAction)
    {
        // Enforce maximum 3 stacked toast cards (dismiss oldest if necessary)
        if (_activeCards.Count >= 3)
        {
            var dismissTarget = _activeCards.FirstOrDefault(c => !c.IsPersistent) ?? _activeCards[0];
            DismissCard(dismissTarget);
        }

        var (glyph, accentBrushKey) = severity switch
        {
            InfoBarSeverity.Success => ("\uE73E", "SystemFillColorSuccessBrush"),
            InfoBarSeverity.Warning => ("\uE7BA", "SystemFillColorCautionBrush"),
            InfoBarSeverity.Error => ("\uEA39", "SystemFillColorCriticalBrush"),
            _ => ("\uE946", "AccentFillColorDefaultBrush")
        };

        var rootGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        // Icon indicator
        var iconBorder = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = Application.Current.Resources.ContainsKey(accentBrushKey)
                ? (Brush)Application.Current.Resources[accentBrushKey]
                : (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon
            {
                Glyph = glyph,
                FontSize = 12,
                Foreground = Application.Current.Resources.ContainsKey("TextOnAccentFillColorPrimaryBrush")
                    ? (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
                    : (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconBorder, 0);
        rootGrid.Children.Add(iconBorder);

        // Text & optional actions
        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center
        };

        string displayTitle = !string.IsNullOrWhiteSpace(title)
            ? title
            : (severity switch
            {
                InfoBarSeverity.Success => "Success",
                InfoBarSeverity.Warning => "Notice",
                InfoBarSeverity.Error => "Alert",
                _ => "Lyrow"
            });

        textStack.Children.Add(new TextBlock
        {
            Text = displayTitle,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var msgTextBlock = new TextBlock
        {
            Text = message,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };
        textStack.Children.Add(msgTextBlock);

        ToastCardHandle? cardHandle = null;

        if (!string.IsNullOrWhiteSpace(actionLabel) && onAction != null)
        {
            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(0, 6, 0, 0)
            };

            var actionBtn = new Button
            {
                Content = actionLabel,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11,
                CornerRadius = new CornerRadius(6)
            };
            actionBtn.Click += (_, _) =>
            {
                if (cardHandle != null) DismissCard(cardHandle);
                onAction();
            };
            actionRow.Children.Add(actionBtn);

            var laterBtn = new Button
            {
                Content = "Later",
                Height = 26,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 11,
                CornerRadius = new CornerRadius(6)
            };
            laterBtn.Click += (_, _) =>
            {
                if (cardHandle != null) DismissCard(cardHandle);
            };
            actionRow.Children.Add(laterBtn);

            textStack.Children.Add(actionRow);
        }

        Grid.SetColumn(textStack, 1);
        rootGrid.Children.Add(textStack);

        // Close button
        var closeBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 9 },
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 0, 0, 0),
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0)
        };
        Grid.SetColumn(closeBtn, 2);
        rootGrid.Children.Add(closeBtn);

        bool isLight = Application.Current.RequestedTheme == ApplicationTheme.Light;
        var solidCardBg = isLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xF2, 0xF2, 0xF7))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1C, 0x1C, 0x1E));
        var solidCardStroke = isLight
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xD1, 0xD1, 0xD6))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x38, 0x38, 0x3A));

        var transform = new TranslateTransform { X = 40 };
        var cardBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 11, 12, 11),
            Background = solidCardBg,
            BorderBrush = solidCardStroke,
            BorderThickness = new Thickness(1),
            Opacity = 0.0,
            RenderTransform = transform,
            Child = rootGrid
        };

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(!string.IsNullOrWhiteSpace(actionLabel) ? 8.0 : 3.8)
        };

        cardHandle = new ToastCardHandle
        {
            Message = message,
            CardBorder = cardBorder,
            AutoDismissTimer = timer,
            MessageTextBlock = msgTextBlock,
            IsPersistent = false
        };

        closeBtn.Click += (_, _) => DismissCard(cardHandle);
        timer.Tick += (_, _) => DismissCard(cardHandle);

        _activeCards.Add(cardHandle);
        _container.Children.Add(cardBorder);

        // Animate slide-in & fade-in (60 FPS smooth transition)
        AnimateEntry(cardBorder, transform);
        timer.Start();
    }

    private void AnimateEntry(Border card, TranslateTransform transform)
    {
        var animStoryboard = new Storyboard();

        var slideAnim = new DoubleAnimation
        {
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideAnim, transform);
        Storyboard.SetTargetProperty(slideAnim, "X");

        var fadeAnim = new DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeAnim, card);
        Storyboard.SetTargetProperty(fadeAnim, "Opacity");

        animStoryboard.Children.Add(slideAnim);
        animStoryboard.Children.Add(fadeAnim);
        animStoryboard.Begin();
    }

    private void DismissCard(ToastCardHandle card)
    {
        card.AutoDismissTimer.Stop();
        if (!_activeCards.Contains(card)) return;
        _activeCards.Remove(card);

        if (card.CardBorder.RenderTransform is TranslateTransform transform)
        {
            var animStoryboard = new Storyboard();

            var slideAnim = new DoubleAnimation
            {
                To = 30.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(slideAnim, transform);
            Storyboard.SetTargetProperty(slideAnim, "X");

            var fadeAnim = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fadeAnim, card.CardBorder);
            Storyboard.SetTargetProperty(fadeAnim, "Opacity");

            animStoryboard.Children.Add(slideAnim);
            animStoryboard.Children.Add(fadeAnim);

            animStoryboard.Completed += (_, _) =>
            {
                _container.Children.Remove(card.CardBorder);
            };

            animStoryboard.Begin();
        }
        else
        {
            _container.Children.Remove(card.CardBorder);
        }
    }

    public void DismissAll()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            var list = _activeCards.ToList();
            foreach (var card in list)
            {
                DismissCard(card);
            }
        });
    }
}
