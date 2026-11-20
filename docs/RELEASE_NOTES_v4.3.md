# Lyrow v4.3 — Sürüm Notları (Yerel Hazırlık)

**Tarih:** 2026-11-20  
**Temel Sürüm:** v4.2 (`7f9ab66`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 713 / 713 PASS (%100, deterministik seri koşu)

---

## Öne Çıkan Yenilikler ve Değişiklikler

### 1. Dar Mod — Ghost Button Tasarımı
- Dar Moddaki **Volume & Mute**, **Play/Pause**, **Previous**, **Next**, **Normal Mode** ve **Compact Lyrics Show/Hide** butonlarının tamamı Açık/Koyu tema destekli Ghost Button stiline (`LyrowGhostButtonStyle`) geçirildi.

### 2. Normal Mod, Tam Ekran Mod ve Dar Mod — İkon-Odaklı Audio Output Butonu
- Normal Modda Play/Pause üzerindeki eski metinli ses çıkışı dropdown'u kaldırıldı; Normal ve Tam Ekran Modda Kenar Işığı butonunun hemen sağına **`28×28`** ölçülerde, Dar Modda ise Volume & Mute ile birebir aynı **`26×26`** ölçülerde ikon-odaklı Ghost Button Audio Output seçicisi (`BtnDeviceSelectorToolbar` / `BtnDeviceSelectorCompact`) eklendi; aktif cihaz bilgisi Tooltip üzerinden sunulur ve tüm modlarda anında senkronize çalışır.

### 3. Kenar Işığı Paneli — Parlaklık (`10–100%`) ve Kalınlık (`0–64 px`) (Tüm Modlarda Birebir Aynı Panel)
- Compact Lyrics Show/Hide butonunun hemen soluna **`26×26`** boyutunda, ikon-odaklı Kenar Işığı butonu (`BtnToggleEdgeGlowCompact`) eklendi.
- Hem Normal/Tam Ekran Modda hem Dar Modda Kenar Işığı butonuna **tıklandığında** birebir aynı ortak panel (`FlyoutEdgeGlowThickness`) açılır; üstte **Kenar Işığı Parlaklığı (`10–100%`)**, altta **Kenar Işığı Kalınlığı (`0–64 px`)** sürgüleri min-max uç etiketleri olmadan sade biçimde yer alır.
- Parlaklık ve kalınlık birbirinden bağımsız ayarlanır; `0 px` Kenar Işığını tamamen kapatır, `1–64 px` kalınlık ve `10–100%` parlaklık anlık uygulanır, tüm modlar ve Ayarlar sayfasıyla senkronize çalışır ve kalıcı kaydedilir.

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
| **Windows Setup EXE** | `Lyrow-4.3-Setup-x64.exe` | 64.76 MB | İmzasız | `0BF9DE457792F95422D6F81FBE0A979ABBFDCFA7C37E1A5F9B386CF079E37E54` |
| **Portable ZIP** | `Lyrow-v4.3-win-x64-portable.zip` | 93.43 MB | — | `946D07F6F95700271DB9776FFC8FE0D3FB7C61127445CE4B3717E42891C091A1` |
| **MSIX x64** | `Lyrow-v4.3-x64.msix` | 95.69 MB | **İmzasız (`NotSigned`)** | `9229BA202441D5AF3AF90BA796027ABA56F2454F98CD84EF5CBEA765D7385BE2` |
