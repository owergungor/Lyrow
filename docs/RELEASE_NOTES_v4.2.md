# Lyrow v4.2 — Sürüm Notları (Yerel Hazırlık)

**Tarih:** 2026-11-19  
**Temel Sürüm:** v4.1 (`c56738f`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 706 / 706 PASS (%100)

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
| **Windows Setup EXE** | `Lyrow-4.2-Setup-x64.exe` | 64.77 MB | İmzasız | `EEB4F3A0631380DDB44CF7810625EEC793C207788AACEAC9C08BB060B9F12757` |
| **Portable ZIP** | `Lyrow-v4.2-win-x64-portable.zip` | 93.43 MB | — | `DCEDB75A942C19845FD4EC182A5D74851F6C64C0DF9CC74C413D1C936060BF63` |
| **MSIX x64** | `Lyrow-v4.2-x64.msix` | 95.69 MB | İmzasız | `E3E50BDFE2D615342C4EA4ABFF0F97502FC41259FA182CD50F54D0B49AB1F920` |
