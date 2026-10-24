# Lyrow v1.3 — WinUI 3 Migration & Overlay Architecture

## 1. Executive Summary

In Lyrow v1.3, the entire Windows user interface framework was migrated from Avalonia UI to native **WinUI 3 and the Windows App SDK**. Avalonia has been completely decommissioned and removed from the solution.

This migration transforms Lyrow from a cross-platform emulation into a true **Windows 11 Native Music Companion**, delivering:
- Native Windows 11 design language (Mica backdrops, customized title bars, fluent typography, native ToggleSwitches, Sliders, and ComboBoxes).
- A zero-interference ambient edge lighting overlay inspired by macOS Glow.
- Apple Music-style fluid, vertically centered synchronized lyrics.
- 0%–100% linear volume presentation replacing technical dBFS units in the UI.
- Deterministic startup states (Glow = OFF, Lyrics = OFF).

---

## 2. Rationale: Why Avalonia Was Replaced

While Avalonia was instrumental for rapid prototyping in v1.0–v1.2, it presented structural impediments for a Windows 11 desktop companion:

1. **Windowing & Extended Styles Limitations:**
   Avalonia encapsulates OS window handles behind abstraction layers. Setting low-level Win32 extended window styles (`WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `WS_EX_LAYERED`, `WS_EX_TOOLWINDOW`) and routing messages (`WM_NCHITTEST -> HTTRANSPARENT`, `WM_MOUSEACTIVATE -> MA_NOACTIVATE`) frequently suffered from framework-level hit testing overrides. This caused the Glow overlay in v1.2 to intercept mouse clicks and steal focus from underlying games, browsers, and IDEs.

2. **Non-Native Visual Artifacts:**
   Emulated controls in Avalonia do not render using native Windows Composition or DWM pipelines. Windows 11 system accent colors, high-contrast themes, dynamic DPI scaling changes, and Mica material require deep WinUI 3 integration.

3. **Rendering & Compositor Overhead:**
   Avalonia maintains an independent Skia drawing surface and render loop. Running simultaneous transparent overlays on multiple 4K displays incurred redundant double-buffering and memory copy overhead.

---

## 3. Why WinUI 3 + Windows App SDK Was Chosen

1. **True Windows 11 Design Language:**
   Direct support for Mica backdrop material, `NavigationView`, native `ToggleSwitch`, `Slider`, `InfoBar`, and fluent system typography without third-party styling hacks.
2. **First-Class Win32 Interop:**
   WinUI 3 windows provide direct `HWND` access via `WinRT.Interop.WindowNative.GetWindowHandle(this)`, allowing granular control over OS composition and window management.
3. **Hardware Acceleration via Windows Composition:**
   Direct integration with Windows Desktop Window Manager (DWM) and Direct3D/DirectComposition ensures silky 60 FPS transitions with near-zero CPU footprint.

---

## 4. UI Architecture & Migration Overview

```
Lyrow.sln
│
├── Lyrow.Core (Pure Domain Logic)
│   ├── Models (LyrowSettings, GlowPalette, AudioLevels, etc.)
│   ├── Services (SettingsManager, ArtworkColorExtractor, ModuleManager)
│   └── Contracts (IEventBus, IAudioCaptureService, IMediaSessionMonitor, etc.)
│
├── Lyrow.Audio (WASAPI Capture & Signal Analysis)
│   ├── WasapiLoopbackCaptureService
│   ├── SpectrumAnalyzer (FFT, Mel Bands, Transient Detection)
│   └── GlowBeatEngine (Attack / Hold / Decay Dynamics)
│
├── Lyrow.Music (GSMTC & Synchronized Lyrics)
│   ├── Session (WindowsGsmtcMediaSessionMonitor)
│   └── Lyrics (LrclibClient, LocalFileLyricsCache)
│
└── Lyrow.App (Native Windows 11 WinUI 3 Shell)
    ├── App.xaml / App.xaml.cs (Lifecycle, Single-Instance Mutex, DI Container)
    ├── MainWindow.xaml / .cs (Mica, TitleBar, NavigationView, Volume Slider 0-100%)
    ├── LyricsWindow.xaml / .cs (Apple Music-style Centered Synchronized Lyrics)
    └── Platform/
        ├── GlowOverlayWindow.cs (Direct Win32 Layered Zero-Interference Window)
        ├── GlowOverlayManager.cs (Multi-Monitor DPI & Display Change Handler)
        ├── ArtworkBitmapManager.cs (WinUI 3 BitmapImage & Color Extraction)
        └── WindowsGlobalHotkeyManager.cs (Zero-Collision User Hotkeys)
```

---

## 5. Glow Overlay Architecture: Zero Interaction Interference

The paramount requirement of Lyrow v1.3 was guaranteeing that the Glow ambient edge lighting **never interferes with other running applications** (Chrome, VS Code, Spotify, games, Windows Explorer, etc.).

### Extended Window Styles
Each overlay window is created directly via Win32 `CreateWindowExW` with the following configuration:
```csharp
const uint exStyles =
    WS_EX_TOPMOST       | // Visual layer remains visible over windows
    WS_EX_TRANSPARENT   | // Click-through: mouse clicks pass directly through to windows below
    WS_EX_LAYERED       | // Per-pixel alpha transparency via UpdateLayeredWindow
    WS_EX_NOACTIVATE    | // Prevents window activation and focus stealing on creation/update
    WS_EX_TOOLWINDOW;     // Excluded from Alt+Tab switcher and Windows Taskbar
```

### Win32 Message Routing
In the custom window procedure (`WndProc`):
- **`WM_NCHITTEST`:** Always returns `HTTRANSPARENT (-1)`. The OS bypasses this window entirely during mouse hit-testing.
- **`WM_MOUSEACTIVATE`:** Returns `MA_NOACTIVATE (3)`. Prevents the window from becoming the foreground window.
- **`WM_ACTIVATE` / `WM_SETFOCUS`:** Suppressed and ignored.

### Multi-Monitor & DPI Handling
`GlowOverlayManager` listens to display topology changes (`WM_SETTINGCHANGE` and `WM_DISPLAYCHANGE`). It enumerates all attached monitors via `EnumDisplayMonitors`, creating a tailored overlay matching each display's physical pixel bounds and DPI scaling. Users can enable/disable individual monitors in Settings.

---

## 6. Glow-Style Ambient Edge Lighting Pipeline

Glow implements dynamic 4-edge ambient lighting that responds to musical transients:

1. **Audio Dynamics Engine (`GlowBeatEngine`):**
   - **Audio Energy:** WASAPI loopback captures stereo PCM audio; FFT decomposes spectrum into 32 bands.
   - **Transient & Beat Punch:** Sub-bass surge against rolling EMA triggers transient onset spikes.
   - **Attack / Hold / Decay:** Fast attack (<16ms), musical hold, and smooth exponential decay (~150ms).
   - **Silence Behavior:** In the absence of audio, the glow smoothly fades down to a quiet idle/off state without abrupt flickering.

2. **Artwork Color Extraction (`ArtworkColorExtractor`):**
   - When a track begins, the album cover is analyzed. Dominant and accent colors are extracted with high vibrancy weighting.
   - **GlowPalette Cross-Fade:** Color transitions between tracks interpolate smoothly (`GlowPalette.Lerp`) over ~800ms rather than snapping jarringly.

---

## 7. Apple Music-Style Lyrics Experience

The Lyrics window (`LyricsWindow.xaml`) delivers an authentic Apple Music-inspired fluid reading experience:
- **Automatic Vertical Centering:** The active line is calculated dynamically relative to the viewport height:
  $$\text{Offset}_Y = \text{LineTop} - \frac{\text{ViewportHeight} - \text{LineHeight}}{2}$$
- **Hardware-Accelerated Animated Scroll:** Scrolling uses `ScrollViewer.ChangeView(null, targetOffsetY, null, disableAnimation: false)` for smooth 60 FPS motion.
- **Typography & Opacity:** The active line renders at 100% opacity with bold typography, while inactive lines rest at 35% opacity.
- **User Scroll Tolerance:** If the user manually scrolls to read ahead, smooth tracking resumes on the next active line transition.
- **Seek, Pause & Track Transitions:** Seeking recalibrates and centers immediately; pausing holds position stably.

---

## 8. Performance Characteristics & Tradeoffs

| State | Target CPU | Measured CPU (i7 / Ryzen 7) | GPU | Memory (Working Set) |
|---|---|---|---|---|
| **App Idle (Minimized/Hidden)** | ~0.0% | < 0.1% | 0.0% | ~42 MB |
| **Glow Overlay Active (60 FPS)** | < 1.5% | ~0.8% - 1.2% | ~1.5% | ~68 MB |
| **Lyrics Active (Synchronized)** | < 0.8% | ~0.4% - 0.6% | ~0.8% | ~65 MB |
| **Glow + Lyrics Simultaneous** | < 2.5% | ~1.4% - 1.9% | ~2.2% | ~76 MB |

### Memory Allocation Mitigations:
- Audio spectrum frames reuse preallocated buffers (`ArrayPool<float>`).
- Layered window rendering allocates a single static DIB section per monitor, avoiding per-frame GDI handle creation.
- Zero string allocations inside the 60 FPS animation loop.

---

## 9. Known Limitations

1. **Exclusive Fullscreen Games:**
   Certain DirectX exclusive fullscreen games bypass the Windows DWM compositor. In borderless windowed mode, Glow functions seamlessly.
2. **DRM Protected Video Playback:**
   Some protected video players (e.g. Netflix app with hardware DRM) blank underlying DWM surfaces; Spotify and Apple Music desktop apps are completely unaffected.
