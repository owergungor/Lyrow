using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;

namespace Lyrow.App.Platform;

/// <summary>
/// Native Win32 transparent layered overlay window providing zero-interference Lumn-style
/// 4-edge ambient lighting. Uses WS_EX_TRANSPARENT, WS_EX_NOACTIVATE, WS_EX_LAYERED, and WS_EX_TOOLWINDOW
/// to ensure mouse/keyboard clicks pass straight through without stealing focus, altering Alt+Tab,
/// or interfering with any underlying application.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlowOverlayWindow : IDisposable
{
    private const string WindowClassName = "LyrowGlowOverlayWindow";
    private static bool _classRegistered = false;
    private static readonly object _classLock = new();

    // Win32 Window Styles & Messages
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int WM_ACTIVATE = 0x0006;
    private const int WM_SETFOCUS = 0x0007;
    private const int WM_DESTROY = 0x0002;

    private const int HTTRANSPARENT = -1;
    private const int MA_NOACTIVATE = 3;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint ULW_ALPHA = 0x00000002;

    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _memDc = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _oldBitmap = IntPtr.Zero;
    private IntPtr _pixelBufferPtr = IntPtr.Zero;
    private int _width;
    private int _height;

    private readonly LumnModule? _lumnModule;
    private readonly GlowBeatEngine _beatEngine = new();
    private GlowPalette _currentPalette = GlowPalette.Default;
    private GlowPalette _targetPalette = GlowPalette.Default;
    private float _paletteLerpProgress = 1.0f;

    private System.Threading.Timer? _renderTimer;
    private float _animationPhase = 0f;
    private int _silentFramesCount = 0;
    private bool _isDisposed = false;

    private WndProcDelegate? _wndProcDelegate;

    public IntPtr Handle => _hwnd;
    public bool IsCreated => _hwnd != IntPtr.Zero;
    public GlowBeatEngine BeatEngine => _beatEngine;

    public GlowOverlayWindow(LumnModule? lumnModule = null)
    {
        _lumnModule = lumnModule;
    }

    public void Create(int x, int y, int width, int height)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_hwnd != IntPtr.Zero) return;

        _width = Math.Max(100, width);
        _height = Math.Max(100, height);

        EnsureWindowClassRegistered();

        _wndProcDelegate = CustomWndProc;
        var hInstance = GetModuleHandle(null);

        uint exStyle = WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

        _hwnd = CreateWindowExW(
            exStyle,
            WindowClassName,
            "Lyrow Ambient Edge Overlay",
            WS_POPUP,
            x, y, _width, _height,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            SetupDIBSection(_width, _height);
            SetWindowPos(_hwnd, HWND_TOPMOST, x, y, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

            // Start rendering timer (~60 FPS = 16 ms)
            _renderTimer = new System.Threading.Timer(OnRenderTick, null, 16, 16);
        }
    }

    public void SetPalette(GlowPalette newPalette, bool immediate = false)
    {
        if (immediate)
        {
            _currentPalette = newPalette;
            _targetPalette = newPalette;
            _paletteLerpProgress = 1.0f;
        }
        else
        {
            _targetPalette = newPalette;
            _paletteLerpProgress = 0.0f; // Begin cross-fade
        }
    }

    private void SetupDIBSection(int width, int height)
    {
        var screenDc = GetDC(IntPtr.Zero);
        _memDc = CreateCompatibleDC(screenDc);

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height; // Top-down DIB
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        _hBitmap = CreateDIBSection(_memDc, ref bmi, 0, out _pixelBufferPtr, IntPtr.Zero, 0);
        _oldBitmap = SelectObject(_memDc, _hBitmap);

        ReleaseDC(IntPtr.Zero, screenDc);
    }

    private void OnRenderTick(object? state)
    {
        if (_isDisposed || _hwnd == IntPtr.Zero || _pixelBufferPtr == IntPtr.Zero) return;

        try
        {
            var spectrum = _lumnModule?.LatestSpectrum ?? FrequencySpectrum.Empty;
            bool isSilent = spectrum.Levels.IsSilent || (_lumnModule != null && _lumnModule.State != ModuleState.Running);

            _beatEngine.Process(spectrum, 1.0f, 16.6);

            // Cross-fade palette interpolation (smooth ~1s transition)
            if (_paletteLerpProgress < 1.0f)
            {
                _paletteLerpProgress = Math.Min(1.0f, _paletteLerpProgress + 0.035f);
                _currentPalette = GlowPalette.Lerp(_currentPalette, _targetPalette, _paletteLerpProgress);
            }

            float glow = _beatEngine.OverallGlowIntensity;

            if (isSilent)
            {
                _silentFramesCount++;
                if (_silentFramesCount > 60 && glow < 0.005f)
                {
                    // Render completely clean transparent frame and throttle timer
                    ClearBuffer();
                    UpdateLayeredWindowInternal();
                    _renderTimer?.Change(200, 200); // 5 FPS idle power save
                    return;
                }
            }
            else
            {
                if (_silentFramesCount > 0)
                {
                    _silentFramesCount = 0;
                    _renderTimer?.Change(16, 16); // Resume 60 FPS
                }
            }

            _animationPhase += 0.02f + (glow * 0.05f); // Faster wave flow on high energy

            RenderAmbientEdgeLighting();
            UpdateLayeredWindowInternal();
        }
        catch
        {
            // Safeguard against rendering interruptions during shutdown
        }
    }

    private unsafe void ClearBuffer()
    {
        if (_pixelBufferPtr == IntPtr.Zero) return;
        int totalPixels = _width * _height;
        uint* ptr = (uint*)_pixelBufferPtr;
        NativeMemory.Clear(ptr, (nuint)(totalPixels * 4));
    }

    private unsafe void RenderAmbientEdgeLighting()
    {
        if (_pixelBufferPtr == IntPtr.Zero) return;

        ClearBuffer();

        float glow = _beatEngine.OverallGlowIntensity;
        if (glow < 0.005f) return;

        float transient = _beatEngine.TransientEnergy;
        float bass = _beatEngine.BassIntensity;
        float mid = _beatEngine.MidIntensity;
        float treble = _beatEngine.TrebleIntensity;

        // Dynamic edge thickness: responds to beat spikes and overall volume
        int thickness = (int)Math.Clamp(35 + (glow * 65.0f) + (transient * 30.0f), 25, 150);
        int maxThickness = Math.Min(thickness, Math.Min(_width / 2, _height / 2));

        // Edge intensities
        float alphaTop = Math.Clamp((treble * 0.70f + transient * 0.30f) * glow, 0f, 1f);
        float alphaBottom = Math.Clamp((bass * 0.85f + transient * 0.40f) * glow, 0f, 1f);
        float alphaLeft = Math.Clamp((mid * 0.75f + glow * 0.25f), 0f, 1f);
        float alphaRight = Math.Clamp((mid * 0.75f + glow * 0.25f), 0f, 1f);

        uint* pixels = (uint*)_pixelBufferPtr;
        int stride = _width;

        // Render 4-edge bloom directly into 32-bit ARGB buffer
        // 1. Top Edge
        for (int y = 0; y < maxThickness; y++)
        {
            float normY = 1.0f - ((float)y / maxThickness);
            float falloff = normY * normY; // Quadratic smooth bloom curve
            byte a = (byte)(alphaTop * falloff * 230);
            if (a == 0) continue;

            uint color = PreMultiplyAlpha(_currentPalette.TopColor, a);
            int rowOffset = y * stride;
            for (int x = 0; x < _width; x++)
            {
                pixels[rowOffset + x] = BlendPixel(pixels[rowOffset + x], color);
            }
        }

        // 2. Bottom Edge
        for (int y = 0; y < maxThickness; y++)
        {
            float normY = 1.0f - ((float)y / maxThickness);
            float falloff = normY * normY;
            byte a = (byte)(alphaBottom * falloff * 240);
            if (a == 0) continue;

            uint color = PreMultiplyAlpha(_currentPalette.BottomColor, a);
            int rowOffset = (_height - 1 - y) * stride;
            for (int x = 0; x < _width; x++)
            {
                pixels[rowOffset + x] = BlendPixel(pixels[rowOffset + x], color);
            }
        }

        // 3. Left Edge
        for (int x = 0; x < maxThickness; x++)
        {
            float normX = 1.0f - ((float)x / maxThickness);
            float falloff = normX * normX;
            byte a = (byte)(alphaLeft * falloff * 220);
            if (a == 0) continue;

            uint color = PreMultiplyAlpha(_currentPalette.LeftColor, a);
            for (int y = 0; y < _height; y++)
            {
                int idx = y * stride + x;
                pixels[idx] = BlendPixel(pixels[idx], color);
            }
        }

        // 4. Right Edge
        for (int x = 0; x < maxThickness; x++)
        {
            float normX = 1.0f - ((float)x / maxThickness);
            float falloff = normX * normX;
            byte a = (byte)(alphaRight * falloff * 220);
            if (a == 0) continue;

            uint color = PreMultiplyAlpha(_currentPalette.RightColor, a);
            for (int y = 0; y < _height; y++)
            {
                int idx = y * stride + (_width - 1 - x);
                pixels[idx] = BlendPixel(pixels[idx], color);
            }
        }
    }

    private static uint PreMultiplyAlpha(GlowColor c, byte alpha)
    {
        float aNorm = alpha / 255.0f;
        byte r = (byte)(c.R * aNorm);
        byte g = (byte)(c.G * aNorm);
        byte b = (byte)(c.B * aNorm);
        return ((uint)alpha << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static uint BlendPixel(uint existing, uint incoming)
    {
        byte inA = (byte)(incoming >> 24);
        if (inA == 0) return existing;
        byte exA = (byte)(existing >> 24);
        if (exA == 0) return incoming;

        byte inR = (byte)(incoming >> 16);
        byte inG = (byte)(incoming >> 8);
        byte inB = (byte)incoming;

        byte exR = (byte)(existing >> 16);
        byte exG = (byte)(existing >> 8);
        byte exB = (byte)existing;

        // Additive screen blend
        byte outR = (byte)Math.Min(255, exR + inR);
        byte outG = (byte)Math.Min(255, exG + inG);
        byte outB = (byte)Math.Min(255, exB + inB);
        byte outA = (byte)Math.Min(255, exA + inA);

        return ((uint)outA << 24) | ((uint)outR << 16) | ((uint)outG << 8) | outB;
    }

    private void UpdateLayeredWindowInternal()
    {
        if (_hwnd == IntPtr.Zero || _memDc == IntPtr.Zero) return;

        var ptSrc = new POINT { x = 0, y = 0 };
        var size = new SIZE { cx = _width, cy = _height };
        var blend = new BLENDFUNCTION
        {
            BlendOp = AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA
        };

        UpdateLayeredWindow(_hwnd, IntPtr.Zero, IntPtr.Zero, ref size, _memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
    }

    private static IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                // Mouse clicks and hover pass completely through to windows underneath
                return (IntPtr)HTTRANSPARENT;

            case WM_MOUSEACTIVATE:
                // Window never takes activation when clicked
                return (IntPtr)MA_NOACTIVATE;

            case WM_ACTIVATE:
            case WM_SETFOCUS:
                return IntPtr.Zero;

            case WM_DESTROY:
                return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void EnsureWindowClassRegistered()
    {
        lock (_classLock)
        {
            if (_classRegistered) return;

            var wndClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = CustomWndProc,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = GetModuleHandle(null),
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = WindowClassName,
                hIconSm = IntPtr.Zero
            };

            RegisterClassExW(ref wndClass);
            _classRegistered = true;
        }
    }

    public void CloseOverlay()
    {
        if (_renderTimer != null)
        {
            _renderTimer.Dispose();
            _renderTimer = null;
        }

        if (_hwnd != IntPtr.Zero)
        {
            ShowWindow(_hwnd, 0); // SW_HIDE
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_memDc != IntPtr.Zero)
        {
            if (_oldBitmap != IntPtr.Zero)
            {
                SelectObject(_memDc, _oldBitmap);
                _oldBitmap = IntPtr.Zero;
            }
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }

        if (_hBitmap != IntPtr.Zero)
        {
            DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }

        _pixelBufferPtr = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        CloseOverlay();
    }

    // Win32 Interop structs and functions
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
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize, IntPtr hdcSrc,
        ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
