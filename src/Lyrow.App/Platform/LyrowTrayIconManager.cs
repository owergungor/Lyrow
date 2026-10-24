using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lyrow.App.Platform;

public enum PreferredAppMode
{
    Default = 0,
    AllowDark = 1,
    ForceDark = 2,
    ForceLight = 3,
    Max = 4
}

/// <summary>
/// Hardware Windows System Tray / Notification Area manager using native Shell_NotifyIcon.
/// Creates a lightweight hidden Win32 message window to receive tray click messages (WM_TRAYICON)
/// and displays a modern, theme-matched Win32 context menu grouped with Now Playing, Playback controls,
/// Windows navigation, Glow, and Application lifecycle commands.
/// Operates completely safely in headless test environments with zero UI thread contention.
/// </summary>
public sealed class LyrowTrayIconManager : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 101;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_COMMAND = 0x0111;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint MF_STRING = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;
    private const uint MF_DISABLED = 0x00000002;
    private const uint MF_CHECKED = 0x00000008;
    private const uint MF_UNCHECKED = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    public const int CMD_TRACK_INFO = 1000;
    public const int CMD_SHOW = 1001;
    public const int CMD_TOGGLE_GLOW = 1002;
    public const int CMD_TOGGLE_LYRICS = 1003;
    public const int CMD_TOGGLE_CLICKTHROUGH = 1004;
    public const int CMD_SETTINGS = 1005;
    public const int CMD_EXIT = 1006;
    public const int CMD_PLAY_PAUSE = 1007;
    public const int CMD_PREV = 1008;
    public const int CMD_NEXT = 1009;
    public const int CMD_MUTE_UNMUTE = 1010;
    public const int CMD_OPEN_PLAYBACK = 1011;
    public const int CMD_OPEN_GLOW = 1012;
    public const int CMD_CHECK_UPDATES = 1013;
    public const int CMD_LYRICS_FULLSCREEN = 1014;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
    private static extern void FlushMenuThemes();

    private bool _isDisposed;
    private bool _isVisible;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private WndProcDelegate? _wndProcDelegate;

    private Action? _onShowMainWindow;
    private Action? _onToggleGlow;
    private Action? _onToggleLyrics;
    private Action? _onToggleClickThrough;
    private Action? _onOpenSettings;
    private Action? _onExit;

    private Action? _onPlayPause;
    private Action? _onPrev;
    private Action? _onNext;
    private Action? _onToggleMute;
    private Action? _onOpenPlayback;
    private Action? _onOpenGlow;
    private Action? _onCheckUpdates;
    private Action? _onToggleLyricsFullscreen;

    private bool _glowOpen;
    private bool _lyricsOpen;
    private bool _clickThroughActive = true;
    private string _nowPlayingText = "Lyrow — Windows 11 Native";
    private bool _isPlaying = false;
    private bool _isMuted = false;
    private PreferredAppMode _currentAppMode = PreferredAppMode.AllowDark;

    public bool IsVisible => _isVisible;
    public string NowPlayingText => _nowPlayingText;
    public bool IsPlaying => _isPlaying;
    public bool IsMuted => _isMuted;
    public bool IsGlowOpen => _glowOpen;
    public bool IsLyricsOpen => _lyricsOpen;
    public bool IsClickThroughActive => _clickThroughActive;
    public PreferredAppMode CurrentAppMode => _currentAppMode;

    public void TriggerShowMainWindow() => _onShowMainWindow?.Invoke();
    public void TriggerToggleGlow() => _onToggleGlow?.Invoke();
    public void TriggerToggleLyrics() => _onToggleLyrics?.Invoke();
    public void TriggerToggleClickThrough() => _onToggleClickThrough?.Invoke();
    public void TriggerOpenSettings() => _onOpenSettings?.Invoke();
    public void TriggerExit() => _onExit?.Invoke();

    public void TriggerPlayPause() => _onPlayPause?.Invoke();
    public void TriggerPrev() => _onPrev?.Invoke();
    public void TriggerNext() => _onNext?.Invoke();
    public void TriggerToggleMute() => _onToggleMute?.Invoke();
    public void TriggerOpenPlayback() => _onOpenPlayback?.Invoke();
    public void TriggerOpenGlow() => _onOpenGlow?.Invoke();
    public void TriggerCheckUpdates() => _onCheckUpdates?.Invoke();
    public void TriggerToggleLyricsFullscreen() => _onToggleLyricsFullscreen?.Invoke();

    public void SetTheme(string? themeName)
    {
        if (string.Equals(themeName, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPreferredAppMode(PreferredAppMode.ForceDark);
        }
        else if (string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPreferredAppMode(PreferredAppMode.ForceLight);
        }
        else
        {
            ApplyPreferredAppMode(PreferredAppMode.AllowDark);
        }
    }

    public void ApplyPreferredAppMode(PreferredAppMode mode)
    {
        _currentAppMode = mode;
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            SetPreferredAppMode((int)mode);
            FlushMenuThemes();
        }
        catch
        {
            // Fallback gracefully on older Windows or constrained runtime
        }
    }

    public void Initialize(
        Action onShowMainWindow,
        Action onToggleGlow,
        Action onToggleLyrics,
        Action onToggleClickThrough,
        Action onOpenSettings,
        Action onExit)
    {
        _onShowMainWindow = onShowMainWindow;
        _onToggleGlow = onToggleGlow;
        _onToggleLyrics = onToggleLyrics;
        _onToggleClickThrough = onToggleClickThrough;
        _onOpenSettings = onOpenSettings;
        _onExit = onExit;

        if (!OperatingSystem.IsWindows())
        {
            _isVisible = true;
            return;
        }

        try
        {
            ApplyPreferredAppMode(PreferredAppMode.AllowDark);
            RegisterHiddenMessageWindow();
            CreateTrayIcon();
            _isVisible = true;
        }
        catch
        {
            _isVisible = false;
        }
    }

    public void AttachExtendedHandlers(
        Action? onPlayPause = null,
        Action? onPrev = null,
        Action? onNext = null,
        Action? onToggleMute = null,
        Action? onOpenPlayback = null,
        Action? onOpenGlow = null,
        Action? onCheckUpdates = null,
        Action? onToggleLyricsFullscreen = null)
    {
        _onPlayPause = onPlayPause;
        _onPrev = onPrev;
        _onNext = onNext;
        _onToggleMute = onToggleMute;
        _onOpenPlayback = onOpenPlayback;
        _onOpenGlow = onOpenGlow;
        _onCheckUpdates = onCheckUpdates;
        _onToggleLyricsFullscreen = onToggleLyricsFullscreen;
    }

    public void UpdateNowPlaying(string? title, string? artist, string? album, bool isPlaying, bool isMuted)
    {
        _isPlaying = isPlaying;
        _isMuted = isMuted;

        if (string.IsNullOrWhiteSpace(title))
        {
            _nowPlayingText = "No Media Playing";
        }
        else
        {
            string shortArtist = string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist;
            _nowPlayingText = $"{title} — {shortArtist}";
        }

        if (_hwnd != IntPtr.Zero)
        {
            try
            {
                var nid = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 101,
                    uFlags = NIF_TIP,
                    szTip = _nowPlayingText.Length > 120 ? _nowPlayingText.Substring(0, 120) : _nowPlayingText
                };
                Shell_NotifyIcon(NIM_MODIFY, ref nid);
            }
            catch { }
        }
    }

    public void UpdateStates(bool glowOpen, bool lyricsOpen, bool clickThroughActive)
    {
        _glowOpen = glowOpen;
        _lyricsOpen = lyricsOpen;
        _clickThroughActive = clickThroughActive;
    }

    private void RegisterHiddenMessageWindow()
    {
        if (_hwnd != IntPtr.Zero) return;

        string className = "LyrowTrayMsgWindow_" + Guid.NewGuid().ToString("N");
        _wndProcDelegate = TrayWndProc;

        var wcx = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = GetModuleHandle(null),
            lpszClassName = className
        };

        RegisterClassEx(ref wcx);
        _hwnd = CreateWindowEx(0, className, "LyrowTrayListener", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, wcx.hInstance, IntPtr.Zero);
    }

    private void CreateTrayIcon()
    {
        if (_hwnd == IntPtr.Zero) return;

        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "lyrow.ico");
        if (File.Exists(iconPath))
        {
            _hIcon = LoadImage(IntPtr.Zero, iconPath, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
        }

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 101,
            uFlags = NIF_MESSAGE | NIF_TIP | (_hIcon != IntPtr.Zero ? NIF_ICON : 0),
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "Lyrow — Ambient Lighting & Synchronized Lyrics"
        };

        Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            int eventId = lParam.ToInt32();
            if (eventId == WM_LBUTTONUP)
            {
                TriggerShowMainWindow();
                return IntPtr.Zero;
            }
            if (eventId == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }
        else if (msg == WM_COMMAND)
        {
            int cmd = wParam.ToInt32() & 0xFFFF;
            switch (cmd)
            {
                case CMD_SHOW:
                    TriggerShowMainWindow();
                    break;
                case CMD_TOGGLE_GLOW:
                    TriggerToggleGlow();
                    break;
                case CMD_TOGGLE_LYRICS:
                    TriggerToggleLyrics();
                    break;
                case CMD_TOGGLE_CLICKTHROUGH:
                    TriggerToggleClickThrough();
                    break;
                case CMD_SETTINGS:
                    TriggerOpenSettings();
                    break;
                case CMD_EXIT:
                    TriggerExit();
                    break;
                case CMD_PLAY_PAUSE:
                    TriggerPlayPause();
                    break;
                case CMD_PREV:
                    TriggerPrev();
                    break;
                case CMD_NEXT:
                    TriggerNext();
                    break;
                case CMD_MUTE_UNMUTE:
                    TriggerToggleMute();
                    break;
                case CMD_OPEN_PLAYBACK:
                    TriggerOpenPlayback();
                    break;
                case CMD_OPEN_GLOW:
                    TriggerOpenGlow();
                    break;
                case CMD_CHECK_UPDATES:
                    TriggerCheckUpdates();
                    break;
                case CMD_LYRICS_FULLSCREEN:
                    TriggerToggleLyricsFullscreen();
                    break;
            }
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (_hwnd == IntPtr.Zero) return;

        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            // 1. Now Playing Section (Header)
            AppendMenu(hMenu, MF_STRING | MF_GRAYED | MF_DISABLED, new IntPtr(CMD_TRACK_INFO), $"♫  {_nowPlayingText}");
            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

            // 2. Playback Transport Section
            string playPauseText = _isPlaying ? "⏸  Pause" : "▶  Play";
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_PLAY_PAUSE), playPauseText);
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_PREV), "⏮  Previous Track");
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_NEXT), "⏭  Next Track");
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_MUTE_UNMUTE), _isMuted ? "🔊  Unmute Volume" : "🔇  Mute Volume");
            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

            // 3. Ambient Glow & Lyrics Features (with active state checks)
            uint glowFlags = MF_STRING | (_glowOpen ? MF_CHECKED : MF_UNCHECKED);
            AppendMenu(hMenu, glowFlags, new IntPtr(CMD_TOGGLE_GLOW), "💡  Ambient Glow");

            uint lyricsFlags = MF_STRING | (_lyricsOpen ? MF_CHECKED : MF_UNCHECKED);
            AppendMenu(hMenu, lyricsFlags, new IntPtr(CMD_TOGGLE_LYRICS), "🎤  Synchronized Lyrics");

            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_LYRICS_FULLSCREEN), "⛶  Lyrics Fullscreen");
            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

            // 4. Windows & Navigation Section
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_SHOW), "🏠  Open Home");
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_OPEN_PLAYBACK), "🎵  Open Playback");
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_OPEN_GLOW), "✨  Open Glow Settings");
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_SETTINGS), "⚙  Settings");
            AppendMenu(hMenu, MF_SEPARATOR, IntPtr.Zero, string.Empty);

            // 5. System & Updates Section
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_CHECK_UPDATES), "🔄  Check for Updates...");
            AppendMenu(hMenu, MF_STRING, new IntPtr(CMD_EXIT), "✕  Exit Lyrow");

            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hwnd);
            TrackPopupMenu(hMenu, TPM_RIGHTBUTTON, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _isVisible = false;

        if (_hwnd != IntPtr.Zero)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 101
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
