using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;

namespace Lyrow.App.Platform;

/// <summary>
/// Hardware Windows global hotkey and display change event manager.
/// Uses Win32 RegisterHotKey with a dedicated background native window sink to receive
/// WM_HOTKEY, WM_DISPLAYCHANGE, and WM_SETTINGCHANGE without polling.
/// </summary>
public sealed class WindowsGlobalHotkeyManager : IGlobalHotkeyManager
{
    private const int HotkeyIdLumn = 1;
    private const int HotkeyIdVerci = 2;
    private const int HotkeyIdClickThrough = 3;

    private const uint WM_DESTROY = 0x0002;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly object _lock = new();
    private readonly List<HotkeyStatus> _statuses = new();

    private Thread? _messageLoopThread;
    private IntPtr _hwnd = IntPtr.Zero;
    private WndProcDelegate? _wndProcDelegate;
    private readonly AutoResetEvent _windowCreatedEvent = new(false);
    private bool _isDisposed;

    private Action? _onLumn;
    private Action? _onVerci;
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
            case WM_HOTKEY:
                int hotkeyId = wParam.ToInt32();
                HandleHotkey(hotkeyId);
                return IntPtr.Zero;

            case WM_DISPLAYCHANGE:
            case WM_SETTINGCHANGE:
                Dispatcher.UIThread.Post(() =>
                {
                    DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);
                });
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
            HotkeyIdLumn => _onLumn,
            HotkeyIdVerci => _onVerci,
            HotkeyIdClickThrough => _onClickThrough,
            _ => null
        };

        if (action != null)
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    public bool RegisterHotkeys(
        HotkeySettings settings,
        Action onLumnToggle,
        Action onVerciToggle,
        Action onClickThroughToggle)
    {
        lock (_lock)
        {
            UnregisterAll();

            _onLumn = onLumnToggle;
            _onVerci = onVerciToggle;
            _onClickThrough = onClickThroughToggle;

            if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero)
            {
                return false;
            }

            bool allSuccess = true;

            allSuccess &= TryRegisterSingleHotkey(HotkeyIdLumn, "Lumn", settings.ToggleLumn);
            allSuccess &= TryRegisterSingleHotkey(HotkeyIdVerci, "Verci", settings.ToggleVerci);
            allSuccess &= TryRegisterSingleHotkey(HotkeyIdClickThrough, "ClickThrough", settings.ToggleClickThrough);

            return allSuccess;
        }
    }

    [SupportedOSPlatform("windows")]
    private bool TryRegisterSingleHotkey(int id, string actionName, string gesture)
    {
        if (!ParseGesture(gesture, out uint modifiers, out uint vk))
        {
            _statuses.Add(new HotkeyStatus(actionName, gesture, false, "Geçersiz tuş kombinasyonu"));
            return false;
        }

        bool ok = RegisterHotKey(_hwnd, id, modifiers | MOD_NOREPEAT, vk);
        if (ok)
        {
            _statuses.Add(new HotkeyStatus(actionName, gesture, true));
            return true;
        }

        int errorCode = Marshal.GetLastWin32Error();
        string message = errorCode == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
            ? "Kısayol tuşu başka bir uygulama tarafından kullanılıyor."
            : $"Win32 Kayıt hatası: {errorCode}";

        _statuses.Add(new HotkeyStatus(actionName, gesture, false, message));
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
                UnregisterHotKey(_hwnd, HotkeyIdLumn);
                UnregisterHotKey(_hwnd, HotkeyIdVerci);
                UnregisterHotKey(_hwnd, HotkeyIdClickThrough);
            }

            _statuses.Clear();
            _onLumn = null;
            _onVerci = null;
            _onClickThrough = null;
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
