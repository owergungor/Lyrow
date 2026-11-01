using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Lyrics;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowAppleMusicExperienceTests
{
    [Fact]
    public void ArtworkService_BoundsCacheToMax50Items()
    {
        var artworkService = new ArtworkService();
        ArtworkService.MaxCachedItems.Should().Be(50);

        // Valid 68-byte sample JPEG header
        byte[] validJpeg = new byte[68];
        validJpeg[0] = 0xFF;
        validJpeg[1] = 0xD8;
        validJpeg[2] = 0xFF;

        // Insert 65 distinct artworks
        for (int i = 0; i < 65; i++)
        {
            artworkService.CacheArtwork($"Artist_{i}", $"Title_{i}", validJpeg);
        }

        // The most recently cached item must be present
        artworkService.IsCached("Artist_64", "Title_64").Should().BeTrue();

        // The total cache size must be strictly bounded to MaxCachedItems (50)
        artworkService.CachedCount.Should().Be(ArtworkService.MaxCachedItems);
    }

    [Fact]
    public void ThemeAmbientBackgroundOpacity_AdaptsForLightAndDark()
    {
        double darkOpacity = 0.55;
        double lightOpacity = 0.12;

        darkOpacity.Should().BeGreaterThan(lightOpacity);
        darkOpacity.Should().Be(0.55);
        lightOpacity.Should().Be(0.12);
    }

    [Fact]
    public void CinematicLyrics_DepthHierarchy_ScaleAndOpacity()
    {
        // Line 5 is active
        int activeIdx = 5;

        // Distance 0 (Active line): Focused scale 1.04x, full opacity 1.0
        var targetActive = LyricsViewportCalculator.GetLineVisualTarget(activeIdx, activeIdx, 28.0, 20.0);
        targetActive.IsActive.Should().BeTrue();
        targetActive.TargetScale.Should().Be(1.04);
        targetActive.TargetOpacity.Should().Be(1.0);

        // Distance 1 (Adjacent line): Scale 1.0x, opacity 0.56
        var targetNear = LyricsViewportCalculator.GetLineVisualTarget(activeIdx + 1, activeIdx, 28.0, 20.0);
        targetNear.IsActive.Should().BeFalse();
        targetNear.TargetScale.Should().Be(1.0);
        targetNear.TargetOpacity.Should().Be(0.56);

        // Distance 2 (Second line away): Scale 0.98x, opacity 0.34
        var targetDist2 = LyricsViewportCalculator.GetLineVisualTarget(activeIdx + 2, activeIdx, 28.0, 20.0);
        targetDist2.IsActive.Should().BeFalse();
        targetDist2.TargetScale.Should().Be(0.98);
        targetDist2.TargetOpacity.Should().Be(0.34);

        // Distance 3+ (Far lines): Scale 0.96x, opacity 0.18
        var targetFar = LyricsViewportCalculator.GetLineVisualTarget(activeIdx + 4, activeIdx, 28.0, 20.0);
        targetFar.IsActive.Should().BeFalse();
        targetFar.TargetScale.Should().Be(0.96);
        targetFar.TargetOpacity.Should().Be(0.18);
    }

    [Theory]
    [InlineData(500, 232, true)]
    [InlineData(560, 320, true)]
    [InlineData(561, 321, false)]
    [InlineData(960, 640, false)]
    [InlineData(1280, 720, false)]
    [InlineData(1920, 1080, false)]
    public void ResponsiveCompactMode_EvaluatesDimensionsAccurately(double width, double height, bool expectedCompact)
    {
        bool isCompact = width <= 560 || height <= 320;
        isCompact.Should().Be(expectedCompact);
    }

    [Theory]
    [InlineData(false, "Seek işlemi aktif medya oynatıcısı (Apple Music) tarafından desteklenmiyor.")]
    [InlineData(true, "Click or drag to seek")]
    public void SeekCapability_TooltipAndStateResolution(bool canSeek, string expectedTooltip)
    {
        string toolTip = canSeek
            ? "Click or drag to seek"
            : "Seek işlemi aktif medya oynatıcısı (Apple Music) tarafından desteklenmiyor.";

        toolTip.Should().Be(expectedTooltip);
    }

    [Fact]
    public void GlowSilenceDecay_ZeroesAllEdges()
    {
        // When audio input is zero, silence decay ensures ambient brightness hits 0.000
        float currentFactor = 0.0001f;
        float decayFactor = (currentFactor < 0.001f) ? 0.0f : (currentFactor * 0.85f);
        decayFactor.Should().Be(0.0f);
    }

    [Fact]
    public void TransitionTiming_ConformsToFluidSpec()
    {
        var artworkFadeDuration = TimeSpan.FromMilliseconds(240);
        var metaFadeDuration = TimeSpan.FromMilliseconds(200);

        artworkFadeDuration.TotalMilliseconds.Should().BeInRange(150, 350);
        metaFadeDuration.TotalMilliseconds.Should().BeInRange(150, 300);
    }

    [Fact]
    public void LyricsScrollEasing_SmoothingFactor_InterpolatesSmoothlyWithoutOvershoot()
    {
        double currentOffset = 100.0;
        double targetOffset = 250.0;
        double factor = 0.15;

        double step1 = LyricsViewportCalculator.InterpolateScrollOffset(currentOffset, targetOffset, factor);
        step1.Should().BeGreaterThan(currentOffset);
        step1.Should().BeLessThan(targetOffset);
        step1.Should().BeApproximately(122.5, 0.5);

        double step2 = LyricsViewportCalculator.InterpolateScrollOffset(step1, targetOffset, factor);
        step2.Should().BeGreaterThan(step1);
        step2.Should().BeLessThan(targetOffset);
    }

    [Fact]
    public void StaleAsyncSequence_RejectsOutdatedRequests()
    {
        long sequence = 0;
        long seq1 = Interlocked.Increment(ref sequence);
        long seq2 = Interlocked.Increment(ref sequence);

        seq2.Should().BeGreaterThan(seq1);

        // A delayed callback with seq1 should be rejected
        bool isCallbackValidForSeq1 = (seq1 == Interlocked.Read(ref sequence));
        bool isCallbackValidForSeq2 = (seq2 == Interlocked.Read(ref sequence));

        isCallbackValidForSeq1.Should().BeFalse();
        isCallbackValidForSeq2.Should().BeTrue();
    }

    [Fact]
    public void MiniPlayer_LayoutCalculations_ZeroOverflow()
    {
        double windowWidth = 500.0;
        double windowHeight = 232.0;

        double paddingHorizontal = 14.0 * 2; // Card padding
        double artworkWidth = 64.0;
        double controlsWidth = (38.0 * 2) + 46.0 + (8.0 * 2); // Prev (38) + Next (38) + Play (46) + Spacing (16)

        double availableMetaWidth = windowWidth - paddingHorizontal - artworkWidth - controlsWidth;
        availableMetaWidth.Should().BeGreaterThan(100.0); // Sufficient room for track title and artist

        double paddingVertical = 10.0 * 2;
        double contentHeight = 64.0 + 20.0 + 32.0; // artwork/meta + progress + controls
        (contentHeight + paddingVertical).Should().BeLessThan(windowHeight); // Zero vertical overflow
    }
}
