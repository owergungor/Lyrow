# LYROW — STAGE 38: UI LAYOUT, LYRICS SYNCHRONIZATION & SETTINGS POLISH

## 1. Executive Summary

In Stage 38, we addressed user acceptance test feedback regarding normal mode window sizing, lyrics display highlighting, track transition remaining time display, and Settings UI organization:

1. **Normal Mode Window Size & Layout Overflow Fix**:
   - Resolved the issue where the "Hide Lyrics" (`BtnToggleLyrics`) and bottom offset controls clipped out of the window.
   - Updated the default normal mode window dimensions to **960 × 720 px** and established explicit minimum constraints (**640 × 520 px**).
   - Recalibrated `LyricsViewportCalculator.CalculateResponsiveMetrics` so artwork dimensions leave at least **280–310 px** of vertical clearance for all media transport, audio device selector, volume slider, and lyrics toggle controls.
   - Wrapped `PlayerInnerPanel` inside a responsive `PlayerScrollViewer` (`x:Name="PlayerScrollViewer"`) with hidden scrollbars to prevent any visual clipping on extreme DPI scale factors or smaller display resolutions.
   - Preserved Compact Mode (Dar Mod) behavior and dimensions completely.

2. **Single Active Lyric Line & Highlight Recovery**:
   - Fixed the bug where two lyric lines appeared white simultaneously.
   - Enforced the invariant that **strictly at most one line** is visually active and highlighted at any moment.
   - Initialized line overlay blocks as `Visibility.Collapsed` with `Rect(0, 0, 0, 0)` clips.
   - On frame transitions and line index changes, any inactive line (`i != _currentActiveIndex`) has its overlay visibility collapsed and its clip geometry immediately zeroed.
   - On the active line, highlight progression is smoothly clipped from 0.0 to 1.0 (with character/word precision) and never spills over to previous or subsequent lines.
   - Handled line-only timing (LRCLIB) cleanly without word timing, ensuring no freezing on automatic track switches.

3. **Track Transition Remaining Time Recovery (`-0:00` Bug Fix)**:
   - Eliminated the brief `-0:00` flash that occurred during track transitions when track duration was not yet reported by the media session.
   - `PlaybackTimelineEngine.CurrentTrackRemainingFormatted` now returns a clean placeholder `--:--` whenever duration is unknown or zero (`_currentTrackDuration <= TimeSpan.Zero`).
   - `LyricsWindow.xaml` and `LyricsWindow.xaml.cs` initialize and reset `TxtRemaining` and `TxtRemainingCompact` to `--:--`.
   - Preserved Stage 33 timeline engine calculations (elapsed and remaining time strictly isolate current track bounds).

4. **Settings Tabs Cleanup & Complete Localization**:
   - Removed the redundant "Sistem" (System) tab from the visible Settings category selector bar (`BtnFilterSystem` set to `Visibility="Collapsed"`).
   - Preserved all system settings (Windows startup, system tray integration, desktop notifications, notify on track change) under the "Appearance & Window" category and "All Settings" view.
   - Verified that "Güncellemeler" (Updates) and "Kısayollar" (Shortcuts) tabs remain distinct, active, and fully functional.
   - Eliminated hardcoded English strings in Turkish mode and hardcoded Turkish strings in English mode across Settings.
   - Verified proper Turkish characters (ş, ç, ğ, ı, ö, ü) across all keys in `LocalizationService.cs`.

---

## 2. Root Cause Analysis & Architectural Solutions

### 2.1 Normal Mode Window Size & Layout Sizing

- **Root Cause**:
  - `LyricsWindow.xaml.cs` previously initialized normal mode window size with `ResizeForCurrentDpi(960, 640)`.
  - The total height of the left column player inner elements (300 px artwork + 44 px title + 22 px artist + 26 px scrubber + 17 px timestamps + 36 px device selector + 58 px playback buttons + 44 px volume + 42 px offset row + 24 px padding + 32 px titlebar) totaled **~645 px**.
  - In `LyricsViewportCalculator.cs`, `maxArtByHeight` was calculated as `playerAvailableH - 250.0`, assuming controls took only 250 px. When window height was 640 px, available height was 596 px, and artwork was sized to 330 px, pushing `GridOffsetRow` with `BtnToggleLyrics` below the window boundary.
- **Solution**:
  - Set default normal mode window dimensions to **960 × 720 px** (matching `WindowStateManager.DefaultNormalSize`).
  - Set explicit `OverlappedPresenter` minimum window bounds to **640 × 520 px** (DPI-scaled).
  - Recalibrated `LyricsViewportCalculator.CalculateResponsiveMetrics`:
    ```csharp
    double maxArtByHeight = Math.Clamp(playerAvailableH - 300.0, 140.0, 560.0);
    ```
    At 960 × 720, artwork size computes to 331 px, leaving ~389 px of vertical room for controls and margins.
  - Added `PlayerScrollViewer` wrapping `PlayerInnerPanel` inside `PlayerColumnContainer` to prevent clipping under extreme DPI scaling (e.g. 175% or 200% on laptop screens).

### 2.2 Synchronized Lyrics Highlighting & Inactive Overlay Invariant

- **Root Cause**:
  - In `LyricsWindow.xaml.cs`, `overlayBlock` elements were constructed with `Visibility = Visibility.Visible` by default.
  - When line transitions occurred, `StepSmoothAnimations` updated `clipList[s].Rect = new Rect(0,0,0,0)` on inactive lines, but did not collapse `overlay.Visibility`.
  - In WinUI 3 D3D rendering, a visible text block with an empty clip rect (`Rect(0,0,0,0)`) on certain drivers fell back to unclipped rendering, causing the previous line to remain visible in bright white (`ActiveLyricBrush`) alongside the newly active line.
- **Solution**:
  - Initialized `overlayBlock.Visibility = Visibility.Collapsed` in `RebuildLyricsUi`.
  - In `StepSmoothAnimations` and `UpdateActiveLineTargets`, unconditionally set `overlayList[s].Visibility = Visibility.Collapsed` and `clipList[s].Rect = new Rect(0,0,0,0)` on every line where `i != _currentActiveIndex`.
  - On the active line (`i == _currentActiveIndex`), only set `overlay.Visibility = Visibility.Visible` when `actualW > 0 && progress > 0.001`.
  - This strictly guarantees that at most one line can ever render with active highlight at any instant.

### 2.3 Track Transition Remaining Time (`-0:00` Bug)

- **Root Cause**:
  - During automatic track transitions, before the new track duration arrived from GSMTC or LRCLIB, `_currentTrackDuration` was `TimeSpan.Zero`.
  - `PlaybackTimelineEngine.CurrentTrackRemainingFormatted` called `FormatRemaining(TimeSpan.Zero)`, which returned `"-0:00"`.
  - `LyricsWindow.xaml.cs` also had hardcoded `"-0:00"` in `ResetTrackState` and `OnUiTimerTick` when `track.IsEmpty`.
- **Solution**:
  - In `PlaybackTimelineEngine.cs`:
    ```csharp
    public string CurrentTrackRemainingFormatted
    {
        get
        {
            lock (_lock)
            {
                if (_currentTrackDuration <= TimeSpan.Zero)
                {
                    return "--:--";
                }
                var pos = CalculateCurrentPositionLocked(DateTime.UtcNow);
                var rem = _currentTrackDuration > pos ? _currentTrackDuration - pos : TimeSpan.Zero;
                return FormatRemaining(rem);
            }
        }
    }
    ```
  - In `LyricsWindow.xaml` and `LyricsWindow.xaml.cs`, updated initial text and reset placeholders from `"-0:00"` to `"--:--"`.

### 2.4 Settings System Tab Cleanup & Localization

- **Root Cause**:
  - The "Sistem" tab duplicated general settings, while users wanted a cleaner tab hierarchy with Updates, Shortcuts, Artwork, Audio, and Appearance.
  - Several UI elements in `PageSettings` had hardcoded English text (`Window Master Opacity`, `Fluid Motion & Animations`, `Desktop Notifications`, `Notify on Track Change`, `Dynamic Ambient Background Layer`, etc.) and were never passed to `LocalizationService`.
- **Solution**:
  - Collapsed `BtnFilterSystem` in `MainWindow.xaml` (`Visibility="Collapsed"`), removing it from the user-facing tab bar.
  - Linked `SectionSystem` (Windows Startup, System Tray, Desktop Notifications) so they remain visible under the "Appearance" and "All" views.
  - Named all unlocalized TextBlocks in `MainWindow.xaml` and added comprehensive localization in `ApplyLocalization()`.
  - Updated `LocalizationService.cs` dictionaries for English and Turkish with complete key mappings and verified valid Turkish character encoding.

---

## 3. Verification & Test Suite Results

All unit and integration tests across the solution passed with zero errors:

| Test Project | Tests Executed | Passed | Failed | Skipped | Duration |
|:---|:---:|:---:|:---:|:---:|:---:|
| `Lyrow.Core.Tests` | 63 | 63 | 0 | 0 | 0.18 s |
| `Lyrow.Audio.Tests` | 53 | 53 | 0 | 0 | 0.53 s |
| `Lyrow.Music.Tests` | 120 | 120 | 0 | 0 | 22.1 s |
| `Lyrow.App.Tests` | 418 | 418 | 0 | 0 | 5.2 s |
| **Total** | **654** | **654** | **0** | **0** | **28.0 s** |

### Verified Test Cases:
1. `NormalMode_DefaultAndMinimumDimensions_AreProperlyConfigured`: Default 960 × 720, compact 528 × 222, `PlayerScrollViewer` and `BtnToggleLyrics` present in XAML.
2. `CalculateResponsiveMetrics_LeavesAdequateRoomForControlsBelowArtwork`: Across 960x720, 1280x800, 960x600, 800x560, and hidden-lyrics mode, vertical clearance for controls is >= 250 px.
3. `LyricsHighlighting_OnlyOneLineCanBeActive_AtAnyPlaybackPosition`: At all playback timestamps, exactly one line evaluates as active; all other lines have passive visual targets.
4. `LyricsFillProgressCalculator_ClearsHighlightCompletely_WhenOutsideActiveWindow`: Fill progress before line start is strictly 0.0; after line end is 1.0; no bleed-over.
5. `PlaybackTimelineEngine_WhenDurationIsUnknown_RemainingTimeShowsPlaceholderNotNegativeZero`: Returns `--:--` when duration is zero; returns valid `-m:ss` when duration is known.
6. `Settings_SystemTab_IsCollapsedInXaml_AndUpdatesAndShortcutsRemainVisible`: System filter button is collapsed; Updates, Shortcuts, Appearance, Artwork, Audio remain visible.
7. `LocalizationService_EnglishAndTurkish_HaveCompleteAndMatchingKeys`: All new and existing keys match between TR and EN dictionaries with proper Turkish characters.
8. `LyricsWindowXaml_DefaultsRemainingTimeToPlaceholder_NotMinusZero`: XAML defaults to `--:--` with zero `-0:00` occurrences.
9. All 642 legacy tests from Stages 1 through 37 remain 100% PASS without regressions.

---

## 4. Protected Release & Distribution Constraints

- **Git Tags**: No new tags created. Existing tags (`v3.5`, `v3.6`, `v3.7`) remain completely untouched.
- **GitHub Releases**: No releases created or modified. No binaries pushed to releases.
- **Product Metadata**: Legacy `3.5` version metadata invariant in project files and `setup.iss` remains preserved for existing verification tests.
- **Installer / Packaging Scripts**: Stage 37 packaging scripts (`installer/build_packages.ps1`) and clean Inno Setup configurations are fully preserved.
