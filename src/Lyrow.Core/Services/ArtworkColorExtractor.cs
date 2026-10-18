using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// Fast, allocation-efficient dominant and accent color extractor for album artwork.
/// Filters out muddy grays, near-black, and near-white pixels to extract vibrant, musical color tones.
/// </summary>
public static class ArtworkColorExtractor
{
    public static GlowPalette ExtractPalette(byte[]? artworkBytes)
    {
        if (artworkBytes == null || artworkBytes.Length < 32 || !ArtworkService.IsValidImageData(artworkBytes))
        {
            return GlowPalette.Default;
        }

        try
        {
            // Sample bytes across the image payload (avoiding file headers)
            // Use 5-bit color quantization (32 levels per channel = 32,768 buckets) to group similar tones
            int startOffset = Math.Min(64, artworkBytes.Length / 4);
            int step = Math.Max(3, (artworkBytes.Length - startOffset) / 800); // Sample ~800 points

            var buckets = new Dictionary<int, (int count, int r, int g, int b)>();

            for (int i = startOffset; i < artworkBytes.Length - 3; i += step)
            {
                byte b1 = artworkBytes[i];
                byte b2 = artworkBytes[i + 1];
                byte b3 = artworkBytes[i + 2];

                // Calculate saturation and luminance approximation
                int max = Math.Max(b1, Math.Max(b2, b3));
                int min = Math.Min(b1, Math.Min(b2, b3));
                int chroma = max - min;
                int luminance = (b1 + b2 + b3) / 3;

                // Discard near-black, near-white, or completely desaturated grays
                if (luminance < 30 || luminance > 230 || chroma < 28)
                {
                    continue;
                }

                // 4-bit per channel quantization (16x16x16 = 4096 bins)
                int key = ((b1 >> 4) << 8) | ((b2 >> 4) << 4) | (b3 >> 4);
                if (buckets.TryGetValue(key, out var entry))
                {
                    buckets[key] = (entry.count + 1, entry.r + b1, entry.g + b2, entry.b + b3);
                }
                else
                {
                    buckets[key] = (1, b1, b2, b3);
                }
            }

            if (buckets.Count == 0)
            {
                return GlowPalette.Default;
            }

            // Rank by vibrancy (combination of frequency count and color chroma)
            var sorted = buckets.Values
                .Select(e =>
                {
                    int avgR = e.r / e.count;
                    int avgG = e.g / e.count;
                    int avgB = e.b / e.count;
                    int chroma = Math.Max(avgR, Math.Max(avgG, avgB)) - Math.Min(avgR, Math.Min(avgG, avgB));
                    double score = e.count * (1.0 + (chroma / 255.0) * 2.0);
                    return (score, r: (byte)avgR, g: (byte)avgG, b: (byte)avgB);
                })
                .OrderByDescending(x => x.score)
                .ToList();

            var dominant = GlowColor.FromRgb(sorted[0].r, sorted[0].g, sorted[0].b);

            // Find an accent color distinct from dominant
            GlowColor accent = dominant;
            for (int i = 1; i < sorted.Count; i++)
            {
                var candidate = GlowColor.FromRgb(sorted[i].r, sorted[i].g, sorted[i].b);
                int diff = Math.Abs(dominant.R - candidate.R) + Math.Abs(dominant.G - candidate.G) + Math.Abs(dominant.B - candidate.B);
                if (diff > 80)
                {
                    accent = candidate;
                    break;
                }
            }

            if (accent == dominant)
            {
                // Synthesize complementary energetic accent
                accent = GlowColor.FromRgb((byte)(255 - dominant.R), (byte)(dominant.B), (byte)(255 - dominant.G));
            }

            return GlowPalette.FromDominantAndAccent(dominant, accent);
        }
        catch
        {
            return GlowPalette.Default;
        }
    }
}
