namespace Lyrow.Core.Models;

/// <summary>
/// Lightweight ARGB color representation independent of UI frameworks.
/// </summary>
public readonly record struct GlowColor(byte A, byte R, byte G, byte B)
{
    public static GlowColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
    public static GlowColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    public static GlowColor ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return FromRgb(137, 180, 250);
        string clean = hex.TrimStart('#');
        if (clean.Length == 6)
        {
            byte r = Convert.ToByte(clean.Substring(0, 2), 16);
            byte g = Convert.ToByte(clean.Substring(2, 2), 16);
            byte b = Convert.ToByte(clean.Substring(4, 2), 16);
            return FromRgb(r, g, b);
        }
        if (clean.Length == 8)
        {
            byte a = Convert.ToByte(clean.Substring(0, 2), 16);
            byte r = Convert.ToByte(clean.Substring(2, 2), 16);
            byte g = Convert.ToByte(clean.Substring(4, 2), 16);
            byte b = Convert.ToByte(clean.Substring(6, 2), 16);
            return FromArgb(a, r, g, b);
        }
        return FromRgb(137, 180, 250);
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static GlowColor Lerp(GlowColor a, GlowColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte la = (byte)(a.A + (b.A - a.A) * t);
        byte lr = (byte)(a.R + (b.R - a.R) * t);
        byte lg = (byte)(a.G + (b.G - a.G) * t);
        byte lb = (byte)(a.B + (b.B - a.B) * t);
        return new GlowColor(la, lr, lg, lb);
    }
}

/// <summary>
/// 4-edge ambient lighting color palette with smooth interpolation for cross-fade transitions.
/// </summary>
public sealed class GlowPalette
{
    public GlowColor TopColor { get; set; }
    public GlowColor BottomColor { get; set; }
    public GlowColor LeftColor { get; set; }
    public GlowColor RightColor { get; set; }
    public GlowColor AccentColor { get; set; }

    public GlowPalette(GlowColor top, GlowColor bottom, GlowColor left, GlowColor right, GlowColor accent)
    {
        TopColor = top;
        BottomColor = bottom;
        LeftColor = left;
        RightColor = right;
        AccentColor = accent;
    }

    /// <summary>
    /// Default vibrant celestial palette (Cyan, Magenta, Teal).
    /// </summary>
    public static GlowPalette Default => new(
        top: GlowColor.FromRgb(94, 178, 255),      // Sky Blue / Lavender
        bottom: GlowColor.FromRgb(255, 83, 136),   // Magenta / Punchy Rose
        left: GlowColor.FromRgb(56, 239, 125),     // Mint / Emerald
        right: GlowColor.FromRgb(138, 43, 226),    // Electric Violet
        accent: GlowColor.FromRgb(137, 180, 250)
    );

    /// <summary>
    /// Aurora Borealis preset (Emerald, Teal, Azure).
    /// </summary>
    public static GlowPalette Aurora => new(
        top: GlowColor.FromRgb(0, 242, 254),
        bottom: GlowColor.FromRgb(79, 172, 254),
        left: GlowColor.FromRgb(67, 233, 123),
        right: GlowColor.FromRgb(56, 249, 215),
        accent: GlowColor.FromRgb(67, 233, 123)
    );

    /// <summary>
    /// Neon Sunset preset (Magenta, Tangerine, Violet).
    /// </summary>
    public static GlowPalette NeonSunset => new(
        top: GlowColor.FromRgb(255, 154, 139),
        bottom: GlowColor.FromRgb(255, 106, 0),
        left: GlowColor.FromRgb(238, 9, 121),
        right: GlowColor.FromRgb(118, 75, 162),
        accent: GlowColor.FromRgb(255, 106, 0)
    );

    /// <summary>
    /// Cyberpunk preset (Electric Cyan, Neon Pink).
    /// </summary>
    public static GlowPalette Cyberpunk => new(
        top: GlowColor.FromRgb(0, 245, 255),
        bottom: GlowColor.FromRgb(255, 0, 127),
        left: GlowColor.FromRgb(128, 0, 255),
        right: GlowColor.FromRgb(0, 255, 170),
        accent: GlowColor.FromRgb(0, 245, 255)
    );

    /// <summary>
    /// Creates a palette from dominant and accent colors.
    /// </summary>
    public static GlowPalette FromDominantAndAccent(GlowColor dominant, GlowColor accent)
    {
        // Top: accent bright
        // Bottom: dominant deep
        // Left: blended
        // Right: blended
        var blend = GlowColor.Lerp(dominant, accent, 0.5f);
        return new GlowPalette(accent, dominant, blend, blend, accent);
    }

    /// <summary>
    /// Interpolates smoothly between two palettes for musical cross-fade.
    /// </summary>
    public static GlowPalette Lerp(GlowPalette a, GlowPalette b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new GlowPalette(
            GlowColor.Lerp(a.TopColor, b.TopColor, t),
            GlowColor.Lerp(a.BottomColor, b.BottomColor, t),
            GlowColor.Lerp(a.LeftColor, b.LeftColor, t),
            GlowColor.Lerp(a.RightColor, b.RightColor, t),
            GlowColor.Lerp(a.AccentColor, b.AccentColor, t)
        );
    }
}
