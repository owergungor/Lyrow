# Lyrow v4.1 — Sürüm Notları (Yerel Hazırlık)

**Tarih:** 2026-11-19  
**Temel Sürüm:** v4.0 (`36c4eac`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 692 / 692 PASS (%100)

---

## Öne Çıkan Yenilikler ve Değişiklikler

### 1. MSIX x64 Paket Desteği
- Dağıtım pipeline'ına (`installer/build_packages.ps1` ve `installer/AppxManifest.xml.template`) standart Windows x64 MSIX paket üretimi eklendi.
- Her sürümde artık 3 paket otomatik üretilir:
  - **Windows Setup EXE:** `Lyrow-4.1-Setup-x64.exe`
  - **Portable ZIP:** `Lyrow-v4.1-win-x64-portable.zip`
  - **MSIX (x64):** `Lyrow-v4.1-x64.msix`
- **İmzalama Durumu:** Bu yerel derlemede kod imzalama sertifikası (`LYROW_PFX_PATH`) tanımlı olmadığı için `Lyrow-v4.1-x64.msix` **imzasız (UNSIGNED)** olarak üretilmiştir. Sideload kurulum için güvenilen bir kod imzalama sertifikası ile imzalanması gerekir.

### 2. Kontrol Paneli Penceresinin (`MainWindow`) Tamamen Kaldırılması
- `MainWindow.xaml` ve `MainWindow.xaml.cs` projeden tamamen silindi.
- `LyricsWindow` uygulamanın tek birincil penceresi (`App.PrimaryWindowInstance`) oldu.
- Sistem tepsisi menüsü, tek-örnek uyandırma, global kısayollar, başlangıç yaşam döngüsü ve güncelleme denetimi doğrudan `LyricsWindow` üzerinden çalışır.

### 3. Dar Modda (Kompakt Mod) Glow Kaldırıldı
- Dar Moddaki Glow butonu ve parlama efektleri tamamen kaldırıldı; Dar Modda sıfır GPU/CPU parlama yükü garanti edilir.
- Normal Moda geri dönüldüğünde kullanıcının kayıtlı Glow tercihi korunur.

### 4. Glow (Şarkı Sözü Parlaması) Davranışı ve Görsel Durumu
- Temiz kurulumda Glow varsayılan olarak **kapalı** (`false`) başlar; kullanıcının seçimi kalıcı olarak kaydedilir.
- Aktif durumda Glow butonu **Play/Pause butonu ile birebir aynı vurgu renklerini** (`AccentButtonStyle`) kullanır; pasif durumda mevcut Ghost/Neumorphism stiline (`LyrowGhostButtonStyle`) döner.
- Şarkı sözleri gizlendiğinde Glow butonu da gizlenir (kayıtlı tercih değiştirilmeden) ve sözler tekrar açıldığında geri görünür.

### 5. Alt Kontrol Satırı Düzeni, Kenar Işığı Butonu ve Geometrik Ortalama
- `- ms` butonunun soluna **Kenar Işığı (Edge Light)** açma/kapama butonu (`BtnToggleEdgeGlow`) eklendi ve mevcut `GlowModule` + `GlowOverlayManager` ekran kenarı ışığına bağlandı.
- Kenar Işığı, `- ms`, `+ ms`, Şarkı Sözü Parlaması ve Şarkı Sözlerini Göster/Gizle butonlarının tamamı eşit **`28×28` (`CornerRadius="6"`)** boyutuna getirildi.
- `- ms / 0 ms / + ms` grubu simetrik `*,Auto,*` sütun yapısıyla **Play/Pause butonunun yatay eksenine geometrik olarak ortalandı**.

### 6. Ayarlar Modalı Sadeleştirmesi ve Dil Birliği
- Ayarlar modalındaki **"Tümü" / "All"** sekmesi ve `Tab_All` yerelleştirme anahtarları tamamen kaldırıldı.
- Türkçe ve İngilizce arayüz metinleri baştan sona denetlenerek tutarlı hale getirildi (**Kenar Işığı / Edge Light**, **Şarkı Sözü Parlaması / Lyrics Glow**, **Dar Mod / Compact Mode**).

---

## Yerel Dağıtım Paketleri ve SHA-256 Doğrulama Özetleri

| Paket | Dosya Adı | Boyut | İmza Durumu | SHA-256 |
|---|---|---|---|---|
| **Windows Setup EXE** | `Lyrow-4.1-Setup-x64.exe` | 64.80 MB | İmzasız | `0E3A6D0A7FCE394C785BFFCF37980E394A8D0EB66CFB87B158625AF0A8C9F72C` |
| **Portable ZIP** | `Lyrow-v4.1-win-x64-portable.zip` | 93.43 MB | — | `86396D0E63D02605AFD342659B522833B15CCE02950417A2975E7BA158341B88` |
| **MSIX x64** | `Lyrow-v4.1-x64.msix` | 95.68 MB | İmzasız | `8D0831F437DC0BD22FA3E982C5B4F13500DE61242E482434D53450CB89722918` |
