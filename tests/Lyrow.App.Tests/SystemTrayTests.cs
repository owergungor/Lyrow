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
        bool lumnCalled = false;
        bool verciCalled = false;
        bool clickThroughCalled = false;
        bool settingsCalled = false;
        bool exitCalled = false;

        trayManager.Initialize(
            onShowMainWindow: () => showCalled = true,
            onToggleLumn: () => lumnCalled = true,
            onToggleVerci: () => verciCalled = true,
            onToggleClickThrough: () => clickThroughCalled = true,
            onOpenSettings: () => settingsCalled = true,
            onExit: () => exitCalled = true);

        // Update states repeatedly
        trayManager.UpdateStates(lumnOpen: true, verciOpen: false, clickThroughActive: true);
        trayManager.UpdateStates(lumnOpen: false, verciOpen: true, clickThroughActive: false);

        // Callbacks should be initially false
        showCalled.Should().BeFalse();
        lumnCalled.Should().BeFalse();
        verciCalled.Should().BeFalse();
        clickThroughCalled.Should().BeFalse();
        settingsCalled.Should().BeFalse();
        exitCalled.Should().BeFalse();

        // Dispose should be safe and idempotent
        trayManager.Dispose();
        var act = () => trayManager.Dispose();
        act.Should().NotThrow();
    }
}
