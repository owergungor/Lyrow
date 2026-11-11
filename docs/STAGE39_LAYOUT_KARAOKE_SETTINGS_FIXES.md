# Lyrow — Stage 39: Layout Recovery, Karaoke Wrapping & Settings Localization

## 1. Executive Summary

Stage 39 addresses all regressions and visual bugs observed during manual testing of Stage 38 in live Windows desktop environments:
1. **Normal Mode Layout (Bottom Controls & Hide Lyrics Visibility)**: Restructured normal mode layout metrics to ensure all player controls, timeline, volume slider, output selector, and the "Hide Lyrics" button (`BtnToggleLyrics`) are completely visible immediately on launch without requiring scrolling.
2. **Karaoke Word Highlight Concurrency on Wrapped Lines**: Eliminated the multi-line clipping race where wrapped words on line 2 were painted white concurrently with line 1 words. Replaced multi-line bounding box clipping with per-subline discrete TextBlocks and isolated sequential word timing partitions.
3. **Settings "Sistem" Tab & Card Elimination**: Permanently removed the obsolete "Sistem" tab button from the visible filter bar, verified the 7 visible tabs (`All`, `Appearance`, `Artwork`, `Audio`, `Updates`, `Shortcuts`, `About`), and renamed the system integration card in Appearance settings from "Tab_System" ("Sistem") to "General_SystemIntegration" ("Sistem ve Entegrasyon").
4. **Localization Parity**: Audited and fixed all hardcoded English strings across Settings tabs, audio diagnostics, theme names, frequency pickers, tooltips, and lyrics view controls. Achieved 100% key parity between English and Turkish in `LocalizationService`.
5. **Compact-to-Normal Switch Icon**: Replaced list glyph `&#xE8FD;` with diagonal expand arrow `&#xE740;` and added localized tooltip.

---

## 2. Root Cause Analysis & Technical Solutions

### A. Normal Mode Layout & Controls Visibility
- **Root Cause**:
  `LyricsViewportCalculator.ComputePlayerArtworkMaxHeight` previously reserved only 280px for the controls below the album artwork. When rendered with default Windows system DPI (100%–125%), the combined height of song title/artist, time progress bar, output device selector, playback control row, and volume control row exceeded 300px. As a result, the artwork took too much vertical space, pushing the lower controls and the "Hide Lyrics" button down into the scroll viewport.
- **Solution**:
  1. Updated `LyricsViewportCalculator.cs` to reserve 340px for controls:
     ```csharp
     double maxArtByHeight = Math.Clamp(playerAvailableH - 340.0, 120.0, 560.0);
     ```
  2. In `LyricsWindow.xaml`, adjusted `PlayerColumnContainer` to `VerticalAlignment="Stretch" Margin="24,8"`, tightened margins on playback hitboxes and rows, and maintained `PlayerScrollViewer` with `VerticalScrollBarVisibility="Auto"` as an overflow safeguard.
  3. Ensured default window size (960x720) and minimum size (640x520) accommodate all elements gracefully.

### B. Karaoke Word Highlight Concurrency on Wrapped Lines
- **Root Cause**:
  In WinUI 3, `UIElement.Clip` is restricted to `RectangleGeometry`. When a long lyric line wrapped within a single `TextBlock`, the clipping rectangle `Rect(0, 0, progressX, totalHeight)` covered the entire bounding box height of all lines. Because `totalHeight` included line 2, any words positioned horizontally within `[0..progressX]` on line 2 were painted with the white fill simultaneously with the first words on line 1.
- **Solution**:
  1. In `LyricsWindow.xaml.cs` (`RebuildLyricsUi`), wrapped lines are broken into discrete sub-blocks with `TextWrapping.NoWrap` inside `lineStack`. Each visual subline has its own distinct base `TextBlock` and overlay `TextBlock`, allowing independent `RectangleGeometry` clips per line.
  2. In `LyricsFillProgressCalculator.cs` (`CalculateSequentialSubLineProgresses`), word timings are partitioned strictly across sublines:
     - For any subline whose time slice has not yet arrived (`currentPosition < slice.Start`), progress is strictly `0.0` (overlay completely clipped/invisible).
     - When playback is within a subline's word timings, progress maps only to that subline's words.
     - When playback passes a subline's slice end, progress is clamped to `1.0`.
  3. Preserved line-based smooth fill (LRCLIB non-karaoke sources) without synthesizing artificial word timings.

### C. Settings "Sistem" Tab & Header Cleanup
- **Root Cause**:
  In Stage 38, `BtnFilterSystem` was collapsed but remained a direct child of the visible tab stack panel. Furthermore, `TxtSectionSystem.Text` inside the "Appearance & Window" section was bound to `loc["Tab_System"]`, rendering the word `"Sistem"` as a card header, misleading users into thinking the tab was still active.
- **Solution**:
  1. Moved `BtnFilterSystem` out of `SettingsFilterBarPanel` into a collapsed background container, retaining its XAML name for legacy reference stability without being part of the visible tab bar children.
  2. Introduced `VisibleSettingsTabKeys = ["All", "Appearance", "Artwork", "Audio", "Updates", "Shortcuts", "About"]` and `GetVisibleSettingsTabs()` in `MainWindow.xaml.cs`.
  3. Replaced card header key with `General_SystemIntegration` ("Sistem ve Entegrasyon" / "System & Integration").
  4. Preserved all system settings (Startup, Tray, Notifications) under "Appearance & Window" card and "All Settings".

### D. Localization Parity
- **Root Cause**:
  Several options in Settings (Theme ComboBox items, Update Frequency items, Audio Capture diagnostics) and tooltips in LyricsWindow were hardcoded in English.
- **Solution**:
  1. Added localized keys to both `en` and `tr` in `LocalizationService.cs`:
     - `Theme_SystemDefault`, `Theme_Dark`, `Theme_Light`
     - `Updates_Freq_OnStartup`, `Updates_Freq_Daily`, `Updates_Freq_Weekly`, `Updates_Freq_Monthly`
     - `Audio_CaptureEndpoint`, `Audio_EndpointStatus`, `Audio_FftSize`, `Audio_DspRefresh`, `Audio_SilenceThreshold`
     - `About_Subtitle`, `About_TechStack`, `About_RestartWizard`, `Updates_ViewRelease`
     - `Compact_NormalMode`, `Compact_ToggleLyrics`, `Lyrics_HideLyrics`, `Lyrics_ShowLyrics`, etc.
  2. Dynamically localized items in `MainWindow.xaml.cs` and `LyricsWindow.xaml.cs` on launch and whenever `LanguageChanged` fires.

### E. Compact-to-Normal Switch Icon
- Replaced hamburger/list glyph `&#xE8FD;` with diagonal expand/open arrow `&#xE740;` in `LyricsWindow.xaml` (`IconNormalModeCompact`).
- Added localized tooltip `Compact_NormalMode` ("Normal Moda Geç" / "Switch to Normal Mode").

---

## 3. Verification & Test Results

### Automated Tests
- **Solution Total**: 673 tests across 4 test projects.
- **Result**: **673 PASS, 0 FAIL**.
- **Stage 39 Dedicated Test Suite** (`LyrowStage39RecoveryAndPolishTests.cs`):
  - `NormalMode_PlayerColumn_HasSufficientControlClearance`: PASS (reserves >= 320px).
  - `NormalMode_PlayerColumn_UsesStretchAndScrollSafeMetrics`: PASS.
  - `WrappedLine_WordHighlight_DoesNotBleedToSecondLine`: PASS (subline 2 progress is strictly 0.0 while line 1 is singing).
  - `WrappedLine_WordHighlight_ProgressesSequentiallyAcrossLines`: PASS (line 1 completes to 1.0 before line 2 advances).
  - `NonKaraoke_LineLevelProgress_DoesNotSynthesizeWordTimings`: PASS.
  - `Settings_VisibleTabKeys_ExcludesSystemTab`: PASS (7 visible tabs, System excluded).
  - `Settings_VisibleFilterBar_ContainsOnlyExpectedSevenTabs`: PASS.
  - `Settings_SystemSection_UsesSystemIntegrationHeaderKey`: PASS.
  - `Localization_AllStage39Keys_HaveTurkishAndEnglishParity`: PASS.
  - `LyricsWindow_CompactSwitchIcon_UsesExpandArrowGlyph`: PASS (`&#xE740;`).
  - `LyricsWindow_AllTooltipsAndControls_AreLocalized`: PASS.
  - `SettingsWindow_ComboBoxItems_AreFullyLocalized`: PASS.

---

## 4. Manual Testing Checklist for User

| Test Case | Steps | Expected Result |
| :--- | :--- | :--- |
| **1. Normal Mode Controls Visibility** | Open Lyrow in Normal mode on standard 1080p / 1440p display. | Album art scales proportionally; Title, Artist, Timeline bar, Audio Output picker, Play/Pause/Skip buttons, Volume slider, and "Hide Lyrics" button are all visible immediately without vertical scrolling. |
| **2. Hide / Show Lyrics Button** | Click "Hide Lyrics" button on the bottom right. | Lyrics panel collapses smoothly; button toggles to "Show Lyrics". Clicking it again expands the lyrics pane. |
| **3. Wrapped Karaoke Lyrics** | Play a track with long karaoke lines (word-by-word synced) that wrap into two visual lines. | While the first line's words are highlighted in white, the second line's words remain muted gray with zero white fill until playback timestamp reaches line 2. |
| **4. Settings Tabs Navigation** | Open Settings. Inspect the top tab bar. | Exactly 7 tabs are displayed: *Tümü, Görünüm ve Pencere, Albüm Kapağı, Ses Motoru, Güncellemeler, Kısayollar, Hakkında*. "Sistem" tab is not present. |
| **5. System Integration Settings** | Click "Görünüm ve Pencere" (or scroll down in "Tümü"). | The card header reads "Sistem ve Entegrasyon" and contains "Windows ile Başlat", "Sistem Tepsisine Küçült", and "Şarkı Değişim Bildirimleri". |
| **6. Language Switching** | Switch language from English to Türkçe and vice-versa in Settings. | Theme names (Sistem / Koyu / Açık), Update frequencies, Audio DSP labels, and Lyrics window tooltips switch immediately without restarting. |
| **7. Compact Mode Expand Icon** | Switch to Compact mode. Hover over the top-right button. | The icon is a diagonal expand arrow (`\uE740`), and its tooltip reads "Normal Moda Geç" (TR) or "Switch to Normal Mode" (EN). |
