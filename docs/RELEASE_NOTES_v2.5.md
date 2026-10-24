# Lyrow v2.5 Sürüm Notları / Release Notes

**Yayın Tarihi:** 24 Ekim 2026  
**Sürüm:** `v2.5`  
**GitHub Tag:** `v2.5`  
**Installer:** `Lyrow-2.5-Setup-x64.exe`  
**Portable:** `Lyrow-v2.5-win-x64-portable.zip`  
**Platform:** Windows 11 / Windows 10 (Build 19041+), x64  
**Mimari:** .NET 10.0 LTS + WinUI 3 + Windows App SDK  

---

## 🌟 Özet / Overview

Lyrow v2.5; Apple Music tasarım estetiğini, VoxelVerse akıcılığını ve müzikle senkron 4 kenarlı dinamik aydınlatmayı en üst düzeye çıkaran kapsamlı bir UX, stabilite ve görsel polish sürümüdür.

Bu sürümde otomatik parça geçişlerindeki zamanlama ve şarkı sözü arama sorunları kalıcı olarak çözülmüş, şarkı sözleri için dinamik albüm kapağı renk alanı arka planı eklenmiş, VoxelVerse tarzı 60 FPS satır hareket durumu makinesi entegre edilmiş, sistem tepsisi WinUI Fluent temasına tam uyumlu hale getirilmiş ve dahili güncelleme mekanizması kurulum sihirbazı penceresi açılmadan tamamen sessiz ikili dosya yenileme desteğine kavuşturulmuştur.

---

## 🚀 Yeni Özellikler ve İyileştirmeler

### 1. 🔀 Kesintisiz Parça Geçişi (Atomic Track Switch & Timeline Reset)
- **Sorun:** Parça A'dan Parça B'ye otomatik geçildiğinde GSMTC'nin eski süreyi (`EndTime`) bildirmesi nedeniyle süre birikmesi, progress bar'ın %100'de kalması, LRCLIB'de süre uyuşmazlığı nedeniyle 404 dönmesi ve eski şarkı kapağının takılı kalması engellendi.
- **Çözüm:**
  - `WindowsGsmtcMediaSessionMonitor` üzerinde parça geçiş anında timeline sıfırlanarak durum `MediaPlaybackStatus.Changing` olarak işaretlenir.
  - `LyricsModule` içinde atomik sıra sayacı (`_trackSequence`) ve parça bazlı `CancellationTokenSource` entegre edildi. Eski parçanın geciken HTTP/Lrclib yanıtları yeni parçayı ezemez.
  - LRCLIB istemcisine süre uyuşmazlığı geri çekilme (`fallback without duration`) eklendi; parça süresi GSMTC ile LRCLIB arasında birkaç saniye farklı olsa dahi sözler eksiksiz bulunur.
  - Ana ekranda ve şarkı sözü penceresinde timeline, progress bar ve kapak görseli anında yenilenir.

### 2. 🎵 VoxelVerse & Apple Music Akıcı Şarkı Sözü Hareketi
- **60 FPS Dinamik Yay Animasyonu:** Satır kaydırma enterpolasyonu `Idle` -> `ApproachingActive` -> `Active` -> `Passed` durum makinesiyle kesintisiz hale getirildi.
- **Hiyerarşik Tipografi ve Opaklık:** Aktif satır %100 opaklık ve optik ölçekleme ile öne çıkarılırken, yaklaşan satırlar %65, geçmiş satırlar %30 yumuşak opaklık kazanır.
- **Dikey Optik Merkezleme:** Aktif şarkı sözü satırı viewport yüksekliğinin tam merkezinde dikey olarak sabitlenir.

### 3. 🎨 Dinamik Albüm Kapağı Arka Planı (Ambient Gradient Color Field)
- `ArtworkPaletteExtractor` mimarisiyle çalan şarkının albüm kapağından 4 dominant/vibrant renk çıkarılır.
- **WCAG Uyumlu Güvenli Parlaklık:** Metin okunabilirliği için güvenli parlaklık tavanı (`MaxSafeLuminance = 55.0`) uygulanarak aktif beyaz sözlerle >7:1 kontrast sağlanır.
- Şarkı geçişlerinde renk alanları 60 FPS yumuşak lineer enterpolasyon (`Lerp`) ile çapraz geçiş (cross-fade) yapar.

### 4. 🖥️ Tam Ekran Apple Music Kompozisyonu (F11)
- `F11` kısayolu veya başlık çubuğundaki tam ekran butonuyla etkinleşen sürükleyici mod.
- **Geniş Ekran İki Kolonlu Yerleşim:**
  - Sol kolon: Optik merkezlenmiş büyük yuvarlak köşeli albüm kapağı, başlık, sanatçı ve albüm tipografisi.
  - Sağ kolon: Geniş tipografiyle dikey merkezlenen 60 FPS akıcı şarkı sözleri.
  - Alt çubuk: İnce ve zarif oynatma çubuğu.

### 5. 📥 Tamamen Sessiz In-App Self-Update (No Wizard Re-installer)
- Mevcut kurulumların üzerine güncelleme yapılırken Inno Setup sihirbaz pencereleri gösterilmez.
- Güncelleme indirildiğinde arka planda `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` parametreleriyle binary değişimi yapılır ve Lyrow anında en son sürümle yeniden başlar.

### 6. 🧭 Sadeliğe Kavuşan Settings ve Tıklanabilir Dashboard
- **Derli Toplu Ayarlar:** Settings sayfası yalnızca `General`, `Updates` ve `Shortcuts` kategorilerini içerir; gereksiz kontroller kaldırılmıştır.
- **Genişletilmiş Kenar Çubuğu (Sidebar):** Home, Lyrics, Glow, Playback ve Settings sekmeleri doğrudan erişilebilirdir.
- **Tıklanabilir Dashboard Kartları:** Ana sayfadaki Parça Kapağı, Ses, Glow ve Şarkı Sözü kartları ilgili sayfalara doğrudan yönlendirir.
- **İkon Tabanlı Medya Kontrolleri:** Önceki, Oynat/Duraklat, Sonraki butonları modern ikon-yalnız stiline ve durum duyarlı gliflere (`\uE768` / `\uE769`) kavuştu.

### 7. 📥 Gelişmiş Windows Sistem Tepsisi (System Tray)
- Windows 11 Fluent temasına tam uyumlu Win32 / WinUI sistem tepsisi menüsü.
- Menü hiyerarşisi:
  - Şimdi Çalıyor Başlığı (Parça, Sanatçı, Albüm)
  - Medya Kontrolleri (Oynat/Duraklat, Önceki, Sonraki, Sesi Kapat/Aç)
  - Pencereler (Ana Sayfa, Şarkı Sözleri, Tam Ekran, Oynatma, Glow, Ayarlar)
  - Uygulama (Güncellemeleri Denetle, Çıkış)

### 8. 🌐 5 Dilde Eksiksiz Yerelleştirme
- English, Türkçe, Français, Español ve Deutsch dillerinde tüm yeni menü öğeleri, ayar etiketleri ve bildirimler eksiksiz çevrildi.

---

## 🧪 Test ve Kalite Güvencesi (QA)

| Test Paketi | Test Sayısı | Durum |
| :--- | :--- | :--- |
| `Lyrow.Core.Tests` | 34 | ✅ PASS (0 fail) |
| `Lyrow.Audio.Tests` | 40 | ✅ PASS (0 fail) |
| `Lyrow.Music.Tests` | 62 | ✅ PASS (0 fail) |
| `Lyrow.App.Tests` | 97 | ✅ PASS (0 fail) |
| **Toplam Test** | **233** | **%100 PASS** |

- **Legacy İsim Kontrolü:** `git grep -i -w "lumn"` ve `git grep -i -w "verci"`: **0 sonuç (Sıfır tolerans)**.
- **Sürümleme Kuralı:** İki parçalı kesin sürüm `2.5` / `v2.5` tüm proje dosyalarında, manifestte, installer'da ve workflow'larda doğrulanmıştır.
