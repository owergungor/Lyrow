namespace Lyrow.Core.Models;

/// <summary>
/// Configuration settings specific to the Lyrics music metadata & synchronized lyrics module.
/// </summary>
public sealed class LyricsModuleSettings
{
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// User manual timestamp sync offset in milliseconds (+ delays lyrics, - advances lyrics).
    /// </summary>
    public int SyncOffsetMs { get; set; } = 0;

    /// <summary>
    /// Target frame rate for lyrics interpolation & progress update.
    /// </summary>
    public int TargetFps { get; set; } = 30;

    /// <summary>
    /// Whether fetched lyrics should be cached locally to disk for instant offline access.
    /// </summary>
    public bool CacheLyricsLocally { get; set; } = true;

    /// <summary>
    /// Maximum cache retention in days.
    /// </summary>
    public int MaxCacheAgeDays { get; set; } = 30;

    /// <summary>
    /// Font size scaling mode: Normal, Large, ExtraLarge.
    /// </summary>
    public string FontScale { get; set; } = "Normal";

    /// <summary>
    /// Whether the dedicated lyrics window is open.
    /// </summary>
    public bool ShowLyricsWindow { get; set; } = false;

    /// <summary>
    /// Line spacing between lyric lines in pixels.
    /// </summary>
    public int LineSpacing { get; set; } = 20;

    /// <summary>
    /// Horizontal text alignment for lyrics (Left or Center).
    /// </summary>
    public string Alignment { get; set; } = "Left";

    /// <summary>
    /// Translucency level of the lyrics window background canvas [40, 100].
    /// </summary>
    public int BackdropOpacity { get; set; } = 85;
}
