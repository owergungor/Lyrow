using System.Runtime.Versioning;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Lyrow.Music.Session;

/// <summary>
/// Hardware Windows GSMTC (Global System Media Transport Controls) media session monitor.
/// Listens to active OS-level media sessions (Spotify, Apple Music, YouTube, Edge, Chrome) without API keys.
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
                AttachCurrentSession(_sessionManager.GetCurrentSession());
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

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        if (!_isMonitoring) return;

        _ = Task.Run(async () =>
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_isMonitoring) return;
                DetachCurrentSession();
                AttachCurrentSession(sender.GetCurrentSession());
            }
            finally
            {
                _lock.Release();
            }
        });
    }

    private void AttachCurrentSession(GlobalSystemMediaTransportControlsSession? session)
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

        _ = UpdateAllPropertiesAsync(_currentSession);
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
                    if (stream.Size > 0)
                    {
                        thumbnailBytes = new byte[stream.Size];
                        using var dataReader = new DataReader(stream);
                        await dataReader.LoadAsync((uint)stream.Size);
                        dataReader.ReadBytes(thumbnailBytes);
                    }
                }
                catch { }
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

            var timeline = new MediaTimelineInfo(pos, dur, status, DateTime.UtcNow);
            CurrentTimeline = timeline;
            TimelineChanged?.Invoke(this, timeline);
        }
        catch (Exception ex)
        {
            MonitorFaulted?.Invoke(this, ex);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        await StopAsync().ConfigureAwait(false);
        _lock.Dispose();
    }
}
