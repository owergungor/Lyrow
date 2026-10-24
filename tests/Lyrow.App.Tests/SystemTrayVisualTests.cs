using System;
using Lyrow.App.Platform;
using Xunit;

namespace Lyrow.App.Tests;

public class SystemTrayVisualTests
{
    [Fact]
    public void SystemTrayManager_InstantiatesCleanly()
    {
        using var tray = new LyrowTrayIconManager();
        Assert.NotNull(tray);
        Assert.False(tray.IsGlowOpen);
        Assert.False(tray.IsLyricsOpen);
        Assert.True(tray.IsClickThroughActive);
        Assert.False(tray.IsPlaying);
    }

    [Fact]
    public void SystemTrayManager_UpdatesNowPlayingProperly()
    {
        using var tray = new LyrowTrayIconManager();
        tray.UpdateNowPlaying("Starboy", "The Weeknd", "Starboy", isPlaying: true, isMuted: false);

        Assert.Contains("Starboy", tray.NowPlayingText);
        Assert.Contains("The Weeknd", tray.NowPlayingText);
        Assert.True(tray.IsPlaying);
        Assert.False(tray.IsMuted);
    }

    [Fact]
    public void SystemTrayManager_UpdatesNowPlayingWhenEmpty()
    {
        using var tray = new LyrowTrayIconManager();
        tray.UpdateNowPlaying("", "", "", isPlaying: false, isMuted: true);

        Assert.Equal("No Media Playing", tray.NowPlayingText);
        Assert.False(tray.IsPlaying);
        Assert.True(tray.IsMuted);
    }

    [Fact]
    public void SystemTrayManager_UpdatesStates()
    {
        using var tray = new LyrowTrayIconManager();
        tray.UpdateStates(glowOpen: true, lyricsOpen: true, clickThroughActive: false);

        Assert.True(tray.IsGlowOpen);
        Assert.True(tray.IsLyricsOpen);
        Assert.False(tray.IsClickThroughActive);
    }

    [Fact]
    public void SystemTrayTheme_SetsPreferredAppMode()
    {
        using var tray = new LyrowTrayIconManager();

        tray.SetTheme("Dark");
        Assert.Equal(PreferredAppMode.ForceDark, tray.CurrentAppMode);

        tray.SetTheme("Light");
        Assert.Equal(PreferredAppMode.ForceLight, tray.CurrentAppMode);

        tray.SetTheme("System");
        Assert.Equal(PreferredAppMode.AllowDark, tray.CurrentAppMode);
    }

    [Fact]
    public void SystemTrayCommands_ConstantsAreDistinct()
    {
        int[] commands =
        [
            LyrowTrayIconManager.CMD_TRACK_INFO,
            LyrowTrayIconManager.CMD_SHOW,
            LyrowTrayIconManager.CMD_TOGGLE_GLOW,
            LyrowTrayIconManager.CMD_TOGGLE_LYRICS,
            LyrowTrayIconManager.CMD_TOGGLE_CLICKTHROUGH,
            LyrowTrayIconManager.CMD_SETTINGS,
            LyrowTrayIconManager.CMD_EXIT,
            LyrowTrayIconManager.CMD_PLAY_PAUSE,
            LyrowTrayIconManager.CMD_PREV,
            LyrowTrayIconManager.CMD_NEXT,
            LyrowTrayIconManager.CMD_MUTE_UNMUTE,
            LyrowTrayIconManager.CMD_OPEN_PLAYBACK,
            LyrowTrayIconManager.CMD_OPEN_GLOW,
            LyrowTrayIconManager.CMD_CHECK_UPDATES,
            LyrowTrayIconManager.CMD_LYRICS_FULLSCREEN
        ];

        var distinct = new System.Collections.Generic.HashSet<int>(commands);
        Assert.Equal(commands.Length, distinct.Count);
    }
}
