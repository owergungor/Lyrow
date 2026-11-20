# Lyrow v4.3 — Sürüm Notları (Yerel Hazırlık)

**Tarih:** 2026-11-20  
**Temel Sürüm:** v4.2 (`7f9ab66`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 711 / 711 PASS (%100, deterministik seri koşu)

---

## Öne Çıkan Yenilikler ve Değişiklikler

### 1. Dar Mod — Ghost Button Tasarımı
- Dar Moddaki **Volume & Mute**, **Play/Pause**, **Previous**, **Next**, **Normal Mode** ve **Compact Lyrics Show/Hide** butonlarının tamamı Açık/Koyu tema destekli Ghost Button stiline (`LyrowGhostButtonStyle`) geçirildi.

### 2. Dar Mod — İkon-Odaklı Audio Output Butonu
- Dar Moddaki Audio Output butonu (`BtnDeviceSelectorCompact`) metinden arındırılarak yalnızca ikon gösteren, Volume & Mute ile birebir aynı **`26×26`** ölçülerde Ghost Button haline getirildi; aktif cihaz bilgisi Tooltip üzerinden sunulur.

### 3. Dar Mod ve Normal Mod — Tıklamayla Açılan `0–64 px` Kenar Işığı Kalınlık Ayarı
- Compact Lyrics Show/Hide butonunun hemen soluna **`26×26`** boyutunda, ikon-odaklı Kenar Işığı butonu (`BtnToggleEdgeGlowCompact`) eklendi.
- Hem Normal Modda hem Dar Modda Kenar Işığı butonuna **tıklandığında** ortak **`0–64 px`** kalınlık sürgüsü açılır (hover kaldırıldı), tekrar tıklama veya dışarı tıklama ile kapanır; dar pencerenin dışına taşmadan uygun konumda açılır (`Placement="TopEdgeAlignedRight"`, `ShouldConstrainToRootBounds="False"`).
- `0 px` Kenar Işığını tamamen kapatır, `1–64 px` anlık uygulanır; Normal Mod, Dar Mod ve Ayarlar sayfası sürgüleri 3 yönlü senkronize çalışır ve tercih kalıcı olarak kaydedilir.

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
| **Windows Setup EXE** | `Lyrow-4.3-Setup-x64.exe` | 64.75 MB | İmzasız | `79A6C6FD34ECEA1E357B72DFBB056466A2693B7777931A966F4F87189DDEBCCC` |
| **Portable ZIP** | `Lyrow-v4.3-win-x64-portable.zip` | 93.43 MB | — | `076B379C2BFE496A4B8509E2DED983844C379CA2B21FEC24D8F8EEC257B9CED6` |
| **MSIX x64** | `Lyrow-v4.3-x64.msix` | 95.69 MB | **İmzasız (`NotSigned`)** | `1BD4F651788497DAE01FB02A5649F5BC8B245053EB2C358DDE397B410CAC88F7` |
