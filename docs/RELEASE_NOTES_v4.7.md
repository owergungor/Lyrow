# Lyrow v4.7 — Album-Adaptive Light Theme & Welcome Experience

Lyrow v4.7 introduces an **Album-Adaptive Light Theme** that transforms every song's album cover into a luminous pastel atmosphere, comprehensive Light Theme readability fixes across every window and modal, and a redesigned **single-screen Immersive Welcome Experience** with live preference controls.

---

## What's New in v4.7

### Album-Adaptive Light Theme & Full Readability Overhaul
- **Dynamic Pastel Atmosphere per Album:** Instead of a flat white or gray canvas, Light Theme now uses Lyrow's existing album artwork color analysis (`ArtworkPaletteExtractor.CreateLightAmbientPalette`) to preserve each cover's dominant hues in HSL space and render airy, high-contrast pastel gradients (lavender/pastel purple, ice sky blue, rose blush, soft sage green, warm cream/amber).
- **Smooth Track-to-Track Cross-Fade:** Background colors transition smoothly at 60 FPS whenever the playing track changes, while turning Dynamic Background off preserves the static Mica Light/Dark surface.
- **High-Contrast Typography Across Every View:**
  - Theme-aware dynamic active (`#0B1220`) and passive (`#C42D3748`) lyric brushes update immediately when switching themes across both the **Main Lyrics Window** and **Mini Player (Compact Mode)**.
  - Softened Light-mode frosted scrim (`#36FFFFFF`) and modal backdrop (`#400F172A`) keep album atmosphere vibrant without washing out contrast or blackening the window.
  - Fixed Settings Modal 7-tab left sidebar navigation to use theme-aware high-contrast ink brushes in Light Theme while preserving Dark Theme 100%.

### Redesigned Single-Screen Immersive Welcome Experience
- **Cinematic Live Preview Hero:** A glowing 4-edge Edge Light aura framing a frosted-glass miniature Lyrow player with animated 5-band audio spectrum bars, dynamic atmospheric lighting, and a highlighted real-time active lyric line that adapts live to your selected language.
- **Compact Live Preferences Bar:** Choose **Language** (`Türkçe` / `English`), **Theme** (`System` / `Dark` / `Light`), and **Music Experience** (**Edge Light**, **Dynamic Background**) on a single zero-scroll screen (`880×576` DIP) with instant live preview.
- **One Clear Primary Action:** Removed the redundant secondary button — click **Start Listening / Dinlemeye Başla** to save and apply your choices permanently. Closing via `X` or `Esc` safely discards temporary preview edits, restores prior settings, and does not mark Welcome as completed.
- **Full Upgrade Protection & Manual Reopen:** Existing v4.5 and v4.6 users keep 100% of their settings without seeing Welcome on upgrade, and anyone can reopen Welcome anytime from **Settings > General** or **Settings > About**.

---

## Downloads

Choose one of the two Windows x64 packages below:

- **Windows Installer (.exe)** — Standard Windows installation (recommended for most users):
  - `Lyrow-4.7-Setup-x64.exe` (`64.77 MB`)
  - SHA-256: `87345799863e402daa89d87e65e5e3c383ca373aa8008bb4a98d9d6963002ff6`
- **Microsoft Store Package (.msix)** — MSIX package for Windows x64 (`crius.Lyrow`, `4.7.0.0`, `runFullTrust`):
  - `Lyrow-v4.7-x64.msix` (`94.80 MB`)
  - SHA-256: `2963af8c886caa79a428071843b497de0e99eb554331fd1006136a4e50ff264f`
  - *Note: This `.msix` package is currently **unsigned** (prepared with Microsoft Partner Center identity; not yet published on the Microsoft Store). For local sideloading, sign it with a trusted certificate, or use the Windows Installer (`.exe`) above.*

---

## Verification Summary
- **Deterministic Serial Release Test Suite:** **752 / 752 PASS** (`Lyrow.Core.Tests`: 83, `Lyrow.Audio.Tests`: 53, `Lyrow.Music.Tests`: 120, `Lyrow.App.Tests`: 496).
- **Live Visual Audit:** Verified on live WinUI 3 windows across Light & Dark themes (Main Lyrics, Mini Player, 7-Tab Settings Modal, Single-Button Welcome EN/TR) and v4.5/v4.6 upgrade isolation.
