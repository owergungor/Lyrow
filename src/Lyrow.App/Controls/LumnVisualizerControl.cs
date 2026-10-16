using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;

namespace Lyrow.App.Controls;

/// <summary>
/// High-performance GPU-rendered audio visualizer control drawing frequency bars,
/// peak indicators and reactive ambient glow with zero allocation per frame.
/// </summary>
public sealed class LumnVisualizerControl : Control
{
    private readonly DispatcherTimer _renderTimer;
    private LumnModule? _lumnModule;
    private float[] _currentBands = [];
    private float[] _peakBars = [];

    private readonly LinearGradientBrush _barGradient;
    private readonly SolidColorBrush _peakBrush;
    private readonly RadialGradientBrush _glowBrush;

    public LumnVisualizerControl()
    {
        ClipToBounds = true;

        // Modern vibrant neon gradient (Cyan -> Magenta -> Amber)
        _barGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(230, 94, 234, 212), 0.0),   // Teal / Cyan
                new GradientStop(Color.FromArgb(240, 137, 180, 250), 0.5),  // Accent Blue
                new GradientStop(Color.FromArgb(255, 243, 139, 168), 1.0)   // Magenta / Pink
            }
        };

        _peakBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

        _glowBrush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.9, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.9, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(70, 137, 180, 250), 0.0),
                new GradientStop(Color.FromArgb(0, 137, 180, 250), 1.0)
            }
        };

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16.6) // ~60 FPS
        };
        _renderTimer.Tick += (_, _) => InvalidateVisual();
    }

    public void AttachModule(LumnModule module)
    {
        _lumnModule = module;
        if (!_renderTimer.IsEnabled)
        {
            _renderTimer.Start();
        }
    }

    public void DetachModule()
    {
        _renderTimer.Stop();
        _lumnModule = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_lumnModule != null && !_renderTimer.IsEnabled)
        {
            _renderTimer.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _renderTimer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var spectrum = _lumnModule?.LatestSpectrum ?? FrequencySpectrum.Empty;
        var bandsMem = spectrum.Bands;

        int barCount = bandsMem.Length > 0 ? bandsMem.Length : 32;
        if (_currentBands.Length != barCount)
        {
            _currentBands = new float[barCount];
            _peakBars = new float[barCount];
        }

        if (bandsMem.Length > 0)
        {
            bandsMem.Span.CopyTo(_currentBands);
        }
        else
        {
            // Smoothly decay to zero when no spectrum is active
            for (int i = 0; i < barCount; i++)
            {
                _currentBands[i] *= 0.85f;
            }
        }

        // Draw ambient bass glow
        float bassEnergy = Math.Clamp(spectrum.Bass * 1.5f + spectrum.SubBass, 0f, 1f);
        if (bassEnergy > 0.05f)
        {
            _glowBrush.GradientStops[0].Color = Color.FromArgb((byte)(bassEnergy * 80), 137, 180, 250);
            context.DrawRectangle(_glowBrush, null, new Rect(0, 0, width, height));
        }

        double barSpacing = 4.0;
        double totalSpacing = barSpacing * (barCount - 1);
        double barWidth = Math.Max(2.0, (width - totalSpacing) / barCount);
        double cornerRadius = Math.Min(3.0, barWidth / 2.0);

        for (int i = 0; i < barCount; i++)
        {
            float val = Math.Clamp(_currentBands[i], 0.02f, 1.0f);
            double barHeight = Math.Max(4.0, val * (height - 15.0));
            double x = i * (barWidth + barSpacing);
            double y = height - barHeight;

            // Draw bar
            var rect = new Rect(x, y, barWidth, barHeight);
            context.DrawRectangle(_barGradient, null, rect, cornerRadius, cornerRadius);

            // Update and draw floating peak cap
            if (val > _peakBars[i])
            {
                _peakBars[i] = val;
            }
            else
            {
                _peakBars[i] = Math.Max(0.02f, _peakBars[i] - 0.015f); // Peak gravity drop
            }

            double peakY = height - (_peakBars[i] * (height - 15.0)) - 3.0;
            if (peakY < height - 6.0)
            {
                context.DrawRectangle(_peakBrush, null, new Rect(x, peakY, barWidth, 2.0), 1.0, 1.0);
            }
        }
    }
}
