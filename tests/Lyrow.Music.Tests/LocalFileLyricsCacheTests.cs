using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class LocalFileLyricsCacheTests : IDisposable
{
    private readonly string _testCacheDir;

    public LocalFileLyricsCacheTests()
    {
        _testCacheDir = Path.Combine(Path.GetTempPath(), "Lyrow_TestCache_" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task SaveAsync_And_GetAsync_ShouldPersistAndRetrieveLyricsCorrectly()
    {
        var cache = new LocalFileLyricsCache(_testCacheDir);
        var lines = new List<LyricLine>
        {
            new(TimeSpan.FromSeconds(5), "First line", 0),
            new(TimeSpan.FromSeconds(15), "Second line", 1)
        };
        var lyrics = new SyncedLyrics("Imagine", "John Lennon", isSynced: true, lines, plainLyrics: "Plain text", TimeSpan.FromMinutes(3));

        await cache.SaveAsync("Imagine", "John Lennon", lyrics);
        var retrieved = await cache.GetAsync("Imagine", "John Lennon");

        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Imagine");
        retrieved.Artist.Should().Be("John Lennon");
        retrieved.IsSynced.Should().BeTrue();
        retrieved.Lines.Should().HaveCount(2);
        retrieved.Lines[0].Text.Should().Be("First line");
        retrieved.Lines[1].Text.Should().Be("Second line");
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenSongNotCached()
    {
        var cache = new LocalFileLyricsCache(_testCacheDir);
        var retrieved = await cache.GetAsync("Unknown Song", "Unknown Artist");
        retrieved.Should().BeNull();
    }

    [Theory]
    [InlineData("AC/DC", "ac_dc")]
    [InlineData("Track: With? Invalid* Chars", "track__with__invalid__chars")]
    [InlineData("Simple Song", "simple_song")]
    public void SanitizeFileName_ShouldProduceCleanSafeFileNames(string input, string expectedSubstring)
    {
        var sanitized = LocalFileLyricsCache.SanitizeFileName(input);
        sanitized.Should().Be(expectedSubstring);
        sanitized.IndexOfAny(Path.GetInvalidFileNameChars()).Should().Be(-1);
    }

    [Fact]
    public async Task ClearAsync_ShouldRemoveAllCachedFiles()
    {
        var cache = new LocalFileLyricsCache(_testCacheDir);
        var lyrics = new SyncedLyrics("Song", "Artist", false, Array.Empty<LyricLine>());

        await cache.SaveAsync("Song 1", "Artist", lyrics);
        await cache.SaveAsync("Song 2", "Artist", lyrics);

        Directory.GetFiles(_testCacheDir, "*.json").Should().HaveCount(2);

        await cache.ClearAsync();

        Directory.GetFiles(_testCacheDir, "*.json").Should().BeEmpty();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testCacheDir))
            {
                Directory.Delete(_testCacheDir, true);
            }
        }
        catch { }
    }
}
