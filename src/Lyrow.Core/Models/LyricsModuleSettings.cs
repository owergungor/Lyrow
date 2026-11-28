namespace Lyrow.Core.Models;

/// <summary>
/// Configuration settings specific to the Lyrics music metadata & synchronized lyrics module.
/// </summary>
public sealed class LyricsModuleSettings
{
    public bool IsEnabled { get; set; } = false;

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

    /// <summary>
    /// Whether the soft glow/light effect on synchronized lyrics is enabled.
    /// Defaults to false (OFF) on fresh install; persisted when toggled by user.
    /// </summary>
    public bool LyricsTextGlowEnabled { get; set; } = false;

    /// <summary>
    /// Preferred lyrics synchronization granularity (Lyrow v5.0).
    /// Defaults to Automatic so Lyrow selects the highest genuinely supported timing precision.
    /// </summary>
    public Lyrow.Core.Lyrics.LyricsSyncMode SyncMode { get; set; } = Lyrow.Core.Lyrics.LyricsSyncMode.Automatic;

    /// <summary>
    /// Persistent general lyrics source preference ("Auto" or a canonical LyricsProviderId name).
    /// Independent of the currently playing song; in "Auto" mode, highest genuine sync quality wins.
    /// </summary>
    public string PreferredProvider { get; set; } = "Auto";

    /// <summary>
    /// User-customized drag-and-drop priority order of active lyrics providers.
    /// Empty uses <see cref="Lyrow.Core.Lyrics.LyricsSourceOrderHelper.DefaultActiveProviderOrder"/>.
    /// Within equal genuine sync quality, earlier providers take precedence; higher sync quality always wins.
    /// </summary>
    public List<string> SourcePriorityOrder { get; set; } = new();
}
