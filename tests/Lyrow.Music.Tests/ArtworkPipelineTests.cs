using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.Music.Tests;

public class ArtworkPipelineTests
{
    private static readonly byte[] ValidJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] ValidPngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] CorruptBytes = [0x00, 0x01, 0x02];

    [Fact]
    public async Task ArtworkAvailable_ReturnsThumbnailBytes_AndCachesInMemory()
    {
        var service = new ArtworkService();
        var track = new MediaTrackInfo(
            Title: "Starboy",
            Artist: "The Weeknd",
            Album: "Starboy",
            Duration: TimeSpan.FromMinutes(3.5),
            ThumbnailData: ValidJpegBytes);

        var bytes = await service.GetArtworkBytesAsync(track);

        bytes.Should().NotBeNull();
        bytes.Should().BeEquivalentTo(ValidJpegBytes);
        service.IsCached("The Weeknd", "Starboy").Should().BeTrue();

        // Query again without ThumbnailData on track — must serve from cache!
        var trackWithoutData = track with { ThumbnailData = null };
        var cached = await service.GetArtworkBytesAsync(trackWithoutData);
        cached.Should().NotBeNull();
        cached.Should().BeEquivalentTo(ValidJpegBytes);
    }

    [Fact]
    public async Task ArtworkUnavailable_ReturnsNull_WithoutThrowing()
    {
        var service = new ArtworkService();
        var track = new MediaTrackInfo(
            Title: "Unknown Track",
            Artist: "Unknown Artist",
            Album: "",
            Duration: TimeSpan.FromMinutes(2),
            ThumbnailData: null);

        var bytes = await service.GetArtworkBytesAsync(track);

        bytes.Should().BeNull();
        service.IsCached("Unknown Artist", "Unknown Track").Should().BeFalse();
    }

    [Fact]
    public async Task CorruptArtworkData_IsRejected_AndReturnsNull()
    {
        var service = new ArtworkService();
        var track = new MediaTrackInfo(
            Title: "Broken Cover",
            Artist: "Faulty Artist",
            Album: "",
            Duration: TimeSpan.FromMinutes(2),
            ThumbnailData: CorruptBytes);

        var bytes = await service.GetArtworkBytesAsync(track);

        bytes.Should().BeNull();
        service.IsCached("Faulty Artist", "Broken Cover").Should().BeFalse();
    }

    [Fact]
    public void CacheInvalidation_RemovesTrackFromCache()
    {
        var service = new ArtworkService();
        service.CacheArtwork("Daft Punk", "Get Lucky", ValidPngBytes);
        service.IsCached("Daft Punk", "Get Lucky").Should().BeTrue();

        service.Invalidate("Daft Punk", "Get Lucky");
        service.IsCached("Daft Punk", "Get Lucky").Should().BeFalse();
    }

    [Fact]
    public void ClearCache_RemovesAllEntries()
    {
        var service = new ArtworkService();
        service.CacheArtwork("Artist 1", "Song 1", ValidPngBytes);
        service.CacheArtwork("Artist 2", "Song 2", ValidJpegBytes);

        service.IsCached("Artist 1", "Song 1").Should().BeTrue();
        service.IsCached("Artist 2", "Song 2").Should().BeTrue();

        service.ClearCache();

        service.IsCached("Artist 1", "Song 1").Should().BeFalse();
        service.IsCached("Artist 2", "Song 2").Should().BeFalse();
    }

    [Fact]
    public void IsValidImageData_DetectsMajorImageSignatures()
    {
        ArtworkService.IsValidImageData(ValidJpegBytes).Should().BeTrue();
        ArtworkService.IsValidImageData(ValidPngBytes).Should().BeTrue();
        ArtworkService.IsValidImageData(CorruptBytes).Should().BeFalse();
        ArtworkService.IsValidImageData([]).Should().BeFalse();
    }
}
