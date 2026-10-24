# Lyrow v2.4 — Release Notes

**Sürüm:** `v2.4`  
**Yayınlanma Tarihi:** 24 Ekim 2026  
**Platform:** Windows 11 Native (x64) | .NET 10 LTS | WinUI 3 + Windows App SDK  

---

## 🌟 Özet

Lyrow **v2.4**, modern Windows müzik deneyimini bir üst seviyeye taşıyor. Bu sürümde tamamen yenilenen resmi **Lyrow logo & ikon kimliği**, kesintisiz arka plan müzik kontrolü sağlayan yerel **Windows Sistem Tepsisi (System Tray)** entegrasyonu, **Apple Music** tarzı 60 FPS akıcı ve dikey merkezlenen şarkı sözü penceresi, **%0 - %500** arasına genişletilmiş tonemapped Beat & Transient duyarlılığı, **0 - 256 px** Edge Bloom kalınlığı, sol menüde **Home / Lyrics / Glow / Playback / Settings** hiyerarşisi ve 5 dilli (`en`, `tr`, `fr`, `es`, `de`) eksiksiz yerelleştirme sunulmaktadır.

Ayrıca kod tabanındaki tüm eski modül ve proje referansları temizlenerek tamamen Lyrow-native mimariye geçilmiştir.

---

## ✨ v2.4 Yenilikleri ve İyileştirmeler

### 1. 🎨 Yeni Resmi Lyrow Logo & Yüksek Çözünürlüklü İkon
- Windows 11 Fluent ve modern cam estetiğine uyumlu resmi Lyrow logosu ve ikonu entegre edildi.
- Uygulama penceresi, WinUI TitleBar, görev çubuğu ve Inno Setup kurulum sihirbazı yeni varlıklarla güncellendi (`Assets/lyrow-logo.png`, `Assets/lyrow-logo.ico`).

### 2. 📥 Windows Sistem Tepsisi (System Tray) Entegrasyonu
- Uygulama başlatıldığında veya arka planda çalışırken Windows bildirim alanında (System Tray) Lyrow ikonu yer alır.
- Pencere kapatıldığında (`Close`) kullanıcı ayarına bağlı olarak doğrudan tepsiye küçülür (`MinimizeToTray`).
- Tepsi simgesine çift tıklama veya sağ tık menüsü:
  - **Open Lyrow / Home**
  - **Show/Hide Glow (Ambient Lighting)**
  - **Show/Hide Lyrics Window**
  - **Enable/Disable Click-Through**
  - **Settings**
  - **Exit Lyrow** (Tüm arka plan döngülerini ve servisleri zarifçe sonlandırır).

### 3. 🎵 Apple Music Tarzı Akıcı Şarkı Sözleri (Motion & Immersive Window)
- Şarkı sözleri penceresi 60 FPS enterpolasyonla aktif satırı daima dikey merkezde tutar.
- Aktif ve inaktif satırlar arasında akıcı ölçek, opaklık ve yumuşak geçiş hiyerarşisi uygulanır.
- Tıklanan satırın zamanına anında atlama (seek-on-click) ve alt araç çubuğunda anlık ses düzeyi kontrolü korunmuştur.

### 4. 🧭 Sidebar Reorganizasyonu (Home / Lyrics / Glow / Playback / Settings)
- "Dashboard" ifadesi "Home" olarak yeniden adlandırıldı ve 5 dilde güncellendi (`Home`, `Ana Sayfa`, `Accueil`, `Inicio`, `Startseite`).
- Ana kullanım kategorileri sol kenar çubuğuna taşındı:
  - **Home:** Canlı medya bilgisi, albüm kapağı, zaman çizelgesi ve hızlı modül durumları.
  - **Lyrics:** Ayrıntılı şarkı sözü penceresi kontrolleri ve ofset ayarları.
  - **Glow:** 4 kenar ortam aydınlatması, parlaklık, renk paleti ve monitör kontrolleri.
  - **Playback:** Aktif medya oturumu ve sistem ana ses seviyesi denetimi.
  - **Settings:** General, Updates, Shortcuts ve Diagnostics ile sadeleştirilmiş ayarlar.

### 5. ⚡ Genişletilmiş Beat & Transient Reactivity (%0 - %500)
- Müzik ritmine tepki veren Glow hassasiyeti %0 ile %500 arasına genişletildi (varsayılan: %100).
- Yüksek hassasiyet seviyelerinde ekran titremesini ve aşırı strobing etkisini önlemek için hiperbolik tanjant (`tanh`) tabanlı yumuşak tonemapping algoritması uygulandı.

### 6. 🌟 0 - 256 px Edge Bloom Kalınlığı
- Kenar aydınlatmasının kalınlık aralığı 0 px ile 256 px arasına çıkarıldı.
- 0 px seçildiğinde CPU/GPU döngüleri sıfır yüke indirgenir; yüksek piksel değerlerinde ekranın derinliklerine kadar yumuşak gradyan yayılır.

### 7. 🧹 Eski Modül İsimlerinin Temizliği
- Kod tabanındaki, testlerdeki, modellerdeki ve dökümantasyondaki tüm eski proje ve modül adları (`Lumn`, `Verci`) Lyrow standartlarına (`GlowModule`, `LyricsModule`, `GlowSettings`, `LyricsSettings`) dönüştürüldü.
- Aktif depoda 0 eşleşme garanti altına alındı.

---

## 📦 Dağıtım Paketleri

| Dosya Adı | Boyut | Açıklama |
|---|---|---|
| `Lyrow-2.4-Setup-x64.exe` | 66.5 MB | Windows Kurulum Sihirbazı (In-App Self Update destekli) |
| `Lyrow-v2.4-win-x64-portable.zip` | 96.0 MB | Kurulum gerektirmeyen bağımsız taşınabilir arşiv |
