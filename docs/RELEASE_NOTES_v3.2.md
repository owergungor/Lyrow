# Lyrow v3.2 Release Notes

**Release Date:** 2026-10-25  
**Target Platform:** Windows 11 (22H2 / 24H2 64-bit) & Windows 10 (Build 19041+)  
**Runtime:** .NET 10 LTS (Self-Contained win-x64)  
**UI Framework:** Native WinUI 3 + Windows App SDK + XAML  

---

## 🌟 Executive Summary

Lyrow v3.2 brings deep visual rendering refinement, deterministic track transition hardening, and true artwork-derived ambient atmosphere:
1. **Critical Lyrics Duplicate/Ghost Rendering Fix:** Completely resolved the dual-instance rendering bug where advancing to subsequent lyric lines left a blurry ghost/duplicate copy of the previous line. Rebuilt the visual hierarchy so that non-active surrounding lines maintain a single canonical visual text path with zero active overlays. Active lines synchronize pixel-perfect font weight and geometry bounds between base and karaoke highlight layers.
2. **Sub-10ms End-of-Track Transition Hardening:** Hardened track progression and Skip Next actions occurring at $T - 10\text{ms}$ (and down to $T - 1\text{ms}$) before track completion. Eliminates floating-point rounding hazards, guarantees immediate atomic `TrackSequence` incrementation, purges in-flight asynchronous operations, and completely isolates Track B duration from Track A state.
3. **Artwork-Derived Dynamic Lyrics Background:** Enhanced ambient background gradient synthesis to dynamically project cinematic color fields derived directly from the current track's album artwork (rich reds, deep blues, violets, and emerald greens). Applied luminance bounding ($\le 55.0$ cd/m²) and subtle vignette darkening to maintain WCAG AAA contrast for foreground typography alongside a silky 650–750ms crossfade between tracks.
4. **Comprehensive Code & Resource Hardening:** Audited solution for unused usings, dead branches, and legacy shims. Verified zero occurrences of legacy nomenclature across all source, docs, and assets.

---

## 🚀 Key Improvements & Highlights

### 1. 🛡️ Canonical Lyrics Visual Tree & Duplicate Elimination
- **Root Cause Identified:** Previously, every lyric container housed both a base `TextBlock` and an active overlay `TextBlock`. When line 0 completed and line 1 became active, line 0's overlay remained active with full-width clip. Because the overlay utilized `FontWeights.Bold` while the base block transitioned to `FontWeights.SemiBold`, kerning and word wrapping differed, creating a doubled, blurry ghost line.
- **Permanent Solution:**
  - Inactive/surrounding lines immediately collapse and clear their overlay layer (`Visibility = Collapsed`, `Clip = Rect(0,0,0,0)`), leaving exactly one canonical `TextBlock` in the visual tree.
  - Active lines maintain bit-for-bit identical typography metrics (`FontFamily`, `FontSize`, `FontWeight = Bold`, `LineHeight`) across both base and overlay, ensuring 100% subpixel glyph alignment during character/word fill.
  - Dynamic scale and opacity easing (350–500ms) transition surrounding lines smoothly without duplicate text instances.

### 2. ⚡ T - 10ms Next Track Transition Reliability
- Verified and hardened Skip Next actions triggered at $T - 10\text{ms}$ before track end:
  - Immediate `TrackSequence` incrementation invalidates in-flight background operations and stale lyrics queries.
  - Duration of Track B starts fresh ($Position = 0:00$, $Duration = \text{Track B duration}$) with zero cumulative duration leakage ($A \to B \to C$ isolation).
  - Cleanly handles zero-duration live streams and floating-point interpolation bounds.
  - Dedicated regression test suite: `NextTrackTriggered10msBeforeEndTests`.

### 3. 🎨 Cinematic Artwork Background Atmosphere
- Palette extractor draws dominant, secondary, tertiary, and accent colors from album artwork pixels:
  - Red dominant artwork $\to$ rich crimson/ruby atmosphere.
  - Blue dominant artwork $\to$ deep cobalt/indigo atmosphere.
  - Purple/Green dominant artwork $\to$ vibrant thematic hues.
  - Darkened atmospheric layer (`#50000000` + soft radial vignette) preserves Apple Music-style contrast and readability.
  - 60 FPS smooth interpolation provides a natural 650–750ms crossfade between track palettes.

---

## 🧪 Quality Assurance & Test Verification

- **Total Test Suite:** All 314 solution tests pass with 0 failures, 0 skipped, and 0 warnings:
  - **Lyrow.Core.Tests:** 47 / 47 PASS
  - **Lyrow.Audio.Tests:** 40 / 40 PASS
  - **Lyrow.Music.Tests:** 75 / 75 PASS
  - **Lyrow.App.Tests:** 152 / 152 PASS
- **New v3.2 Regression Suites:**
  - `NextTrackTriggered10msBeforeEndTests`: Validated $T - 20\text{ms}$, $T - 10\text{ms}$, $T - 1\text{ms}$, $T$, and tolerance boundaries.
  - `LyrowV32FeatureVerificationTests`: Validated lyrics single visual path, uniqueness of visual identity, stale visual removal, red/blue background color projection, and palette crossfade interpolation.
- **Windows 11 Native Smoke Test:**
  - Self-contained `publish/win-x64/Lyrow.App.exe` launched and monitored over 9-second run.
  - Startup: PASS (53 ms).
  - Responsiveness: PASS (at 3s and 9s).
  - Valid HWND: PASS (1245526).
  - Graceful Shutdown: PASS (Exit code 0, 0 crash, 0 hang).

---

## 📥 Distribution Packages

| Asset | Type | Target Architecture |
| :--- | :--- | :--- |
| `Lyrow-3.2-Setup-x64.exe` | Windows Installer (Inno Setup) | x64 (AMD64 / Intel 64) |
| `Lyrow-v3.2-win-x64-portable.zip` | Standalone Portable Archive | x64 (AMD64 / Intel 64) |

*User settings under `%APPDATA%\Lyrow` are preserved across upgrades.*
