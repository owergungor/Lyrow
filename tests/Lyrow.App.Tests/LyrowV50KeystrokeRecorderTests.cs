using Lyrow.Core.Localization;
using Lyrow.Core.Models;
using Lyrow.Core.Services;
using Xunit;

namespace Lyrow.App.Tests;

public class LyrowV50KeystrokeRecorderTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void CtrlPressAndRelease_UpdatesLiveModifierBlocks_AndNeverCompletesAlone()
    {
        var session = new KeystrokeRecorderSession();
        session.SetCommittedGesture("Ctrl+Alt+G");
        session.StartRecording();

        // Press Left Ctrl -> live highlighted [Ctrl]
        var down = session.HandleKeyDown(KeystrokeRecorderSession.VkLControl);
        Assert.Equal(KeystrokeRecorderEventKind.ModifierUpdated, down.Kind);
        Assert.True(session.IsRecording);
        var tokens = session.GetVisualTokens();
        Assert.Single(tokens);
        Assert.Equal("Ctrl", tokens[0].Label);
        Assert.True(tokens[0].IsActivePressed);

        // Release Right Ctrl -> normalized to same Ctrl modifier, removed while recording
        var up = session.HandleKeyUp(KeystrokeRecorderSession.VkRControl);
        Assert.Equal(KeystrokeRecorderEventKind.ModifierUpdated, up.Kind);
        Assert.True(session.IsRecording);
        Assert.Empty(session.GetVisualTokens());
    }

    [Fact]
    public void CtrlAltG_RecordsLiveBlocks_CompletesAutomatically_AndRetainsOnModifierRelease()
    {
        var session = new KeystrokeRecorderSession();
        session.StartRecording();

        Assert.Equal(KeystrokeRecorderEventKind.ModifierUpdated, session.HandleKeyDown(KeystrokeRecorderSession.VkControl).Kind);
        Assert.Equal(KeystrokeRecorderEventKind.ModifierUpdated, session.HandleKeyDown(KeystrokeRecorderSession.VkMenu).Kind);

        var liveTokens = session.GetVisualTokens();
        Assert.Equal(2, liveTokens.Count);
        Assert.Equal("Ctrl", liveTokens[0].Label);
        Assert.Equal("Alt", liveTokens[1].Label);
        Assert.All(liveTokens, t => Assert.True(t.IsActivePressed));

        // Press G -> completes automatically
        var done = session.HandleKeyDown('G');
        Assert.Equal(KeystrokeRecorderEventKind.Completed, done.Kind);
        Assert.False(session.IsRecording);
        Assert.Equal("Alt+Ctrl+G", done.Gesture);

        // Releasing Ctrl and Alt after completion must NOT erase the completed combination
        Assert.Equal(KeystrokeRecorderEventKind.None, session.HandleKeyUp(KeystrokeRecorderSession.VkMenu).Kind);
        Assert.Equal(KeystrokeRecorderEventKind.None, session.HandleKeyUp(KeystrokeRecorderSession.VkControl).Kind);
        Assert.Equal("Alt+Ctrl+G", session.CurrentGesture);

        var finalTokens = session.GetVisualTokens();
        Assert.Equal(new[] { "Ctrl", "Alt", "G" }, finalTokens.Select(t => t.Label).ToArray());
        Assert.All(finalTokens, t => Assert.False(t.IsActivePressed));
    }

    [Fact]
    public void CtrlShiftL_AndWinPlusKey_RecordAndNormalizeCorrectly()
    {
        var session = new KeystrokeRecorderSession();

        // Ctrl + Shift + L (using right-side modifiers to verify left/right normalization)
        session.StartRecording();
        session.HandleKeyDown(KeystrokeRecorderSession.VkRControl);
        session.HandleKeyDown(KeystrokeRecorderSession.VkRShift);
        var res1 = session.HandleKeyDown('L');
        Assert.Equal(KeystrokeRecorderEventKind.Completed, res1.Kind);
        Assert.Equal("Ctrl+Shift+L", res1.Gesture);
        Assert.Equal(new[] { "Ctrl", "Shift", "L" }, session.GetVisualTokens().Select(t => t.Label).ToArray());

        // Win + F24
        session.StartRecording();
        session.HandleKeyDown(KeystrokeRecorderSession.VkLWin);
        var res2 = session.HandleKeyDown(KeystrokeRecorderSession.VkF24);
        Assert.Equal(KeystrokeRecorderEventKind.Completed, res2.Kind);
        Assert.Equal("Win+F24", res2.Gesture);

        // Win + Arrow Up
        session.StartRecording();
        session.HandleKeyDown(KeystrokeRecorderSession.VkRWin);
        var res3 = session.HandleKeyDown(KeystrokeRecorderSession.VkUp);
        Assert.Equal(KeystrokeRecorderEventKind.Completed, res3.Kind);
        Assert.Equal("Win+Up", res3.Gesture);
    }

    [Fact]
    public void ModifierOnly_CannotBeSaved_AndValidatorRejectsModifierOnly()
    {
        Assert.False(HotkeyValidator.IsValidGesture("Ctrl", out _));
        Assert.False(HotkeyValidator.IsValidGesture("Ctrl+Alt", out _));
        Assert.False(HotkeyValidator.IsValidGesture("Shift+Win", out _));

        var session = new KeystrokeRecorderSession();
        session.StartRecording("Ctrl+Alt+G");
        session.HandleKeyDown(KeystrokeRecorderSession.VkControl);
        session.HandleKeyDown(KeystrokeRecorderSession.VkShift);
        Assert.True(session.IsRecording);
        session.HandleKeyUp(KeystrokeRecorderSession.VkShift);
        session.HandleKeyUp(KeystrokeRecorderSession.VkControl);
        Assert.True(session.IsRecording); // Still waiting for a normal key
    }

    [Fact]
    public void EscapeCancelsAndRestoresPrevious_BackspaceClears()
    {
        var session = new KeystrokeRecorderSession();
        session.SetCommittedGesture("Ctrl+Alt+G");

        // Start recording, press Ctrl, then Escape -> cancels and restores Alt+Ctrl+G
        session.StartRecording();
        session.HandleKeyDown(KeystrokeRecorderSession.VkControl);
        var esc = session.HandleKeyDown(KeystrokeRecorderSession.VkEscape);
        Assert.Equal(KeystrokeRecorderEventKind.Cancelled, esc.Kind);
        Assert.False(session.IsRecording);
        Assert.Equal("Alt+Ctrl+G", session.CurrentGesture);

        // Start recording, press Backspace -> clears shortcut
        session.StartRecording();
        var bs = session.HandleKeyDown(KeystrokeRecorderSession.VkBackspace);
        Assert.Equal(KeystrokeRecorderEventKind.Cleared, bs.Kind);
        Assert.False(session.IsRecording);
        Assert.Equal(string.Empty, session.CurrentGesture);
        Assert.Empty(session.GetVisualTokens());
    }

    [Fact]
    public void InvalidKeys_FocusLoss_Timeout_AndCollisionRollback_AreHandledSafely()
    {
        var session = new KeystrokeRecorderSession();
        session.SetCommittedGesture("Ctrl+Alt+G");

        // Bare normal key without modifier rejected
        session.StartRecording();
        var bareKey = session.HandleKeyDown('G');
        Assert.Equal(KeystrokeRecorderEventKind.InvalidKey, bareKey.Kind);
        Assert.True(session.IsRecording);

        // Unsupported key (e.g. CapsLock 0x14) rejected
        session.HandleKeyDown(KeystrokeRecorderSession.VkControl);
        var caps = session.HandleKeyDown(0x14);
        Assert.Equal(KeystrokeRecorderEventKind.InvalidKey, caps.Kind);
        Assert.True(session.IsRecording);

        // Focus loss cancels safely and restores previous
        var lost = session.HandleFocusLost();
        Assert.Equal(KeystrokeRecorderEventKind.Cancelled, lost.Kind);
        Assert.False(session.IsRecording);
        Assert.Equal("Alt+Ctrl+G", session.CurrentGesture);

        // Timeout cancels safely and restores previous
        session.StartRecording();
        session.HandleKeyDown(KeystrokeRecorderSession.VkShift);
        var timeout = session.HandleTimeout();
        Assert.Equal(KeystrokeRecorderEventKind.Cancelled, timeout.Kind);
        Assert.False(session.IsRecording);
        Assert.Equal("Alt+Ctrl+G", session.CurrentGesture);

        // Collision rollback restores previous valid shortcut
        session.StartRecording();
        session.HandleKeyDown(KeystrokeRecorderSession.VkControl);
        session.HandleKeyDown(KeystrokeRecorderSession.VkMenu);
        session.HandleKeyDown('L');
        Assert.Equal("Alt+Ctrl+L", session.CurrentGesture);

        var colliding = new HotkeySettings { ToggleGlow = session.CurrentGesture, ToggleLyrics = "Ctrl+Alt+L" };
        Assert.False(HotkeyValidator.CheckInternalCollisions(colliding, out _));
        session.RollbackToPrevious();
        Assert.Equal("Alt+Ctrl+G", session.CurrentGesture);
    }

    [Fact]
    public async Task SupportedExtendedKeys_ValidateAndPersistAcrossReload()
    {
        string[] extendedGestures =
        {
            "Ctrl+F13", "Alt+F24", "Ctrl+Shift+Space", "Ctrl+Tab",
            "Ctrl+Home", "Ctrl+End", "Alt+PageUp", "Alt+PageDown",
            "Shift+Insert", "Ctrl+Delete", "Ctrl+Alt+Up", "Ctrl+Alt+Down",
            "Ctrl+Alt+Left", "Ctrl+Alt+Right"
        };

        foreach (string g in extendedGestures)
        {
            Assert.True(HotkeyValidator.IsValidGesture(g, out string? err), $"Expected {g} valid, got: {err}");
        }

        string tempFile = Path.Combine(Path.GetTempPath(), $"lyrow_kr_{Guid.NewGuid():N}.json");
        try
        {
            var sm1 = new SettingsManager(tempFile);
            await sm1.LoadAsync();
            sm1.Current.Hotkeys.ToggleGlow = HotkeyValidator.NormalizeGesture("Ctrl+Alt+PageUp");
            sm1.Current.Hotkeys.ToggleLyrics = HotkeyValidator.NormalizeGesture("Win+F15");
            await sm1.SaveAsync(sm1.Current);

            var sm2 = new SettingsManager(tempFile);
            var reloaded = await sm2.LoadAsync();
            Assert.Equal("Alt+Ctrl+PageUp", reloaded.Hotkeys.ToggleGlow);
            Assert.Equal("Win+F15", reloaded.Hotkeys.ToggleLyrics);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void WinUI3KeystrokeRecorder_UsesRealKeyEvents_SuppressesOldHotkeys_AndHasLocalizedStrings()
    {
        string xaml = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml"));
        string v4Cs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.V4Features.cs"));
        string lyricsCs = File.ReadAllText(Path.Combine(RepoRoot, "src", "Lyrow.App", "LyricsWindow.xaml.cs"));

        // Visual recorder buttons with keycap token panels (no plain TextBox)
        Assert.DoesNotContain("<TextBox x:Name=\"TxtModalHotkeyGlow\"", xaml);
        Assert.DoesNotContain("<TextBox x:Name=\"TxtModalHotkeyLyrics\"", xaml);
        Assert.Contains("<Button x:Name=\"TxtModalHotkeyGlow\"", xaml);
        Assert.Contains("x:Name=\"PanelHotkeyGlowTokens\"", xaml);
        Assert.Contains("<Button x:Name=\"TxtModalHotkeyLyrics\"", xaml);
        Assert.Contains("x:Name=\"PanelHotkeyLyricsTokens\"", xaml);

        // Real WinUI 3 PreviewKeyDown / PreviewKeyUp / LostFocus / Window Deactivated / Timeout / OS hotkey suspension
        Assert.Contains("button.PreviewKeyDown +=", v4Cs);
        Assert.Contains("button.PreviewKeyUp +=", v4Cs);
        Assert.Contains("button.LostFocus +=", v4Cs);
        Assert.Contains("WindowActivationState.Deactivated", v4Cs);
        Assert.Contains("_hotkeyRecordTimeoutTimer", v4Cs);
        Assert.Contains("hotkeyManager.UnregisterAll()", v4Cs);
        Assert.Contains("if (IsHotkeyRecordingActive) return;", v4Cs);
        Assert.Contains("if (IsHotkeyRecordingActive)", lyricsCs);

        // EN and TR localization keys
        var loc = LocalizationService.Instance;
        loc.SetLanguage("en");
        Assert.Equal("Click to record shortcut", loc["Hotkey_ClickToRecord"]);
        loc.SetLanguage("tr");
        Assert.Equal("Kısayol kaydetmek için tıklayın", loc["Hotkey_ClickToRecord"]);
        loc.SetLanguage("en");
    }
}
