# Lyrow v4.3 — Sürüm Notları (Yerel Hazırlık)

**Tarih:** 2026-11-20  
**Temel Sürüm:** v4.2 (`7f9ab66`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 712 / 712 PASS (%100, deterministik seri koşu)

---

## Öne Çıkan Yenilikler ve Değişiklikler

### 1. Dar Mod — Ghost Button Tasarımı
- Dar Moddaki **Volume & Mute**, **Play/Pause**, **Previous**, **Next**, **Normal Mode** ve **Compact Lyrics Show/Hide** butonlarının tamamı Açık/Koyu tema destekli Ghost Button stiline (`LyrowGhostButtonStyle`) geçirildi.

### 2. Dar Mod — İkon-Odaklı Audio Output Butonu
- Dar Moddaki Audio Output butonu (`BtnDeviceSelectorCompact`) metinden arındırılarak yalnızca ikon gösteren, Volume & Mute ile birebir aynı **`26×26`** ölçülerde Ghost Button haline getirildi; aktif cihaz bilgisi Tooltip üzerinden sunulur.

### 3. Kenar Işığı Kalınlık (`0–64 px`) ve Parlaklık (`0–100%`) Kontrolleri
- Compact Lyrics Show/Hide butonunun hemen soluna **`26×26`** boyutunda, ikon-odaklı Kenar Işığı kalınlık butonu (`BtnToggleEdgeGlowCompact`) eklendi.
- Normal Mod ve Tam Ekran Mod araç çubuğunda Kenar Işığı Kalınlığı butonunun hemen sağına birebir aynı **`28×28`** ölçülerde **Kenar Işığı Parlaklığı** butonu (`BtnEdgeGlowBrightness`) eklendi; tıklandığında **`0–100%`** parlaklık sürgüsü açılır, kalınlıktan bağımsız çalışır ve Ayarlar > Işık sekmesiyle anlık senkronize olur.
- Hem Normal Modda hem Dar Modda Kenar Işığı kalınlık butonuna **tıklandığında** ortak **`0–64 px`** kalınlık sürgüsü açılır (hover kaldırıldı), tekrar tıklama veya dışarı tıklama ile kapanır; dar pencerenin dışına taşmadan uygun konumda açılır.
- `0 px` kalınlık Kenar Işığını tamamen kapatır, `1–64 px` anlık uygulanır; tüm kalınlık ve parlaklık tercihleri kalıcı olarak kaydedilir.

### 4. Ayarlar Sayfası — Sola Hizalı Sidebar, Sabit Boyut ve 7 Sekmeli Windows 11 Fluent Design
- `LyrowGhostButtonStyle` şablonunda `HorizontalContentAlignment` bağlanarak sol sidebar ikonları ve sekme adları tüm pencere genişliklerinde tutarlı şekilde sola hizalandı.
- Ayarlar modalı sabit boyutlu (`740×520`, dar pencerede güvenli kısıtlı) hale getirildi; sekme değiştikçe pencere/modal boyutu ve konumu kesinlikle değişmez, içerik bağımsız dikey kaydırma ile gösterilir.
- Ayarlar tam **7 ayrı sekmede** düzenlendi (Türkçe / İngilizce):
  1. **Genel / General:** Dil, tema ve genel uygulama tercihleri.
  2. **Görünüm / Appearance:** Pencere saydamlığı, dinamik arka plan ve albüm kapağı ayarları.
  3. **Işık / Lighting:** Ses motoru (FFT) ve Kenar Işığı ayarları.
  4. **Şarkı Sözleri / Lyrics:** Şarkı sözü parlaması, arka plan saydamlığı ve aktif/pasif yazı tipi boyutları.
  5. **Sistem / System:** Windows başlangıcı, sistem tepsisi, tepsiye küçültme ve bildirimler.
  6. **Güncellemeler / Updates:** Otomatik güncelleme, denetim sıklığı ve sürüm kontrolü.
  7. **Hakkında / About:** Kısayollar, uygulama bilgisi, sürüm, MIT lisansı ve tanılama araçları.

---

## Yerel Dağıtım Paketleri ve SHA-256 Doğrulama Özetleri

| Paket | Dosya Adı | Boyut | İmza Durumu | SHA-256 |
|---|---|---|---|---|
| **Windows Setup EXE** | `Lyrow-4.3-Setup-x64.exe` | 64.80 MB | İmzasız | `9564BD133725F04537ECA548FEBE7DA975B5BAD0F48C515A4EEFC9A2B37389B2` |
| **Portable ZIP** | `Lyrow-v4.3-win-x64-portable.zip` | 93.44 MB | — | `D891A0829E46FE39710085F730615BD6A5E2C92D284D870B892D43691203519A` |
| **MSIX x64** | `Lyrow-v4.3-x64.msix` | 95.69 MB | **İmzasız (`NotSigned`)** | `4880EC2ADEBB093CE8EC1903C23991F6B41BEEA87E15A682EA716BEC7EAF41EF` |
