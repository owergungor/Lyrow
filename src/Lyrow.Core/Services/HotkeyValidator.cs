using System.Globalization;
using Lyrow.Core.Models;

namespace Lyrow.Core.Services;

/// <summary>
/// Validates keyboard shortcut combinations, detects internal duplicate collisions,
/// and normalizes key gestures for consistent comparison across platforms.
/// </summary>
public static class HotkeyValidator
{
    private static readonly HashSet<string> ValidModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctrl", "control", "shift", "alt", "win", "windows"
    };

    private static readonly Dictionary<string, string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPACE"] = "Space",
        ["TAB"] = "Tab",
        ["ENTER"] = "Enter",
        ["RETURN"] = "Enter",
        ["HOME"] = "Home",
        ["END"] = "End",
        ["PAGEUP"] = "PageUp",
        ["PAGE UP"] = "PageUp",
        ["PGUP"] = "PageUp",
        ["PRIOR"] = "PageUp",
        ["PAGEDOWN"] = "PageDown",
        ["PAGE DOWN"] = "PageDown",
        ["PGDN"] = "PageDown",
        ["NEXT"] = "PageDown",
        ["INSERT"] = "Insert",
        ["INS"] = "Insert",
        ["DELETE"] = "Delete",
        ["DEL"] = "Delete",
        ["UP"] = "Up",
        ["DOWN"] = "Down",
        ["LEFT"] = "Left",
        ["RIGHT"] = "Right",
        ["ESC"] = "Escape",
        ["ESCAPE"] = "Escape"
    };

    /// <summary>
    /// Normalizes a shortcut string into a canonical, sorted format (e.g. "Shift+Ctrl+L" -> "Ctrl+Shift+L").
    /// Returns empty string if the gesture is null or whitespace.
    /// </summary>
    public static string NormalizeGesture(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return string.Empty;

        var tokens = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return string.Empty;

        var modifiers = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string keyToken = NormalizeKeyToken(tokens[^1]);

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            string mod = tokens[i].ToLowerInvariant();
            if (mod is "control") mod = "ctrl";
            if (mod is "windows") mod = "win";

            if (ValidModifiers.Contains(mod))
            {
                modifiers.Add(mod switch
                {
                    "ctrl" => "Ctrl",
                    "shift" => "Shift",
                    "alt" => "Alt",
                    "win" => "Win",
                    _ => mod
                });
            }
        }

        if (modifiers.Count == 0)
        {
            return keyToken;
        }

        return $"{string.Join("+", modifiers)}+{keyToken}";
    }

    /// <summary>
    /// Normalizes a single non-modifier key token to its canonical representation.
    /// </summary>
    public static string NormalizeKeyToken(string rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return string.Empty;

        string trimmed = rawKey.Trim();
        if (NamedKeys.TryGetValue(trimmed, out string? canonicalNamed))
        {
            return canonicalNamed;
        }

        string upper = trimmed.ToUpperInvariant();
        if (upper.Length == 1 && ((upper[0] >= 'A' && upper[0] <= 'Z') || (upper[0] >= '0' && upper[0] <= '9')))
        {
            return upper;
        }

        if (upper.StartsWith('F') && int.TryParse(upper[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f >= 1 && f <= 24)
        {
            return $"F{f}";
        }

        return upper;
    }

    /// <summary>
    /// Returns true if the given token is a supported normal (non-modifier) shortcut key.
    /// </summary>
    public static bool IsSupportedNormalKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return false;

        string keyToken = rawKey.Trim();
        if (ValidModifiers.Contains(keyToken)) return false;

        if (NamedKeys.ContainsKey(keyToken))
        {
            return true;
        }

        string upper = keyToken.ToUpperInvariant();
        if (upper.Length == 1 && ((upper[0] >= 'A' && upper[0] <= 'Z') || (upper[0] >= '0' && upper[0] <= '9')))
        {
            return true;
        }

        if (upper.StartsWith('F') && int.TryParse(upper[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f >= 1 && f <= 24)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a key gesture has a valid syntax (at least one modifier and one valid key) or is empty.
    /// </summary>
    public static bool IsValidGesture(string? gesture, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return true; // Empty / unassigned is valid
        }

        var tokens = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            error = "Kısayol en az bir değiştirici tuş (Ctrl, Alt, Shift, Win) ve bir tuş içermelidir.";
            return false;
        }

        string rawKeyToken = tokens[^1];
        if (ValidModifiers.Contains(rawKeyToken))
        {
            error = "Değiştirici tuşlar tek başına kısayol olarak kaydedilemez. En az bir normal tuş ekleyin.";
            return false;
        }

        bool hasModifier = false;

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            string mod = tokens[i].ToLowerInvariant();
            if (ValidModifiers.Contains(mod))
            {
                hasModifier = true;
            }
            else
            {
                error = $"Bilinmeyen değiştirici tuş: {tokens[i]}";
                return false;
            }
        }

        if (!hasModifier)
        {
            error = "En az bir değiştirici tuş (Ctrl, Alt, Shift, Win) gereklidir.";
            return false;
        }

        if (IsSupportedNormalKey(rawKeyToken))
        {
            return true;
        }

        error = $"Desteklenmeyen tuş: {rawKeyToken.ToUpperInvariant()}. (A-Z, 0-9, F1-F24, Space, Tab, Home, End, PageUp, PageDown, Insert, Delete, Ok tuşları kullanılabilir)";
        return false;
    }

    /// <summary>
    /// Checks whether two gestures represent the exact same keyboard combination.
    /// Empty gestures never collide.
    /// </summary>
    public static bool AreColliding(string? gestureA, string? gestureB)
    {
        string normA = NormalizeGesture(gestureA);
        string normB = NormalizeGesture(gestureB);

        if (string.IsNullOrEmpty(normA) || string.IsNullOrEmpty(normB))
        {
            return false;
        }

        return string.Equals(normA, normB, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if any configured action shortcuts in the settings duplicate each other.
    /// </summary>
    public static bool CheckInternalCollisions(HotkeySettings settings, out string? collisionError)
    {
        collisionError = null;

        if (AreColliding(settings.ToggleGlow, settings.ToggleLyrics))
        {
            collisionError = $"'{NormalizeGesture(settings.ToggleGlow)}' hem Glow hem de Lyrics için kullanılamaz.";
            return false;
        }

        if (AreColliding(settings.ToggleGlow, settings.ToggleClickThrough))
        {
            collisionError = $"'{NormalizeGesture(settings.ToggleGlow)}' hem Glow hem de Tıklama Geçirgenliği için kullanılamaz.";
            return false;
        }

        if (AreColliding(settings.ToggleLyrics, settings.ToggleClickThrough))
        {
            collisionError = $"'{NormalizeGesture(settings.ToggleLyrics)}' hem Lyrics hem de Tıklama Geçirgenliği için kullanılamaz.";
            return false;
        }

        return true;
    }
}
