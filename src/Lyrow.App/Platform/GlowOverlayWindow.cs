using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lyrow.Audio.Analysis;
using Lyrow.Core.Contracts;
using Lyrow.Core.Models;
using Lyrow.Core.Modules;

namespace Lyrow.App.Platform;

/// <summary>
/// Native Win32 transparent layered overlay window providing zero-interference ambient
/// 4-edge ambient lighting. Uses WS_EX_TRANSPARENT, WS_EX_NOACTIVATE, WS_EX_LAYERED, and WS_EX_TOOLWINDOW
/// on a dedicated Win32 render/message thread so mouse/keyboard/scroll events pass straight through
/// without stealing focus, altering Alt+Tab, or blocking UI scrolling.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlowOverlayWindow : IDisposable
{
    private const string WindowClassName = "LyrowGlowOverlayWindow_v2";
    private static bool _classRegistered = false;
    private static readonly object _classLock = new();
    private static readonly WndProcDelegate SharedWndProc = CustomWndProc;

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
    private const uint PM_REMOVE = 0x0001;

    private const int HTTRANSPARENT = -1;
    private const int MA_NOACTIVATE = 3;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOSENDCHANGING = 0x0400;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint ULW_ALPHA = 0x00000002;

    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _memDc = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _oldBitmap = IntPtr.Zero;
    private IntPtr _pixelBufferPtr = IntPtr.Zero;
    private int _x;
    private int _y;
    private int _width;
    private int _height;

    private readonly GlowModule? _glowModule;
    private readonly ISettingsManager? _settingsManager;
    private readonly GlowBeatEngine _beatEngine = new();
    private readonly object _paletteLock = new();

    private GlowPalette _sourcePalette = GlowPalette.Default;
    private GlowPalette _currentPalette = GlowPalette.Default;
    private GlowPalette _targetPalette = GlowPalette.Default;
    private float _paletteLerpProgress = 1.0f;
    private volatile bool _isPaused = false;
    private float _fadeMultiplier = 1.0f;
    private float _targetFadeMultiplier = 1.0f;

    private Thread? _renderThread;
    private readonly ManualResetEventSlim _createdEvent = new(false);
    private volatile bool _stopRequested = false;
    private float _animationPhase = 0f;
    private bool _isDisposed = false;

    public IntPtr Handle => _hwnd;
    public bool IsCreated => _hwnd != IntPtr.Zero;
    public GlowBeatEngine BeatEngine => _beatEngine;
    public GlowPalette CurrentPalette => _currentPalette;
    public bool IsPaused => _isPaused;
    public float FadeMultiplier => _fadeMultiplier;

    public void SetPlaybackState(MediaPlaybackStatus status)
    {
        _isPaused = (status == MediaPlaybackStatus.Paused);
    }

    public void SetFadeTarget(float target)
    {
        _targetFadeMultiplier = Math.Clamp(target, 0.0f, 1.0f);
    }

    public float LastRenderedTopAlpha { get; private set; }
    public float LastRenderedBottomAlpha { get; private set; }
    public float LastRenderedLeftAlpha { get; private set; }
    public float LastRenderedRightAlpha { get; private set; }
    public long RenderedFrameCount { get; private set; }

    public GlowOverlayWindow(GlowModule? glowModule = null, ISettingsManager? settingsManager = null)
    {
        _glowModule = glowModule;
        _settingsManager = settingsManager;
    }

    public void Create(int x, int y, int width, int height)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_hwnd != IntPtr.Zero || _renderThread != null) return;

        _x = x;
        _y = y;
        _width = Math.Max(100, width);
        _height = Math.Max(100, height);
        _stopRequested = false;
        _createdEvent.Reset();

        _renderThread = new Thread(RenderThreadMain)
        {
            IsBackground = true,
            Name = "LyrowGlowOverlayRenderThread",
            Priority = ThreadPriority.AboveNormal
        };
        _renderThread.SetApartmentState(ApartmentState.STA);
        _renderThread.Start();

        _createdEvent.Wait(TimeSpan.FromSeconds(2));
    }

    public void SetPalette(GlowPalette newPalette, bool immediate = false)
    {
        lock (_paletteLock)
        {
            if (immediate)
            {
                _sourcePalette = newPalette;
                _currentPalette = newPalette;
                _targetPalette = newPalette;
                _paletteLerpProgress = 1.0f;
            }
            else
            {
                _sourcePalette = _currentPalette;
                _targetPalette = newPalette;
                _paletteLerpProgress = 0.0f; // Begin smooth cross-fade
            }
        }
    }

    private void RenderThreadMain()
    {
        try
        {
            EnsureWindowClassRegistered();

            var hInstance = GetModuleHandle(null);
            uint exStyle = WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

            _hwnd = CreateWindowExW(
                exStyle,
                WindowClassName,
                "Lyrow Ambient Edge Overlay",
                WS_POPUP,
                _x, _y, _width, _height,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                _createdEvent.Set();
                return;
            }

            SetupDIBSection(_width, _height);
            ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
            SetWindowPos(_hwnd, HWND_TOPMOST, _x, _y, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOSENDCHANGING);

            // Render initial ambient frame immediately so overlay is visible right away
            RenderStep();

            _createdEvent.Set();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int topMostCounter = 0;

            while (!_stopRequested && !_isDisposed && _hwnd != IntPtr.Zero)
            {
                long frameStartMs = stopwatch.ElapsedMilliseconds;

                // 1. Pump pending Win32 messages for this overlay window
                while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }

                // 2. Render 60 FPS ambient lighting frame
                RenderStep();

                // 3. Periodically reinforce topmost non-activating z-order (~every 2 seconds)
                topMostCounter++;
                if (topMostCounter >= 120)
                {
                    topMostCounter = 0;
                    SetWindowPos(_hwnd, HWND_TOPMOST, _x, _y, _width, _height, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOSENDCHANGING);
                }

                long elapsedMs = stopwatch.ElapsedMilliseconds - frameStartMs;
                int sleepMs = (int)Math.Max(1, 16 - elapsedMs);
                Thread.Sleep(sleepMs);
            }
        }
        catch
        {
            _createdEvent.Set();
        }
        finally
        {
            CleanupNativeResourcesOnThread();
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

    public void RenderStep()
    {
        if (_isDisposed || _pixelBufferPtr == IntPtr.Zero) return;

        try
        {
            var spectrum = _glowModule?.LatestSpectrum ?? FrequencySpectrum.Empty;
            float sensitivity = _settingsManager?.Current.Glow.Sensitivity ?? 1.0f;

            if (_isPaused)
            {
                // When paused, freeze beat dynamic animation and process silence to allow smooth decay
                _beatEngine.Process(FrequencySpectrum.Empty, sensitivity, 16.6);
            }
            else
            {
                _beatEngine.Process(spectrum, sensitivity, 16.6);
                float glow = _beatEngine.OverallGlowIntensity;
                _animationPhase += 0.025f + (glow * 0.065f);
                if (_animationPhase > 628.3185f) _animationPhase -= 628.3185f;
            }

            // Cross-fade palette interpolation (smooth ~750ms ease transition)
            GlowPalette activePalette;
            lock (_paletteLock)
            {
                if (_paletteLerpProgress < 1.0f)
                {
                    _paletteLerpProgress = Math.Min(1.0f, _paletteLerpProgress + 0.022f);
                    float smoothT = _paletteLerpProgress * _paletteLerpProgress * (3.0f - 2.0f * _paletteLerpProgress);
                    _currentPalette = GlowPalette.Lerp(_sourcePalette, _targetPalette, smoothT);
                }
                activePalette = _currentPalette;
            }

            // Smooth frame-rate independent fade multiplier interpolation (~600-900ms)
            if (Math.Abs(_targetFadeMultiplier - _fadeMultiplier) > 0.001f)
            {
                float step = _targetFadeMultiplier > _fadeMultiplier ? 0.05f : 0.035f;
                _fadeMultiplier = Math.Clamp(_fadeMultiplier + (_targetFadeMultiplier - _fadeMultiplier) * step, 0.0f, 1.0f);
            }

            RenderAmbientEdgeLighting(activePalette);
            UpdateLayeredWindowInternal();
            RenderedFrameCount++;
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

    private unsafe void RenderAmbientEdgeLighting(GlowPalette palette)
    {
        if (_pixelBufferPtr == IntPtr.Zero) return;

        ClearBuffer();

        int userIntensityPct = _settingsManager?.Current.GlowIntensity ?? 80;
        int userThicknessPx = _settingsManager?.Current.GlowThickness ?? 48;

        float effectiveScale = Math.Clamp((userIntensityPct / 100.0f) * _fadeMultiplier, 0.0f, 1.0f);
        if (effectiveScale < 0.005f)
        {
            LastRenderedTopAlpha = 0f;
            LastRenderedBottomAlpha = 0f;
            LastRenderedLeftAlpha = 0f;
            LastRenderedRightAlpha = 0f;
            return;
        }

        float overall = _beatEngine.OverallGlowIntensity;
        float transient = _beatEngine.TransientEnergy;
        float topEnergy = _beatEngine.TopEdgeIntensity;
        float bottomEnergy = _beatEngine.BottomEdgeIntensity;
        float leftEnergy = _beatEngine.LeftEdgeIntensity;
        float rightEnergy = _beatEngine.RightEdgeIntensity;

        // Subtle organic breathing baseline so Glow is immediately visible when enabled,
        // surging dramatically with musical energy and beat transients (Attack -> Hold -> Decay)
        float breathTop = 0.24f + 0.05f * MathF.Sin(_animationPhase);
        float breathBottom = 0.28f + 0.06f * MathF.Sin(_animationPhase + 1.2f);
        float breathLeft = 0.25f + 0.05f * MathF.Sin(_animationPhase + 2.4f);
        float breathRight = 0.25f + 0.05f * MathF.Sin(_animationPhase + 3.6f);

        float alphaTop = Math.Clamp((breathTop + topEnergy * 0.72f + transient * 0.32f) * effectiveScale, 0.0f, 1.0f);
        float alphaBottom = Math.Clamp((breathBottom + bottomEnergy * 0.78f + transient * 0.38f) * effectiveScale, 0.0f, 1.0f);
        float alphaLeft = Math.Clamp((breathLeft + leftEnergy * 0.74f + transient * 0.28f) * effectiveScale, 0.0f, 1.0f);
        float alphaRight = Math.Clamp((breathRight + rightEnergy * 0.74f + transient * 0.28f) * effectiveScale, 0.0f, 1.0f);

        LastRenderedTopAlpha = alphaTop;
        LastRenderedBottomAlpha = alphaBottom;
        LastRenderedLeftAlpha = alphaLeft;
        LastRenderedRightAlpha = alphaRight;

        // Dynamic edge thickness: base user setting (0 - 256 px) + subtle dynamic pulse on musical beats
        int baseThickness = Math.Clamp(userThicknessPx, 0, 256);
        if (baseThickness == 0)
        {
            LastRenderedTopAlpha = 0f;
            LastRenderedBottomAlpha = 0f;
            LastRenderedLeftAlpha = 0f;
            LastRenderedRightAlpha = 0f;
            return;
        }

        int dynamicBoost = (int)(overall * 32.0f + transient * 24.0f);
        int thickness = Math.Clamp(baseThickness + dynamicBoost, 0, 256);
        int maxThickness = Math.Min(thickness, Math.Min(_width / 2, _height / 2));

        uint* pixels = (uint*)_pixelBufferPtr;
        int stride = _width;

        // Precompute blended corner colors for ambient flowing gradients along each edge
        var topStart = GlowColor.Lerp(palette.LeftColor, palette.TopColor, 0.65f);
        var topEnd = GlowColor.Lerp(palette.RightColor, palette.TopColor, 0.65f);
        var bottomStart = GlowColor.Lerp(palette.LeftColor, palette.BottomColor, 0.65f);
        var bottomEnd = GlowColor.Lerp(palette.RightColor, palette.BottomColor, 0.65f);

        // 1. Top Edge (Treble + Mid + Transients)
        for (int y = 0; y < maxThickness; y++)
        {
            float normY = 1.0f - ((float)y / maxThickness);
            float falloff = normY * normY * (1.25f - 0.25f * normY); // Smooth soft bloom curve
            byte baseA = (byte)Math.Clamp((int)(alphaTop * falloff * 245.0f), 0, 255);
            if (baseA == 0) continue;

            int rowOffset = y * stride;
            // Render in horizontal segments for smooth color gradient without per-pixel Lerp overhead
            int segWidth = Math.Max(16, _width / 24);
            for (int xStart = 0; xStart < _width; xStart += segWidth)
            {
                int xEnd = Math.Min(_width, xStart + segWidth);
                float tX = (float)xStart / _width;
                float wave = 0.5f + 0.5f * MathF.Sin(_animationPhase * 0.7f + tX * 3.14159f);
                var edgeColor = GlowColor.Lerp(topStart, topEnd, wave);
                uint packed = PreMultiplyAlpha(edgeColor, baseA);

                for (int x = xStart; x < xEnd; x++)
                {
                    pixels[rowOffset + x] = BlendPixel(pixels[rowOffset + x], packed);
                }
            }
        }

        // 2. Bottom Edge (Bass + Sub-Bass + Transients)
        for (int y = 0; y < maxThickness; y++)
        {
            float normY = 1.0f - ((float)y / maxThickness);
            float falloff = normY * normY * (1.25f - 0.25f * normY);
            byte baseA = (byte)Math.Clamp((int)(alphaBottom * falloff * 252.0f), 0, 255);
            if (baseA == 0) continue;

            int rowOffset = (_height - 1 - y) * stride;
            int segWidth = Math.Max(16, _width / 24);
            for (int xStart = 0; xStart < _width; xStart += segWidth)
            {
                int xEnd = Math.Min(_width, xStart + segWidth);
                float tX = (float)xStart / _width;
                float wave = 0.5f + 0.5f * MathF.Cos(_animationPhase * 0.8f + tX * 3.14159f);
                var edgeColor = GlowColor.Lerp(bottomStart, bottomEnd, wave);
                uint packed = PreMultiplyAlpha(edgeColor, baseA);

                for (int x = xStart; x < xEnd; x++)
                {
                    pixels[rowOffset + x] = BlendPixel(pixels[rowOffset + x], packed);
                }
            }
        }

        // 3. Left Edge (Mid + Bass Harmonics)
        uint[] leftColors = new uint[maxThickness];
        for (int x = 0; x < maxThickness; x++)
        {
            float normX = 1.0f - ((float)x / maxThickness);
            float falloff = normX * normX * (1.25f - 0.25f * normX);
            byte a = (byte)Math.Clamp((int)(alphaLeft * falloff * 240.0f), 0, 255);
            leftColors[x] = a > 0 ? PreMultiplyAlpha(palette.LeftColor, a) : 0u;
        }

        for (int y = 0; y < _height; y++)
        {
            int rowOffset = y * stride;
            for (int x = 0; x < maxThickness; x++)
            {
                uint c = leftColors[x];
                if (c != 0u)
                {
                    pixels[rowOffset + x] = BlendPixel(pixels[rowOffset + x], c);
                }
            }
        }

        // 4. Right Edge (Mid + Treble Harmonics)
        uint[] rightColors = new uint[maxThickness];
        for (int x = 0; x < maxThickness; x++)
        {
            float normX = 1.0f - ((float)x / maxThickness);
            float falloff = normX * normX * (1.25f - 0.25f * normX);
            byte a = (byte)Math.Clamp((int)(alphaRight * falloff * 240.0f), 0, 255);
            rightColors[x] = a > 0 ? PreMultiplyAlpha(palette.RightColor, a) : 0u;
        }

        for (int y = 0; y < _height; y++)
        {
            int rowOffset = y * stride;
            for (int x = 0; x < maxThickness; x++)
            {
                uint c = rightColors[x];
                if (c != 0u)
                {
                    int idx = rowOffset + (_width - 1 - x);
                    pixels[idx] = BlendPixel(pixels[idx], c);
                }
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

        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            var ptDst = new POINT { x = _x, y = _y };
            var ptSrc = new POINT { x = 0, y = 0 };
            var size = new SIZE { cx = _width, cy = _height };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            UpdateLayeredWindow(_hwnd, screenDc, ref ptDst, ref size, _memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (screenDc != IntPtr.Zero)
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    private static IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                // Mouse clicks, wheel scrolling, and hover pass completely through to windows underneath
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
                lpfnWndProc = SharedWndProc,
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

    private void CleanupNativeResourcesOnThread()
    {
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

    public void CloseOverlay()
    {
        _stopRequested = true;
        var thread = _renderThread;
        _renderThread = null;

        if (thread != null && thread.IsAlive && Thread.CurrentThread != thread)
        {
            thread.Join(TimeSpan.FromMilliseconds(600));
        }

        CleanupNativeResourcesOnThread();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        CloseOverlay();
        _createdEvent.Dispose();
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
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
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

    [DllImport("user32.dll")]
    private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc,
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
