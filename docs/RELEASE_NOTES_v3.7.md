# Lyrow v3.7 — Playback Timeline Engine Rewrite & Header Cleanup

**Sürüm:** `v3.7`  
**Hedef Platform:** Windows 10 (19041+) / Windows 11 Native (`C#` / `.NET 10 LTS` / `WinUI 3` / `Windows App SDK 1.8` / `win-x64`)  
**Dağıtım Bağlantısı:** [GitHub Release v3.7](https://github.com/owergungor/Lyrow/releases/tag/v3.7)  
**Doğrulama Durumu:** **635 / 635 Test PASS (%100 Başarı)**, GitHub Actions CI SUCCESS  

---

## 🎯 v3.7 Genel Bakış

Lyrow v3.7 sürümü, gerçek kullanıcı kabul testlerinde tespit edilen playback süresi kümülatif artış kusurunu kökünden çözmek üzere tasarlanan **Merkezi Playback Timeline Motoru (`IPlaybackTimelineEngine`)** mimarisini, şarkı başına izole süre ve kalan süre hesaplamasını, otomatik şarkı geçişlerinde (`A → B → C`) tam parça izolasyonunu ve Lyrics penceresi sol üst başlık metninin minimalist bir anlayışla kaldırılmasını içerir.

---

## 🚀 Temel Yenilikler ve Düzeltmeler

### 1. Merkezi Playback Timeline Motoru (`IPlaybackTimelineEngine`)
- **Tek Yetkili Zaman Kaynağı (Single Source of Truth):**
  Önceki sürümlerde `WindowsGsmtcMediaSessionMonitor`, `LyricsModule` ve `LyricsWindow` pencereleri arasında zaman çizelgesi hesaplamaları yer yer ayrı ayrı yürütülmekteydi. v3.7 ile `Lyrow.Core.Timeline.PlaybackTimelineEngine` merkezi otorite olarak konumlandırıldı.
- **Kümülatif Süre Bug'ının Giderilmesi:**
  Kullanıcının ardışık şarkı dinlemelerinde süre sayacının önceki şarkıların sürelerini toplayarak ilerlemesi (örn. 3:30'luk ilk şarkı bittikten sonra başlayan şarkının süresinin 3:31, 3:32... şeklinde görünmesi) engellendi. Artık dinlenen süre yalnızca o an aktif olan parçanın başlangıcından itibaren geçen süreyi (`CurrentTrackPositionFormatted`) gösterir.
- **Doğru Kalan Süre Hesaplaması:**
  Kalan süre hesabı, kümülatif veya harici kaymalardan tamamen izole edilerek kesin olarak `CurrentTrackDuration - CurrentTrackPosition` formülüyle eksi işaretli formatta (`CurrentTrackRemainingFormatted`, örn. `-2:10`) sunulur.

### 2. Otomatik Şarkı Geçişlerinde (`A → B`) Tam İzolasyon
- Şarkı değiştiğinde (`MediaTrackInfo` değişikliği), önceki parçanın tüm enterpolasyon değişkenleri, son pozisyonu ve önbelleğe alınmış zaman damgaları atomik olarak temizlenir.
- Otomatik şarkı geçişi sonrasında (ilk 3 saniye içinde) GSMTC'den gelebilecek olan eski şarkının son anlarına ait gecikmeli (stale) geri çağırmalar (`pos >= _previousTrackDuration - 5s`) filtrelenerek yeni parçanın zaman çizelgesini bozması engellenir.
- Web oynatıcılarının (YouTube Music Web / Apple Music Web) ardışık akışlarında parça süresini aşan kümülatif ofsetler (`pos > duration + 1s`), parça başlangıcından itibaren geçen gerçek duvar saati süresine yumuşakça dengelenir.

### 3. Lyrics Penceresi Başlık Sadeleştirmesi
- [`LyricsWindow.xaml`](file:///c:/Users/omer/Projects/Lyrow/src/Lyrow.App/LyricsWindow.xaml) ve [`LyricsWindow.xaml.cs`](file:///c:/Users/omer/Projects/Lyrow/src/Lyrow.App/LyricsWindow.xaml.cs) üzerindeki sol üst `Lyrow — Synchronized Lyrics` metni tamamen kaldırıldı.
- Başlık çubuğu alanı boş bırakılarak Neumorphic arka planın sade estetiği öne çıkarıldı; pencere sürükleme yeteneği ve kontrol butonları eksiksiz korundu.

### 4. Normal Mod ve Dar Mod Senkronizasyonu
- Hem Normal Mod (tam ekran / geniş sözler) hem de Dar Mod (352×148 px kompakt mini oynatıcı) arayüz zamanlayıcıları (`OnUiTimerTick`) aynı merkezi motoru sorgulayarak tam olarak özdeş süre değerlerini ekrana yansıtır.

---

## 📦 Dağıtım ve Paket Doğrulama

- **Paket Adı:** `Lyrow-v3.7-win-x64.zip`
- **Tür:** Bağımsız (Self-Contained) Windows x64 Arşivi
- **Boyut:** 97.863.708 bayt (~93,33 MB)
- **SHA-256:** `601664444F7D983B05E3F675D220E8F25EDB7701520D4E923EFCD8F4E0A85B73`
- **İndirme:** https://github.com/owergungor/Lyrow/releases/download/v3.7/Lyrow-v3.7-win-x64.zip

### Test ve Doğrulama Sonuçları
- **Birim ve Entegrasyon Testleri:** **635 / 635 PASS (%100 Başarı)**
  - `Lyrow.Core.Tests`: 59 PASS
  - `Lyrow.Audio.Tests`: 53 PASS
  - `Lyrow.Music.Tests`: 120 PASS
  - `Lyrow.App.Tests`: 403 PASS (Stage 33 için 13 yeni timeline regresyon testi dahil)
- **GitHub Actions CI:** Run `42788361887` — SUCCESS.
- **Yerel Smoke Test:** `Lyrow.App.exe --smoke-test` -> Exit code 0 (PASS).
- **Kullanıcı Canlı Doğrulaması:**
  - Dinlenen süre yalnızca mevcut parçaya ait: **PASS**
  - Kalan süre doğru hesaplanıyor: **PASS**
  - Lyrics sol üst başlık temizliği: **PASS**
