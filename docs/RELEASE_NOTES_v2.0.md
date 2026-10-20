# Lyrow v2.0 — Major UX, Audio, Lyrics & Glow Overhaul

**Sürüm:** `v2.0`  
**Platform:** Windows 11 Native (`WinUI 3` / `Windows App SDK 1.6` / `.NET 10.0`)

---

## 🎯 Genel Bakış

**Lyrow v2.0**, `v1.3` temel alınarak geliştirilen ve uygulamanın görsel tasarımını, ses kontrolünü, medya yönetimini, Lumn tarzı 4 kenar ambiyans aydınlatmasını ve Apple Music tarzı şarkı sözü deneyimini baştan sona yenileyen kapsamlı bir ana sürümdür.

---

## 🚀 Yenilikler ve Düzeltmeler

### 1. Lumn Tarzı 4 Kenar Ambient Glow Motoru
- **4 Bağımsız Kenar Enerjisi:** Üst (`Top`), Alt (`Bottom`), Sol (`Left`) ve Sağ (`Right`) ekran kenarları için bas, mid, tiz ve transient (vuruş) frekanslarından beslenen bağımsız yoğunluk hesaplaması.
- **Akıcı Zarf (Attack → Hold → Decay):** Keskin aç/kapa (ON/OFF) davranışı yerine `Audio Energy → Smoothing → Attack → Hold → Decay → Rendered Intensity` modeliyle vuruş geldiğinde parlaklığın hızla yükselmesi, kısa süre tutunması ve yumuşakça sönümlenmesi.
- **Ayrılmış STA Win32 Render Thread'i:** `GlowOverlayWindow` kendi mesaj döngüsüne sahip ayrılmış STA thread'inde 32-bit pre-multiplied ARGB DIB ve `UpdateLayeredWindow` kullanarak 60 FPS akıcılığında çizim yapar.
- **Sıfır Etkileşim Engeli (Click-Through):** `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW` ve `WM_NCHITTEST -> HTTRANSPARENT` korunarak fare, klavye, kaydırma (scroll), Alt+Tab ve görev çubuğu etkileşimlerinin engellenmemesi garanti altına alındı.

### 2. Albüm Kapağı → Canlı Glow Renk Paleti (Artwork Color Extraction)
- **WIC Gerçek Piksel Çözümleme:** GSMTC üzerinden gelen albüm kapağı akışı Windows Imaging Component (`BitmapDecoder`) ile 32×32 RGBA piksellerine dönüştürülür.
- **Canlı Renk Güçlendirme (`BoostVibrancy`):** Dominant ve vurgu renkleri HSV uzayında doygunluk ve parlaklık açısından güçlendirilerek mor, mavi, kırmızı, turuncu, yeşil ve pembe gibi zengin ambiyans tonları üretilir.
- **Yumuşak Geçiş (`GlowPalette.Lerp`):** Şarkı değiştiğinde eski paletten yeni palete ~800ms süren pürüzsüz renk enterpolasyonu uygulanır.

### 3. Apple Music Tarzı 60 FPS Lyrics & Tam Dikey Merkezleme
- **Daima Merkezde Aktif Satır:** `LyricsViewportCalculator` ve `TransformToVisual` kullanılarak aktif şarkı sözü satırı her zaman pencerenin tam dikey merkezinde (`viewportHeight / 2`) konumlandırılır.
- **60 FPS Enterpolasyonlu Kaydırma ve Tipografi:** Aktif satır, yakın satırlar (`±1`, `±2`) ve uzak satırlar arasında opaklık, ölçek ve tipografi geçişleri ~16ms (60 FPS) zamanlayıcı ile yumuşakça enterpole edilir.
- **Gerçek Zaman Çizelgesi & Seek Desteği:** Play, Pause, Seek, Next ve Previous olaylarında zaman çizelgesi gerçek zamanlı hesaplanır; ileri/geri sarma (seek) yapıldığında binary search ile yeni aktif satır anında bulunup merkeze kaydırılır.

### 4. Gerçek Windows Ses Kontrolü (`0% — 100%`)
- **Doğrudan Windows Ses Seviyesi:** UI'daki `0%–100%` Slider kontrolü, Windows CoreAudio (`IAudioSessionManager2` / `ISimpleAudioVolume` ve `IAudioEndpointVolume`) üzerinden aktif medya oturumunun ve sistem sesinin gerçek seviyesini değiştirir.
- **dBFS Kaldırıldı:** Kullanıcı arayüzünde yalnızca anlaşılır `0% — 100%` yüzde gösterimi yer alır.
- **Audio Signal Kutusu Kaldırıldı:** Gereksiz `Audio Signal` durum kartı XAML ve kod tabanından tamamen temizlendi.

### 5. Gerçek GSMTC Medya Kontrolleri
- **Play / Pause / Previous / Next:** Doğrudan aktif Windows GSMTC medya oturumuna komut gönderir ve durum değiştiğinde UI'ı anında günceller.
- **Oturum Yokken Güvenli Davranış:** Aktif medya oturumu bulunmadığında çökme yaşanmaz, kontroller devre dışı bırakılır ve kullanıcıya bilgilendirici `InfoBar` gösterilir.

### 6. Otomatik Güncelleme (Auto Update)
- **GitHub Releases Kontrolü:** Settings sayfasına eklenen **Auto Update** bölümü ile mevcut sürüm (`v2.0`) bilgisi, otomatik kontrol anahtarı (varsayılan `ON`) ve manuel **"Check for Updates"** aksiyonu sunulur.
- **Güvenli ve Kullanıcı Odaklı:** Kullanıcı onayı olmadan uygulamayı kapatmaz veya dosya değiştirmez; çevrimdışı durumda hata/çökme üretmez.

### 7. Kaydırma (Scroll), DPI ve Arayüz İyileştirmeleri
- **Fare Tekerleği & Touchpad Kaydırma:** `ScrollViewer` yapılandırması yenilenerek fare tekerleği, touchpad, `PageUp`/`PageDown`/`Home`/`End` tuşları ve kaydırma çubuğu tam uyumlu hale getirildi.
- **PerMonitorV2 DPI Keskinliği:** `app.manifest` ve pencere DPI ölçeklemesi ile `%100`–`%200` ekran ölçeklerinde net metin ve ikon görünümü sağlandı.
- **Kompakt Kenar Çubuğu:** Sol `NavigationView` genişliği `240px` yerine `188px` yapılarak içerik alanına daha fazla yer açıldı.

---

## 📦 Kurulum Dosyaları

- **Kurulum Sihirbazı (Installer):** `Lyrow-2.0-Setup-x64.exe`
- **Taşınabilir Paket (Portable):** `Lyrow-v2.0-win-x64-portable.zip`
