# Lyrow v3.0 Release Notes

**Release Date:** 2026-10-24  
**Target Platform:** Windows 11 (22H2 / 24H2 64-bit) & Windows 10 (Build 19041+)  
**Runtime:** .NET 10 LTS (Self-Contained win-x64)  
**UI Framework:** Native WinUI 3 + Windows App SDK + XAML  

---

## 🌟 Executive Summary

Lyrow v3.0 is a major milestone release focused on precision timing accuracy, deep state isolation during automatic track transitions, in-place persistent update progress UX, and refreshed Fluent iconography. All features from previous releases (WASAPI loopback audio edge lighting, Apple Music-style 60 FPS karaoke lyrics, dynamic artwork palette background, system tray controls, and 5-language localization) are fully preserved.

---

## 🚀 Key Improvements & Highlights

### 1. 💡 Refreshed Modern Fluent Glow Icon
- Replaced legacy icons with native Windows 11 Fluent vector `BrightLight` typography (`\uE706`), reflecting radiant 4-edge ambient illumination.
- System tray context menu polished with clean native labels, eliminating non-standard emojis while retaining checkable state flags.
- Default visualizer standards preserved: 80% Brightness, 48px Edge Thickness, 250% Audio Reactivity.

### 2. 🔄 Persistent In-Place Update Toast with Live Progress
- Clicking the "Update" (Güncelle) button no longer dismisses the notification; the card seamlessly morphs in-place into an Apple-inspired compact progress bar with real-time download percentage.
- Smooth state transitions: `Downloading (0–100%)` → `Preparing update (100%)` → `Installing update...`.
- In case of network disconnection, the toast displays an actionable error message with an in-place "Retry" button.
- Fully accessible via keyboard and screen readers across English, Turkish, French, Spanish, and German.
- Retains silent, non-intrusive self-contained installer execution without wizard prompts.

### 3. 🛡️ Robust Automatic Track Transition State Isolation (A → B)
- Resolved cumulative duration accumulation when songs transition automatically through GSMTC without explicit user skips.
- Calculated relative timeline position (`position - startTime`) and effective duration (`endTime - startTime`), preventing player stream offsets from inflating track duration.
- Atomic `TrackSequence` guards invalidate outdated async LRCLIB lyrics requests and artwork extraction responses.
- Transitioning from Track A (240s) to Track B (180s) immediately resets playback position to 0s, sets duration strictly to 180s (never 420s), and clears prior lyric/artwork artifacts.

### 4. 🎤 Independent Multi-Line & Token Karaoke Progress
- Fixed simultaneous fill glitch when multiple lines or multi-sentence blocks are active.
- Integrated deterministic `LyricsProgressCalculator` calculating independent progress per line:
  - Future lines (`currentTime < lineStart`): progress = 0.0 (unfilled, passive dim style).
  - Active line (`lineStart <= currentTime < lineEnd`): real-time smooth fill progress [0.0, 1.0].
  - Completed lines (`currentTime >= lineEnd`): progress = 1.0 (fully filled, highlighted text).
- Granular token-level progress support (`LyricToken`) with fallback word/character distribution.
- Pausing playback instantly freezes fill animations; resuming continues seamlessly without jumping; forward and backward seeks recalculate all lines instantly from the new playback position.

---

## 🧪 Quality Assurance & Test Verification

- **Total Solution Unit & Regression Tests:** All tests PASS with 0 failures, 0 skipped, and 0 warnings.
- **Regression Test Coverage:**
  - `LyricsTwoLineIndependentProgressTests`: Verified independent progress at t=1, t=3, and t=5 for lines [0..2] and [2..4].
  - `LyricsTokenIndependentProgressTests`: Verified token boundaries for word-level karaoke fill.
  - `AutomaticTrackTransitionStateIsolationTests`: Verified zero duration accumulation across automatic track changes (A → B and A → B → C → D).
  - `UpdateToastPersistenceTests` & `UpdateDownloadProgressTests`: Verified in-place card transformation and progress reporting.
  - `GlowIconTests`: Verified Fluent icon consistency across all views.
- **Windows Native Smoke Test:** Verified running `publish/win-x64/Lyrow.App.exe` with active UI pump, responsive HWND, 9-second continuous runtime, and graceful `WM_CLOSE` exit code 0.

---

## 📥 Distribution Packages

| Asset | Type | Target Architecture |
| :--- | :--- | :--- |
| `Lyrow-3.0-Setup-x64.exe` | Windows Installer (Inno Setup) | x64 (AMD64 / Intel 64) |
| `Lyrow-v3.0-win-x64-portable.zip` | Standalone Portable Archive | x64 (AMD64 / Intel 64) |

*User configurations stored under `%APPDATA%\Lyrow` remain fully compatible and preserved.*
