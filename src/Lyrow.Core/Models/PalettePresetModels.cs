namespace Lyrow.Core.Models;

/// <summary>
/// Predefined rich 4-edge ambient glow palette preset with display metadata and avatar color circles.
/// </summary>
public sealed record FallbackPalettePreset(
    int Id,
    string Name,
    string Description,
    GlowPalette Palette,
    IReadOnlyList<string> DotHexColors)
{
    public string TopColorHex => DotHexColors[0];
    public string BottomColorHex => DotHexColors[1];
    public string LeftColorHex => DotHexColors[2];
    public string RightColorHex => DotHexColors[3];
}

public static class PalettePresets
{
    public static readonly IReadOnlyList<FallbackPalettePreset> All = new List<FallbackPalettePreset>
    {
        // 0: Celestial (Soft Blue, Lavender, Pink, Soft White)
        new(
            0,
            "Celestial",
            "Ethereal cosmic glow with lavender and soft blue accents",
            new GlowPalette(
                top: GlowColor.FromRgb(137, 180, 250),
                bottom: GlowColor.FromRgb(203, 166, 247),
                left: GlowColor.FromRgb(245, 194, 231),
                right: GlowColor.FromRgb(248, 250, 252),
                accent: GlowColor.FromRgb(180, 190, 254)),
            new[] { "#89B4FA", "#CBA6F7", "#F5C2E7", "#F8FAFC" }),

        // 1: Aurora (Emerald Green, Cyan Blue, Purple, Vibrant Pink)
        new(
            1,
            "Aurora",
            "Vivid northern lights with polar greens, cyan, and violet",
            new GlowPalette(
                top: GlowColor.FromRgb(166, 227, 161),
                bottom: GlowColor.FromRgb(137, 180, 250),
                left: GlowColor.FromRgb(203, 166, 247),
                right: GlowColor.FromRgb(245, 194, 231),
                accent: GlowColor.FromRgb(148, 226, 213)),
            new[] { "#A6E3A1", "#89B4FA", "#CBA6F7", "#F5C2E7" }),

        // 2: Sunset (Warm Orange, Crimson Red, Coral Pink, Velvet Purple)
        new(
            2,
            "Sunset",
            "Warm golden-hour radiance with dusk amber and coral magenta",
            new GlowPalette(
                top: GlowColor.FromRgb(250, 179, 135),
                bottom: GlowColor.FromRgb(243, 139, 168),
                left: GlowColor.FromRgb(245, 194, 231),
                right: GlowColor.FromRgb(203, 166, 247),
                accent: GlowColor.FromRgb(249, 226, 175)),
            new[] { "#FAB387", "#F38BA8", "#F5C2E7", "#CBA6F7" }),

        // 3: Ocean (Deep Azure, Cyan Wave, Seafoam Green, Foam White)
        new(
            3,
            "Ocean",
            "Deep marine luminescence with dynamic aquamarine currents",
            new GlowPalette(
                top: GlowColor.FromRgb(137, 180, 250),
                bottom: GlowColor.FromRgb(116, 199, 236),
                left: GlowColor.FromRgb(148, 226, 213),
                right: GlowColor.FromRgb(226, 232, 240),
                accent: GlowColor.FromRgb(137, 220, 235)),
            new[] { "#89B4FA", "#74C7EC", "#94E2D5", "#E2E8F0" }),

        // 4: Ember (Flame Red, Amber Orange, Sun Gold, Royal Violet)
        new(
            4,
            "Ember",
            "Fiery rhythmic warmth with molten amber and ember highlights",
            new GlowPalette(
                top: GlowColor.FromRgb(243, 139, 168),
                bottom: GlowColor.FromRgb(250, 179, 135),
                left: GlowColor.FromRgb(249, 226, 175),
                right: GlowColor.FromRgb(203, 166, 247),
                accent: GlowColor.FromRgb(235, 160, 172)),
            new[] { "#F38BA8", "#FAB387", "#F9E2AF", "#CBA6F7" }),

        // 5: Midnight (Electric Blue, Deep Violet, Shadow Navy, Neon Cyan)
        new(
            5,
            "Midnight",
            "Subtle club ambiance with neon cyan pulses and deep velvet shadow",
            new GlowPalette(
                top: GlowColor.FromRgb(137, 180, 250),
                bottom: GlowColor.FromRgb(114, 135, 253),
                left: GlowColor.FromRgb(49, 50, 68),
                right: GlowColor.FromRgb(137, 220, 235),
                accent: GlowColor.FromRgb(180, 190, 254)),
            new[] { "#89B4FA", "#7287FD", "#313244", "#89DCEB" })
    };

    public static FallbackPalettePreset GetPreset(int index)
    {
        int clamped = Math.Clamp(index, 0, All.Count - 1);
        return All[clamped];
    }
}
