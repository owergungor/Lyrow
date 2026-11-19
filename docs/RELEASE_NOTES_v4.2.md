# Lyrow v4.2 — Sürüm Notları (Yerel Hazırlık)

**Tarih:** 2026-11-19  
**Temel Sürüm:** v4.1 (`c56738f`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 705 / 705 PASS (%100)

---

## Öne Çıkan Yenilikler ve Değişiklikler

### 1. Kapak Resmine Göre Otomatik Kenar Işığı Rengi
- Çalan parçanın albüm kapağından çıkarılan baskın ve temsil edici renk paleti (`ArtworkColorExtractor`) artık doğrudan `GlowOverlayManager` ekran kenarı ışığına aktarılıyor.
- Parça değiştiğinde Kenar Işığı rengi yumuşak cross-fade animasyonuyla otomatik güncellenir; aynı albüm kapağı tekrar kullanıldığında içerik özeti önbelleği (`_paletteByContentHash`) sayesinde gereksiz analiz yapılmaz.
- Kapak resmi bulunamazsa varsayılan palete (`GlowPalette.Default`) güvenli dönüş yapılır.

### 2. Kenar Işığı Kalınlığı Hover Sürgüsü
- Kenar Işığı aç/kapa butonunun (`BtnToggleEdgeGlow`) üzerine gelindiğinde kalınlık ayar sürgüsü (`FlyoutEdgeGlowThickness` / `SliderEdgeGlowThickness`) açılır.
- Kalınlık değişimi ekran kenarında anlık uygulanır, fare ayrıldığında uygun gecikmeyle (320 ms) kapanır, klavye (Yukarı/Aşağı ok) ile erişilebilir ve kalıcı olarak kaydedilir.

### 3. Dar Modda Üç Nokta Butonu Gizlendi
- Dar Modda sol üst köşedeki üç nokta ayarlar butonu (`BtnOpenSettingsModal`) tamamen gizlenir; Normal Moda dönüldüğünde tekrar görünür.

### 4. Ghost Button Tasarımı
- Dar Mod, Tam Ekran, Önceki Parça, Sonraki Parça, Oynat/Duraklat (`46×46` merkez boyut hiyerarşisi korunarak), `- ms` ve `+ ms` butonları Açık/Koyu tema destekli Ghost Button stiline (`LyrowGhostButtonStyle`) geçirildi.

### 5. Modern Windows 11 Ayarlar Sayfası
- Ayarlar modalı sıfırdan modern Windows 11 Fluent tasarımıyla yenilendi: solda vurgu çizgili ve ikonlu kategori navigasyonu, sağda bağımsız kaydırılabilir Settings Cards düzeni, dar pencerede otomatik kompakt ikon rayı, Açık/Koyu tema ve eksiksiz Türkçe/İngilizce yerelleştirme.
- "Tümü" sekmesi olmadan tüm mevcut ayarlar, kayıt/yükleme mekanizması, merkezleme, arka plan karartma ve ESC ile kapatma davranışı eksiksiz korundu.

### 6. Şarkı Sözlerini Göster/Gizle — 2× / 0.5× Pencere Genişliği
- Normal Modda şarkı sözleri gizlendiğinde pencere X genişliği yumuşak animasyonla tam yarıya iner; tekrar gösterildiğinde tam iki katına çıkar.
- Pencerenin Y konumu ve yüksekliği kesinlikle değişmez, ekran dışına taşma engellenir, DPI ölçeklendirmesi ve hızlı çift tıklama iptali desteklenir; Dar Mod davranışı ayrı tutulur.

---

## Yerel Dağıtım Paketleri ve SHA-256 Doğrulama Özetleri

| Paket | Dosya Adı | Boyut | İmza Durumu | SHA-256 |
|---|---|---|---|---|
| **Windows Setup EXE** | `Lyrow-4.2-Setup-x64.exe` | 64.76 MB | İmzasız | `ECEADF46033ACF6C0355652B244287F547DD65AC20C1935B3D854F3DAA57B21C` |
| **Portable ZIP** | `Lyrow-v4.2-win-x64-portable.zip` | 93.43 MB | — | `269189C24BE2D9226F75598E185A1FF29F1F6EB8F49395C076087A4E78639380` |
| **MSIX x64** | `Lyrow-v4.2-x64.msix` | 95.68 MB | İmzasız | `7B52E0D2420E634CC9136F92C83C054CBB51787C06F01C673EACA6F147A8EB9C` |
