# Lyrow v2.7 Release Notes

Lyrow v2.7 brings an elevated music companion experience on Windows 11 with standardized sidebar navigation, a persistent in-app update toast displaying live download progress and retry handling, hardened automatic track transitions with instant timeline reset and async sequence guards, and precision click-to-seek and auto-centering for Apple Music-style lyrics.

---

## What's New in v2.7

### 1. Standardized Sidebar Navigation Order
- **Consistent Hierarchy:** Reorganized primary sidebar navigation items to strictly follow:
  1. **Home** (Dashboard and primary modules)
  2. **Playback** (Media sessions and audio stream routing)
  3. **Glow** (4-edge ambient lighting settings)
  4. **Lyrics** (Synchronized lyrics and typography settings)
- **Settings Footers:** Footer navigation preserved with General Settings and About.

### 2. Persistent Update Toast with Live Download Progress
- **Non-Dismissible During Download:** Toast notification remains pinned while self-update packages are downloading.
- **Real-Time Visual Progress:** Smooth progress bar with percentage readout (`Updating 45%`) updated continuously as chunks stream in.
- **Interactive Error Recovery:** On network failure, presents a "Retry" button allowing instant re-download without restarting the app.
- **Silent In-App Handoff:** Executes the downloaded installer silently (`/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS`) without opening web browsers or installation wizards.

### 3. Precision Lyrics Auto-Scroll & Click-to-Seek
- **Automatic Centering on Open:** Automatically computes and centers the active lyric line as soon as the lyrics window or panel opens, requiring zero manual clicks.
- **Continuous 60 FPS Smooth Centering:** Smooth easing equation targets viewport vertical center (`(viewport - itemHeight) / 2`).
- **Model-Bound Click-to-Seek:** Clicking any lyric line directly seeks playback to that exact line's immutable timestamp, recalculates karaoke character clipping instantly, and centers the line.

### 4. Hardened Automatic Track Transitions & Duration Reset
- **No Duration Summation:** When Track A finishes and transitions to Track B, the playback duration immediately updates to Track B's duration and position resets to 0. Duration accumulation (`Track A + Track B`) is completely eliminated.
- **Window Sequence Counter Guard:** Async artwork loading, palette extraction, and lyrics fetches are guarded by an atomic sequence counter. Late responses from Track A are discarded if Track B has started.
- **Animation & Karaoke Reset:** Progress clip geometry and font styles reset cleanly upon track switch.

### 5. Glow Default Standards & Migration Safety
- **Tuned Default Configuration:** Fresh installations default to:
  - **Brightness:** 80%
  - **Thickness:** 48 px
  - **Reactivity:** 250%
- **Settings Migration:** Existing user preferences stored in `%APPDATA%\Lyrow` are safely preserved without overwrite.

---

## Verification & QA

- **Strict Two-Part Versioning:** `v2.7` / `2.7`.
- **Zero Legacy Names:** 0 occurrences across entire active codebase.
- **Unit & Integration Tests:** 273 / 273 PASS (0 failures, 0 skipped).
- **Architecture:** Windows 11 + WinUI 3 + Windows App SDK + C# + .NET 10 LTS.
- **Supported Languages:** English, Turkish, French, Spanish, German.
