using Lyrow.Core.Contracts;

namespace Lyrow.Audio.Control;

/// <summary>
/// Deterministic in-memory implementation of <see cref="IAudioVolumeService"/> for unit tests and non-Windows environments.
/// Simulates master volume, per-session volume, mute state, and media player switching.
/// </summary>
public sealed class SyntheticAudioVolumeService : IAudioVolumeService
{
    private readonly Dictionary<string, int> _sessionVolumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _sessionMutes = new(StringComparer.OrdinalIgnoreCase);
    private int _masterVolumePercent = 78;
    private bool _masterMuted = false;
    private string? _preferredAppId;
    private bool _isDisposed;

    public event EventHandler<int>? VolumeChanged;

    public int MasterVolumePercent
    {
        get => _masterVolumePercent;
        set => _masterVolumePercent = Math.Clamp(value, 0, 100);
    }

    public int SessionVolumePercent
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_preferredAppId) && _sessionVolumes.TryGetValue(_preferredAppId, out int vol))
            {
                return vol;
            }
            return _masterVolumePercent;
        }
    }

    public bool IsMuted
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_preferredAppId) && _sessionMutes.TryGetValue(_preferredAppId, out bool m))
            {
                return m || _masterMuted;
            }
            return _masterMuted;
        }
    }

    public bool HasActiveMediaSession => !string.IsNullOrWhiteSpace(_preferredAppId);

    public string ActiveTargetName => !string.IsNullOrWhiteSpace(_preferredAppId) ? _preferredAppId! : "System Output";

    public int GetVolumePercent()
    {
        if (!string.IsNullOrWhiteSpace(_preferredAppId) && _sessionVolumes.TryGetValue(_preferredAppId, out int sessionVol))
        {
            return sessionVol;
        }
        return _masterVolumePercent;
    }

    public bool SetVolumePercent(int percent)
    {
        if (_isDisposed) return false;
        int clamped = Math.Clamp(percent, 0, 100);

        if (!string.IsNullOrWhiteSpace(_preferredAppId))
        {
            _sessionVolumes[_preferredAppId] = clamped;
            if (clamped > 0)
            {
                _sessionMutes[_preferredAppId] = false;
                _masterMuted = false;
            }
        }
        else
        {
            _masterVolumePercent = clamped;
            if (clamped > 0)
            {
                _masterMuted = false;
            }
        }

        VolumeChanged?.Invoke(this, clamped);
        return true;
    }

    public bool SetMute(bool muted)
    {
        if (_isDisposed) return false;

        if (!string.IsNullOrWhiteSpace(_preferredAppId))
        {
            _sessionMutes[_preferredAppId] = muted;
        }
        else
        {
            _masterMuted = muted;
        }

        VolumeChanged?.Invoke(this, GetVolumePercent());
        return true;
    }

    public bool ToggleMute()
    {
        bool next = !IsMuted;
        SetMute(next);
        return next;
    }

    public void SetPreferredAppId(string? appUserModelId)
    {
        string? normalized = string.IsNullOrWhiteSpace(appUserModelId) ? null : appUserModelId.Trim();
        _preferredAppId = normalized;
        if (normalized != null && !_sessionVolumes.ContainsKey(normalized))
        {
            _sessionVolumes[normalized] = _masterVolumePercent;
            _sessionMutes[normalized] = false;
        }
        RefreshAndReconnect();
    }

    public bool RefreshAndReconnect()
    {
        if (_isDisposed) return false;
        VolumeChanged?.Invoke(this, GetVolumePercent());
        return true;
    }

    public static string FormatVolumePercentage(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        return $"{clamped}%";
    }

    public void Dispose()
    {
        _isDisposed = true;
    }
}
