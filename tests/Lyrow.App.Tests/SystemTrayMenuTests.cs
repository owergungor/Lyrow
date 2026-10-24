using System;
using Lyrow.App.Platform;
using Xunit;

namespace Lyrow.App.Tests;

public class SystemTrayMenuTests
{
    [Fact]
    public void SystemTrayManager_CanAttachExtendedHandlersWithoutCrashing()
    {
        var manager = new LyrowTrayIconManager();

        bool playPauseTriggered = false;
        bool prevTriggered = false;
        bool nextTriggered = false;

        manager.AttachExtendedHandlers(
            onPlayPause: () => playPauseTriggered = true,
            onPrev: () => prevTriggered = true,
            onNext: () => nextTriggered = true,
            onToggleMute: () => { },
            onOpenPlayback: () => { },
            onOpenGlow: () => { },
            onCheckUpdates: () => { },
            onToggleLyricsFullscreen: () => { });

        // UpdateNowPlaying updates tooltip and menu state safely even without Win32 tray active
        manager.UpdateNowPlaying("Bohemian Rhapsody", "Queen", "A Night at the Opera", isPlaying: true, isMuted: false);

        Assert.False(playPauseTriggered);
        Assert.False(prevTriggered);
        Assert.False(nextTriggered);

        manager.Dispose();
    }
}
