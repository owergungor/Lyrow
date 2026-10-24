using FluentAssertions;
using Lyrow.App.Platform;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class HotkeyV12Tests
{
    [Fact]
    public void DefaultInstall_HasZeroGlobalHotkeys()
    {
        var settings = new HotkeySettings();

        settings.ToggleGlow.Should().BeEmpty();
        settings.ToggleLyrics.Should().BeEmpty();
        settings.ToggleClickThrough.Should().BeEmpty();

        var manager = new SyntheticGlobalHotkeyManager();
        bool ok = manager.RegisterHotkeys(settings, () => { }, () => { }, () => { });

        ok.Should().BeTrue();
        var statuses = manager.GetStatus();
        statuses.Should().HaveCount(3);
        statuses.Should().AllSatisfy(s =>
        {
            s.IsRegistered.Should().BeFalse();
            s.State.Should().Be(HotkeyRegistrationState.NotRegistered);
        });
    }

    [Fact]
    public void UserDefinedHotkey_RegistersSuccessfully()
    {
        var manager = new SyntheticGlobalHotkeyManager();
        var settings = new HotkeySettings
        {
            ToggleGlow = "Ctrl+Alt+G",
            ToggleLyrics = "Ctrl+Shift+L",
            ToggleClickThrough = "Ctrl+Win+C"
        };

        bool ok = manager.RegisterHotkeys(settings, () => { }, () => { }, () => { });

        ok.Should().BeTrue();
        var statuses = manager.GetStatus();
        statuses.Should().HaveCount(3);
        statuses.Should().AllSatisfy(s =>
        {
            s.IsRegistered.Should().BeTrue();
            s.State.Should().Be(HotkeyRegistrationState.Registered);
        });
    }

    [Fact]
    public void ClearHotkey_UnregistersAndLeavesUnassigned()
    {
        var manager = new SyntheticGlobalHotkeyManager();
        int glowHits = 0;

        var settings = new HotkeySettings { ToggleGlow = "Ctrl+Alt+G" };
        manager.RegisterHotkeys(settings, () => glowHits++, () => { }, () => { });
        manager.TriggerGlow();
        glowHits.Should().Be(1);

        // User clears hotkey
        settings.ToggleGlow = "";
        manager.RegisterHotkeys(settings, () => glowHits++, () => { }, () => { });

        manager.GetStatus().First(s => s.ActionName == "Glow").State.Should().Be(HotkeyRegistrationState.NotRegistered);
        manager.TriggerGlow();
        glowHits.Should().Be(1); // Callback not invoked
    }

    [Fact]
    public void DuplicateHotkey_AcrossDifferentActions_IsRejected()
    {
        var manager = new SyntheticGlobalHotkeyManager();
        var settings = new HotkeySettings
        {
            ToggleGlow = "Ctrl+Shift+L",
            ToggleLyrics = "Ctrl+Shift+L" // Collision!
        };

        bool ok = manager.RegisterHotkeys(settings, () => { }, () => { }, () => { });

        ok.Should().BeFalse();
        var statuses = manager.GetStatus();
        statuses.Should().Contain(s => s.State == HotkeyRegistrationState.Conflict);
        var conflict = statuses.First(s => s.State == HotkeyRegistrationState.Conflict);
        conflict.ErrorMessage.Should().Contain("hem Glow hem de Lyrics için kullanılamaz");
    }

    [Fact]
    public void DuplicateHotkey_NormalizedCaseAndOrderCollision_IsDetected()
    {
        // "Shift+Ctrl+L" vs "Ctrl+Shift+L" must collide
        HotkeyValidator.AreColliding("Shift+Ctrl+L", "ctrl+shift+l").Should().BeTrue();
        HotkeyValidator.AreColliding("Ctrl+Alt+C", "Alt+Ctrl+C").Should().BeTrue();
        HotkeyValidator.AreColliding("Ctrl+Alt+C", "Ctrl+Shift+C").Should().BeFalse();
    }

    [Fact]
    public void OSCollision_SimulatedWin32Conflict_IsRejectedWithClearMessage()
    {
        var manager = new SyntheticGlobalHotkeyManager { SimulateConflict = true };
        var settings = new HotkeySettings { ToggleGlow = "Ctrl+Shift+L" };

        bool ok = manager.RegisterHotkeys(settings, () => { }, () => { }, () => { });

        ok.Should().BeFalse();
        var statuses = manager.GetStatus();
        var glowStatus = statuses.First(s => s.ActionName == "Glow");
        glowStatus.State.Should().Be(HotkeyRegistrationState.Conflict);
        glowStatus.ErrorMessage.Should().Contain("başka bir uygulama tarafından kullanılıyor");
    }

    [Fact]
    public async Task CustomHotkeys_PersistAcrossSettingsReload()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"Lyrow_Hotkey_{Guid.NewGuid():N}.json");
        try
        {
            var manager = new SettingsManager(tempFile);
            await manager.LoadAsync();

            await manager.UpdateAsync(s =>
            {
                s.Hotkeys.ToggleGlow = "Ctrl+Alt+1";
                s.Hotkeys.ToggleLyrics = "Ctrl+Alt+2";
                s.Hotkeys.ToggleClickThrough = "Ctrl+Alt+3";
            });

            var reloaded = new SettingsManager(tempFile);
            var fresh = await reloaded.LoadAsync();

            fresh.Hotkeys.ToggleGlow.Should().Be("Ctrl+Alt+1");
            fresh.Hotkeys.ToggleLyrics.Should().Be("Ctrl+Alt+2");
            fresh.Hotkeys.ToggleClickThrough.Should().Be("Ctrl+Alt+3");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Win32_1408_RegressionTest_ThreadOwnershipGuaranteed()
    {
        // Ensure that WindowsGlobalHotkeyManager can be instantiated, queried, and disposed without throwing 1408
        if (OperatingSystem.IsWindows())
        {
            var manager = new WindowsGlobalHotkeyManager();
            manager.IsSupported.Should().BeTrue();

            // Calling from external thread (xUnit test thread)
            var settings = new HotkeySettings(); // Empty defaults
            bool ok = manager.RegisterHotkeys(settings, () => { }, () => { }, () => { });
            ok.Should().BeTrue();

            manager.UnregisterAll();
            manager.Dispose();
        }
    }
}
