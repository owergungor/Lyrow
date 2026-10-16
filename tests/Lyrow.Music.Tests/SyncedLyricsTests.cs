using FluentAssertions;
using Lyrow.Core.Models;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class SyncedLyricsTests
{
    private readonly SyncedLyrics _testLyrics;

    public SyncedLyricsTests()
    {
        string rawLrc = """
            [00:10.00]Line 0 (10s)
            [00:20.00]Line 1 (20s)
            [00:30.00]Line 2 (30s)
            [00:45.00]Line 3 (45s)
            """;
        _testLyrics = LrcParser.Parse(rawLrc);
    }

    [Fact]
    public void FindActiveLineIndex_ShouldReturnMinusOne_WhenPositionIsBeforeFirstLine()
    {
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(5)).Should().Be(-1);
        _testLyrics.FindActiveLineIndex(TimeSpan.Zero).Should().Be(-1);
    }

    [Fact]
    public void FindActiveLineIndex_ShouldReturnZero_WhenPositionExactlyMatchesFirstLine()
    {
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(10)).Should().Be(0);
    }

    [Fact]
    public void FindActiveLineIndex_ShouldReturnCorrectLine_WhenPositionIsBetweenTimestamps()
    {
        // Between 10s and 20s should remain on Line 0
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(15)).Should().Be(0);

        // At 20s moves to Line 1
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(20)).Should().Be(1);

        // Between 20s and 30s remains on Line 1
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(29.9)).Should().Be(1);

        // At 30s moves to Line 2
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(30)).Should().Be(2);
    }

    [Fact]
    public void FindActiveLineIndex_ShouldReturnLastLine_WhenPositionIsAfterAllTimestamps()
    {
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(60)).Should().Be(3);
        _testLyrics.FindActiveLineIndex(TimeSpan.FromHours(1)).Should().Be(3);
    }

    [Fact]
    public void FindActiveLineIndex_ShouldHandleInstantSeek_BackAndForth()
    {
        // Jump to end (45s)
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(46)).Should().Be(3);

        // Seek backward to 12s
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(12)).Should().Be(0);

        // Seek forward to 31s
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(31)).Should().Be(2);

        // Seek backward to before beginning (2s)
        _testLyrics.FindActiveLineIndex(TimeSpan.FromSeconds(2)).Should().Be(-1);
    }

    [Fact]
    public void FindActiveLineIndex_ShouldReturnMinusOne_ForEmptyLyrics()
    {
        SyncedLyrics.Empty.FindActiveLineIndex(TimeSpan.FromSeconds(10)).Should().Be(-1);
    }
}
