# Lyrow v3.1 Release Notes

**Release Date:** 2026-10-24  
**Target Platform:** Windows 11 (22H2 / 24H2 64-bit) & Windows 10 (Build 19041+)  
**Runtime:** .NET 10 LTS (Self-Contained win-x64)  
**UI Framework:** Native WinUI 3 + Windows App SDK + XAML  

---

## 🌟 Executive Summary

Lyrow v3.1 introduces critical track transition lifecycle isolation and a persistent in-place update experience with real progress telemetry:
1. **Atomic Lyrics Window Reset on Track Transitions:** When a new song starts—either manually or automatically via GSMTC—all lyrics, timeline positions, elapsed/total duration counters, karaoke fill progress, and scroll states are immediately purged. Stale asynchronous lyrics and artwork responses from the previous song are discarded instantly, guaranteeing zero cross-track leakage.
2. **Persistent In-Place Update Toast with Real Download Progress:** Clicking the "Update" button preserves the toast in-place throughout the entire upgrade lifecycle. The notification dynamically displays real bytes and percentages (`12.4 MB / 21.3 MB`), transitions smoothly from download to verification and silent background installation, and provides an actionable retry capability upon network disruption.

All established features from prior releases—including WASAPI 4-edge ambient lighting, Fluent vector iconography (`\uE706`), Apple Music-style 60 FPS karaoke fill animations, dynamic palette generation, system tray integration, and 5-language localization—remain fully intact.

---

## 🚀 Key Improvements & Highlights

### 1. 🛡️ Atomic Track Transition State Machine & Complete Lyrics Window Reset
- Implemented deterministic state reset whenever a track switch is signaled by GSMTC:
  - `TrackSequence` counter is atomically incremented, instantly invalidating in-flight HTTP requests and background cache fetches for the previous song.
  - Active lyric collection is purged immediately; old `TextBlock` and overlay geometries are cleared before new metadata arrives, preventing even single-frame remnants of old lyrics.
  - Playback position resets to `0:00` and duration resets to `0:00` until the new track's duration is loaded.
  - Visual animations, character fill clipping, vertical scroll offset, and font scaling start completely clean from $t = 0$.
  - When seeking within the same track, state is preserved and karaoke fill is smoothly recomputed at the target timestamp.
- Strict duration isolation: Duration of Track B is always independent (e.g., 180s, never cumulative $240 + 180 = 420\text{s}$). Transitions across multiple songs ($A \to B \to C \to D$) maintain clean individual durations.

### 2. 🔄 Persistent Update Toast with Real Progress Telemetry
- Upon clicking "Update" (Güncelle):
  - The notification card stays pinned and persistent, disabling timeout, dismissal, and auto-hide behaviors.
  - Action buttons hide as the card smoothly transforms into a progress viewer.
  - Real byte stream telemetry (`bytesRead / totalBytes`) drives the progress bar and secondary status label (`12.4 MB / 21.3 MB`).
  - Zero artificial, random, or timer-based percentages: if `Content-Length` is absent from the release server, an indeterminate progress indicator is displayed with actual downloaded megabytes.
  - Sequential state machine: `Available` $\to$ `Downloading` $\to$ `Verifying` $\to$ `Installing` $\to$ `Completed` (or `Failed` with Retry).
  - Network errors display an in-place "Retry" (Tekrar Dene) action that resets progress and initiates a fresh download.
  - Silent update execution preserves user flow: no browser redirects, no installer wizard dialogs.

### 3. 🌐 Full 5-Language Localization
- Expanded localization coverage for update verification, installation, and byte progress across all supported locales:
  - **English (en):** "Verifying update...", "Update complete. Lyrow will restart.", "{0:0.0} MB / {1:0.0} MB", "Later", "Retry"
  - **Türkçe (tr):** "Güncelleme doğrulanıyor...", "Güncelleme hazır. Lyrow yeniden başlatılacak.", "{0:0.0} MB / {1:0.0} MB", "Daha Sonra", "Tekrar Dene"
  - **Français (fr):** "Vérification de la mise à jour...", "Mise à jour prête. Lyrow va redémarrer.", "{0:0.0} Mo / {1:0.0} Mo", "Plus tard", "Réessayer"
  - **Español (es):** "Verificando actualización...", "Actualización lista. Lyrow se reiniciará.", "{0:0.0} MB / {1:0.0} MB", "Más tarde", "Reintentar"
  - **Deutsch (de):** "Update wird überprüft...", "Update bereit. Lyrow wird neu gestartet.", "{0:0.0} MB / {1:0.0} MB", "Später", "Wiederholen"

---

## 🧪 Quality Assurance & Test Verification

- **Total Test Suite:** All 287+ solution tests pass with 0 failures, 0 skipped, and 0 warnings.
- **Dedicated v3.1 Regression & Verification Coverage:**
  - `AutomaticTrackTransitionResetsLyricsWindow`: Verifies UI clearance on track switch.
  - `AutomaticTrackTransitionResetsDuration`: Asserts clean duration reset and zero cumulative duration across tracks.
  - `AutomaticTrackTransitionResetsPosition`: Asserts position starts at 0 for new tracks.
  - `AutomaticTrackTransitionClearsOldLyrics`: Asserts old lines are never appended or visible.
  - `AutomaticTrackTransitionResetsLyricAnimation`: Asserts line progress starts at 0.0.
  - `AutomaticTrackTransitionRejectsStaleLyrics`: Verifies stale async LRCLIB responses are discarded via `TrackSequence`.
  - `AutomaticTrackTransitionRejectsStaleArtwork`: Verifies stale bitmap extraction responses are discarded.
  - `AutomaticTrackTransition_A_To_B_To_C_Isolated`: Asserts zero cumulative duration across $A \to B \to C$.
  - `UpdateToast_PersistsDuringDownload`: Asserts card remains persistent during download.
  - `UpdateToast_ShowsRealDownloadProgress`: Verifies true byte/percentage calculation.
  - `UpdateToast_DoesNotUseFakeProgress`: Confirms absence of artificial timer/random progress loops.
  - `UpdateToast_TransitionsToVerifying`: Verifies transition to verifying state.
  - `UpdateToast_TransitionsToInstalling`: Verifies transition to installing state.
  - `UpdateToast_ShowsRetryOnFailure`: Verifies actionable retry button upon failure.
  - `UpdateToast_RetryRestartsDownload`: Verifies retry resets progress and triggers re-download.
  - `UpdateToast_AllFiveLanguagesHaveRequiredKeys`: Asserts all update localization keys exist across EN, TR, FR, ES, DE.
- **Native Smoke Test:** Executed on self-contained `win-x64/Lyrow.App.exe` verifying responsive HWND, active UI pump, 9s runtime, and clean exit code 0.

---

## 📥 Distribution Packages

| Asset | Type | Target Architecture |
| :--- | :--- | :--- |
| `Lyrow-3.1-Setup-x64.exe` | Windows Installer (Inno Setup) | x64 (AMD64 / Intel 64) |
| `Lyrow-v3.1-win-x64-portable.zip` | Standalone Portable Archive | x64 (AMD64 / Intel 64) |

*User settings under `%APPDATA%\Lyrow` are preserved across upgrades.*
