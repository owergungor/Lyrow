using FluentAssertions;
using Lyrow.Music.Metadata;
using Xunit;

namespace Lyrow.Music.Tests;

public sealed class MediaMetadataNormalizerBoundaryTests
{
    private readonly MediaMetadataNormalizer _normalizer = new();

    [Fact]
    public void Normalize_MultipleHyphensInArtist_ShouldPreserveHyphenatedArtist()
    {
        // e.g. "K-Ci & Jo-Jo", "Jean-Michel Jarre"
        var r1 = _normalizer.Normalize("Tell Me It's Real", "K-Ci & Jo-Jo - It's Real", "");
        r1.Artist.Should().Be("K-Ci & Jo-Jo");
        r1.Album.Should().Be("It's Real");

        var r2 = _normalizer.Normalize("Oxygene IV", "Jean-Michel Jarre - Oxygene - 30th Anniversary Edition", "");
        r2.Artist.Should().Be("Jean-Michel Jarre");
        r2.Album.Should().Be("Oxygene - 30th Anniversary Edition");
    }

    [Fact]
    public void Normalize_MultipleHyphensInAlbum_ShouldPreserveFullAlbumTitle()
    {
        // e.g. "The Dark Side of the Moon - Live at Wembley - 1974"
        var r = _normalizer.Normalize("Money", "Pink Floyd - The Dark Side of the Moon - Live at Wembley - 1974", "");
        r.Artist.Should().Be("Pink Floyd");
        r.Album.Should().Be("The Dark Side of the Moon - Live at Wembley - 1974");
    }

    [Fact]
    public void Normalize_HyphenInArtistAndHyphenInAlbumTogether_ShouldExtractBothAccurately()
    {
        // Artist with hyphen + Album with hyphen:
        // "blink-182 - Enema of the State - 20th Anniversary Deluxe Edition"
        var r1 = _normalizer.Normalize("What's My Age Again?", "blink-182 - Enema of the State - 20th Anniversary Deluxe Edition", "");
        r1.Artist.Should().Be("blink-182");
        r1.Album.Should().Be("Enema of the State - 20th Anniversary Deluxe Edition");
        r1.CleanSearchArtist.Should().Be("blink-182");

        // "Jay-Z - The Blueprint - Collector's Edition"
        var r2 = _normalizer.Normalize("Izzo (H.O.V.A.)", "Jay-Z - The Blueprint - Collector's Edition", "");
        r2.Artist.Should().Be("Jay-Z");
        r2.Album.Should().Be("The Blueprint - Collector's Edition");
        r2.CleanSearchArtist.Should().Be("Jay-Z");
    }

    [Fact]
    public void Normalize_EmptyAlbumTitle_AndEmptyArtist_ShouldReturnSafeEmptyStrings()
    {
        var r = _normalizer.Normalize("Ambient Noise", "   ", "   ");
        r.Title.Should().Be("Ambient Noise");
        r.Artist.Should().BeEmpty();
        r.Album.Should().BeEmpty();
        r.CleanSearchTitle.Should().Be("Ambient Noise");
        r.CleanSearchArtist.Should().BeEmpty();
    }

    [Fact]
    public void Normalize_TurkishCharactersAndSpecialPunctuation_ShouldPreserveExactSymbols()
    {
        var r = _normalizer.Normalize("Gözleri Aşka Gülen", "Müzeyyen Senar - Son Veda - Klasikler", "");
        r.Title.Should().Be("Gözleri Aşka Gülen");
        r.Artist.Should().Be("Müzeyyen Senar");
        r.Album.Should().Be("Son Veda - Klasikler");
        r.CleanSearchArtist.Should().Be("Müzeyyen Senar");
    }

    [Fact]
    public void Normalize_ParenthesesRemixAndFeat_ShouldProduceCleanSearchTokens()
    {
        // Title has Remix in parentheses:
        var r1 = _normalizer.Normalize("Levitating (The Blessed Madonna Remix)", "Dua Lipa - Club Future Nostalgia", "");
        r1.Title.Should().Be("Levitating (The Blessed Madonna Remix)");
        r1.CleanSearchTitle.Should().Be("Levitating");

        // Title has brackets:
        var r2 = _normalizer.Normalize("Midnight City [Eric Prydz Remix]", "M83", "Hurry Up, We're Dreaming");
        r2.CleanSearchTitle.Should().Be("Midnight City");

        // Title has feat in parentheses:
        var r3 = _normalizer.Normalize("Stay (feat. Justin Bieber)", "The Kid LAROI", "F*CK LOVE 3: OVER YOU");
        r3.CleanSearchTitle.Should().Be("Stay");
    }

    [Fact]
    public void Normalize_DeluxeAndSingleAndEP_ShouldPreserveInAlbum_AndStripFromCleanTitle()
    {
        var r1 = _normalizer.Normalize("Starboy - Deluxe Edition", "The Weeknd - Starboy - Deluxe Edition", "");
        r1.Album.Should().Be("Starboy - Deluxe Edition");
        r1.CleanSearchTitle.Should().Be("Starboy");

        var r2 = _normalizer.Normalize("Motion - EP", "Boy Pablo - Roy Pablo - EP", "");
        r2.Album.Should().Be("Roy Pablo - EP");
        r2.CleanSearchTitle.Should().Be("Motion");
    }
}
