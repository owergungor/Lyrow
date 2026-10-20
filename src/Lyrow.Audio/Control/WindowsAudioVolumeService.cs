using System.Diagnostics;
using System.Runtime.Versioning;
using Lyrow.Core.Contracts;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Lyrow.Audio.Control;

/// <summary>
/// Hardware Windows CoreAudio / WASAPI volume controller.
/// Controls active playback session volume (ISimpleAudioVolume) and system master endpoint volume (IAudioEndpointVolume)
/// in a clean 0%–100% scale, automatically reconnecting when media sessions or output devices change.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioVolumeService : IAudioVolumeService
{
    private readonly object _lock = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _currentDevice;
    private string? _preferredAppId;
    private string _activeTargetName = "System Output";
    private bool _hasActiveMediaSession;
    private int _lastKnownPercent = 78;
    private bool _isDisposed;

    public event EventHandler<int>? VolumeChanged;

    public WindowsAudioVolumeService()
    {
        if (OperatingSystem.IsWindows())
        {
            RefreshAndReconnect();
        }
    }

    public int MasterVolumePercent
    {
        get
        {
            lock (_lock)
            {
                try
                {
                    EnsureDeviceConnected();
                    if (_currentDevice?.AudioEndpointVolume != null)
                    {
                        float scalar = _currentDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                        return Math.Clamp((int)Math.Round(scalar * 100.0f), 0, 100);
                    }
                }
                catch { }
                return _lastKnownPercent;
            }
        }
    }

    public int SessionVolumePercent
    {
        get
        {
            lock (_lock)
            {
                try
                {
                    EnsureDeviceConnected();
                    var sessions = FindMatchingMediaSessions(_currentDevice, _preferredAppId, out _, out _);
                    if (sessions.Count > 0 && sessions[0].SimpleAudioVolume != null)
                    {
                        float scalar = sessions[0].SimpleAudioVolume.Volume;
                        return Math.Clamp((int)Math.Round(scalar * 100.0f), 0, 100);
                    }
                }
                catch { }
                return MasterVolumePercent;
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (_lock)
            {
                try
                {
                    EnsureDeviceConnected();
                    if (_currentDevice?.AudioEndpointVolume?.Mute == true)
                    {
                        return true;
                    }

                    var sessions = FindMatchingMediaSessions(_currentDevice, _preferredAppId, out _, out _);
                    if (sessions.Count > 0 && sessions[0].SimpleAudioVolume != null)
                    {
                        return sessions[0].SimpleAudioVolume.Mute;
                    }
                }
                catch { }
                return false;
            }
        }
    }

    public bool HasActiveMediaSession => _hasActiveMediaSession;

    public string ActiveTargetName => _activeTargetName;

    public int GetVolumePercent()
    {
        if (!OperatingSystem.IsWindows() || _isDisposed) return _lastKnownPercent;

        lock (_lock)
        {
            try
            {
                EnsureDeviceConnected();
                if (_currentDevice == null) return _lastKnownPercent;

                var sessions = FindMatchingMediaSessions(_currentDevice, _preferredAppId, out string targetLabel, out bool matchedSession);
                _hasActiveMediaSession = matchedSession;
                _activeTargetName = targetLabel;

                if (sessions.Count > 0 && sessions[0].SimpleAudioVolume != null)
                {
                    float sessionScalar = sessions[0].SimpleAudioVolume.Volume;
                    _lastKnownPercent = Math.Clamp((int)Math.Round(sessionScalar * 100.0f), 0, 100);
                    return _lastKnownPercent;
                }

                if (_currentDevice.AudioEndpointVolume != null)
                {
                    float masterScalar = _currentDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                    _lastKnownPercent = Math.Clamp((int)Math.Round(masterScalar * 100.0f), 0, 100);
                    return _lastKnownPercent;
                }
            }
            catch
            {
                // Recover on next call
            }

            return _lastKnownPercent;
        }
    }

    public bool SetVolumePercent(int percent)
    {
        int clamped = Math.Clamp(percent, 0, 100);
        _lastKnownPercent = clamped;

        if (!OperatingSystem.IsWindows() || _isDisposed) return false;

        bool applied = false;
        lock (_lock)
        {
            try
            {
                EnsureDeviceConnected();
                if (_currentDevice == null) return false;

                float scalar = clamped / 100.0f;

                var sessions = FindMatchingMediaSessions(_currentDevice, _preferredAppId, out string targetLabel, out bool matchedSession);
                _hasActiveMediaSession = matchedSession;
                _activeTargetName = targetLabel;

                if (sessions.Count > 0)
                {
                    foreach (var session in sessions)
                    {
                        try
                        {
                            if (session.SimpleAudioVolume != null)
                            {
                                session.SimpleAudioVolume.Volume = scalar;
                                if (clamped > 0 && session.SimpleAudioVolume.Mute)
                                {
                                    session.SimpleAudioVolume.Mute = false;
                                }
                                applied = true;
                            }
                        }
                        catch { }
                    }

                    // Ensure master endpoint is not muted or near-zero when user raises volume
                    if (_currentDevice.AudioEndpointVolume != null)
                    {
                        try
                        {
                            if (clamped > 0 && _currentDevice.AudioEndpointVolume.Mute)
                            {
                                _currentDevice.AudioEndpointVolume.Mute = false;
                            }
                            if (clamped > 0 && _currentDevice.AudioEndpointVolume.MasterVolumeLevelScalar < 0.05f)
                            {
                                _currentDevice.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
                            }
                        }
                        catch { }
                    }
                }
                else if (_currentDevice.AudioEndpointVolume != null)
                {
                    _currentDevice.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
                    if (clamped > 0 && _currentDevice.AudioEndpointVolume.Mute)
                    {
                        _currentDevice.AudioEndpointVolume.Mute = false;
                    }
                    applied = true;
                }
            }
            catch
            {
                // Reconnect on device invalidation
                DisposeDevice();
                try
                {
                    EnsureDeviceConnected();
                    if (_currentDevice?.AudioEndpointVolume != null)
                    {
                        _currentDevice.AudioEndpointVolume.MasterVolumeLevelScalar = clamped / 100.0f;
                        applied = true;
                    }
                }
                catch { }
            }
        }

        if (applied)
        {
            VolumeChanged?.Invoke(this, clamped);
        }

        return applied;
    }

    public bool SetMute(bool muted)
    {
        if (!OperatingSystem.IsWindows() || _isDisposed) return false;

        bool applied = false;
        lock (_lock)
        {
            try
            {
                EnsureDeviceConnected();
                if (_currentDevice == null) return false;

                var sessions = FindMatchingMediaSessions(_currentDevice, _preferredAppId, out _, out _);
                if (sessions.Count > 0)
                {
                    foreach (var s in sessions)
                    {
                        try
                        {
                            if (s.SimpleAudioVolume != null)
                            {
                                s.SimpleAudioVolume.Mute = muted;
                                applied = true;
                            }
                        }
                        catch { }
                    }
                }
                else if (_currentDevice.AudioEndpointVolume != null)
                {
                    _currentDevice.AudioEndpointVolume.Mute = muted;
                    applied = true;
                }
            }
            catch { }
        }

        if (applied)
        {
            VolumeChanged?.Invoke(this, _lastKnownPercent);
        }
        return applied;
    }

    public bool ToggleMute()
    {
        bool next = !IsMuted;
        SetMute(next);
        return next;
    }

    public void SetPreferredAppId(string? appUserModelId)
    {
        lock (_lock)
        {
            _preferredAppId = string.IsNullOrWhiteSpace(appUserModelId) ? null : appUserModelId.Trim();
        }
        RefreshAndReconnect();
    }

    public bool RefreshAndReconnect()
    {
        if (!OperatingSystem.IsWindows() || _isDisposed) return false;

        int currentVol;
        lock (_lock)
        {
            try
            {
                DisposeDevice();
                EnsureDeviceConnected();
                if (_currentDevice == null) return false;

                var sessions = FindMatchingMediaSessions(_currentDevice, _preferredAppId, out string targetLabel, out bool matchedSession);
                _hasActiveMediaSession = matchedSession;
                _activeTargetName = targetLabel;
                currentVol = GetVolumePercent();
            }
            catch
            {
                return false;
            }
        }

        VolumeChanged?.Invoke(this, currentVol);
        return true;
    }

    private void EnsureDeviceConnected()
    {
        if (_currentDevice != null) return;
        _enumerator ??= new MMDeviceEnumerator();
        _currentDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private void DisposeDevice()
    {
        try
        {
            _currentDevice?.Dispose();
        }
        catch { }
        _currentDevice = null;
    }

    private static List<AudioSessionControl> FindMatchingMediaSessions(
        MMDevice? device,
        string? preferredAppId,
        out string targetLabel,
        out bool matchedSession)
    {
        var matched = new List<AudioSessionControl>();
        targetLabel = "System Output";
        matchedSession = false;

        if (device?.AudioSessionManager == null) return matched;

        try
        {
            device.AudioSessionManager.RefreshSessions();
            var sessions = device.AudioSessionManager.Sessions;
            if (sessions == null || sessions.Count == 0) return matched;

            int selfPid = Environment.ProcessId;
            string? cleanPreferred = ExtractAppToken(preferredAppId);

            var activeNonSystem = new List<(AudioSessionControl session, string label)>();
            var preferredMatches = new List<(AudioSessionControl session, string label, bool isActive)>();

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session == null) continue;

                try
                {
                    if (session.IsSystemSoundsSession) continue;
                    int pid = (int)session.GetProcessID;
                    if (pid == selfPid) continue;

                    string procName = TryGetProcessName(pid);
                    string dispName = session.DisplayName ?? string.Empty;
                    string sessionId = session.GetSessionIdentifier ?? string.Empty;
                    bool isActive = session.State == AudioSessionState.AudioSessionStateActive;

                    string friendly = !string.IsNullOrWhiteSpace(procName)
                        ? FormatProcessFriendlyName(procName)
                        : (!string.IsNullOrWhiteSpace(dispName) ? dispName : "Media Player");

                    if (!string.IsNullOrWhiteSpace(cleanPreferred))
                    {
                        bool isMatch =
                            procName.Contains(cleanPreferred, StringComparison.OrdinalIgnoreCase) ||
                            cleanPreferred.Contains(procName, StringComparison.OrdinalIgnoreCase) && procName.Length >= 3 ||
                            sessionId.Contains(cleanPreferred, StringComparison.OrdinalIgnoreCase) ||
                            dispName.Contains(cleanPreferred, StringComparison.OrdinalIgnoreCase);

                        if (isMatch)
                        {
                            preferredMatches.Add((session, friendly, isActive));
                        }
                    }

                    if (isActive)
                    {
                        activeNonSystem.Add((session, friendly));
                    }
                }
                catch { }
            }

            // Priority 1: Preferred app sessions (active first, then all sessions belonging to that app)
            if (preferredMatches.Count > 0)
            {
                var activePref = preferredMatches.Where(x => x.isActive).ToList();
                var chosen = activePref.Count > 0 ? activePref : preferredMatches;
                foreach (var item in chosen)
                {
                    matched.Add(item.session);
                }
                targetLabel = chosen[0].label;
                matchedSession = true;
                return matched;
            }

            // Priority 2: Any currently active non-system audio render session
            if (activeNonSystem.Count > 0)
            {
                foreach (var item in activeNonSystem)
                {
                    matched.Add(item.session);
                }
                targetLabel = activeNonSystem[0].label;
                matchedSession = true;
                return matched;
            }
        }
        catch { }

        return matched;
    }

    private static string? ExtractAppToken(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId)) return null;
        string token = appUserModelId.Trim();
        if (token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            token = token[..^4];
        }
        if (token.Contains("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (token.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)) return "AppleMusic";
        if (token.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase)) return "MediaPlayer";
        if (token.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "chrome";
        if (token.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "msedge";
        if (token.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "firefox";
        if (token.Contains("vlc", StringComparison.OrdinalIgnoreCase)) return "vlc";

        int bang = token.IndexOf('!');
        if (bang > 0) token = token[..bang];
        int underscore = token.IndexOf('_');
        if (underscore > 0) token = token[..underscore];

        return token;
    }

    private static string TryGetProcessName(int pid)
    {
        if (pid <= 0) return string.Empty;
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatProcessFriendlyName(string procName)
    {
        if (procName.Equals("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (procName.Equals("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (procName.Equals("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (procName.Equals("Spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (procName.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        if (procName.Equals("vlc", StringComparison.OrdinalIgnoreCase)) return "VLC";
        if (procName.Length > 1) return char.ToUpperInvariant(procName[0]) + procName[1..];
        return procName;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        lock (_lock)
        {
            DisposeDevice();
            try
            {
                _enumerator?.Dispose();
            }
            catch { }
            _enumerator = null;
        }
    }
}
