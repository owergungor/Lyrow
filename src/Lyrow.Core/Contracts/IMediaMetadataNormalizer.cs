namespace Lyrow.Core.Contracts;

/// <summary>
/// Result of normalizing raw media session metadata from Windows GSMTC or other streaming clients.
/// </summary>
public sealed record NormalizedMediaMetadata(
    string Title,
    string Artist,
    string Album,
    string RawTitle,
    string RawArtist,
    string RawAlbum,
    string CleanSearchTitle,
    string CleanSearchArtist);

/// <summary>
/// Normalizes and cleans raw track title, artist, and album metadata across streaming players
/// (such as Apple Music, Spotify, YouTube, web browsers) for accurate UI presentation and lyrics matching.
/// </summary>
public interface IMediaMetadataNormalizer
{
    /// <summary>
    /// Parses raw metadata strings, separating concatenated artist-album strings, preserving hyphens in artist/album names,
    /// and generating clean title/artist tokens optimized for lyrics database search.
    /// </summary>
    NormalizedMediaMetadata Normalize(string? rawTitle, string? rawArtist, string? rawAlbum);
}
