using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class GlobalHotkeyManagerTests
{
    [Fact]
    public void SyntheticGlobalHotkeyManager_RegistersAndTriggersCallbacks()
    {
        var manager = new SyntheticGlobalHotkeyManager();
        int lumnHits = 0;
        int verciHits = 0;
        int clickThroughHits = 0;

        var settings = new HotkeySettings
        {
            ToggleLumn = "Ctrl+Shift+L",
            ToggleVerci = "Ctrl+Shift+V",
            ToggleClickThrough = "Ctrl+Shift+C"
        };

        bool ok = manager.RegisterHotkeys(
            settings,
            onLumnToggle: () => lumnHits++,
            onVerciToggle: () => verciHits++,
            onClickThroughToggle: () => clickThroughHits++);

        ok.Should().BeTrue();
        manager.GetStatus().Should().HaveCount(3);
        manager.GetStatus().Should().AllSatisfy(s => s.IsRegistered.Should().BeTrue());

        manager.TriggerLumn();
        lumnHits.Should().Be(1);

        manager.TriggerVerci();
        verciHits.Should().Be(1);

        manager.TriggerClickThrough();
        clickThroughHits.Should().Be(1);

        manager.UnregisterAll();
        manager.TriggerLumn();
        lumnHits.Should().Be(1); // No new hit after unregister
    }

    [Fact]
    public void SyntheticGlobalHotkeyManager_SimulatesConflictHandling()
    {
        var manager = new SyntheticGlobalHotkeyManager { SimulateConflict = true };

        var settings = new HotkeySettings();
        bool ok = manager.RegisterHotkeys(settings, () => { }, () => { }, () => { });

        ok.Should().BeFalse();
        var statuses = manager.GetStatus();
        statuses.Should().Contain(s => !s.IsRegistered && s.ErrorMessage != null);
    }

    [Fact]
    public void WindowsGlobalHotkeyManager_IsSupported_MatchesOperatingSystem()
    {
        var manager = new WindowsGlobalHotkeyManager();
        manager.IsSupported.Should().Be(OperatingSystem.IsWindows());
        manager.Dispose();
    }
}
