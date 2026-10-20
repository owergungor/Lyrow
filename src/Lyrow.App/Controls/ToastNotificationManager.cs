using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Lyrow.Core.Services;

namespace Lyrow.App.Controls;

/// <summary>
/// Manages elegant Mac-style floating toast notifications in the top-right safe area of the application.
/// Provides smooth slide/fade animations, automatic debounce/deduplication, stacking (up to 3 cards),
/// action button support, and auto-dismissal without shifting the main layout.
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
                    existing.AutoDismissTimer.Stop();
                    existing.AutoDismissTimer.Start();
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
            DismissCard(_activeCards[0]);
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
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(7),
            Background = Application.Current.Resources.TryGetValue("LayerFillColorDefaultBrush", out var layerBrush)
                ? (Brush)layerBrush
                : new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 10, 0),
            Child = new FontIcon
            {
                Glyph = glyph,
                FontSize = 13,
                Foreground = Application.Current.Resources.TryGetValue(accentBrushKey, out var accBrush)
                    ? (Brush)accBrush
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

        // Modern Mac-style rounded acrylic card with subtle translucent fill
        var transform = new TranslateTransform { X = 40 };
        var cardBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 11, 12, 11),
            Background = Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var cardBg)
                ? (Brush)cardBg
                : new SolidColorBrush(Windows.UI.Color.FromArgb(240, 30, 30, 30)),
            BorderBrush = Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var stroke)
                ? (Brush)stroke
                : new SolidColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255)),
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
            MessageTextBlock = msgTextBlock
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
            foreach (var card in _activeCards.ToList())
            {
                DismissCard(card);
            }
        });
    }
}
