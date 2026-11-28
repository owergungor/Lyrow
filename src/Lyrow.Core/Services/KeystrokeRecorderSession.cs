namespace Lyrow.Core.Services;

/// <summary>
/// Outcome of a single key event processed by <see cref="KeystrokeRecorderSession"/>.
/// </summary>
public enum KeystrokeRecorderEventKind
{
    None,
    ModifierUpdated,
    Completed,
    Cancelled,
    Cleared,
    InvalidKey
}

/// <summary>
/// Represents a single visual keycap block rendered by the Keystroke Recorder UI.
/// </summary>
public sealed record KeycapToken(string Label, bool IsActivePressed);

/// <summary>
/// Result returned after handling a KeyDown or KeyUp event in <see cref="KeystrokeRecorderSession"/>.
/// </summary>
public sealed record KeystrokeRecorderResult(
    KeystrokeRecorderEventKind Kind,
    string Gesture,
    string? ErrorMessage = null);

/// <summary>
/// Deterministic, platform-independent state machine for the Lyrow v5.0 visual Keystroke Recorder.
/// Tracks real modifier press/release states (normalizing left/right Ctrl, Alt, Shift, Win),
/// prevents modifier-only shortcuts, handles Escape cancel, Backspace clear, focus loss, and timeout.
/// </summary>
public sealed class KeystrokeRecorderSession
{
    // Win32 / Windows.System.VirtualKey integer codes
    public const int VkBackspace = 0x08;
    public const int VkTab = 0x09;
    public const int VkReturn = 0x0D;
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12; // Alt
    public const int VkEscape = 0x1B;
    public const int VkSpace = 0x20;
    public const int VkPageUp = 0x21;
    public const int VkPageDown = 0x22;
    public const int VkEnd = 0x23;
    public const int VkHome = 0x24;
    public const int VkLeft = 0x25;
    public const int VkUp = 0x26;
    public const int VkRight = 0x27;
    public const int VkDown = 0x28;
    public const int VkInsert = 0x2D;
    public const int VkDelete = 0x2E;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;
    public const int VkF1 = 0x70;
    public const int VkF24 = 0x87;
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRControl = 0xA3;
    public const int VkLMenu = 0xA4;
    public const int VkRMenu = 0xA5;

    private static readonly string[] DisplayModifierOrder = { "Ctrl", "Alt", "Shift", "Win" };

    private readonly HashSet<string> _heldModifiers = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRecording { get; private set; }
    public string CurrentGesture { get; private set; } = string.Empty;
    public string PreviousGesture { get; private set; } = string.Empty;
    public string? LastError { get; private set; }
    public TimeSpan TimeoutDuration { get; set; } = TimeSpan.FromSeconds(10);

    public IReadOnlyCollection<string> HeldModifiers => _heldModifiers;

    /// <summary>
    /// Sets the persisted/committed gesture outside of recording mode.
    /// </summary>
    public void SetCommittedGesture(string? gesture)
    {
        CurrentGesture = HotkeyValidator.NormalizeGesture(gesture);
        PreviousGesture = CurrentGesture;
        _heldModifiers.Clear();
        IsRecording = false;
        LastError = null;
    }

    /// <summary>
    /// Enters interactive keystroke recording mode, preserving the previous valid gesture for rollback.
    /// </summary>
    public void StartRecording(string? existingGesture = null)
    {
        if (existingGesture != null)
        {
            CurrentGesture = HotkeyValidator.NormalizeGesture(existingGesture);
        }

        PreviousGesture = CurrentGesture;
        _heldModifiers.Clear();
        LastError = null;
        IsRecording = true;
    }

    /// <summary>
    /// Processes a real KeyDown virtual key code during recording mode.
    /// </summary>
    public KeystrokeRecorderResult HandleKeyDown(int virtualKey)
    {
        if (!IsRecording)
        {
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.None, CurrentGesture);
        }

        LastError = null;

        // Escape cancels recording and restores previous valid shortcut
        if (virtualKey == VkEscape)
        {
            Cancel();
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.Cancelled, CurrentGesture);
        }

        // Backspace (without modifiers) clears the recorded shortcut
        if (virtualKey == VkBackspace && _heldModifiers.Count == 0)
        {
            _heldModifiers.Clear();
            CurrentGesture = string.Empty;
            IsRecording = false;
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.Cleared, string.Empty);
        }

        // Modifier keys: track live pressed state, never complete alone
        string? modifierName = TryMapModifier(virtualKey);
        if (modifierName != null)
        {
            _heldModifiers.Add(modifierName);
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.ModifierUpdated, CurrentGesture);
        }

        // Normal key: must be supported AND accompanied by at least one held modifier
        string? keyName = TryMapNormalKey(virtualKey);
        if (keyName == null)
        {
            LastError = "Desteklenmeyen tuş.";
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.InvalidKey, CurrentGesture, LastError);
        }

        if (_heldModifiers.Count == 0)
        {
            LastError = "En az bir değiştirici tuş (Ctrl, Alt, Shift, Win) gereklidir.";
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.InvalidKey, CurrentGesture, LastError);
        }

        // Build candidate gesture in natural order, then normalize & validate via HotkeyValidator
        var orderedMods = DisplayModifierOrder.Where(m => _heldModifiers.Contains(m));
        string rawGesture = $"{string.Join("+", orderedMods)}+{keyName}";

        if (!HotkeyValidator.IsValidGesture(rawGesture, out string? validationError))
        {
            LastError = validationError ?? "Geçersiz kısayol.";
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.InvalidKey, CurrentGesture, LastError);
        }

        CurrentGesture = HotkeyValidator.NormalizeGesture(rawGesture);
        _heldModifiers.Clear();
        IsRecording = false;
        return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.Completed, CurrentGesture);
    }

    /// <summary>
    /// Processes a real KeyUp virtual key code.
    /// Only updates live held modifiers while actively recording; never erases a completed combination.
    /// </summary>
    public KeystrokeRecorderResult HandleKeyUp(int virtualKey)
    {
        if (!IsRecording)
        {
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.None, CurrentGesture);
        }

        string? modifierName = TryMapModifier(virtualKey);
        if (modifierName != null && _heldModifiers.Remove(modifierName))
        {
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.ModifierUpdated, CurrentGesture);
        }

        return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.None, CurrentGesture);
    }

    /// <summary>
    /// Cancels recording and restores the previous valid shortcut.
    /// </summary>
    public void Cancel()
    {
        _heldModifiers.Clear();
        CurrentGesture = PreviousGesture;
        IsRecording = false;
        LastError = null;
    }

    /// <summary>
    /// Safely cancels recording when the control or window loses focus.
    /// </summary>
    public KeystrokeRecorderResult HandleFocusLost()
    {
        if (!IsRecording)
        {
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.None, CurrentGesture);
        }

        Cancel();
        return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.Cancelled, CurrentGesture);
    }

    /// <summary>
    /// Safely cancels recording when the recording timeout elapses.
    /// </summary>
    public KeystrokeRecorderResult HandleTimeout()
    {
        if (!IsRecording)
        {
            return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.None, CurrentGesture);
        }

        Cancel();
        return new KeystrokeRecorderResult(KeystrokeRecorderEventKind.Cancelled, CurrentGesture, "Timeout");
    }

    /// <summary>
    /// Rolls back to the previous valid gesture (e.g. if conflict or OS registration fails).
    /// </summary>
    public void RollbackToPrevious()
    {
        _heldModifiers.Clear();
        CurrentGesture = PreviousGesture;
        IsRecording = false;
    }

    /// <summary>
    /// Returns the ordered visual keycap tokens for rendering in the UI.
    /// During recording, shows live held modifiers as active/highlighted blocks.
    /// When idle, shows the committed shortcut in natural display order (Ctrl, Alt, Shift, Win, Key).
    /// </summary>
    public IReadOnlyList<KeycapToken> GetVisualTokens()
    {
        if (IsRecording)
        {
            var activeTokens = new List<KeycapToken>();
            foreach (string mod in DisplayModifierOrder)
            {
                if (_heldModifiers.Contains(mod))
                {
                    activeTokens.Add(new KeycapToken(mod, IsActivePressed: true));
                }
            }
            return activeTokens;
        }

        if (string.IsNullOrWhiteSpace(CurrentGesture))
        {
            return Array.Empty<KeycapToken>();
        }

        var parts = CurrentGesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return Array.Empty<KeycapToken>();
        }

        var modSet = new HashSet<string>(parts[..^1], StringComparer.OrdinalIgnoreCase);
        var result = new List<KeycapToken>(parts.Length);

        foreach (string mod in DisplayModifierOrder)
        {
            if (modSet.Contains(mod))
            {
                result.Add(new KeycapToken(mod, IsActivePressed: false));
            }
        }

        result.Add(new KeycapToken(FormatDisplayKeyLabel(parts[^1]), IsActivePressed: false));
        return result;
    }

    /// <summary>
    /// Maps left/right/generic modifier virtual keys to canonical modifier names.
    /// </summary>
    public static string? TryMapModifier(int virtualKey) => virtualKey switch
    {
        VkControl or VkLControl or VkRControl => "Ctrl",
        VkMenu or VkLMenu or VkRMenu => "Alt",
        VkShift or VkLShift or VkRShift => "Shift",
        VkLWin or VkRWin => "Win",
        _ => null
    };

    /// <summary>
    /// Maps supported normal virtual keys to canonical HotkeyValidator key tokens.
    /// </summary>
    public static string? TryMapNormalKey(int virtualKey)
    {
        // A-Z (0x41 - 0x5A)
        if (virtualKey >= 0x41 && virtualKey <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        // 0-9 (0x30 - 0x39)
        if (virtualKey >= 0x30 && virtualKey <= 0x39)
        {
            return ((char)virtualKey).ToString();
        }

        // F1-F24 (0x70 - 0x87)
        if (virtualKey >= VkF1 && virtualKey <= VkF24)
        {
            return $"F{virtualKey - VkF1 + 1}";
        }

        return virtualKey switch
        {
            VkSpace => "Space",
            VkTab => "Tab",
            VkHome => "Home",
            VkEnd => "End",
            VkPageUp => "PageUp",
            VkPageDown => "PageDown",
            VkInsert => "Insert",
            VkDelete => "Delete",
            VkUp => "Up",
            VkDown => "Down",
            VkLeft => "Left",
            VkRight => "Right",
            _ => null
        };
    }

    private static string FormatDisplayKeyLabel(string keyToken) => keyToken switch
    {
        "PageUp" => "PgUp",
        "PageDown" => "PgDn",
        _ => keyToken
    };
}
