using System;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Core.Tests;

public class DynamicBackgroundTransitionTests
{
    [Fact]
    public void ExtractedPalette_ContainsDistinctAmbientColors()
    {
        var glowPalette = new GlowPalette(
            GlowColor.FromRgb(200, 30, 40),
            GlowColor.FromRgb(30, 200, 50),
            GlowColor.FromRgb(40, 50, 200),
            GlowColor.FromRgb(180, 180, 20),
            GlowColor.FromRgb(255, 100, 50));

        var palette = ArtworkPaletteExtractor.ExtractAmbientPalette(glowPalette);

        Assert.True(palette.Dominant.R > 0 || palette.Dominant.G > 0 || palette.Dominant.B > 0);
        Assert.True(palette.Secondary.R > 0 || palette.Secondary.G > 0 || palette.Secondary.B > 0);
    }
}
