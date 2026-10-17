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
        string keyToken = tokens[^1].ToUpperInvariant();

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            string mod = tokens[i].ToLowerInvariant();
            if (mod is "control") mod = "ctrl";
            if (mod is "windows") mod = "win";

            if (ValidModifiers.Contains(mod))
            {
                // Canonical capitalization
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
            error = "Kısayol en az bir değiştirici tuş (Ctrl, Alt, Shift) ve bir tuş içermelidir.";
            return false;
        }

        string keyToken = tokens[^1].ToUpperInvariant();
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
            error = "En az bir değiştirici tuş (Ctrl, Alt, Shift) gereklidir.";
            return false;
        }

        // Validate key token (A-Z, 0-9, F1-F12, Space)
        if (keyToken.Length == 1 && ((keyToken[0] >= 'A' && keyToken[0] <= 'Z') || (keyToken[0] >= '0' && keyToken[0] <= '9')))
        {
            return true;
        }

        if (keyToken.StartsWith('F') && int.TryParse(keyToken[1..], out int f) && f >= 1 && f <= 12)
        {
            return true;
        }

        if (keyToken is "SPACE" or "ENTER" or "TAB" or "ESC" or "ESCAPE")
        {
            return true;
        }

        error = $"Desteklenmeyen tuş: {keyToken}. (A-Z, 0-9, F1-F12 kullanılabilir)";
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

        if (AreColliding(settings.ToggleLumn, settings.ToggleVerci))
        {
            collisionError = $"'{NormalizeGesture(settings.ToggleLumn)}' hem Glow hem de Lyrics için kullanılamaz.";
            return false;
        }

        if (AreColliding(settings.ToggleLumn, settings.ToggleClickThrough))
        {
            collisionError = $"'{NormalizeGesture(settings.ToggleLumn)}' hem Glow hem de Tıklama Geçirgenliği için kullanılamaz.";
            return false;
        }

        if (AreColliding(settings.ToggleVerci, settings.ToggleClickThrough))
        {
            collisionError = $"'{NormalizeGesture(settings.ToggleVerci)}' hem Lyrics hem de Tıklama Geçirgenliği için kullanılamaz.";
            return false;
        }

        return true;
    }
}
