# Lyrow — Stage 19: Real-World Validation Plan & Manual Test Protocol

> **Belge Sürümü:** 1.0
> **Tarih:** 2026-11-02
> **Kapsam:** Lyrow v3.5 Sürüm Öncesi Gerçek Kullanıcı & Donanım Doğrulama Protokolü
> **Durum:** DOKÜMANTASYON & TEST PLANI (Manuel Donanım Testleri: `NOT TESTED`)

---

## 1. Giriş ve Amaç

Bu doküman, Lyrow masaüstü uygulamasının otomatik CI/CD ve birim/entegrasyon test ortamında (headless GitHub Actions sanal makinelerinde) doğrudan doğrulanamayan fiziksel donanım, lisanslı DRM medya oturumları, Windows kabuk entegrasyonu ve erişilebilirlik deneyimlerini gerçek bir Windows 11/10 fiziksel makinesinde uçtan uca test etmek üzere hazırlanmış adım adım doğrulama protokolüdür.

Tüm adımlar, test mühendisi veya sürüm sorumlusunun kolaylıkla uygulayabileceği ve objektif kriterlerle `PASS` / `FAIL` değerlendirmesi yapabileceği şekilde yapılandırılmıştır.

---

## 2. Test Ortamı Ön Gereksinimleri

| Gereksinim | Tanım |
|---|---|
| **İşletim Sistemi** | Windows 11 (22H2 veya üstü) veya Windows 10 (21H2 veya üstü x64) |
| **.NET Runtime** | .NET 10.0 x64 Masaüstü Çalışma Zamanı (veya self-contained paket) |
| **Ses Donanımı** | Tümleşik 3.5 mm ses jakı ve çıkarılabilir USB kulaklık/DAC cihazı |
| **Medya Oynatıcı** | Apple Music for Windows (veya Spotify Desktop) aktif oturumu |
| **Ekran Okuyucu** | Windows Narrator (Win + Ctrl + Enter) |

---

## 3. Adım Adım Doğrulama Senaryoları

### Senaryo 1: Temiz Kurulum (Clean Installation)
- **Hedef:** Sisteme ilk kez yüklenen Lyrow yükleyicisinin standart dizinlere hatasız kurulması.
- **Test Adımları:**
  1. `Lyrow-Setup-x64.exe` dosyasını çalıştırın.
  2. Kurulum sihirbazındaki lisans ve hedef dizin adımlarını takip edin (Varsayılan: `%LOCALAPPDATA%\Programs\Lyrow`).
  3. Kurulumun tamamlanmasını bekleyin ve "Lyrow'u Başlat" seçeneğini işaretleyip bitirin.
- **Beklenen Sonuç:**
  - Kurulum hatasız tamamlanır; masaüstü ve Başlat menüsü kısayolları oluşur.
  - Uygulama ilk açılışta Welcome Wizard / Hoş Geldiniz sihirbazını gösterir.
- **Gerçekleşen Durum:** `NOT TESTED` *(Otomatik runner üzerinde fiziksel masaüstü bulunmadığından manuel doğrulama beklemektedir)*.

---

### Senaryo 2: Standart Yönetici Olmayan Kullanıcı Kurulumu (Non-Admin Install)
- **Hedef:** UAC yönetici yetkisi gerektirmeden per-user kurulumun doğrulanması.
- **Test Adımları:**
  1. Standart (yönetici olmayan) bir Windows kullanıcı hesabıyla oturum açın.
  2. `Lyrow-Setup-x64.exe` çalıştırın.
- **Beklenen Sonuç:**
  - Yükleyici UAC ekranı istemeden kullanıcının yerel AppData dizinine kurulur.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 3: Üzerine Kurulum / Güncelleme (Upgrade Installation)
- **Hedef:** Mevcut bir Lyrow kurulumunun üzerine yeni sürüm kurulduğunda veri kaybı olmaması.
- **Test Adımları:**
  1. Kurulu Lyrow'da özel ayarlar yapın (Tema, Glow hassasiyeti vb.).
  2. Lyrow açıkken yükleyiciyi başlatın.
- **Beklenen Sonuç:**
  - Yükleyici çalışan Lyrow sürecini (`AppMutex`) algılar ve kapatılmasını ister.
  - Kurulum bittikten sonra önceki ayarlar (`settings.json`) bozulmadan korunur.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 4: Kaldırma ve Veri Koruma İsteği (Uninstall Data Preservation)
- **Hedef:** Uygulama kaldırılırken kullanıcı verilerinin silinme onayının sorulması.
- **Test Adımları:**
  1. Windows "Program Ekle/Kaldır" üzerinden Lyrow'u kaldır seçin.
  2. "Kullanıcı verilerini (ayarlar, şarkı sözü önbelleği) silmek istiyor musunuz?" uyarısını inceleyin.
  3. "Hayır" seçildiğinde AppData verilerinin korunduğunu; "Evet" seçildiğinde tamamen temizlendiğini doğrulayın.
- **Beklenen Sonuç:**
  - Kullanıcı kararına uygun şekilde temizlik yapılır veya veriler korunur.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 5: Portable ZIP ve Konum Değiştirme (Portable Relocation)
- **Hedef:** Kurulum gerektirmeyen taşınabilir sürümün farklı dizinlerde ve USB bellekte çalışabilmesi.
- **Test Adımları:**
  1. `Lyrow-Portable-x64.zip` arşivini `C:\TestPortable\Lyrow` klasörüne çıkarın.
  2. `Lyrow.App.exe` çalıştırın ve bir ayar değiştirin.
  3. Uygulamayı kapatıp klasörü `D:\LyrowMoved` dizinine taşıyın ve yeniden çalıştırın.
- **Beklenen Sonuç:**
  - Bağımsız çalışır, mutlak yol bağımlılığı nedeniyle çökme yaşanmaz.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 6: Canlı Apple Music Oturumu Takibi (Apple Music DRM Session)
- **Hedef:** Gerçek Apple Music Windows uygulaması ile GSMTC medya entegrasyonu.
- **Test Adımları:**
  1. Apple Music Windows uygulamasını açın ve bir şarkı başlatın.
  2. Lyrow'u açın.
- **Beklenen Sonuç:**
  - Lyrow şarkı başlığını, sanatçıyı ve albüm kapağını gecikmesiz ekranda gösterir.
  - Duraklat/Oynat butonları Apple Music ile anında senkronize olur.
- **Gerçekleşen Durum:** `NOT TESTED` *(DRM korumalı ticari istemci testi CI ortamında yürütülemez)*.

---

### Senaryo 7: Şarkı Değişimi ve Meta Veri Senkronizasyonu
- **Hedef:** Arka arkaya şarkı değişimlerinde UI titremesi veya bellek sızıntısı olmaması.
- **Test Adımları:**
  1. Oynatma listesinde arka arkaya 5 şarkıyı hızlıca ileri sarın/değiştirin.
- **Beklenen Sonuç:**
  - UI donmaz, önceki şarkı sözleri iptal edilir ve yeni şarkının sözleri doğru şekilde yüklenir.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 8: LRCLIB Senkronize Şarkı Sözleri Doğrulaması
- **Hedef:** Gerçek internet bağlantısında LRCLIB API üzerinden senkronize söz akışı.
- **Test Adımları:**
  1. Popüler bir parça çalarken Lyrow senkronize söz modunu aktif edin.
- **Beklenen Sonuç:**
  - Şarkı ilerledikçe aktif satır vurgulanır ve otomatik kaydırma akıcı şekilde çalışır.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 9: Gerçek Ses Çıkışı ile Ambient Glow DSP
- **Hedef:** WASAPI loopback capture ile sistem sesine duyarlı ortam aydınlatması.
- **Test Adımları:**
  1. Ambient Glow modunu açın.
  2. Müziği duraklatın ve ardından yeniden başlatın.
- **Beklenen Sonuç:**
  - Müzik durduğunda Glow sakinleşir / söner; müzik başladığında ritimle senkronize renk dalgalanmaları başlar.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 10: 3.5 mm Ses Jakı Çıkarma ve Yeniden Takma (Audio Jack Hotplug)
- **Hedef:** Fiziksel ses çıkışı aniden kesildiğinde veya değiştiğinde çökme yaşanmaması.
- **Test Adımları:**
  1. Ambient Glow açıkken ve müzik çalarken 3.5 mm kulaklık jakını çıkarın.
  2. Sistem varsayılan hoparlöre geçtiğinde 5 saniye bekleyin.
  3. Jakı tekrar takın.
- **Beklenen Sonuç:**
  - Lyrow çökmez (`AudioCaptureService` cihaz değişim olayını yakalar ve yeniden bağlanır).
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 11: USB Ses Aygıtı Çıkarma / Yeniden Bağlama (USB Audio Hotplug)
- **Hedef:** USB DAC / kulaklık bağlantısı koparıldığında WASAPI loopback yakalama kurtarma kabiliyeti.
- **Test Adımları:**
  1. USB kulaklık üzerinden müzik çalarken USB kablosunu çekin.
  2. 3 saniye sonra USB kablosunu geri takın.
- **Beklenen Sonuç:**
  - `AudioCaptureService` kaynakları serbest bırakır ve yeni aktif varsayılan aygıttan yakalamayı sürdürür.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 12: Windows Narrator ile Erişilebilirlik Denetimi
- **Hedef:** Görme engelli kullanıcılar için ekran okuyucu uyumluluğu.
- **Test Adımları:**
  1. `Win + Ctrl + Enter` ile Narrator'ı başlatın.
  2. `Tab` ve ok tuşları ile Lyrow arayüzünde gezinin.
- **Beklenen Sonuç:**
  - Her buton, kaydırıcı ve metin alanı mantıklı `AutomationProperties.Name` okur.
  - Tab sırası görsel sırayla birebir uyumludur.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 13: Sistem Tepsisi (Tray) ve Genel Kısayollar (Global Hotkeys)
- **Hedef:** Arka plana küçültme, tepsi menüsü ve global tuş tepkileri.
- **Test Adımları:**
  1. Kapat veya küçült butonuna basarak uygulamayı tepsiye gönderin.
  2. Yapılandırılmış global kısayolları (örneğin Mini Player geçişi) deneyin.
  3. Tepsi simgesine çift tıklayarak pencereyi geri getirin.
- **Beklenen Sonuç:**
  - Görev çubuğundan gizlenip tepside sessizce çalışır; kısayollar tepki verir.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 14: Yeniden Başlatma Kalıcılığı (State Persistence)
- **Hedef:** Pencere boyutu, konumu, compact mod durumu ve ayarların saklanması.
- **Test Adımları:**
  1. Pencereyi ekranın sağ alt köşesine taşıyın, Compact Player moduna alın ve kapatın.
  2. Yeniden açın.
- **Beklenen Sonuç:**
  - Son konum ve mod hatasız hatırlanır.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

### Senaryo 15: SmartScreen Uyarı Davranışı
- **Hedef:** İmzasız ikili dosyalarda Windows Defender SmartScreen uyarısının gözlemlenmesi.
- **Test Adımları:**
  1. Paketlenen yükleyiciyi internetten indirilmiş gibi (Mark-of-the-Web zone identifier ile) çalıştırın.
- **Beklenen Sonuç:**
  - "Windows Kişisel Bilgisayarınızı Korudu" (Bilinmeyen Yayıncı) SmartScreen ekranı çıkar.
  - "Ek bilgi" -> "Yine de çalıştır" seçildiğinde uygulama sorunsuz açılır.
- **Gerçekleşen Durum:** `NOT TESTED`.

---

## 4. Doğrulama Özeti ve Takip Çizelgesi

| # | Test Senaryosu | Otomasyon Durumu | Manuel Donanım Durumu |
|---|---|---|---|
| 1 | Temiz Kurulum | Komut dosyası & Inno doğrulandı | `NOT TESTED` |
| 2 | Non-Admin Kurulum | Inno Setup `PrivilegesRequired=lowest` | `NOT TESTED` |
| 3 | Üzerine Kurulum / Güncelleme | `AppMutex` doğrulaması | `NOT TESTED` |
| 4 | Kaldırma ve Veri Koruma | Kaldırıcı kod denetimi | `NOT TESTED` |
| 5 | Portable ZIP | Arşivleme otomasyonu hazır | `NOT TESTED` |
| 6 | Canlı Apple Music Oturumu | Mock oturum testleri `PASS` | `NOT TESTED` |
| 7 | Şarkı Değişimi Meta Veri | Mock servis testleri `PASS` | `NOT TESTED` |
| 8 | LRCLIB Senkronize Sözler | HTTP mock testleri `PASS` | `NOT TESTED` |
| 9 | Gerçek Ses Ambient Glow | Sentetik ses testleri `PASS` | `NOT TESTED` |
| 10 | 3.5 mm Jak Hotplug | WASAPI hata yönetimi kodlandı | `NOT TESTED` |
| 11 | USB Ses Aygıtı Hotplug | Yeniden bağlanma lojiği mevcut | `NOT TESTED` |
| 12 | Windows Narrator | AutomationProperties tanımlı | `NOT TESTED` |
| 13 | Sistem Tepsisi & Kısayollar | Hook/Tray mekanizması test edildi | `NOT TESTED` |
| 14 | Yeniden Başlatma Kalıcılığı | SettingsManager testleri `PASS` | `NOT TESTED` |
| 15 | SmartScreen Davranışı | İmzasızlık riski dokümante edildi | `NOT TESTED` |
