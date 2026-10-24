using FluentAssertions;
using Lyrow.App.Platform;
using Xunit;

namespace Lyrow.App.Tests;

public class SystemTrayTests
{
    [Fact]
    public void LyrowTrayIconManager_StateTransitions_UpdateWithoutError()
    {
        var trayManager = new LyrowTrayIconManager();

        bool showCalled = false;
        bool glowCalled = false;
        bool lyricsCalled = false;
        bool clickThroughCalled = false;
        bool settingsCalled = false;
        bool exitCalled = false;

        trayManager.Initialize(
            onShowMainWindow: () => showCalled = true,
            onToggleGlow: () => glowCalled = true,
            onToggleLyrics: () => lyricsCalled = true,
            onToggleClickThrough: () => clickThroughCalled = true,
            onOpenSettings: () => settingsCalled = true,
            onExit: () => exitCalled = true);

        // Update states repeatedly
        trayManager.UpdateStates(glowOpen: true, lyricsOpen: false, clickThroughActive: true);
        trayManager.UpdateStates(glowOpen: false, lyricsOpen: true, clickThroughActive: false);

        // Callbacks should be initially false
        showCalled.Should().BeFalse();
        glowCalled.Should().BeFalse();
        lyricsCalled.Should().BeFalse();
        clickThroughCalled.Should().BeFalse();
        settingsCalled.Should().BeFalse();
        exitCalled.Should().BeFalse();

        // Dispose should be safe and idempotent
        trayManager.Dispose();
        var act = () => trayManager.Dispose();
        act.Should().NotThrow();
    }
}
