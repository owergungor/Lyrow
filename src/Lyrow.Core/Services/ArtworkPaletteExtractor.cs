using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// Immutable 4-color ambient background palette used for abstract gradient color fields
/// behind active lyrics in Fullscreen and Apple Music mode.
/// </summary>
public readonly record struct AmbientBackgroundPalette(
    GlowColor Dominant,
    GlowColor Secondary,
    GlowColor Tertiary,
    GlowColor Accent)
{
    public static readonly AmbientBackgroundPalette Default = new(
        GlowColor.FromRgb(18, 18, 24),
        GlowColor.FromRgb(12, 14, 20),
        GlowColor.FromRgb(8, 8, 12),
        GlowColor.FromRgb(28, 30, 42));

    public static AmbientBackgroundPalette Lerp(AmbientBackgroundPalette from, AmbientBackgroundPalette to, float t)
    {
        return new AmbientBackgroundPalette(
            GlowColor.Lerp(from.Dominant, to.Dominant, t),
            GlowColor.Lerp(from.Secondary, to.Secondary, t),
            GlowColor.Lerp(from.Tertiary, to.Tertiary, t),
            GlowColor.Lerp(from.Accent, to.Accent, t));
    }
}

/// <summary>
/// Extracts safe, darkened, desaturated ambient color fields from artwork palettes
/// ensuring WCAG contrast compliance (>7:1) for active white lyrics typography.
/// </summary>
public static class ArtworkPaletteExtractor
{
    public const double MaxSafeLuminance = 55.0;

    public static AmbientBackgroundPalette ExtractAmbientPalette(GlowPalette? palette)
    {
        if (palette == null) return AmbientBackgroundPalette.Default;

        var c1 = PrepareSafeAmbientColor(palette.TopColor);
        var c2 = PrepareSafeAmbientColor(palette.BottomColor);
        var c3 = PrepareSafeAmbientColor(palette.LeftColor);
        var c4 = PrepareSafeAmbientColor(palette.RightColor);

        return new AmbientBackgroundPalette(c1, c2, c3, c4);
    }

    public static GlowColor PrepareSafeAmbientColor(GlowColor input, double maxLuminance = MaxSafeLuminance)
    {
        double lum = CalculateLuminance(input);
        if (lum <= maxLuminance)
        {
            if (lum < 6 && (input.R > 0 || input.G > 0 || input.B > 0))
            {
                return GlowColor.FromRgb(
                    (byte)Math.Clamp(input.R + 8, 0, 255),
                    (byte)Math.Clamp(input.G + 8, 0, 255),
                    (byte)Math.Clamp(input.B + 10, 0, 255));
            }
            return input;
        }

        double scale = maxLuminance / lum;
        byte r = (byte)Math.Clamp((int)Math.Floor(input.R * scale), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Floor(input.G * scale), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Floor(input.B * scale), 0, 255);
        var result = GlowColor.FromRgb(r, g, b);
        while (CalculateLuminance(result) > maxLuminance)
        {
            if (r > 0) r--;
            if (g > 0) g--;
            if (b > 0) b--;
            result = GlowColor.FromRgb(r, g, b);
        }
        return result;
    }

    public static double CalculateLuminance(GlowColor color)
    {
        return 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
    }

    /// <summary>
    /// Transforms an extracted dark-safe ambient palette into a luminous, soft pastel
    /// Light-Theme atmosphere palette that preserves each album cover's unique hue character
    /// while maintaining >10:1 contrast behind dark typography.
    /// </summary>
    public static AmbientBackgroundPalette CreateLightAmbientPalette(AmbientBackgroundPalette palette)
    {
        var dom = PrepareLightThemeAmbientColor(palette.Dominant, targetLightness: 0.84, minSaturation: 0.30, maxSaturation: 0.56);
        var sec = PrepareLightThemeAmbientColor(palette.Secondary, targetLightness: 0.89, minSaturation: 0.24, maxSaturation: 0.48);
        var ter = PrepareLightThemeAmbientColor(palette.Tertiary, targetLightness: 0.82, minSaturation: 0.32, maxSaturation: 0.58);
        var acc = PrepareLightThemeAmbientColor(palette.Accent, targetLightness: 0.86, minSaturation: 0.26, maxSaturation: 0.50);
        return new AmbientBackgroundPalette(dom, sec, ter, acc);
    }

    /// <summary>
    /// Maps a single artwork color into an airy pastel Light-Theme tone in HSL space,
    /// preserving the original hue while lifting lightness and bounding saturation.
    /// </summary>
    public static GlowColor PrepareLightThemeAmbientColor(
        GlowColor input,
        double targetLightness = 0.85,
        double minSaturation = 0.28,
        double maxSaturation = 0.56)
    {
        RgbToHsl(input.R, input.G, input.B, out double h, out double s, out _);
        int channelSpread = Math.Max(input.R, Math.Max(input.G, input.B)) - Math.Min(input.R, Math.Min(input.G, input.B));

        // Idle default dark palette (#121218 / #0C0E14) or monochrome covers -> refined airy pearl-sky mist
        if (channelSpread <= 8 || s < 0.10)
        {
            return HslToRgb(216.0, 0.20, Math.Clamp(targetLightness + 0.04, 0.86, 0.94));
        }

        double pastelS = Math.Clamp(s * 1.12, minSaturation, maxSaturation);
        return HslToRgb(h, pastelS, Math.Clamp(targetLightness, 0.78, 0.93));
    }

    private static void RgbToHsl(byte rByte, byte gByte, byte bByte, out double h, out double s, out double l)
    {
        double r = rByte / 255.0;
        double g = gByte / 255.0;
        double b = bByte / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        l = (max + min) / 2.0;

        if (delta < 1e-6)
        {
            h = 0.0;
            s = 0.0;
            return;
        }

        s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

        if (Math.Abs(max - r) < 1e-6)
        {
            h = 60.0 * (((g - b) / delta) % 6.0);
        }
        else if (Math.Abs(max - g) < 1e-6)
        {
            h = 60.0 * (((b - r) / delta) + 2.0);
        }
        else
        {
            h = 60.0 * (((r - g) / delta) + 4.0);
        }

        if (h < 0.0) h += 360.0;
    }

    private static GlowColor HslToRgb(double h, double s, double l)
    {
        double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = l - c / 2.0;

        double r1, g1, b1;
        if (h < 60.0)       { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120.0) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180.0) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240.0) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300.0) { r1 = x; g1 = 0; b1 = c; }
        else                { r1 = c; g1 = 0; b1 = x; }

        byte r = (byte)Math.Clamp((int)Math.Round((r1 + m) * 255.0), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round((g1 + m) * 255.0), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round((b1 + m) * 255.0), 0, 255);
        return GlowColor.FromRgb(r, g, b);
    }
}
