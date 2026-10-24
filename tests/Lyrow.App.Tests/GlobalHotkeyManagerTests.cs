using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Xunit;

namespace Lyrow.App.Tests;

public class GlobalHotkeyManagerTests
{
    [Fact]
    public void SyntheticGlobalHotkeyManager_RegistersAndTriggersCallbacks()
    {
        var manager = new SyntheticGlobalHotkeyManager();
        int glowHits = 0;
        int lyricsHits = 0;
        int clickThroughHits = 0;

        var settings = new HotkeySettings
        {
            ToggleGlow = "Ctrl+Shift+L",
            ToggleLyrics = "Ctrl+Shift+V",
            ToggleClickThrough = "Ctrl+Shift+C"
        };

        bool ok = manager.RegisterHotkeys(
            settings,
            onGlowToggle: () => glowHits++,
            onLyricsToggle: () => lyricsHits++,
            onClickThroughToggle: () => clickThroughHits++);

        ok.Should().BeTrue();
        manager.GetStatus().Should().HaveCount(3);
        manager.GetStatus().Should().AllSatisfy(s =>
        {
            s.IsRegistered.Should().BeTrue();
            s.State.Should().Be(HotkeyRegistrationState.Registered);
        });

        manager.TriggerGlow();
        glowHits.Should().Be(1);

        manager.TriggerLyrics();
        lyricsHits.Should().Be(1);

        manager.TriggerClickThrough();
        clickThroughHits.Should().Be(1);

        manager.UnregisterAll();
        manager.TriggerGlow();
        glowHits.Should().Be(1); // No new hit after unregister
    }

    [Fact]
    public void SyntheticGlobalHotkeyManager_SimulatesConflictHandling_WithClearUserMessage()
    {
        var manager = new SyntheticGlobalHotkeyManager { SimulateConflict = true };

        var settings = new HotkeySettings
        {
            ToggleGlow = "Ctrl+Shift+L",
            ToggleLyrics = "Ctrl+Shift+V",
            ToggleClickThrough = "Ctrl+Shift+C"
        };
        bool ok = manager.RegisterHotkeys(settings, () => { }, () => { }, () => { });

        ok.Should().BeFalse();
        var statuses = manager.GetStatus();
        statuses.Should().Contain(s => !s.IsRegistered && s.State == HotkeyRegistrationState.Conflict);
        var conflict = statuses.First(s => s.State == HotkeyRegistrationState.Conflict);
        conflict.ErrorMessage.Should().Contain("başka bir uygulama tarafından kullanılıyor");
    }

    [Fact]
    public void DuplicateRegistration_DoesNotCrashOrLeak()
    {
        var manager = new SyntheticGlobalHotkeyManager();
        var settings = new HotkeySettings();

        // First registration
        manager.RegisterHotkeys(settings, () => { }, () => { }, () => { }).Should().BeTrue();
        manager.GetStatus().Should().HaveCount(3);

        // Immediate second registration with same or new settings
        manager.RegisterHotkeys(settings, () => { }, () => { }, () => { }).Should().BeTrue();
        manager.GetStatus().Should().HaveCount(3);

        manager.UnregisterAll();
        manager.GetStatus().Should().BeEmpty();
    }

    [Fact]
    public void WindowsGlobalHotkeyManager_IsSupported_MatchesOperatingSystem()
    {
        var manager = new WindowsGlobalHotkeyManager();
        manager.IsSupported.Should().Be(OperatingSystem.IsWindows());
        manager.Dispose();
    }
}
