using System;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public class DynamicArtworkPaletteTests
{
    [Fact]
    public void PaletteExtraction_WithNullPalette_ReturnsDefaultAmbientPalette()
    {
        var palette = ArtworkPaletteExtractor.ExtractAmbientPalette(null);

        Assert.Equal(AmbientBackgroundPalette.Default.Dominant, palette.Dominant);
        Assert.Equal(AmbientBackgroundPalette.Default.Secondary, palette.Secondary);
        Assert.Equal(AmbientBackgroundPalette.Default.Tertiary, palette.Tertiary);
        Assert.Equal(AmbientBackgroundPalette.Default.Accent, palette.Accent);
    }

    [Fact]
    public void PaletteExtraction_CapsLuminance_ToSafeThreshold()
    {
        // Pure bright white color
        var brightColor = GlowColor.FromRgb(255, 255, 255);
        var safeColor = ArtworkPaletteExtractor.PrepareSafeAmbientColor(brightColor);

        double luminance = ArtworkPaletteExtractor.CalculateLuminance(safeColor);
        Assert.True(luminance <= ArtworkPaletteExtractor.MaxSafeLuminance);
    }

    [Fact]
    public void AmbientBackgroundPalette_Lerp_InterpolatesSmoothly()
    {
        var from = new AmbientBackgroundPalette(
            GlowColor.FromRgb(0, 0, 0),
            GlowColor.FromRgb(10, 10, 10),
            GlowColor.FromRgb(20, 20, 20),
            GlowColor.FromRgb(30, 30, 30));

        var to = new AmbientBackgroundPalette(
            GlowColor.FromRgb(100, 100, 100),
            GlowColor.FromRgb(50, 50, 50),
            GlowColor.FromRgb(80, 80, 80),
            GlowColor.FromRgb(70, 70, 70));

        var half = AmbientBackgroundPalette.Lerp(from, to, 0.5f);

        Assert.Equal(50, half.Dominant.R);
        Assert.Equal(30, half.Secondary.R);
        Assert.Equal(50, half.Tertiary.R);
        Assert.Equal(50, half.Accent.R);

        var start = AmbientBackgroundPalette.Lerp(from, to, 0.0f);
        Assert.Equal(0, start.Dominant.R);

        var end = AmbientBackgroundPalette.Lerp(from, to, 1.0f);
        Assert.Equal(100, end.Dominant.R);
    }
}
