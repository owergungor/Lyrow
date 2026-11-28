# Lyrow v5.0

Lyrow v5.0 introduces a multi-provider synchronized lyrics engine, redesigned 5-step **Welcome V4** onboarding, drag-and-drop lyrics source ordering, live provider badges, keystroke shortcut recording, and improved remaining-time & seek reliability for Windows 10 and 11 (x64).

---

## Highlights / Öne Çıkan Yenilikler

- **Multi-Provider Lyrics Synchronization Engine (`LyricsSyncEngine`):**
  - Supports **Syllable**, **Word**, **Line**, **Asynchronous**, and **Automatic** synchronization with O(log N) evaluation and zero per-frame heap allocations.
  - Queries **Better Lyrics (TTML)**, **LiriQo** (Apple Music, QQ Music, NetEase, KuGou, Kuwo, Migu, PetitLyrics), **Better Lyrics Unison**, and **LRCLIB** in parallel, progressively upgrading to the highest genuinely supported timing granularity (`Syllable → Word → Line → Asynchronous`).
- **6-Dot Drag-and-Drop Source Priority & Live Provider Badge:**
  - Reorder lyrics providers directly in **Settings > Lyrics** via 6-dot drag handles; preferences persist across restarts while preserving syllable-over-word quality priority.
  - Displays a subtle, theme-aware source code pill (`WEB`, `APL`, `SPT`, `BLU`, `LRC`, `QQ`, `NTE`, `KG`, `KW`, `MG`, `PL`) in both Normal and Compact lyrics views showing the real upstream provider.
  - Removed manual `±250 ms` timing offset controls to keep lyrics settings clean (legacy `settings.json` entries are safely ignored).
- **Welcome V4 First-Run Experience:**
  - Redesigned responsive 5-step Windows 11 Fluent onboarding with zero horizontal overflow in Dark & Light themes (English & Turkish).
  - Music Reactivity defaults to **Expressive (Belirgin — Recommended)** on fresh installs while preserving existing users' saved preferences.
- **Keystroke Shortcut Recorder:**
  - Record global hotkey combinations directly in **Settings > Shortcuts** with built-in conflict and modifier validation.
- **Remaining Duration, Click-to-Seek & System Tray Polish:**
  - Accurate remaining song time calculation (`GsmtcDurationResolver`) handling non-zero `StartTime`, missing/invalid `EndTime`, live streams (`--:--`), and track switches.
  - Unified seek sequence guard (`_activeSeekSequence`), live-index lyric click mapping (`LyricLineClickCoordinator`), and preserved `Paused` state when seeking while paused.
  - System tray icon display name and tooltip simplified to `Lyrow`.

---

## Downloads & SHA-256 Verification

| Package | Size | SHA-256 |
| :--- | :--- | :--- |
| **Windows Installer (`Lyrow-5.0-Setup-x64.exe`)** | `64.84 MB` (`67,994,092` bytes) | `05E0F41212FDFBEE1760A2F3B68C3AF1B7DE4D5668FEC40661FB6033BC72638E` |
| **MSIX Package (`Lyrow-v5.0-x64.msix`)** *(unsigned)* | `94.88 MB` (`99,493,553` bytes) | `C339EB87D4E936D142A9B44699B052874BF5EAAF5420517759382465498747BC` |

> Self-contained Windows x64 build — no separate .NET Runtime or Windows App SDK installation is required. Use **Lyrow-5.0-Setup-x64.exe** for standard Windows installation.

---

## Validation

- **Automated Test Suite (Release, Serial):** **851 / 851 PASS** (`Lyrow.Core.Tests`: 99, `Lyrow.Audio.Tests`: 53, `Lyrow.Music.Tests`: 140, `Lyrow.App.Tests`: 559).
- **Binary & Manifest Identity:** `Lyrow.App.exe` (`5.0.0.0`), MSIX `crius.Lyrow` (`5.0.0.0`, `x64`, `runFullTrust`).
