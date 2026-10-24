# Lyrow v2.8 Release Notes

**Release Date:** 2026-10-24  
**Target Platform:** Windows 11 (22H2 / 24H2 64-bit) & Windows 10 (Build 19041+)  
**Runtime:** .NET 10 LTS (Self-Contained win-x64)  
**UI Framework:** Native WinUI 3 + Windows App SDK + XAML  

---

## 🌟 Executive Summary

Lyrow v2.8 builds directly on the frozen v2.7 baseline, introducing essential post-release hardening, rock-solid automatic track transition state isolation, enhanced fullscreen lyrics layout ergonomics, and bulletproof Win32 window lifetime management.

---

## 🚀 Key Improvements & Highlights

### 1. 🛡️ Automatic Track Transition State Isolation (A → B Atomic Hardening)
- **Zero Stale State Leakage:** When transitioning from Track A to Track B (e.g. 240s track followed by a 180s track), playback position, duration, lyrics count, and artwork palette extractors are cleanly re-initialized to the new track's boundary.
- **Sequence Generation Guards:** `LyricsModule` now tracks atomic sequence IDs (`TrackSequence`), discarding any asynchronous LRCLIB or network responses destined for prior tracks.
- **Strict Verification:** Integrated full automated isolation tests asserting that duration never accumulates across transitions, lyrics reflect only the active track, and background transitions trigger immediately.

### 2. 🖥️ Fullscreen Lyrics Experience (40/60 Split Layout)
- **Ergonomic Visual Balance:** Redesigned fullscreen layout allocates 40% width to the left player/artwork hero column and 60% to the right scrolling lyrics column, perfectly proportioned across 720p, 1080p, and 1440p displays.
- **Radial Vignette & Safe Ambient Contrast:** Deep ambient gradient background now features subtle radial darkening and WCAG-compliant luminance clamping, ensuring high-contrast readability of active and inactive lyrics lines.
- **Floating Controls & Scrub Timeline:** Intuitive transport controls, playback scrub bar, and translucent pills for exit, volume, and playback options.

### 3. ⚙️ Graceful Shutdown & Tray Lifetime Hardening
- **Win32 Window Subclassing:** Subclassed `WM_CLOSE` messages directly via `SetWindowSubclass` and `TrayWndProc` ensuring consistent application shutdown or minimizing behavior across all Windows desktop states.
- **Non-blocking Asynchronous Cleanup:** Application shutdown routine encapsulates service disposal inside timed non-blocking tasks with a 2-second fallback timeout, completely preventing synchronization-context deadlocks during OS exit or smoke testing.
- **Smoke Test Mode:** Integrated automated testing command flag (`--smoke-test`) for deterministic startup/shutdown verification.

### 4. 📦 Metadata & CI/CD Pipeline Alignment
- Cleanly updated all project metadata, AssemblyVersion, FileVersion, Inno Setup compiler definitions, and GitHub Actions workflows to strictly two-part `v2.8`.
- Complete eradication of legacy terminology across all source trees and release assets.

---

## 🧪 Quality Assurance & Test Verification

- **Total Solution Unit & Integration Tests:** 279 / 279 PASS (100% pass rate, 0 failed, 0 skipped, 0 warnings).
  - `Lyrow.Core.Tests`: 47 / 47 PASS
  - `Lyrow.Music.Tests`: 67 / 67 PASS
  - `Lyrow.Audio.Tests`: 40 / 40 PASS
  - `Lyrow.App.Tests`: 125 / 125 PASS
- **Windows Smoke Test:** Verified running unpacked `publish/win-x64/Lyrow.App.exe` with active UI pump, responding process, valid Win32 HWND, and graceful exit code 0.

---

## 📥 Distribution Packages

| Asset | Type | Target Architecture |
| :--- | :--- | :--- |
| `Lyrow-2.8-Setup-x64.exe` | Windows Installer (Inno Setup) | x64 (AMD64 / Intel 64) |
| `Lyrow-v2.8-win-x64-portable.zip` | Standalone Portable Archive | x64 (AMD64 / Intel 64) |

*User configurations stored under `%APPDATA%\Lyrow` remain fully compatible and preserved.*
