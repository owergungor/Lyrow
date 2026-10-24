using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Services;

namespace Lyrow.App.Platform;

/// <summary>
/// Hardware Windows global hotkey and display change event manager.
/// Uses Win32 RegisterHotKey inside a dedicated background native message-loop thread
/// to eliminate Win32 error 1408 (ERROR_WINDOW_OF_OTHER_THREAD) and receive
/// WM_HOTKEY, WM_DISPLAYCHANGE, and WM_SETTINGCHANGE without polling.
/// </summary>
public sealed class WindowsGlobalHotkeyManager : IGlobalHotkeyManager
{
    private const int HotkeyIdGlow = 1;
    private const int HotkeyIdLyrics = 2;
    private const int HotkeyIdClickThrough = 3;

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_APP_DISPATCH = 0x8001;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly object _lock = new();
    private readonly List<HotkeyStatus> _statuses = new();
    private readonly ConcurrentQueue<Action> _dispatchQueue = new();

    private Thread? _messageLoopThread;
    private IntPtr _hwnd = IntPtr.Zero;
    private WndProcDelegate? _wndProcDelegate;
    private readonly AutoResetEvent _windowCreatedEvent = new(false);
    private bool _isDisposed;

    private Action? _onGlow;
    private Action? _onLyrics;
    private Action? _onClickThrough;

    public bool IsSupported => OperatingSystem.IsWindows();

    public event EventHandler? DisplayConfigurationChanged;

    public WindowsGlobalHotkeyManager()
    {
        if (OperatingSystem.IsWindows())
        {
            StartMessageLoopThread();
        }
    }

    [SupportedOSPlatform("windows")]
    private void StartMessageLoopThread()
    {
        _messageLoopThread = new Thread(MessageLoopRun)
        {
            Name = "LyrowNativeHookSinkThread",
            IsBackground = true
        };
        _messageLoopThread.SetApartmentState(ApartmentState.STA);
        _messageLoopThread.Start();
        _windowCreatedEvent.WaitOne(TimeSpan.FromSeconds(3));
    }

    [SupportedOSPlatform("windows")]
    private void MessageLoopRun()
    {
        _wndProcDelegate = CustomWndProc;
        string className = "LyrowNativeSink_" + Guid.NewGuid().ToString("N");

        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            lpszClassName = className
        };

        RegisterClassEx(ref wndClass);

        _hwnd = CreateWindowEx(
            0,
            className,
            "LyrowNativeHookSink",
            0x80000000, // WS_POPUP
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            wndClass.hInstance,
            IntPtr.Zero);

        _windowCreatedEvent.Set();

        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        UnregisterClass(className, wndClass.hInstance);
    }

    [SupportedOSPlatform("windows")]
    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_APP_DISPATCH:
                while (_dispatchQueue.TryDequeue(out var action))
                {
                    try
                    {
                        action();
                    }
                    catch
                    {
                        // Prevent callback exception from killing native pump
                    }
                }
                return IntPtr.Zero;

            case WM_HOTKEY:
                int hotkeyId = wParam.ToInt32();
                HandleHotkey(hotkeyId);
                return IntPtr.Zero;

            case WM_DISPLAYCHANGE:
            case WM_SETTINGCHANGE:
                Task.Run(() => DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty));
                return IntPtr.Zero;

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void HandleHotkey(int id)
    {
        Action? action = id switch
        {
            HotkeyIdGlow => _onGlow,
            HotkeyIdLyrics => _onLyrics,
            HotkeyIdClickThrough => _onClickThrough,
            _ => null
        };

        if (action != null)
        {
            Task.Run(action);
        }
    }

    /// <summary>
    /// Executes a delegate synchronously on the dedicated message-loop thread.
    /// This guarantees calling RegisterHotKey/UnregisterHotKey from the exact thread that owns _hwnd.
    /// </summary>
    private bool RunOnMessageLoopThread(Action action, int timeoutMs = 2000)
    {
        if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero) return false;

        using var completionEvent = new ManualResetEventSlim(false);
        _dispatchQueue.Enqueue(() =>
        {
            try
            {
                action();
            }
            finally
            {
                completionEvent.Set();
            }
        });

        PostMessage(_hwnd, WM_APP_DISPATCH, IntPtr.Zero, IntPtr.Zero);
        return completionEvent.Wait(timeoutMs);
    }

    public bool RegisterHotkeys(
        HotkeySettings settings,
        Action onGlowToggle,
        Action onLyricsToggle,
        Action onClickThroughToggle)
    {
        lock (_lock)
        {
            _onGlow = onGlowToggle;
            _onLyrics = onLyricsToggle;
            _onClickThrough = onClickThroughToggle;

            if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero)
            {
                _statuses.Clear();
                _statuses.Add(new HotkeyStatus("Glow", settings.ToggleGlow, false, "Windows platformu gereklidir", HotkeyRegistrationState.Failed));
                return false;
            }

            // 1. Check for local collisions between configured shortcuts
            if (!HotkeyValidator.CheckInternalCollisions(settings, out var collisionError))
            {
                _statuses.Clear();
                bool glowCol = HotkeyValidator.AreColliding(settings.ToggleGlow, settings.ToggleLyrics) || HotkeyValidator.AreColliding(settings.ToggleGlow, settings.ToggleClickThrough);
                bool lyricsCol = HotkeyValidator.AreColliding(settings.ToggleLyrics, settings.ToggleGlow) || HotkeyValidator.AreColliding(settings.ToggleLyrics, settings.ToggleClickThrough);
                bool clickCol = HotkeyValidator.AreColliding(settings.ToggleClickThrough, settings.ToggleGlow) || HotkeyValidator.AreColliding(settings.ToggleClickThrough, settings.ToggleLyrics);

                _statuses.Add(new HotkeyStatus("Glow", settings.ToggleGlow, false, glowCol ? collisionError : null, glowCol ? HotkeyRegistrationState.Conflict : (string.IsNullOrWhiteSpace(settings.ToggleGlow) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered)));
                _statuses.Add(new HotkeyStatus("Lyrics", settings.ToggleLyrics, false, lyricsCol ? collisionError : null, lyricsCol ? HotkeyRegistrationState.Conflict : (string.IsNullOrWhiteSpace(settings.ToggleLyrics) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered)));
                _statuses.Add(new HotkeyStatus("ClickThrough", settings.ToggleClickThrough, false, clickCol ? collisionError : null, clickCol ? HotkeyRegistrationState.Conflict : (string.IsNullOrWhiteSpace(settings.ToggleClickThrough) ? HotkeyRegistrationState.NotRegistered : HotkeyRegistrationState.Registered)));
                return false;
            }

            bool allSuccess = false;

            // Execute registration strictly on the message loop thread that owns _hwnd
            RunOnMessageLoopThread(() =>
            {
                UnregisterHotkeysInternal();
                _statuses.Clear();

                bool ok1 = TryRegisterSingleHotkeyInternal(HotkeyIdGlow, "Glow", settings.ToggleGlow);
                bool ok2 = TryRegisterSingleHotkeyInternal(HotkeyIdLyrics, "Lyrics", settings.ToggleLyrics);
                bool ok3 = TryRegisterSingleHotkeyInternal(HotkeyIdClickThrough, "ClickThrough", settings.ToggleClickThrough);

                allSuccess = ok1 && ok2 && ok3;
            });

            return allSuccess;
        }
    }

    [SupportedOSPlatform("windows")]
    private bool TryRegisterSingleHotkeyInternal(int id, string actionName, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            _statuses.Add(new HotkeyStatus(
                actionName,
                string.Empty,
                false,
                ErrorMessage: null,
                HotkeyRegistrationState.NotRegistered));
            return true;
        }

        if (!ParseGesture(gesture, out uint modifiers, out uint vk))
        {
            _statuses.Add(new HotkeyStatus(
                actionName,
                gesture,
                false,
                "Geçersiz tuş kombinasyonu",
                HotkeyRegistrationState.Failed));
            return false;
        }

        bool ok = RegisterHotKey(_hwnd, id, modifiers | MOD_NOREPEAT, vk);
        if (ok)
        {
            _statuses.Add(new HotkeyStatus(
                actionName,
                gesture,
                true,
                ErrorMessage: null,
                HotkeyRegistrationState.Registered));
            return true;
        }

        int errorCode = Marshal.GetLastWin32Error();
        string message = errorCode == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
            ? $"{gesture} kullanılamıyor. Bu kısayol Windows veya başka bir uygulama tarafından kullanılıyor."
            : $"{gesture} kısayolu kaydedilemedi (Win32 hata kodu: {errorCode}).";

        _statuses.Add(new HotkeyStatus(
            actionName,
            gesture,
            false,
            message,
            HotkeyRegistrationState.Conflict));

        return false;
    }

    private static bool ParseGesture(string gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        if (string.IsNullOrWhiteSpace(gesture)) return false;

        var tokens = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        string keyToken = tokens[^1].ToUpperInvariant();

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            string mod = tokens[i].ToLowerInvariant();
            if (mod is "ctrl" or "control") modifiers |= MOD_CONTROL;
            else if (mod is "shift") modifiers |= MOD_SHIFT;
            else if (mod is "alt") modifiers |= MOD_ALT;
            else if (mod is "win" or "windows") modifiers |= MOD_WIN;
        }

        if (keyToken.Length == 1 && keyToken[0] >= 'A' && keyToken[0] <= 'Z')
        {
            vk = (uint)keyToken[0];
            return true;
        }

        if (keyToken.Length == 1 && keyToken[0] >= '0' && keyToken[0] <= '9')
        {
            vk = (uint)keyToken[0];
            return true;
        }

        if (keyToken.StartsWith('F') && int.TryParse(keyToken[1..], out int fIndex) && fIndex >= 1 && fIndex <= 12)
        {
            vk = (uint)(0x70 + (fIndex - 1)); // VK_F1 = 0x70
            return true;
        }

        return false;
    }

    public void UnregisterAll()
    {
        lock (_lock)
        {
            if (OperatingSystem.IsWindows() && _hwnd != IntPtr.Zero)
            {
                RunOnMessageLoopThread(UnregisterHotkeysInternal);
            }

            _statuses.Clear();
            _onGlow = null;
            _onLyrics = null;
            _onClickThrough = null;
        }
    }

    [SupportedOSPlatform("windows")]
    private void UnregisterHotkeysInternal()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HotkeyIdGlow);
            UnregisterHotKey(_hwnd, HotkeyIdLyrics);
            UnregisterHotKey(_hwnd, HotkeyIdClickThrough);
        }
    }

    public IReadOnlyList<HotkeyStatus> GetStatus()
    {
        lock (_lock)
        {
            return _statuses.ToList().AsReadOnly();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        UnregisterAll();

        if (OperatingSystem.IsWindows() && _hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            _messageLoopThread?.Join(TimeSpan.FromSeconds(2));
        }

        _windowCreatedEvent.Dispose();
    }

    #region Win32 P/Invoke Definitions

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    #endregion
}
