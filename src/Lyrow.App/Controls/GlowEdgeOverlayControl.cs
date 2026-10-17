using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;

namespace Lyrow.App.Controls;

/// <summary>
/// GPU-rendered 4-sided ambient screen edge lighting control.
/// Synchronizes edge glow intensity, thickness, and color pulses with music beats and frequency bands
/// with zero allocations per frame and zero-CPU idle throttling.
/// </summary>
public sealed class GlowEdgeOverlayControl : Control
{
    private readonly DispatcherTimer _renderTimer;
    private readonly GlowBeatEngine _beatEngine = new();
    private LumnModule? _lumnModule;

    // Pre-allocated gradient brushes for zero per-frame GC allocations
    private readonly LinearGradientBrush _topGradient;
    private readonly LinearGradientBrush _bottomGradient;
    private readonly LinearGradientBrush _leftGradient;
    private readonly LinearGradientBrush _rightGradient;

    private int _consecutiveSilentFrames;

    public GlowEdgeOverlayControl()
    {
        ClipToBounds = true;

        // Top edge: radiant cyan/celestial blue flowing downward
        _topGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 137, 180, 250), 0.0),
                new GradientStop(Color.FromArgb(0, 137, 180, 250), 1.0)
            }
        };

        // Bottom edge: deep magenta/indigo driven by bass punch flowing upward
        _bottomGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 1.0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 0.0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 243, 139, 168), 0.0),
                new GradientStop(Color.FromArgb(0, 243, 139, 168), 1.0)
            }
        };

        // Left edge: vibrant teal flowing inward
        _leftGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1.0, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 94, 234, 212), 0.0),
                new GradientStop(Color.FromArgb(0, 94, 234, 212), 1.0)
            }
        };

        // Right edge: vibrant teal flowing inward
        _rightGradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(1.0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.0, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, 94, 234, 212), 0.0),
                new GradientStop(Color.FromArgb(0, 94, 234, 212), 1.0)
            }
        };

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16.6) // ~60 FPS
        };
        _renderTimer.Tick += OnRenderTimerTick;
    }

    public GlowBeatEngine BeatEngine => _beatEngine;

    public void AttachModule(LumnModule module)
    {
        _lumnModule = module;
        ResumeRendering();
    }

    public void DetachModule()
    {
        PauseRendering();
        _lumnModule = null;
    }

    public void PauseRendering()
    {
        if (_renderTimer.IsEnabled)
        {
            _renderTimer.Stop();
        }
    }

    public void ResumeRendering()
    {
        if (_lumnModule != null && !_renderTimer.IsEnabled)
        {
            _renderTimer.Interval = TimeSpan.FromMilliseconds(16.6);
            _renderTimer.Start();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResumeRendering();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        PauseRendering();
    }

    private void OnRenderTimerTick(object? sender, EventArgs e)
    {
        var spectrum = _lumnModule?.LatestSpectrum ?? FrequencySpectrum.Empty;
        bool isSilent = spectrum.Levels.IsSilent || _lumnModule?.State != ModuleState.Running;

        // Process audio dynamics and beat detection
        _beatEngine.Process(spectrum, 1.0f);

        if (isSilent)
        {
            _consecutiveSilentFrames++;
            if (_consecutiveSilentFrames > 60)
            {
                // Throttle to 5 FPS during prolonged silence to ensure 0% idle CPU
                if (_renderTimer.Interval.TotalMilliseconds < 200)
                {
                    _renderTimer.Interval = TimeSpan.FromMilliseconds(200);
                }
            }
        }
        else
        {
            _consecutiveSilentFrames = 0;
            if (_renderTimer.Interval.TotalMilliseconds > 20)
            {
                _renderTimer.Interval = TimeSpan.FromMilliseconds(16.6); // Resume 60 FPS
            }
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        float glow = _beatEngine.OverallGlowIntensity;
        if (glow < 0.008f)
        {
            return; // Completely clean transparent screen during absolute silence
        }

        float transient = _beatEngine.TransientEnergy;
        float bass = _beatEngine.BassIntensity;
        float mid = _beatEngine.MidIntensity;
        float treble = _beatEngine.TrebleIntensity;

        // Responsive edge thickness: expands smoothly with beats
        double thickness = Math.Clamp(40.0 + (glow * 65.0) + (transient * 25.0), 30.0, 160.0);

        // Alpha values modulated by audio bands & beat transient pulse
        byte alphaBottom = (byte)Math.Clamp((bass * 170.0f) + (transient * 75.0f), 0f, 240f);
        byte alphaSides = (byte)Math.Clamp((mid * 140.0f) + (glow * 60.0f), 0f, 200f);
        byte alphaTop = (byte)Math.Clamp((treble * 130.0f) + (transient * 50.0f), 0f, 190f);

        // Update pre-allocated gradient stops without allocations
        _topGradient.GradientStops[0].Color = Color.FromArgb(alphaTop, 137, 180, 250);     // Light blue/lavender
        _bottomGradient.GradientStops[0].Color = Color.FromArgb(alphaBottom, 243, 139, 168); // Punchy magenta
        _leftGradient.GradientStops[0].Color = Color.FromArgb(alphaSides, 94, 234, 212);    // Teal
        _rightGradient.GradientStops[0].Color = Color.FromArgb(alphaSides, 94, 234, 212);   // Teal

        // Draw 4-sided ambient screen edge borders
        // 1. Top border
        context.DrawRectangle(_topGradient, null, new Rect(0, 0, width, thickness));

        // 2. Bottom border (bass)
        context.DrawRectangle(_bottomGradient, null, new Rect(0, height - thickness, width, thickness));

        // 3. Left border
        context.DrawRectangle(_leftGradient, null, new Rect(0, 0, thickness, height));

        // 4. Right border
        context.DrawRectangle(_rightGradient, null, new Rect(width - thickness, 0, thickness, height));
    }
}
