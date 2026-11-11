# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [3.8.0] - 2026-11-11

### Added
- **Karaoke Kelime Vurgulama Motoru:** Kelime zaman damgası (word-level timing) içeren şarkı sözlerinde aktif kelimelerin playback akışına paralel olarak soldan sağa beyaz renkte pürüzsüz biçimde vurgulanması sağlandı.
- **Satır Sarmalı (Wrapped) Karaoke Desteği:** İki veya daha fazla alt satıra bölünen uzun sözlerde, 1. alt satır tamamlanmadan ve 2. satırın zaman damgası gelmeden 2. satırdaki kelimelerin erken boyanması kesinlikle engellendi.
- **Stage 41 Regresyon ve Doğrulama Test Paketi:** Karaoke kelime vurgulama zamanlaması, alt satır izolasyonu, düz senkronize satır uyumu ve düzen koruma testleri eklendi.

### Changed
- **Normal Mod Pencere Düzeni ve Boyutlandırma:** Normal modda "Hide Lyrics" butonu, albüm kapağı, cihaz seçimi ve tüm alt oynatma kontrolleri ilk açılışta ve farklı DPI ölçeklerinde pencere içinde eksiksiz görünecek şekilde optimize edildi.
- **Kompakt Mod Geçiş Simgesi:** Dar moddan normal moda geçiş butonu simgesi standart köşegen genişletme simgesi (`\uE740`) ile güncellendi.
- **Ayarlar Sekme Yapısı:** Kullanıcı arayüzünde işlevsiz kalan Sistem sekmesi tamamen kaldırılarak Güncellemeler ve Kısayollar sekmeleri öne çıkarıldı.

### Fixed
- **Karaoke Vurgulama Regresyonu:** WinUI 3'te `Visibility.Collapsed` olarak başlatılan TextBlock elemanlarının ölçümünün sıfırlanması sonucu kelimelerin boyanmaması sorunu giderildi; layout ölçümü korunurken `RectangleGeometry` kırpmasıyla akıcı 60 FPS Apple Music stili boyama sağlandı.
- **Şarkı Geçişinde Kalan Süre Eşitlemesi:** Parça geçişlerinde yeni şarkının toplam süresi GSMTC'den okunana kadar kalan sürenin eski parçaya ait kalması engellendi; süreler anında sıfırlanıp yeni parça süresiyle eşzamanlanır.
- **Ayarlar Yerelleştirmesi:** Türkçe ve İngilizce dil dosyalarındaki eksik çeviri anahtarları tamamlandı ve tutarlı hale getirildi.

### Validation
- **Birim ve Entegrasyon Testleri:** 676 / 676 PASS (%100 başarı).
- **GitHub Actions CI:** Run `43065963040` — SUCCESS.
- **Dağıtım Paketleri:** Portable ZIP (`Lyrow-v3.8-win-x64-portable.zip`) ve Inno Setup Installer (`Lyrow-3.8-Setup-x64.exe`) başarıyla üretildi.

---

## [3.7.0] - 2026-11-10

### Added
- **Merkezi Playback Timeline Motoru (`IPlaybackTimelineEngine`):** Tüm pencereler (`MainWindow`, `LyricsWindow`), arka plan servisleri (`LyricsModule`) ve donanım oturum yöneticisi (`WindowsGsmtcMediaSessionMonitor`) için tek ve yetkili zaman çizelgesi motoru mimarisi eklendi.
- **Deterministik Zaman Formatlayıcıları:** Geçen süre için saniye tabanlı tabanlama (`FormatElapsed`), kalan süre için enterpolasyon kaynaklı salınımları engelleyen yuvarlama (`FormatRemaining`) eklendi.
- **Stage 33 Regresyon Test Paketi:** 13 yeni otomatik test ile parça geçiş izolasyonu, geçen ve kalan süre hesaplamaları, seek ve duraklatma senaryoları doğrulandı.

### Changed
- **Lyrics Penceresi Başlık Sadeleştirmesi:** Lyrics penceresinin sol üst köşesinde yer alan `Lyrow — Synchronized Lyrics` metni kaldırılarak başlık çubuğu alanı temiz ve minimalist bir görünüme kavuşturuldu.
- **Zaman Çizelgesi İzolasyonu:** Otomatik parça geçişlerinde (`A → B`) önceki şarkının süresi ve son pozisyonu tamamen temizlenerek yeni parçanın zaman çizelgesine taşınması engellendi.
- **Normal ve Dar Mod Eşzamanlılığı:** Hem Normal Mod hem de Dar Mod aynı merkezi zaman motorunu dinleyecek şekilde eşitlendi.

### Fixed
- **Kümülatif Playback Time Hatası:** Dinlenen sürenin önceki parçaların sürelerini toplayarak artması (örn. 3:30 + 1:01 = 4:31) kök neden analiziyle giderildi; geçen süre artık yalnızca o an çalan parçanın süresini (örn. `1:01`) gösterir.
- **Kalan Süre Hesaplama Hatası:** Kalan sürenin kümülatif süreler üzerinden hesaplanması düzeltildi; kalan süre bağımsız olarak `Parça Süresi - Geçen Süre` formülüyle (örn. `-2:10`) hesaplanır.
- **Stale GSMTC Callback Koruması:** Otomatik parça geçişi sonrasında eski parçanın son saniyelerini içeren gecikmeli GSMTC callback'lerinin yeni parçanın zaman çizelgesini bozması engellendi.

### Validation
- **Birim ve Entegrasyon Testleri:** 635 / 635 PASS (%100 başarı).
- **GitHub Actions CI:** Run `42788361887` — SUCCESS.
- **Smoke Test:** Windows x64 self-contained yürütülebilir dosyası üzerinde başarıyla doğrulandı.

---

## [3.6.0] - 2026-11-08

### Added
- **İlerleme Çubuğu ve Söz Satırından Konum Sarma (Seek):** İlerleme çubuğu ve senkronize şarkı sözü satırları üzerinden doğrudan şarkı konumu atlama (`TryChangePlaybackPositionAsync`).
- **Gecikme Giderici Zaman Enterpolasyonu:** GSMTC oturumlarının `Playing` durumundaki zaman çizelgesi `pos + (UtcNow - LastUpdatedTime)` formülüyle enterpole edilerek şarkı sözlerindeki 3 saniyelik gecikme giderildi.
- **Compact Player Genişletilebilir Şarkı Sözü Paneli:** Mini oynatıcı modunda ekran sınırlarını koruyarak aşağı doğru açılan kompakt şarkı sözü paneli.
- **WASAPI Ses Aygıtı Seçimi:** Lyrics penceresinden varsayılan ses çıkış/yakalama aygıtının anında değiştirilmesi ve loopback oturumunun yeniden bağlanması.

### Changed
- **Ayarlar Sekmesi:** Tekrarlayan ayarlar sadeleştirildi.
- **Animasyonlu Albüm Kapağı & Erişilebilirlik:** Windows Reduced Motion tercihlerine tam uyum sağlandı.

### Validation
- 566 / 566 test PASS.
- GitHub Actions CI SUCCESS.

---

## [3.5.0] - 2026-10-31

### Added
- **Yatay Taşmayan Şarkı Sözü Sarma (`TextWrapping`):** Uzun sözlerin pencere kenarlarından taşması engellendi.
- **Yüksek Çözünürlüklü & Animasyonlu Kapak Desteği:** Donanım hızlandırmalı animasyonlu GIF ve WebP kapak oynatma.
- **24px Varsayılan Edge Bloom:** Glow parlama kalınlığı dengeli 24px değerine ayarlandı.
- **Gelişmiş Beat ve Tempo Algılama:** 5 bantlı enerji ayrımı ve Spectral Flux vuruş takibi.

### Validation
- 362 / 362 test PASS.

---

## [3.4.0] - 2026-10-31
- Duyarlı (responsive) oynatıcı düzeni ve pencere yeniden boyutlandırma optimizasyonları.
- LRCLIB çok aşamalı arama stratejisi ve yerel şarkı sözü önbellekleme.

---

## [3.3.0] - 2026-10-31
- Apple Music benzeri 60 FPS dikey merkezlenen karaoke şarkı sözü akışı.
- Sol-sağ karakter/kelime dolum animasyonları.

---

## [3.2.0] - 2026-10-25
- Windows GSMTC oturum dinleme ve metadata normalizasyonu geliştirmeleri.
- Otomatik parça geçişlerinde temiz yükleme durumları.

---

## [3.1.0] - 2026-10-24
- 4 kenar ortam aydınlatması (Top, Bottom, Left, Right) ve WASAPI Loopback ses yakalama motoru.
- Sıfır tahsisli (zero-allocation) FFT analiz algoritması.

---

## [3.0.0] - 2026-10-24
- Windows 11 Native WinUI 3 ve Windows App SDK mimarisine geçiş.
- Neumorphic kullanıcı arayüzü ve Fluent Mica arka plan entegrasyonu.

---

## [2.0.0] - 2026-10-20
- İlk Windows 11 yerel arayüz prototipleri ve modüler mimari tasarımı.

---

## [1.0.0] - 2026-10-16
- İlk sürüm: Temel ortam aydınlatması ve şarkı sözü yakalama altyapısı.
