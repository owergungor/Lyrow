using FluentAssertions;
using Lyrow.Music.Lyrics;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class LrcParserTests
{
    [Fact]
    public void Parse_ShouldParseStandardLrcCorrectly()
    {
        string rawLrc = """
            [ti:Bohemian Rhapsody]
            [ar:Queen]
            [00:01.50]Is this the real life?
            [00:05.20]Is this just fantasy?
            [00:10.00]Caught in a landslide, no escape from reality
            """;

        var lyrics = LrcParser.Parse(rawLrc);

        lyrics.IsSynced.Should().BeTrue();
        lyrics.Title.Should().Be("Bohemian Rhapsody");
        lyrics.Artist.Should().Be("Queen");
        lyrics.Lines.Should().HaveCount(3);

        lyrics.Lines[0].Timestamp.Should().Be(TimeSpan.FromSeconds(1.5));
        lyrics.Lines[0].Text.Should().Be("Is this the real life?");
        lyrics.Lines[0].LineIndex.Should().Be(0);

        lyrics.Lines[1].Timestamp.Should().Be(TimeSpan.FromSeconds(5.2));
        lyrics.Lines[1].Text.Should().Be("Is this just fantasy?");

        lyrics.Lines[2].Timestamp.Should().Be(TimeSpan.FromSeconds(10.0));
    }

    [Fact]
    public void Parse_ShouldSupportThreeDigitMilliseconds()
    {
        string rawLrc = """
            [01:15.123]Line with three digit milliseconds
            [01:20.456]Next line
            """;

        var lyrics = LrcParser.Parse(rawLrc);

        lyrics.IsSynced.Should().BeTrue();
        lyrics.Lines.Should().HaveCount(2);
        lyrics.Lines[0].Timestamp.TotalMilliseconds.Should().BeApproximately(75123, 1);
        lyrics.Lines[1].Timestamp.TotalMilliseconds.Should().BeApproximately(80456, 1);
    }

    [Fact]
    public void Parse_ShouldHandleMultiTimestampLines()
    {
        string rawLrc = """
            [00:10.00][00:30.00]Repeated chorus line
            """;

        var lyrics = LrcParser.Parse(rawLrc);

        lyrics.IsSynced.Should().BeTrue();
        lyrics.Lines.Should().HaveCount(2);

        lyrics.Lines[0].Timestamp.Should().Be(TimeSpan.FromSeconds(10));
        lyrics.Lines[0].Text.Should().Be("Repeated chorus line");
        lyrics.Lines[0].LineIndex.Should().Be(0);

        lyrics.Lines[1].Timestamp.Should().Be(TimeSpan.FromSeconds(30));
        lyrics.Lines[1].Text.Should().Be("Repeated chorus line");
        lyrics.Lines[1].LineIndex.Should().Be(1);
    }

    [Fact]
    public void Parse_ShouldSortDisorderedLinesChronologically()
    {
        string rawLrc = """
            [00:20.00]Second line
            [00:05.00]First line
            [00:35.00]Third line
            """;

        var lyrics = LrcParser.Parse(rawLrc);

        lyrics.Lines.Should().HaveCount(3);
        lyrics.Lines[0].Text.Should().Be("First line");
        lyrics.Lines[0].Timestamp.Should().Be(TimeSpan.FromSeconds(5));
        lyrics.Lines[1].Text.Should().Be("Second line");
        lyrics.Lines[2].Text.Should().Be("Third line");
    }

    [Fact]
    public void Parse_ShouldFallbackToPlainLyrics_WhenNoTimestampsPresent()
    {
        string rawLyrics = """
            Just plain text line one
            Just plain text line two
            """;

        var lyrics = LrcParser.Parse(rawLyrics, "My Title", "My Artist");

        lyrics.IsSynced.Should().BeFalse();
        lyrics.Lines.Should().BeEmpty();
        lyrics.PlainLyrics.Should().Contain("Just plain text line one");
        lyrics.Title.Should().Be("My Title");
    }

    [Fact]
    public void Parse_ShouldReturnEmptyModel_WhenInputIsNullOrWhitespace()
    {
        var lyrics = LrcParser.Parse(null);

        lyrics.IsSynced.Should().BeFalse();
        lyrics.Lines.Should().BeEmpty();
        lyrics.PlainLyrics.Should().BeNull();
    }
}
