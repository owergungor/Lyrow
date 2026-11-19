# Lyrow v4.3 — Sürüm Notları (Yerel Hazırlık)

**Tarih:** 2026-11-19  
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

### 4. Ayarlar Sayfası — Sola Hizalı Sidebar, Sabit Boyut ve 3 Sekmeli Windows 11 Fluent Design
- `LyrowGhostButtonStyle` şablonunda `HorizontalContentAlignment` bağlanarak sol sidebar ikonları ve sekme adları tüm pencere genişliklerinde tutarlı şekilde sola hizalandı.
- Ayarlar modalı sabit boyutlu (`740×520`, dar pencerede güvenli kısıtlı) hale getirildi; sekme değiştikçe pencere/modal boyutu ve konumu kesinlikle değişmez, içerik bağımsız dikey kaydırma ile gösterilir.
- Ayarlar tam **3 ayrı sekmede** düzenlendi: **Genel**, **Şarkı Sözleri** ve **Sistem ve Bildirimler** (mevcut tüm ayarlar, Açık/Koyu/Sistem teması, Türkçe/İngilizce yerelleştirme, klavye erişimi ve ESC davranışı eksiksiz korundu).

---

## Yerel Dağıtım Paketleri ve SHA-256 Doğrulama Özetleri

| Paket | Dosya Adı | Boyut | İmza Durumu | SHA-256 |
|---|---|---|---|---|
| **Windows Setup EXE** | `Lyrow-4.3-Setup-x64.exe` | 64.77 MB | İmzasız | `EB0818786AA40E8372E6889E65F7F61FAD3A757E7263CE8D3D98E8FAE5E8EB8D` |
| **Portable ZIP** | `Lyrow-v4.3-win-x64-portable.zip` | 93.43 MB | — | `B13E7F659776DD6927E0FB30D1EA9D5A0D812C323BEA3A0DF3C031160EE0E662` |
| **MSIX x64** | `Lyrow-v4.3-x64.msix` | 95.69 MB | **İmzasız (`NotSigned`)** | `B75CC4A0259A7924E655A2284D8D035C955371E8F5F901E0F34644EC637694A1` |
