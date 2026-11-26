# Lyrow v4.7 — Single-Screen Fluent Welcome Experience

Lyrow v4.7 streamlines the first-run Welcome experience into a clean, focused, single-screen Windows 11 Fluent Design introduction — no multi-step wizard, no forced device or theme setup, and instant entry into your music with one clear button.

## What's New in v4.7

### Redesigned Single-Screen Welcome Experience
- **Immediate Value, Zero Friction:** Replaced the 5-step setup wizard with a crisp, single-screen `MicaBackdrop` (`BaseAlt`) welcome window (`680×550` DIP, DPI-aware and centered, zero scrolling).
- **Clear Identity & Messaging:** Displays the Lyrow brand logo and hero artwork with the headline **"Your Music. Your Lyrics. Your Atmosphere."** (*"Müziğin. Şarkı Sözlerin. Atmosferin."*) and a single-sentence overview.
- **Three Concise Benefits:** Highlights synchronized real-time lyrics, music-reactive visuals, and audio-reactive lighting without technical jargon.
- **One Dominant Primary Action:** Large, high-contrast **Start Listening** (*Dinlemeye Başla*) accent button plus a subtle **Customize later** (*Daha sonra özelleştir*) secondary option.
- **Respects System Theme & Defaults:** Uses your current Windows theme automatically without interrupting startup with theme, audio device, or lighting configuration prompts.
- **Light & Fast Entrance:** Smooth `160 ms` entrance animation (respecting `EnableAnimations`) and full keyboard accessibility (`Enter` to start, `Esc` to dismiss).
- **Safe Upgrade & Manual Reopen:** Existing v4.5 and v4.6 users upgrading to v4.7 never see the Welcome screen again and keep 100% of their settings; anyone can still reopen it manually anytime from **Settings > General** or **Settings > About**.

### Full English & Turkish Localization
- Complete, natural English and Turkish copy verified visually on live WinUI 3 windows with zero text clipping or overflow.
