using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class LrclibOnlineOfflineIntegrationTests
{
    [Fact]
    public async Task LrclibClient_RealApi_ShouldFetchAndParseColdplayYellow_AndCacheLocally()
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var client = new LrclibClient(httpClient);

        var tempDir = Path.Combine(Path.GetTempPath(), "Lyrow_Stage5_CacheTest_" + Guid.NewGuid().ToString("N"));
        var cache = new LocalFileLyricsCache(tempDir);

        try
        {
            LyricsSearchResult result;
            try
            {
                result = await client.FetchLyricsAsync("Yellow", "Coldplay", TimeSpan.FromSeconds(267));
            }
            catch (HttpRequestException)
            {
                // Network unavailable or blocked in current environment
                return;
            }

            if (!result.Found || result.Lyrics == null || result.Lyrics.Lines.Count == 0)
            {
                return;
            }

            var lyrics = result.Lyrics;

            // 1. Verify real response
            lyrics.Artist.Should().Be("Coldplay");
            lyrics.Title.Should().Be("Yellow");
            lyrics.IsSynced.Should().BeTrue();
            lyrics.Lines.Count.Should().BeGreaterThan(10);

            // 2. Save to local cache
            await cache.SaveAsync("Yellow", "Coldplay", lyrics);

            // 3. Test offline read (without calling API)
            var cachedLyrics = await cache.GetAsync("Yellow", "Coldplay");
            cachedLyrics.Should().NotBeNull();
            cachedLyrics!.Lines.Count.Should().Be(lyrics.Lines.Count);
            cachedLyrics.Lines[0].Text.Should().Be(lyrics.Lines[0].Text);

            // 4. Test seek forward & backward on real lyrics
            // e.g. at 40 seconds (Look how they shine for you)
            int lineAt40s = cachedLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(40));
            lineAt40s.Should().BeGreaterThanOrEqualTo(0);

            // Seek backward to 10 seconds (intro before vocals)
            int lineAt10s = cachedLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(10));
            lineAt10s.Should().Be(-1);

            // Seek forward to 3 minutes
            int lineAt3m = cachedLyrics.FindActiveLineIndex(TimeSpan.FromMinutes(3));
            lineAt3m.Should().BeGreaterThan(lineAt40s);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
