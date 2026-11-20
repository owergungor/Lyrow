# Lyrow v4.4 — Sürüm Notları

**Tarih:** 2026-11-20  
**Temel Sürüm:** v4.3 (`27f1371`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 728 / 728 PASS (%100, deterministik/seri koşu)

---

## Öne Çıkan Yenilikler ve Değişiklikler

### 1. Yeni Sürüm Bulunduğunda Sağ Üst Windows 11 Fluent Toast Bildirimi
- Yeni bir sürüm tespit edildiğinde uygulamanın sağ üst köşesinde (`LyricsToastContainer`, `Canvas.ZIndex="1000"`) Windows 11 Fluent Design uyumlu, yuvarlatılmış köşeli, `ThemeShadow` gölgeli ve Açık/Koyu tema duyarlı bir toast bildirimi görünür.
- Normal Mod, Tam Ekran Mod ve Dar Modda sağ üst konumunu korur, diğer uygulama kontrollerini engellemez, ekran okuyucu (`AutomationProperties.Name`) ve klavye (`Esc` ile kapatma) erişilebilirliği sunar.
- Bildirimde başlık, yeni sürüm numarası, kısa sürüm özeti ve iki buton bulunur:
  - **Şimdi Kur / Install Now:** Ortak güncelleme akışını başlatır, canlı indirme ilerlemesini gösterir, PE + SHA-256 doğrulaması yapar, ayarları güvenle kaydeder ve uygulamayı yeniden başlatarak güncellemeyi uygular.
  - **Sonra / Later:** Bildirimi kapatır, aynı oturumda tekrar tekrar toast gösterilmesini engeller; güncelleme bilgisini silmeden **Ayarlar > Güncellemeler** bölümünde hazır tutar.

### 2. Ayarlar > Güncellemeler Deneyimi
- **Yeni sürüm varsa:** Yeni sürüm başlığı, yayın tarihi (`published_at`), kısa sürüm açıklaması, belirgin **Yeni Sürümü Kur** butonu, canlı indirme/kurulum ilerleme çubuğu ve GitHub Release bağlantısı gösterilir.
- **Yeni sürüm yoksa:** Uygulamanın güncel olduğu açıkça belirtilir ve gereksiz kurulum butonu gizlenir.
- **Denetim sırasında:** `ProgressRing` yükleniyor göstergesi ve durum metni gösterilir; butonlar uygun şekilde devre dışı bırakılır.
- **Hata veya desteklenmeyen dağıtım durumunda:** Anlaşılır hata/bilgi mesajı ve **Tekrar Dene** seçeneği sunulur; uygulama çökmez.

### 3. Ortak Güncelleme Koordinatörü (`UpdateCoordinator`) ve Güvenlik
- Toast bildirimi ve **Ayarlar > Güncellemeler** aynı ortak `UpdateCoordinator` servisini kullanır; eşzamanlı çift indirme/kurulum başlatılması kesin olarak engellenir.
- Mevcut GitHub Releases API entegrasyonu, asset `digest` (`sha256:...`) + release notes SHA-256 doğrulaması ve PE imza kontrolü korunur; doğrulanmamış paket asla çalıştırılmaz.
- Setup EXE, Portable ZIP ve MSIX dağıtım türleri ayırt edilir; MSIX ve Portable kurulumlarında güvenli kurulum açıklaması ve GitHub Releases bağlantısı sunulur.
- Türkçe ve İngilizce (ve diğer desteklenen diller) ile Açık/Koyu tema uyumu tam olarak korunur.

---

## Dağıtım Paketleri ve SHA-256 Doğrulama Özetleri

| Paket | Dosya Adı | Boyut | İmza Durumu | SHA-256 |
|---|---|---|---|---|
| **Windows Setup EXE** | `Lyrow-4.4-Setup-x64.exe` | 64.79 MB | İmzasız | `73BEE5BEA6E99646D27502CC13C9E7ADB40B65C966BB97809D8EA6F7C825E282` |
| **Portable ZIP** | `Lyrow-v4.4-win-x64-portable.zip` | 93.45 MB | — | `A0F13BB98B42B3A5612043AA9E0962C7B192DCC2AB2159543D225EF1C8423AEF` |
| **MSIX x64** | `Lyrow-v4.4-x64.msix` | 95.70 MB | **İmzasız (`NotSigned`)** | `FF6B43D9E9E16A12B36C81A94BD22E6E9C80CE5E3DD732510F517CCA64EBB0E7` |

> **Not:** `Lyrow-v4.4-x64.msix` paketi imzasız (`NotSigned`) olarak üretilmiştir; Windows üzerinde doğrudan yüklemek için güvenilen bir kod imzalama sertifikasıyla imzalanması gerekir.
