using System.Runtime.Versioning;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Lyrow.Music.Session;

/// <summary>
/// Hardware Windows GSMTC (Global System Media Transport Controls) media session monitor.
/// Listens to active OS-level media sessions (Spotify, Apple Music, YouTube, Edge, Chrome)
/// and dispatches real Play, Pause, TogglePlayPause, Next, and Previous commands.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsGsmtcMediaSessionMonitor : IMediaSessionMonitor
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private bool _isMonitoring;
    private bool _isDisposed;

    public WindowsGsmtcMediaSessionMonitor()
    {
        CurrentTrack = MediaTrackInfo.Empty;
        CurrentTimeline = MediaTimelineInfo.Zero;
    }

    public event EventHandler<MediaTrackInfo>? TrackChanged;
    public event EventHandler<MediaTimelineInfo>? TimelineChanged;
    public event EventHandler<Exception>? MonitorFaulted;

    public bool IsMonitoring => _isMonitoring;
    public bool HasActiveSession => _currentSession != null;
    public string? SourceAppId => _currentSession?.SourceAppUserModelId;
    public MediaTrackInfo CurrentTrack { get; private set; }
    public MediaTimelineInfo CurrentTimeline { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException("GSMTC requires Windows 10 build 19041 or higher.");
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isMonitoring) return;

            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                _sessionManager.SessionsChanged += OnSessionsChanged;
                var session = ResolveBestSession(_sessionManager);
                await AttachSessionInternalAsync(session).ConfigureAwait(false);
            }

            _isMonitoring = true;
        }
        catch (Exception ex)
        {
            MonitorFaulted?.Invoke(this, ex);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;
            DetachCurrentSession();

            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _sessionManager.SessionsChanged -= OnSessionsChanged;
                _sessionManager = null;
            }

            CurrentTrack = MediaTrackInfo.Empty;
            CurrentTimeline = MediaTimelineInfo.Zero;
        }
        finally
        {
            _lock.Release();
        }
    }

    private GlobalSystemMediaTransportControlsSession? ResolveBestSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        try
        {
            var current = manager.GetCurrentSession();
            if (current != null) return current;

            var all = manager.GetSessions();
            if (all == null || all.Count == 0) return null;

            foreach (var s in all)
            {
                try
                {
                    var info = s.GetPlaybackInfo();
                    if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        return s;
                    }
                }
                catch { }
            }

            foreach (var s in all)
            {
                try
                {
                    var info = s.GetPlaybackInfo();
                    if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    {
                        return s;
                    }
                }
                catch { }
            }

            return all[0];
        }
        catch
        {
            return null;
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        if (!_isMonitoring) return;
        _ = RefreshSessionBindingAsync(sender);
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        if (!_isMonitoring) return;
        _ = RefreshSessionBindingAsync(sender);
    }

    private async Task RefreshSessionBindingAsync(GlobalSystemMediaTransportControlsSessionManager sender)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_isMonitoring) return;
            var best = ResolveBestSession(sender);
            DetachCurrentSession();
            await AttachSessionInternalAsync(best).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task AttachSessionInternalAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        _currentSession = session;
        if (_currentSession == null)
        {
            CurrentTrack = MediaTrackInfo.Empty;
            CurrentTimeline = MediaTimelineInfo.Zero;
            TrackChanged?.Invoke(this, CurrentTrack);
            TimelineChanged?.Invoke(this, CurrentTimeline);
            return;
        }

        _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        await UpdateAllPropertiesAsync(_currentSession).ConfigureAwait(false);
    }

    private void DetachCurrentSession()
    {
        if (_currentSession != null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }
            catch { }
            _currentSession = null;
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        _ = UpdateMediaPropertiesAsync(sender);
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        _ = UpdatePlaybackAndTimelineAsync(sender);
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        _ = UpdatePlaybackAndTimelineAsync(sender);
    }

    private async Task UpdateAllPropertiesAsync(GlobalSystemMediaTransportControlsSession session)
    {
        await UpdateMediaPropertiesAsync(session).ConfigureAwait(false);
        await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
    }

    private async Task UpdateMediaPropertiesAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null) return;

            byte[]? thumbnailBytes = null;
            if (props.Thumbnail != null)
            {
                try
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    if (stream != null && stream.Size > 0)
                    {
                        stream.Seek(0);
                        using var netStream = stream.AsStreamForRead();
                        using var ms = new MemoryStream();
                        await netStream.CopyToAsync(ms).ConfigureAwait(false);
                        thumbnailBytes = ms.ToArray();
                    }
                }
                catch
                {
                    try
                    {
                        using var stream = await props.Thumbnail.OpenReadAsync();
                        if (stream != null && stream.Size > 0)
                        {
                            stream.Seek(0);
                            thumbnailBytes = new byte[stream.Size];
                            using var dataReader = new DataReader(stream);
                            await dataReader.LoadAsync((uint)stream.Size);
                            dataReader.ReadBytes(thumbnailBytes);
                        }
                    }
                    catch { }
                }
            }

            var timelineProps = session.GetTimelineProperties();
            var duration = timelineProps?.EndTime ?? TimeSpan.Zero;

            var track = new MediaTrackInfo(
                Title: props.Title ?? string.Empty,
                Artist: props.Artist ?? string.Empty,
                Album: props.AlbumTitle ?? string.Empty,
                Duration: duration,
                ThumbnailData: thumbnailBytes);

            CurrentTrack = track;
            TrackChanged?.Invoke(this, track);
        }
        catch (Exception ex)
        {
            MonitorFaulted?.Invoke(this, ex);
        }
    }

    private Task UpdatePlaybackAndTimelineAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var playbackInfo = session.GetPlaybackInfo();
            var timelineProps = session.GetTimelineProperties();

            var status = playbackInfo?.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackStatus.Stopped,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackStatus.Changing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackStatus.Opened,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackStatus.Closed,
                _ => MediaPlaybackStatus.Unknown
            };

            var pos = timelineProps?.Position ?? TimeSpan.Zero;
            var dur = timelineProps?.EndTime ?? CurrentTrack.Duration;
            if (dur <= TimeSpan.Zero && CurrentTrack.Duration > TimeSpan.Zero)
            {
                dur = CurrentTrack.Duration;
            }

            var lastUpdatedUtc = DateTime.UtcNow;
            if (timelineProps != null && timelineProps.LastUpdatedTime.Year > 2020)
            {
                var reportedUtc = timelineProps.LastUpdatedTime.UtcDateTime;
                var age = DateTime.UtcNow - reportedUtc;
                if (age >= TimeSpan.Zero && age < TimeSpan.FromHours(2))
                {
                    lastUpdatedUtc = reportedUtc;
                }
            }

            var timeline = new MediaTimelineInfo(pos, dur, status, lastUpdatedUtc);
            CurrentTimeline = timeline;
            TimelineChanged?.Invoke(this, timeline);
        }
        catch (Exception ex)
        {
            MonitorFaulted?.Invoke(this, ex);
        }

        return Task.CompletedTask;
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> EnsureActiveSessionAsync()
    {
        if (_currentSession != null) return _currentSession;

        if (_sessionManager == null)
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            }
            catch { }
        }

        if (_sessionManager != null)
        {
            var best = ResolveBestSession(_sessionManager);
            if (best != null)
            {
                await AttachSessionInternalAsync(best).ConfigureAwait(false);
            }
        }

        return _currentSession;
    }

    public async Task<bool> TryPlayAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            bool ok = await session.TryPlayAsync();
            await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TryPauseAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            bool ok = await session.TryPauseAsync();
            await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TryTogglePlayPauseAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            var status = session.GetPlaybackInfo()?.PlaybackStatus;
            bool ok = false;
            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                ok = await session.TryPauseAsync();
                if (!ok) ok = await session.TryTogglePlayPauseAsync();
            }
            else if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused ||
                     status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped)
            {
                ok = await session.TryPlayAsync();
                if (!ok) ok = await session.TryTogglePlayPauseAsync();
            }
            else
            {
                ok = await session.TryTogglePlayPauseAsync();
            }

            await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TrySkipNextAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            bool ok = await session.TrySkipNextAsync();
            _ = Task.Run(async () =>
            {
                await Task.Delay(200).ConfigureAwait(false);
                await UpdateAllPropertiesAsync(session).ConfigureAwait(false);
            });
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TrySkipPreviousAsync()
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            bool ok = await session.TrySkipPreviousAsync();
            _ = Task.Run(async () =>
            {
                await Task.Delay(200).ConfigureAwait(false);
                await UpdateAllPropertiesAsync(session).ConfigureAwait(false);
            });
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> TrySeekAsync(TimeSpan position)
    {
        var session = await EnsureActiveSessionAsync().ConfigureAwait(false);
        if (session == null) return false;

        try
        {
            // Windows GSMTC takes requested playback position in 100-nanosecond units (ticks)
            bool ok = await session.TryChangePlaybackPositionAsync(position.Ticks);
            if (ok)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    await UpdatePlaybackAndTimelineAsync(session).ConfigureAwait(false);
                });
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await StopAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
