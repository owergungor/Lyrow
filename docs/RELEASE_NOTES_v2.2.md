# Lyrow v2.2 Sürüm Notları (Release Notes)

**Sürüm:** `v2.2`  
**Tarih:** 20 Ekim 2026  
**Platform:** Windows 11 (22H2 / 24H2) / Windows 10 (Build 19041+) 64-bit  
**Geliştirme Çatısı:** .NET 10 LTS, WinUI 3, Windows App SDK 1.6+  
**Tasarım Dili:** Windows 11 Mica / Fluent + Apple Music Estetiği (Glow Ambient Lighting Experience)

---

## 🌟 Genel Bakış

Lyrow `v2.2`, macOS'taki Glow ambient-lighting deneyimini Windows 11'in yerel tasarım diliyle buluşturan, Mac tarzı sağ üst yüzen bildirim (toast) mimarisine geçen, çift ses seviyesi kontrolü (aktif oynatıcı oturumu + sistem ana çıkışı) sunan, şarkı sözü penceresine medya kontrolleri entegre eden ve tipografi/ortam aydınlatması üzerinde kusursuz bir kontrol sağlayan büyük bir deneyim ve cila güncellemesidir.

---

## 🚀 Yeni Özellikler ve İyileştirmeler

### 1. 🔔 Mac Tarzı Sağ Üst Yüzen Bildirim Sistemi (Floating Acrylic Toasts)
- Bildirimler artık ana pencerenin sayfa düzenini aşağı öteleyen `InfoBar` yerine pencerenin sağ üst güvenli alanında (safe area) `ZIndex: 1000` ile bağımsız yüzen kartlar olarak gösterilir.
- **Sıfır Layout Kayması:** Bildirim açılıp kapandığında ana sayfa kontrolleri, slider'lar veya butonlar zıplamaz.
- **Akıllı Debounce & Spam Engelleme:** `ToastDebouncer` sayesinde 2.5 saniyelik zaman aralığında gelen aynı bildirimler bastırılır veya mevcut kart üzerinde sayaç olarak güncellenir (`(x2)`, `(x3)`).
- **Yumuşak Slide & Fade-in / Fade-out:** Kartlar sağdan 24px yumuşak giriş yapar, 3.5 saniye sonra veya kapatıldığında pürüzsüzce kaybolur.
- **En Fazla 3 Kart Yığılma (Stacking):** Ekranda aynı anda en fazla 3 aktif bildirim kartı yer alır, fazlası en eskiden başlanarak temizlenir.
- **Aksiyon Buton Desteği:** Yeni sürüm bildirimi gibi etkileşim gerektiren durumlarda doğrudan kart üzerinde "View Release" gibi eylem butonları sunulur.

### 2. 🎚️ Çift Ses Kontrolü (Playback Session + System Master Volume)
- **Active Playback Session Volume:** Aktif müzik çaların (Spotify, Apple Music, Tarayıcı vb.) Windows ses karıştırıcısındaki oturum sesini `0% — 100%` aralığında yönetir.
- **System Master Volume:** Windows'un varsayılan ses çıkış cihazının (hoparlör/kulaklık) donanım master sesini `0% — 100%` aralığında yönetir.
- **Bağımsız Sessize Alma (Safe Mute):** Her iki ses kanalı da bağımsız olarak sessize alınabilir (`\uE74F` ve `\uE767` dinamik glifleri).
- **Hedef Cihaz/Uygulama Bilgisi:** Slider üzerinde bağlı uygulamanın adı (örn. Spotify) ve ana ses aygıtının durumu gerçek zamanlı gösterilir.

### 3. ⏯️ Şarkı Sözü Penceresi İçi Medya Kontrolleri (Lyrics Window Media Bar)
- `LyricsWindow` alt çubuğuna entegre edilen 3'lü medya butonları (Önceki, Oynat/Duraklat, Sonraki) ile kullanıcı tam ekranda veya kompakt modda şarkı sözlerini takip ederken ana uygulamaya geçmeden müziği yönetebilir.
- GSMTC donanım durumuyla iki yönlü senkronize çalışır; şarkı durduğunda ikon dinamik olarak Oynat (`\uE768`), şarkı devam ettiğinde Duraklat (`\uE769`) halini alır.

### 4. 🎨 6 Zengin Hazır Ortam Aydınlatma Paleti (Glow Presets with Color Dots)
- Albüm kapağı senkronizasyonu kapalıyken veya parça kapağı bulunmadığında kullanılmak üzere 6 yüksek çözünürlüklü palet önayarı eklendi:
  1. **Celestial:** Gök mavisi, lavanta, pembe ve yumuşak beyaz.
  2. **Aurora:** Kutup yeşili, camgöbeği, menekşe ve canlı pembe.
  3. **Sunset:** Sıcak amber, gün batımı kırmızısı, mercan ve kadife mor.
  4. **Ocean:** Derin deniz mavisi, turkuaz akıntı, köpük beyazı.
  5. **Ember:** Alev kırmızısı, köz turuncusu, güneş sarısı, asil menekşe.
  6. **Midnight:** Elektrik mavisi, neon camgöbeği ve derin gece mavisi.
- ComboBox açılır listesinde ve seçili durumunda 4 kenarı temsil eden 4 renkli daire noktasıyla canlı görsel önizleme sunulur.

### 5. ⏸️ Müzik Duraklatma & Kapanma Gecikmesi (Inactivity Delay & Freeze)
- Müzik duraklatıldığında (Pause), Glow katmanı ani ve rahatsız edici bir flaş patlaması yapmadan beat animasyon fazını dondurur.
- Müzik tamamen bittiğinde veya oturum kapandığında kullanıcı tercihine göre belirlenen gecikme süresince (Hemen, 1s, 3s, 5s, 10s, 30s, 1 dk) bekler.
- Süre dolduğunda ekran kenarlarındaki ışık ~800ms yumuşak üssel solma (exponential fade-out) ile zarifçe kararır. Müzik tekrar başladığında ani sıçrama olmadan yumuşakça canlanır.

### 6. 🔤 Bağımsız Şarkı Sözü Tipografi Ölçeklendirme
- Settings -> Lyrics ve Lyrics sekmesinde iki bağımsız slider:
  - **Focused Line Font Size:** Aktif çalan satırın punto büyüklüğü (`20–48 px`, varsayılan `28 px`).
  - **Unfocused Lines Font Size:** Çevredeki inaktif satırların punto büyüklüğü (`14–36 px`, varsayılan `20 px`).
- Değerler değiştirildiği anda açık şarkı sözü penceresindeki tüm satırlar 60 FPS akıcılıkla yeni boyutlarına yeniden hizalanır; dikey merkez kilitlenmesi korunur.

### 7. 🔄 Esnek Güncelleme Sıklığı & Güvenli Auto Update
- Auto Update bölümüne kontrol periyodu eklendi:
  - **On Startup (Açılışta)**
  - **Daily (Günde 1 kez)**
  - **Weekly (Haftada 1 kez)**
  - **Monthly (Ayda 1 kez)**
- Son kontrol zamanı `LastUpdateCheckUtc` olarak kalıcı kaydedilir. Çevrimdışı durumlarda müzik akışını veya kullanıcıyı rahatsız edecek hiçbir hata oluşturulmaz.

---

## 📦 Dağıtım ve Paketleme

- **Kurulum Paketi:** `dist/Lyrow-2.2-Setup-x64.exe` (Inno Setup 6 tabanlı, `%APPDATA%\Lyrow` ayarlarını korur).
- **Taşınabilir Sürüm:** `dist/Lyrow-v2.2-win-x64-portable.zip` (Bağımsız, .NET yüklemesi gerektirmez).
- **Resmi Git Tag:** `v2.2` (Kesinlikle iki parçalı sürüm kuralına uygundur).
