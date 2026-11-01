namespace Lyrow.Core.Models;

/// <summary>
/// Root persistent user configuration model.
/// </summary>
public sealed class LyrowSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.SystemDefault;

    public string Language { get; set; } = "en";

    public bool GlowEnabled { get; set; } = false;

    public GlowModuleSettings Glow { get; set; } = new();

    public bool LyricsEnabled { get; set; } = false;

    public LyricsModuleSettings Lyrics { get; set; } = new();

    public bool VisualIntegrationEnabled { get; set; } = true;

    public bool ClickThroughEnabled { get; set; } = true;

    public int GlowIntensity { get; set; } = 80;

    public int GlowThickness { get; set; } = 24;

    public string GlowPaletteMode { get; set; } = "AlbumArt";

    public bool SyncWithAlbumArt { get; set; } = true;

    public bool MultiMonitorGlow { get; set; } = true;

    public List<int> SelectedDisplayIndices { get; set; } = new() { 0 };

    public OverlayPosition OverlayPosition { get; set; } = OverlayPosition.Center;

    public int TargetMonitorIndex { get; set; } = 0;

    public bool AutoFetchLyrics { get; set; } = true;

    public double MasterOpacity { get; set; } = 0.95;

    public string AccentColor { get; set; } = "#89B4FA";

    public bool StartWithWindows { get; set; } = false;

    public bool MinimizeToTray { get; set; } = true;

    public bool SystemTrayEnabled { get; set; } = true;

    public bool HasCompletedFirstRun { get; set; } = false;

    public bool EnableAnimations { get; set; } = true;

    public bool NotificationsEnabled { get; set; } = true;

    public bool NotifyOnTrackChange { get; set; } = true;

    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    public bool IsCompactMode { get; set; } = false;

    public bool IsMaximized { get; set; } = false;

    public string LastActivePage { get; set; } = "Home";

    public bool AutoUpdateEnabled { get; set; } = true;

    public UpdateCheckFrequency UpdateFrequency { get; set; } = UpdateCheckFrequency.Weekly;

    public DateTime? LastUpdateCheckUtc { get; set; }

    public int GlowShutdownDelaySeconds { get; set; } = 3;

    public double FocusedLyricFontSize { get; set; } = 28.0;

    public double UnfocusedLyricFontSize { get; set; } = 20.0;

    public int FallbackPalettePresetIndex { get; set; } = 0;

    public int LyricsFontScaleIndex { get; set; } = 0;

    public int GlowPresetIndex { get; set; } = 0;

    public double ArtworkBackgroundSaturation { get; set; } = 1.0;

    public double ArtworkCornerRadius { get; set; } = 12.0;

    public int SeekStepSeconds { get; set; } = 5;

    public bool AutoPauseOnMute { get; set; } = false;

    public HotkeySettings Hotkeys { get; set; } = new();

    public Dictionary<string, string> ModuleCustomSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a clean default instance.</summary>
    public static LyrowSettings CreateDefault() => new();
}

/// <summary>
/// Predefined anchor positions for overlay windows on target display screen.
/// </summary>
public enum OverlayPosition
{
    Center = 0,
    TopLeft = 1,
    TopCenter = 2,
    TopRight = 3,
    BottomLeft = 4,
    BottomCenter = 5,
    BottomRight = 6
}
