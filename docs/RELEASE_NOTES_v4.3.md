# Lyrow v4.3 — Sürüm Notları (Yerel Hazırlık)

**Tarih:** 2026-11-19  
**Temel Sürüm:** v4.2 (`7f9ab66`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 711 / 711 PASS (%100)

---

## Öne Çıkan Yenilikler ve Değişiklikler

### 1. Dar Mod — Ghost Button Tasarımı
- Dar Moddaki **Volume & Mute**, **Play/Pause**, **Previous**, **Next**, **Normal Mode** ve **Compact Lyrics Show/Hide** butonlarının tamamı Açık/Koyu tema destekli Ghost Button stiline (`LyrowGhostButtonStyle`) geçirildi.

### 2. Dar Mod — İkon-Odaklı Audio Output Butonu
- Dar Moddaki Audio Output butonu (`BtnDeviceSelectorCompact`) metinden arındırılarak yalnızca ikon gösteren, Volume & Mute ile birebir aynı **`26×26`** ölçülerde Ghost Button haline getirildi; aktif cihaz bilgisi Tooltip üzerinden sunulur.

### 3. Dar Mod — Kenar Işığı Butonu
- Compact Lyrics Show/Hide butonunun hemen soluna **`26×26`** boyutunda, ikon-odaklı Kenar Işığı aç/kapa butonu (`BtnToggleEdgeGlowCompact`) eklendi ve ortak Kenar Işığı motoruna bağlandı.

### 4. Normal Mod — Tıklamayla Açılan `0–64 px` Kenar Işığı Kalınlık Ayarı
- Kalınlık sürgüsünün hover ile açılması kaldırıldı; artık yalnızca Kenar Işığı butonuna **tıklandığında** açılır, tekrar tıklama veya dışarı tıklama ile kapanır; klavye erişilebilirliği korunur.
- Kalınlık aralığı **`0–64 px`** olarak güncellendi: `0 px` Kenar Işığını tamamen kapatır, `1–64 px` arasında kalınlık anlık uygulanır, kalıcı kaydedilir ve Ayarlar sayfasıyla senkronize çalışır.

### 5. Ayarlar Sayfası — Microsoft Windows 11 Fluent Design
- Windows 11 Settings tasarım ilkelerine (Zahmetsiz, Sakin, Kişisel, Tanıdık, Tutarlı) göre Fluent ikonlu `SettingsCard` düzeni, kategori alt başlıkları ("Genel", "Şarkı Sözleri", "Sistem ve Bildirimler"), tutarlı köşe yuvarlaklıkları ve tipografi hiyerarşisi uygulandı.

---

## Yerel Dağıtım Paketleri ve SHA-256 Doğrulama Özetleri

| Paket | Dosya Adı | Boyut | İmza Durumu | SHA-256 |
|---|---|---|---|---|
| **Windows Setup EXE** | `Lyrow-4.3-Setup-x64.exe` | 64.77 MB | İmzasız | `FFC62CDE5A1A143920BDF2F47EAD2BDCFF7090ABC953140D8EC131ABAFBE59EC` |
| **Portable ZIP** | `Lyrow-v4.3-win-x64-portable.zip` | 93.43 MB | — | `0968753288EC4C53C92E6B8899B579B108EFC6992691569A3A06426DCC5C4F6D` |
| **MSIX x64** | `Lyrow-v4.3-x64.msix` | 95.69 MB | İmzasız | `181BB7DE686EF5A4BF68E047D85673FDA12CFF4874C2A04B900402F065D3C4D5` |
