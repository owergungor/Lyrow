# Lyrow — Stage 40: Gerçek EXE Doğrulama ve Güncel Dağıtım Raporu

## 1. Kök Neden Tespiti: Neden Eski Davranışlar Görünüyordu?

Kullanıcı ortamında çalışan ve Başlat Menüsü / masaüstü kısayollarından tetiklenen gerçek uygulama konumu:
- **Kısayol Hedefi**: `C:\Users\omer\AppData\Local\Programs\Lyrow\Lyrow.App.exe`
- **Eski Dosya Durumu**:
  - Son Değiştirilme: `10.11.2026 09:21:28` (v3.7 Release tarihi)
  - Boyut: `675.328 bytes` (DLL boyutu: `855.552 bytes`)
  - SHA-256 (`Lyrow.App.exe`): `561AABB194E551B5DAC07CDA54A4EFDF7F14DDB665E0E2E08656066545A5B334`
- **Sonuç**:
  Stage 39 kodları GitHub'a pushlanıp CI'da geçmesine rağmen, kullanıcının Windows ortamında kurulu olan `AppData\Local\Programs\Lyrow` klasöründeki dosya 10 Kasım tarihli eski v3.7 sürümüydü. Kullanıcı uygulamayı Başlat menüsünden veya kurulu konumdan açtığında doğal olarak eski exe çalışıyordu.

---

## 2. Gerçekleştirilen İşlemler

1. **Çalışan İşlem Kontrolü**:
   - Sistemde çalışan eski `Lyrow` veya `Lyrow.App.exe` işlemleri kontrol edildi; açık işlem bulunmadığı doğrulandı.
2. **Kapsamlı Kod Temizliği**:
   - `Tab_System` anahtarı tüm dil sözlüklerinden (`tr`, `en`, `fr`, `es`, `de`) tamamen silindi.
   - Testlerdeki eski `Tab_System` beklentileri güncellendi.
   - Değişiklikler commitlendi (`cd1bbe9`) ve `origin/main`e pushlandı.
   - GitHub Actions CI Run [43059848620](https://github.com/owergungor/Lyrow/actions/runs/43059848620) **SUCCESS** olarak tamamlandı.
3. **Temiz Self-Contained Publish**:
   - `build_packages.ps1` dağıtım pipeline'ı Release konfigürasyonunda çalıştırıldı (`dotnet publish -c Release -r win-x64 --self-contained true`).
   - `C:\Users\omer\Projects\Lyrow\publish\win-x64\Lyrow.App.exe` ve tüm bağımlılıklar sıfırdan derlendi.
4. **Kurulu Uygulama Dizinine Eşitleme**:
   - Güncel publish çıktısı `C:\Users\omer\AppData\Local\Programs\Lyrow\` dizinine kopyalanarak eski v3.7 sürümünün üzerine yazıldı.
   - Kısayollar ve doğrudan çalıştırmalar için her iki konum da birebir aynı güncel binary'ye getirildi.

---

## 3. Güncel EXE ve DLL Doğrulama Verileri

### A. Publish Klasörü:
**Yol:** `C:\Users\omer\Projects\Lyrow\publish\win-x64\Lyrow.App.exe`
- **Son Değiştirilme:** `11.11.2026 19:36:09`
- **Boyut:** `675.328 bytes`
- **SHA-256 (`Lyrow.App.exe`):** `3001A27D48CA78884A3F8392CA661A62D4A9CC1D520B79427ACB770A000EB605`
- **SHA-256 (`Lyrow.App.dll`):** `E39B33A2F3A13FCE5E16B4D394FF7A6486EA162BC2441E545C2A4F6E8912E476`

### B. Kurulu Program Klasörü (Başlat Menüsü Hedefi):
**Yol:** `C:\Users\omer\AppData\Local\Programs\Lyrow\Lyrow.App.exe`
- **Son Değiştirilme:** `11.11.2026 19:36:09`
- **Boyut:** `675.328 bytes`
- **SHA-256 (`Lyrow.App.exe`):** `3001A27D48CA78884A3F8392CA661A62D4A9CC1D520B79427ACB770A000EB605`
- **SHA-256 (`Lyrow.App.dll`):** `E39B33A2F3A13FCE5E16B4D394FF7A6486EA162BC2441E545C2A4F6E8912E476`

*(Her iki konumun dosya karmaları ve boyutları birebir eşleşmektedir).*

---

## 4. Stage 39 Düzeltmelerinin Yeni EXE'de Doğrulanması

1. **Normal Modda Hide Lyrics Butonu ve Alt Kontroller**:
   - `LyricsViewportCalculator` artık kontroller için 340 px rezerve ediyor.
   - Başlık, sanatçı, süre çubuğu, ses çıkış seçici, playback kontrolleri, ses düzeyi ve sağ alttaki "Hide Lyrics" butonu doğrudan görünür durumdadır.
2. **Satır Saran Karaoke Sözlerinde Kelime Vurgulama**:
   - İki satıra bölünen dizede her alt satır ayrık `TextBlock` (`TextWrapping.NoWrap`) ve bağımsız `RectangleGeometry` kırpması ile çizilir.
   - İkinci satırın zamanlaması gelmeden ikinci satırdaki kelimeler asla beyazlaşmaz.
3. **Ayarlar Sekmelerinden Sistem'in Kaldırılması**:
   - Sekme çubuğunda yalnızca 7 sekme (`All`, `Appearance`, `Artwork`, `Audio`, `Updates`, `Shortcuts`, `About`) yer alır.
   - `Tab_System` anahtarı tüm sözlüklerden silinmiştir.
   - Görünüm sekmesindeki kartın adı "Sistem ve Entegrasyon"dur.
4. **Türkçe ve İngilizce Ayar Çevirileri**:
   - Tema isimleri, güncelleme sıklığı, ses yakalama teşhis metinleri, buton ipuçları ve Hakkında alt metinleri eksiksiz yerelleştirilmiştir.
5. **Dar Moddan Normal Moda Geçiş Simgesi**:
   - Dar mod sağ üst simgesi çapraz açılma oku (`\uE740`) olarak derlenmiştir ve tooltip'i yerelleştirilmiştir.
