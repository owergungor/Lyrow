using FluentAssertions;
using Lyrow.Music.Metadata;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class MediaMetadataNormalizerTests
{
    private readonly MediaMetadataNormalizer _normalizer = new();

    [Fact]
    public void Normalize_ArtistDashAlbum_ShouldSeparateArtistAndAlbum_WhenAlbumTitleEmpty()
    {
        // Apple Music pattern: Artist - Album
        var result = _normalizer.Normalize("Beni Sen İnandır", "Pinhani - Kediköy", "");

        result.Title.Should().Be("Beni Sen İnandır");
        result.Artist.Should().Be("Pinhani");
        result.Album.Should().Be("Kediköy");
        result.RawTitle.Should().Be("Beni Sen İnandır");
        result.RawArtist.Should().Be("Pinhani - Kediköy");
        result.RawAlbum.Should().Be("");
        result.CleanSearchArtist.Should().Be("Pinhani");
        result.CleanSearchTitle.Should().Be("Beni Sen İnandır");
    }

    [Fact]
    public void Normalize_ArtistDashSingle_ShouldSeparateArtistAndPreserveSingleSuffix()
    {
        // Apple Music pattern: Artist - Single Name - Single
        var result = _normalizer.Normalize("N'aptın Kendinle", "Adamlar - N'aptın Kendinle - Single", "");

        result.Title.Should().Be("N'aptın Kendinle");
        result.Artist.Should().Be("Adamlar");
        result.Album.Should().Be("N'aptın Kendinle - Single");
        result.CleanSearchArtist.Should().Be("Adamlar");
        result.CleanSearchTitle.Should().Be("N'aptın Kendinle");
    }

    [Fact]
    public void Normalize_HyphenInArtistName_ShouldNotSplitArtistName_WhenNoSpacesAroundHyphen()
    {
        // Artists with hyphens: blink-182, Jay-Z, A-Ha
        var result1 = _normalizer.Normalize("All the Small Things", "blink-182", "Enema of the State");
        result1.Artist.Should().Be("blink-182");
        result1.Album.Should().Be("Enema of the State");
        result1.CleanSearchArtist.Should().Be("blink-182");

        var result2 = _normalizer.Normalize("Empire State of Mind", "Jay-Z - The Blueprint 3", "");
        result2.Artist.Should().Be("Jay-Z");
        result2.Album.Should().Be("The Blueprint 3");
        result2.CleanSearchArtist.Should().Be("Jay-Z");

        var result3 = _normalizer.Normalize("Take On Me", "A-Ha - Hunting High and Low", "");
        result3.Artist.Should().Be("A-Ha");
        result3.Album.Should().Be("Hunting High and Low");

        var result4 = _normalizer.Normalize("Oxygene, Pt. 4", "Jean-Michel Jarre - Oxygene", "");
        result4.Artist.Should().Be("Jean-Michel Jarre");
        result4.Album.Should().Be("Oxygene");
    }

    [Fact]
    public void Normalize_HyphenInAlbumName_ShouldPreserveAlbumHyphen()
    {
        // Album with hyphen: Pink Floyd - The Dark Side of the Moon - Live at Wembley 1974
        var result = _normalizer.Normalize("Time", "Pink Floyd - The Dark Side of the Moon - Live at Wembley 1974", "");

        result.Artist.Should().Be("Pink Floyd");
        result.Album.Should().Be("The Dark Side of the Moon - Live at Wembley 1974");
        result.CleanSearchArtist.Should().Be("Pink Floyd");
    }

    [Fact]
    public void Normalize_EmptyAlbumTitle_ShouldExtractFromArtistIfPresent_OrStayEmpty()
    {
        var resultWithDelimiter = _normalizer.Normalize("Song", "Dua Lipa - Future Nostalgia", "");
        resultWithDelimiter.Artist.Should().Be("Dua Lipa");
        resultWithDelimiter.Album.Should().Be("Future Nostalgia");

        var resultWithoutDelimiter = _normalizer.Normalize("Song", "Dua Lipa", "");
        resultWithoutDelimiter.Artist.Should().Be("Dua Lipa");
        resultWithoutDelimiter.Album.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_EmptyArtist_ShouldHandleGracefullyWithoutException()
    {
        var result = _normalizer.Normalize("Instrumental", "", "");

        result.Title.Should().Be("Instrumental");
        result.Artist.Should().BeEmpty();
        result.Album.Should().BeEmpty();
        result.CleanSearchTitle.Should().Be("Instrumental");
        result.CleanSearchArtist.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_MultiPartMetadata_ShouldKeepArtistAndJoinAlbumParts()
    {
        // 3+ parts: Queen - Bohemian Rhapsody - The Original Soundtrack
        var result = _normalizer.Normalize("Bohemian Rhapsody", "Queen - Bohemian Rhapsody - The Original Soundtrack", "");

        result.Artist.Should().Be("Queen");
        result.Album.Should().Be("Bohemian Rhapsody - The Original Soundtrack");
        result.CleanSearchArtist.Should().Be("Queen");
    }

    [Fact]
    public void Normalize_TurkishCharacters_ShouldPreserveCasingAndDiacritics()
    {
        // Turkish songs tested in Apple Music GSMTC
        var r1 = _normalizer.Normalize("Sen Varsın Diye", "Yüzyüzeyken Konuşuruz - Sen Varsın Diye - Single", "");
        r1.Title.Should().Be("Sen Varsın Diye");
        r1.Artist.Should().Be("Yüzyüzeyken Konuşuruz");
        r1.Album.Should().Be("Sen Varsın Diye - Single");
        r1.CleanSearchArtist.Should().Be("Yüzyüzeyken Konuşuruz");

        var r2 = _normalizer.Normalize("Bi Öyle Bi Böyle", "Adamlar - Eski Dostum Tankla Gelmiş", "");
        r2.Title.Should().Be("Bi Öyle Bi Böyle");
        r2.Artist.Should().Be("Adamlar");
        r2.Album.Should().Be("Eski Dostum Tankla Gelmiş");
        r2.CleanSearchArtist.Should().Be("Adamlar");
    }

    [Fact]
    public void Normalize_ShouldStripTrackSuffixes_ForCleanSearchTitle()
    {
        var r1 = _normalizer.Normalize("Lucky (Jack D. Elliot Radio Mix)", "Britney Spears - Oops!... I Did It Again", "");
        r1.CleanSearchTitle.Should().Be("Lucky");
        r1.CleanSearchArtist.Should().Be("Britney Spears");
        r1.Title.Should().Be("Lucky (Jack D. Elliot Radio Mix)"); // Preserved for UI

        var r2 = _normalizer.Normalize("Hotel California - 2013 Remaster", "Eagles", "Hotel California");
        r2.CleanSearchTitle.Should().Be("Hotel California");

        var r3 = _normalizer.Normalize("One Dance (feat. Wizkid & Kyla)", "Drake", "Views");
        r3.CleanSearchTitle.Should().Be("One Dance");
    }

    [Fact]
    public void Normalize_Delimiters_ShouldSupportStandardDashEmDashAndEnDash()
    {
        var emDash = _normalizer.Normalize("Song", "Artist — Album", "");
        emDash.Artist.Should().Be("Artist");
        emDash.Album.Should().Be("Album");

        var enDash = _normalizer.Normalize("Song", "Artist – Album", "");
        enDash.Artist.Should().Be("Artist");
        enDash.Album.Should().Be("Album");
    }
}
