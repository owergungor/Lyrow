# Lyrow v3.5 — Fluid Lyrics, High-Res Artwork & Advanced Beat Reactivity

**Sürüm:** `v3.5` (`ProductVersion: 3.5`, `FileVersion: 3.5.0.0`)  
**Taban Sürüm:** `v3.4` (`f88cca7e805a10ef8f9be4c2b473bf9f897f4d7d`)  
**Platform:** Windows 10 (19041+) / Windows 11 Native (`C#` / `.NET 10 LTS` / `WinUI 3` / `Windows App SDK` / `XAML` / `MVVM`)

---

## 🎯 v3.5 Özet

Lyrow v3.5; şarkı sözlerindeki yatay taşma ve kesilme sorunlarının tamamen giderilmesini, yüksek çözünürlüklü albüm kapağı korumasını, animasyonlu kapak desteğini (GIF/WebP), Edge Bloom (Glow) varsayılan kalınlığının optimize edilmesini, otomatik şarkı geçişlerinde (`A → B → C → D → E`) kusursuz şarkı sözü arama/yükleme ve temizleme güvencesini, tıklama ve sürükleme ile deterministik seek desteğini ve gerçek zamanlı müzik ritmi algılamasını içerir.

---

## 🚀 Temel Geliştirmeler ve Düzeltmeler

### 1. Lyrics Görünümünde Yatay Taşma ve Kesilme Sorununun Giderilmesi
- **Otomatik Satır Sarma (TextWrapping):** `baseBlock` ve `overlayBlock` metin blokları `TextWrapping.Wrap` moduna geçirilerek uzun sözlerin yatay pencere sınırlarını aşması engellendi.
- **Uzun Kelimelerin Bölünmesi:** `LyricsFillProgressCalculator.SplitIntoSequentialSubLines` içerisine uzun ve boşluksuz kelimeleri/token'ları sınır genişliğine göre güvenle bölen mantık eklendi.
- **Dinamik Yatay Dolgu:** `UpdateViewportPadding` pencere genişliğine (`LyricsScrollViewer.ActualWidth`) göre yatay dolguyu dinamik hesaplayarak (`Math.Clamp(actualWidth * 0.05, 24.0, 56.0)`) dar ve geniş pencerelerde optimum kenar boşluğu sağladı.
- **Aktif Satır Ölçekleme Düzeltmesi:** Aktif satır hedef ölçeği `1.04`'ten `1.0`'a çekildi; böylece aktifleşen satırın büyüme etkisiyle kenarlardan kesilmesi önlendi, dikey odaklama ve opaklık hiyerarşisi korundu.

### 2. Albüm Kapağı Çözünürlüğü ve Kalite Koruması
- `ArtworkBitmapManager` ham görsel baytlarını en yüksek kalitede önbellekte tutar; GSMTC veya oynatıcıdan gelen sonraki düşük çözünürlüklü küçük resimlerin önbellekteki yüksek çözünürlüklü kapağın üzerine yazması engellendi.
- Görsel yükleme öncesinde yapay downsampling yapılmaz, WinUI 3 donanım hızlandırmalı ölçekleyicisi kullanılır.

### 3. Edge Bloom (Glow) Varsayılan Kalınlığı
- Varsayılan `GlowThickness` değeri hem ayarlarda (`LyrowSettings`), hem render katmanında (`GlowOverlayWindow`), hem de kullanıcı arayüzünde (`MainWindow.xaml`) `24 px` olarak güncellendi.

### 4. Animasyonlu Albüm Kapağı Desteği
- `BitmapImage.AutoPlay = true` özelliği etkinleştirilerek animasyonlu GIF ve WebP kapaklarının doğrudan WinUI 3 donanım hızlandırmasıyla oynatılması sağlandı. Şarkı değişiminde animasyon döngüsü temizlenerek sızıntı önlendi.

### 5. Progress Bar Üzerinden Deterministik Seek
- Yatay ve Dar Mod ilerleme çubukları (`GridProgressHitBox`, `GridProgressCompactHitBox`) üzerinden tıklanan veya sürüklenen konuma göre milisaniye hassasiyetinde `TrySeekAsync` çağrılır ve şarkı sözü zamanlayıcısı anında yeni konuma kilitlenir.

### 6. Otomatik Parça Geçişlerinde (`A → B → C → D → E`) LRCLIB Arama Güvencesi
- Çoklu ardışık şarkı geçişlerinde arama isteklerinin iptal edilip sözsüz kalması önlendi.
- `LrclibClient` çok aşamalı arama stratejisi yürütür:
  1. Tam eşleşme (`/api/get` başlık + sanatçı + süre).
  2. Süresiz tam eşleşme (`/api/get` başlık + sanatçı).
  3. Parça adından sürüm ve konuk sanatçı eklerini temizleyen sorgu (`- Remastered`, `feat.`).
  4. Geniş arama motoru (`/api/search?q={query}`) ve en yüksek puanlı eşleşme seçimi.

### 7. Önceki Şarkı Sözlerinin Atomik Olarak Temizlenmesi
- Yeni şarkıya geçildiğinde önceki şarkının tüm satırları, aktif satır indeksi ve kaydırma konumu atomik olarak sıfırlanır; yeni şarkının sözleri gelene kadar temiz yükleme durumu sunulur.

### 8. Arkaplan ve Glow Renklerinin Yenilenmesi
- Yeni şarkının albüm kapağından 5 anahtar renk (`Vibrant`, `Muted`, `Dominant`, `SecondaryDominant`, `Background`) çıkarılır.
- Arkaplan parlaklığı sinematik `<= 55 cd/m²` sınırıyla yumuşak cross-fade geçişle güncellenir; ekranın 4 kenarındaki Glow efektleri ve ritim motoru yeni parçaya göre sıfırlanır.

### 9. Gelişmiş Gerçek Zamanlı Beat, Tempo ve Transient Algılama
- WASAPI loopback akışı üzerinden 5 frekans bandı (Sub-Bass, Bass, Low-Mid, Mid, High-Mid/Treble) Spectral Flux ile analiz edilir.
- Otokorelasyonlu tempo ve vuruş takibiyle gerçek müzik ritmine uyumlu dinamik kenar ışıması sağlanır.

---

## 📦 Dağıtım ve Paket Doğrulama

- **Kurulum Paketi (Installer):** `Lyrow-3.5-Setup-x64.exe`  
  - SHA256: `9FF74CEF9CE0B1646B2AC4559D58BA95F34B6CFAE5178D9FB155073A26B407EC`
- **Taşınabilir Sürüm (Portable):** `Lyrow-v3.5-win-x64-portable.zip`  
  - SHA256: `7626932A03A2EFC83FEC49DC618D7BEB15A74EC6B01AB5F91E5C94D478FCAC71`

### Test ve Doğrulama
- 362/362 unit ve entegrasyon testi eksiksiz geçti (`0 failed, 0 warnings`).
- Windows Native Smoke Test (`test_smoke.ps1`) ile başlatma (733ms), HWND yakalama (3344328) ve temiz kapatma doğrulandı.

