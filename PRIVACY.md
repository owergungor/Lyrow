# Lyrow Privacy Policy

**Last Updated:** November 24, 2026  
**Publisher:** crius  
**Application:** Lyrow (Windows 10 / 11 Desktop Application)  
**Project Repository:** https://github.com/owergungor/Lyrow

---

## Introduction

This Privacy Policy explains how **Lyrow** handles information when you use the Lyrow desktop application on Windows 10 and Windows 11. Lyrow is designed as a privacy-first, local Windows music companion for synchronized lyrics, ambient edge lighting, and compact media controls.

We believe your listening habits and personal data belong to you. Lyrow does **not** require an account, does **not** collect personal information, and does **not** include telemetry, analytics, or advertising SDKs.

---

## Information We Collect

Lyrow does **not** collect personal information or operate any backend servers that store user data.

To provide its core features, Lyrow processes the following **non-personal, ephemeral media and playback information locally on your device**, and sends minimal search queries to public third-party services only when required for those features:

1. **Currently Playing Media Metadata (Local & Ephemeral):**
   - Lyrow reads the active track title, artist name, album name, playback duration, playback position, playback status, and album thumbnail locally from the Windows Global System Media Transport Controls (GSMTC) API.
   - When fetching synchronized lyrics or fallback album artwork, only the track title, artist name, and (where applicable) album name and track duration are sent in standard HTTPS queries to public APIs (detailed under *Third-Party Services*).
2. **Real-Time Audio Output Levels (Strictly Local, In-Memory Only):**
   - When the optional Edge Light feature is enabled, Lyrow analyzes system audio output locally in real time using Windows WASAPI Loopback to compute frequency spectrum energy for lighting effects.
   - Audio data is processed strictly in volatile memory (RAM). It is **never** recorded, written to disk, or transmitted over the network.
3. **Application Version for Update Checks (Optional):**
   - When update checks are enabled or triggered manually, Lyrow queries the public GitHub Releases API for the latest release metadata and sends a standard HTTP `User-Agent` header containing the application name and version (e.g., `Lyrow/4.4`).

---

## Information We Do Not Collect

Lyrow does **not** collect, store, sell, rent, or share:

- Names, email addresses, phone numbers, or physical addresses
- User accounts, login credentials, or music service authentication tokens
- Listening history or user profiles
- Device identifiers, hardware IDs, MAC addresses, or advertising IDs
- Location or GPS data
- Telemetry, usage analytics, or behavioral tracking data
- Automatic crash reports or diagnostic uploads
- Browser cookies or cross-app tracking data
- Audio recordings or microphone input

---

## How Information Is Used

The limited non-personal information processed by Lyrow is used exclusively to deliver core application functionality:

- **Synchronized Lyrics:** Track title, artist, album, and duration are used to look up synchronized `.lrc` lyrics and display them in time with playback.
- **Album Artwork & Dynamic Visuals:** Album thumbnails (and fallback artwork search results when unavailable locally) are used to display cover art and extract color palettes for dynamic backgrounds and Edge Light colors.
- **Audio-Reactive Edge Light:** Local in-memory WASAPI loopback spectrum data drives real-time screen-edge lighting effects.
- **Software Updates:** Public GitHub Releases metadata is checked to notify you when a newer version of Lyrow is available. (Note: Microsoft Store / MSIX installations are updated through the Microsoft Store rather than Setup EXE downloads.)

---

## Third-Party Services

Lyrow connects to the following public third-party services over HTTPS solely to provide its features. Lyrow does not send any personal account information or user identifiers to these services. Standard network metadata (such as your IP address and HTTP headers) is handled by these providers under their respective privacy policies:

1. **LRCLIB (`https://lrclib.net`)**
   - **Purpose:** Searching and retrieving synchronized and plain lyrics (`https://lrclib.net/api/get`, `https://lrclib.net/api/search`).
   - **Data Sent:** Normalized track name, artist name, optional album name, optional track duration, and standard `User-Agent` header.
2. **Apple iTunes Search API (`https://itunes.apple.com`)**
   - **Purpose:** Fallback high-resolution album artwork lookup (`https://itunes.apple.com/search`) when local media session artwork is unavailable or low resolution.
   - **Data Sent:** Track artist and title query string and standard `User-Agent` header.
3. **GitHub Releases (`https://api.github.com` / `https://github.com/owergungor/Lyrow`)**
   - **Purpose:** Checking for new Lyrow releases and, if you explicitly choose to install an update in the non-Store Setup edition, downloading the release installer.
   - **Data Sent:** Standard HTTPS request and `User-Agent` header (`Lyrow/<version>`). Automatic update checks can be disabled at any time in **Settings > Updates**.
4. **External Web Links (User-Initiated Only):**
   - If you click a release notes link or choose to open Apple Music Web (`https://music.apple.com/`), Lyrow opens the URL in your default web browser. Your interaction with those websites is governed by their respective privacy policies.

---

## Local Data Storage

All preferences, caches, and logs created by Lyrow remain strictly on your local Windows device and are never uploaded:

- **Application Preferences:** Stored locally in `%APPDATA%\Lyrow\settings.json` (or `settings.json` in the application folder when using the Portable edition). This includes theme, language, opacity, Edge Light settings, hotkeys, and update preferences.
- **Lyrics Cache:** Retrieved lyrics are cached locally in `%APPDATA%\Lyrow\LyricsCache\` as JSON files to improve performance and reduce repeated network requests.
- **Local Diagnostic Logs:** Zero-telemetry diagnostic logs are written locally to `%APPDATA%\Lyrow\logs\` (or `logs\` in Portable mode) solely to help you troubleshoot issues. They are never transmitted automatically.
- **Temporary Update Downloads (Setup Edition Only):** Downloaded update installers are stored temporarily in `%TEMP%\Lyrow\Updates\` and verified (Windows PE header and SHA-256 checksum) before execution.
- **In-Memory Caches:** Decoded artwork images and extracted color palettes are held temporarily in RAM and cleared when Lyrow closes.

You can reset your preferences at any time in **Settings > About > Reset to Defaults**, or remove all local data by deleting `%APPDATA%\Lyrow`.

---

## Data Security

- All outbound network requests (LRCLIB, Apple iTunes Search API, and GitHub Releases) use encrypted **HTTPS** connections.
- Downloaded Setup update packages are verified for Windows executable integrity and **SHA-256 checksum** before installation; corrupted or unverified packages are rejected and deleted.
- Because Lyrow does not collect personal data or maintain user databases, there is no centralized server storing your information.

---

## Children's Privacy

Lyrow does not knowingly collect any personal information from anyone, including children under the age of 13 (or the applicable age of digital consent in your jurisdiction). Because the application requires no account and collects no personal data, no personal information from children is gathered or stored.

---

## Changes to This Policy

We may update this Privacy Policy from time to time to reflect changes in Lyrow's features or applicable legal requirements. When we make changes, we will update the **"Last Updated"** date at the top of this policy and publish the revised version in the Lyrow repository and web page.

---

## Contact Information

If you have questions, concerns, or requests regarding this Privacy Policy or Lyrow's privacy practices, please reach out through our official project channels:

- **Publisher:** crius
- **GitHub Repository:** https://github.com/owergungor/Lyrow
- **Support & Privacy Inquiries (GitHub Issues):** https://github.com/owergungor/Lyrow/issues
