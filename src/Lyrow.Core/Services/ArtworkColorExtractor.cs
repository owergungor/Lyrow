using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// Fast, allocation-efficient dominant and vibrant palette extractor for album artwork.
/// Supports both decoded RGBA pixel buffers and raw image streams, boosting color saturation
/// to produce vivid, musical 4-edge ambient glow palettes (purple, blue, red, orange, green, pink, cyan).
/// </summary>
public static class ArtworkColorExtractor
{
    /// <summary>
    /// Extracts a 4-edge vibrant palette from decoded 32-bit RGBA pixel data (4 bytes per pixel: R, G, B, A).
    /// </summary>
    public static GlowPalette ExtractPaletteFromRgbaPixels(ReadOnlySpan<byte> rgbaPixels)
    {
        if (rgbaPixels.Length < 16)
        {
            return GlowPalette.Default;
        }

        var buckets = new Dictionary<int, (int count, long r, long g, long b)>();
        int pixelCount = rgbaPixels.Length / 4;
        int step = Math.Max(1, pixelCount / 1024);

        for (int p = 0; p < pixelCount; p += step)
        {
            int idx = p * 4;
            if (idx + 3 >= rgbaPixels.Length) break;

            byte r = rgbaPixels[idx];
            byte g = rgbaPixels[idx + 1];
            byte b = rgbaPixels[idx + 2];
            byte a = rgbaPixels[idx + 3];

            if (a < 64) continue;

            AccumulateColorCandidate(buckets, r, g, b);
        }

        return BuildPaletteFromBuckets(buckets);
    }

    public static GlowPalette ExtractPalette(byte[]? artworkBytes)
    {
        if (artworkBytes == null || artworkBytes.Length < 32 || !ArtworkService.IsValidImageData(artworkBytes))
        {
            return GlowPalette.Default;
        }

        try
        {
            int startOffset = Math.Min(64, artworkBytes.Length / 4);
            int step = Math.Max(3, (artworkBytes.Length - startOffset) / 800);

            var buckets = new Dictionary<int, (int count, long r, long g, long b)>();

            for (int i = startOffset; i < artworkBytes.Length - 3; i += step)
            {
                byte b1 = artworkBytes[i];
                byte b2 = artworkBytes[i + 1];
                byte b3 = artworkBytes[i + 2];

                AccumulateColorCandidate(buckets, b1, b2, b3);
            }

            return BuildPaletteFromBuckets(buckets);
        }
        catch
        {
            return GlowPalette.Default;
        }
    }

    private static void AccumulateColorCandidate(
        Dictionary<int, (int count, long r, long g, long b)> buckets,
        byte r,
        byte g,
        byte b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        int chroma = max - min;
        int luminance = (r + g + b) / 3;

        // Discard near-black, near-white, or completely desaturated grays
        if (luminance < 24 || luminance > 238 || chroma < 24)
        {
            return;
        }

        // 4-bit per channel quantization (16x16x16 = 4096 bins)
        int key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
        if (buckets.TryGetValue(key, out var entry))
        {
            buckets[key] = (entry.count + 1, entry.r + r, entry.g + g, entry.b + b);
        }
        else
        {
            buckets[key] = (1, r, g, b);
        }
    }

    private static GlowPalette BuildPaletteFromBuckets(Dictionary<int, (int count, long r, long g, long b)> buckets)
    {
        if (buckets.Count == 0)
        {
            return GlowPalette.Default;
        }

        var sorted = buckets.Values
            .Select(e =>
            {
                int avgR = (int)(e.r / e.count);
                int avgG = (int)(e.g / e.count);
                int avgB = (int)(e.b / e.count);
                int chroma = Math.Max(avgR, Math.Max(avgG, avgB)) - Math.Min(avgR, Math.Min(avgG, avgB));
                double score = e.count * (1.0 + (chroma / 255.0) * 2.5);
                var vibrant = BoostVibrancy(
                    (byte)Math.Clamp(avgR, 0, 255),
                    (byte)Math.Clamp(avgG, 0, 255),
                    (byte)Math.Clamp(avgB, 0, 255));
                return (score, color: vibrant);
            })
            .OrderByDescending(x => x.score)
            .ToList();

        var dominant = sorted[0].color;

        GlowColor accent = dominant;
        GlowColor secondary = dominant;
        GlowColor tertiary = dominant;

        var distinctColors = new List<GlowColor> { dominant };
        for (int i = 1; i < sorted.Count && distinctColors.Count < 4; i++)
        {
            var candidate = sorted[i].color;
            bool isDistinct = distinctColors.All(existing => ColorDistance(existing, candidate) > 65);
            if (isDistinct)
            {
                distinctColors.Add(candidate);
            }
        }

        if (distinctColors.Count >= 2)
        {
            accent = distinctColors[1];
        }
        else
        {
            accent = BoostVibrancy((byte)(255 - dominant.R), dominant.B, (byte)(255 - dominant.G));
        }

        if (distinctColors.Count >= 3)
        {
            secondary = distinctColors[2];
        }
        else
        {
            secondary = GlowColor.Lerp(dominant, accent, 0.35f);
        }

        if (distinctColors.Count >= 4)
        {
            tertiary = distinctColors[3];
        }
        else
        {
            tertiary = GlowColor.Lerp(dominant, accent, 0.65f);
        }

        return new GlowPalette(
            top: accent,
            bottom: dominant,
            left: secondary,
            right: tertiary,
            accent: accent);
    }

    private static int ColorDistance(GlowColor a, GlowColor b)
    {
        return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
    }

    /// <summary>
    /// Boosts saturation and ensures ambient glow colors are vivid (never washed-out white or dull dark gray).
    /// </summary>
    public static GlowColor BoostVibrancy(byte r, byte g, byte b)
    {
        float rf = r / 255.0f;
        float gf = g / 255.0f;
        float bf = b / 255.0f;

        float max = MathF.Max(rf, MathF.Max(gf, bf));
        float min = MathF.Min(rf, MathF.Min(gf, bf));
        float delta = max - min;

        if (delta < 0.05f)
        {
            return GlowColor.FromRgb(r, g, b);
        }

        // Convert to HSV
        float h;
        if (MathF.Abs(max - rf) < 0.0001f)
            h = 60.0f * (((gf - bf) / delta) % 6.0f);
        else if (MathF.Abs(max - gf) < 0.0001f)
            h = 60.0f * (((bf - rf) / delta) + 2.0f);
        else
            h = 60.0f * (((rf - gf) / delta) + 4.0f);

        if (h < 0f) h += 360.0f;

        float s = max <= 0.0001f ? 0f : (delta / max);
        float v = max;

        // Boost saturation and value so ambient edge lighting glows richly
        s = Math.Clamp(MathF.Max(s * 1.25f, 0.55f), 0.45f, 1.0f);
        v = Math.Clamp(MathF.Max(v * 1.15f, 0.72f), 0.65f, 1.0f);

        // Convert HSV back to RGB
        float c = v * s;
        float x = c * (1.0f - MathF.Abs(((h / 60.0f) % 2.0f) - 1.0f));
        float m = v - c;

        float r1, g1, b1;
        if (h < 60f) { r1 = c; g1 = x; b1 = 0f; }
        else if (h < 120f) { r1 = x; g1 = c; b1 = 0f; }
        else if (h < 180f) { r1 = 0f; g1 = c; b1 = x; }
        else if (h < 240f) { r1 = 0f; g1 = x; b1 = c; }
        else if (h < 300f) { r1 = x; g1 = 0f; b1 = c; }
        else { r1 = c; g1 = 0f; b1 = x; }

        byte outR = (byte)Math.Clamp((int)MathF.Round((r1 + m) * 255.0f), 0, 255);
        byte outG = (byte)Math.Clamp((int)MathF.Round((g1 + m) * 255.0f), 0, 255);
        byte outB = (byte)Math.Clamp((int)MathF.Round((b1 + m) * 255.0f), 0, 255);

        return GlowColor.FromRgb(outR, outG, outB);
    }
}
