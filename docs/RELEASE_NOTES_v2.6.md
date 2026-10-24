# Lyrow v2.6 Release Notes

Lyrow v2.6 introduces real-time Apple Music-style character and word karaoke fill progress, a redesigned modern Windows 11 system tray notification menu, tactile press animations on Home cards, improved default Glow parameters, and hardened media track transitions.

---

## What's New in v2.6

### 1. Apple Music-Style Karaoke Lyrics System
- **Subpixel Dynamic Clipping:** Active lyric line features dual superimposed text layout with dynamic GPU clipping (`RectangleGeometry`).
- **Synchronized with Playback:** Seamlessly tracks playback position at 60 FPS, freezes instantly on pause, leaps smoothly on seek forward/backward, and resets on track switch.
- **Word & Character-Level Interpolation:** Smooth continuous gradient transition without abrupt jumps or string allocations.

### 2. Modern System Tray Experience
- **Theme-Adaptive Win32 Menu:** Native light/dark theme matching via `SetPreferredAppMode` and `FlushMenuThemes`.
- **Media Controls & State Toggles:**
  - Now playing header with track title and artist.
  - Play/Pause, Previous, Next, and Mute transport controls.
  - Ambient Glow & Synchronized Lyrics checkboxes with live state feedback (`MF_CHECKED`).
  - Quick navigation to Home, Playback, Glow Settings, General Settings, and In-App Updates.

### 3. Tactile Home Card Interaction
- **Smooth Spring Micro-Animations:** Tactile `1.0 -> 0.97 -> 1.0` scale animation on card press/release.
- **Keyboard & Accessibility Support:** Interactive cards are keyboard focusable and trigger navigation with `Enter` and `Space`.

### 4. Tuned Default Glow Parameters
- **Thickness:** Set to `48 px` for a refined, modern edge profile.
- **Reactivity / Sensitivity:** Set to `250%` for vivid response to music transients.
- **Brightness:** Maintained at `80%`.

### 5. Robust Media Session & Track Transition Hardening
- **Sequence-Guarded Media Events:** Track transition atomic sequence counter (`_trackSequence`) protects against stale async responses and race conditions.
- **Instant Duration & Timeline Reset:** Prevents position extrapolation or duration summation during track shifts.

---

## Verification & QA

- **Strict Two-Part Versioning:** `v2.6` / `2.6`.
- **Zero Legacy Names:** 0 occurrences across active codebase.
- **Unit & Integration Tests:** 261 / 261 PASS (0 failures, 0 skipped).
- **Architecture:** Windows 11 + WinUI 3 + Windows App SDK + C# + .NET 10 LTS.
- **Supported Languages:** English, Turkish, French, Spanish, German.
