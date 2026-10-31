# Lyrow v3.3 Release Notes

**Release Date:** 2026-10-31  
**Target Platform:** Windows 11 (22H2 / 24H2 64-bit) & Windows 10 (Build 19041+)  
**Runtime:** .NET 10 LTS (Self-Contained win-x64)  
**UI Framework:** Native WinUI 3 + Windows App SDK + XAML  

---

## 🌟 Executive Summary

Lyrow v3.3 delivers a ground-up redesign of the horizontal lyrics player and compact mini-player experience alongside a deterministic fix for automatic track-transition lyrics synchronization:
1. **50/50 Horizontal Lyrics & Player Redesign:** Rebuilt the horizontal lyrics window from scratch with an exact 50% Player / 50% Lyrics responsive split. The left player column features aspect-preserved rounded artwork, left-aligned song title and artist/album metadata, artwork-width progress bar and elapsed/remaining time indicators, centered dynamic audio output device indicator, centered playback controls flanked by Compact Mode (Dar Mod) and Fullscreen buttons, artwork-width volume slider, and a Lyrics toggle button.
2. **Centered Player When Lyrics Toggled Off:** Toggling the Lyrics button collapses the right-side lyrics viewport and smoothly centers the player horizontally across the entire window, restoring the 50/50 split when toggled back on.
3. **Apple Music-Inspired Compact Mode (Dar Mod):** Redesigned the compact mini-player mode with horizontal rounded layout, left artwork, song title, artist/album, scrubber, elapsed/remaining timestamps, dynamic output device indicator, playback controls, volume mute, and instant expand back to the horizontal lyrics window.
4. **Automatic Track Transition Lyrics Synchronization Fix:** Eliminated the root cause of lyrics desynchronization when Track A automatically advances to Track B. Added stale-timeline filtering on duration/position mismatches during track switches, prevented intermediate GSMTC duration mutations from firing false `TrackChanged` events, and added immediate layout-measurement centering snap so Track B's initial lyric lines center and synchronize at `0:00` without delay.
5. **Untouched v3.2 Artwork Background Engine:** Preserved `ArtworkPaletteExtractor`, safe luminance bounding ($\le 55.0\text{ cd/m}^2$), dark atmospheric vignette, and 60 FPS palette crossfade completely intact.

---

## 🚀 Key Improvements & Highlights

### 1. 📐 50/50 Horizontal Player & Lyrics Architecture
- **Left Column (50% — Player):**
  - Rounded album artwork (`300x300`, `CornerRadius="16"`), preserving aspect ratio without cropping.
  - Song title (`Bold`, `22px`) and `Artist — Album` secondary text aligned strictly with the artwork's left edge.
  - Playback progress bar and `Elapsed` / `-Remaining` timestamps spanning the exact width of the artwork.
  - Centered hardware output device label with dynamic native Fluent icon (`\uE7F6` Headphones, `\uE702` Bluetooth, `\uE767` Speakers) powered by WASAPI/MMDevice endpoint detection.
  - Centered `Previous`, `Play/Pause`, and `Next` controls with `BtnDarMod` aligned to the artwork's left edge and `BtnFullscreen` aligned to the artwork's right edge.
  - Volume slider spanning the exact width of the artwork, with `BtnToggleLyrics` positioned below the right edge.
- **Right Column (50% — Synchronized Lyrics):**
  - Preserves 60 FPS vertical centering, click-to-seek, word/character karaoke fill, and single canonical visual text representation (zero duplicate/ghost lines).

### 2. 📱 Apple Music-Reference Compact Mode (Dar Mod)
- Compact horizontal window (`480x180` logical px, DPI-aware) presenting artwork, track metadata, progress scrubber, elapsed/remaining time, output device, transport controls, mute, and one-click expand to the 50/50 lyrics view.

### 3. 🔄 Deterministic Automatic Track Transition Sync
- **Stale Timeline Rejection:** Filters out delayed GSMTC timeline callbacks carrying Track A's duration/end position after Track B has started.
- **Sequence-Guarded Async Pipeline:** Every track switch increments `TrackSequence`, invalidating in-flight LRCLIB, cache, artwork, and palette tasks from the previous track.
- **Post-Measurement Center Snap:** Automatically snaps the active lyric line to the vertical center as soon as WinUI completes layout measurement of newly built lyric containers.

---

## 🧪 Quality Assurance & Test Verification

- **Total Test Suite:** All solution tests pass with 0 failures, 0 skipped, and 0 warnings:
  - **Lyrow.Core.Tests:** PASS
  - **Lyrow.Audio.Tests:** PASS
  - **Lyrow.Music.Tests:** PASS
  - **Lyrow.App.Tests:** PASS
- **New v3.3 Regression Suites:**
  - `AutomaticTrackTransitionLyricsSyncTests`: Verifies automatic Track A $\to$ Track B transitions, stale timeline rejection, stale LRCLIB response discarding, and zero position leakage.
  - `LyrowV33FeatureVerificationTests`: Verifies 50/50 horizontal split, centered player on lyrics toggle, Dar Mod layout, dynamic audio device detection, untouched v3.2 background engine, and `v3.3` two-part versioning.

---

## 📥 Distribution Packages

| Asset | Type | Target Architecture |
| :--- | :--- | :--- |
| `Lyrow-3.3-Setup-x64.exe` | Windows Installer (Inno Setup) | x64 (AMD64 / Intel 64) |
| `Lyrow-v3.3-win-x64-portable.zip` | Standalone Portable Archive | x64 (AMD64 / Intel 64) |

*User settings under `%APPDATA%\Lyrow` are preserved across upgrades.*
