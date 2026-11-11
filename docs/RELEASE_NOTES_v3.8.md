# Lyrow v3.8 — Karaoke Word Highlight, Layout Recovery & Polish

**Sürüm:** `v3.8`  
**Tarih:** 11 Kasım 2026  
**Dağıtım Bağlantısı:** [GitHub Release v3.8](https://github.com/owergungor/Lyrow/releases/tag/v3.8)  
**Kaynak Kodu:** [owergungor/Lyrow](https://github.com/owergungor/Lyrow)  

---

## 🎯 v3.8 Genel Bakış

Lyrow v3.8 sürümü, özellikle kelime zamanlamalı şarkı sözlerinde (word-level synced lyrics) karaoke kelime vurgulamalarının sırayla ve akıcı biçimde beyaz renkte renklendirilmesini, satır sarmalı (wrapped) sözlerde kelime vurgusunun alt satırlara doğru uygulanmasını, Normal Mod arayüz yerleşiminin ("Hide Lyrics" ve alt kontrollerin her ekran çözünürlüğünde tam görünürlüğü), şarkı geçişlerindeki kalan süre göstergesinin ve ayarlar sekmelerinin cilalanmasını içerir.

---

## 🚀 Temel Yenilikler ve Düzeltmeler

### 1. Karaoke Kelime Vurgulama Motoru (Word-Level Timing)
- **Gerçek Zamanlı Kelime Boyama:** Kelime zaman damgası bulunan şarkılarda aktif söz satırı içerisindeki kelimeler oynatma süresine göre sırayla ve akıcı bir şekilde beyaz renkle vurgulanır.
- **Ölçüm Kilitlenmesi Giderildi:** WinUI 3'te `Visibility.Collapsed` elemanların genişlik ölçümünü (`ActualWidth = 0`) kaybetmesi sonucu kelimelerin boyanamaması sorunu, `Visibility.Visible` tutulup geometri kırpması (`RectangleGeometry`) kullanılarak çözüldü.

### 2. Satır Sarmalı Şarkı Sözleri (Wrapped Lyrics Isolation)
- Uzun söz satırları pencere genişliğine göre alt alta sarıldığında, 1. alt satır tamamlanmadan ve 2. alt satırın zaman damgası gelmeden 2. satırdaki kelimelerin erken boyanması kesin olarak engellendi.
- Her iki alt satır da bağımsız zaman dilimlerine göre sıralı ve pürüzsüz boyanır.

### 3. Normal Mod Arayüz Yerleşimi ve Görünürlük
- Normal modda albüm kapağı, parça bilgileri, zaman çizelgesi, ses çıkış seçimi, oynatma kontrolleri, ses seviyesi ve "Hide Lyrics" butonu ilk açılışta ve farklı Windows DPI ölçeklerinde pencere dışına taşmadan tam görünür hale getirildi.
- Duyarlı boyutlandırma hesaplamaları güçlendirildi.

### 4. Şarkı Geçişlerinde Kalan Süre Eşitlemesi
- Parça geçişlerinde yeni şarkının toplam süresi GSMTC'den okunana kadar kalan sürenin eski parçanın değerini göstermesi engellendi; süreler anında sıfırlanıp yeni parça süresiyle eşzamanlanır.

### 5. Ayarlar Sekmeleri ve Yerelleştirme İyileştirmeleri
- Ayarlar penceresinde kullanılmayan ve boş kalan Sistem sekmesi tamamen kaldırılarak Güncellemeler ve Kısayollar sekmeleri öne çıkarıldı.
- Türkçe ve İngilizce dil çevirilerindeki tüm eksik anahtarlar tamamlandı.

### 6. Kompakt Mod (Dar Mod) Genişletme Simgesi
- Kompakt moddan normal moda geçiş butonu simgesi standart köşegen genişletme simgesi (`\uE740`) ile güncellendi.

---

## 📦 Dağıtım Dosyaları ve SHA-256 Doğrulama Özetleri

Lyrow v3.8 bağımsız (self-contained) bir Windows x64 uygulamasıdır. Çalışmak için harici bir .NET Runtime veya Windows App SDK kurulumu gerektirmez.

| Dosya Adı | Açıklama | Boyut | SHA-256 Checksum |
| :--- | :--- | :--- | :--- |
| **`Lyrow-3.8-Setup-x64.exe`** | Kullanıcı seviyesinde Inno Setup Kurulum Sihirbazı | ~64.6 MB | `7C580CB087FD390652C3198C6EB76AA01FE960580478DF5B6EFAD2D4C3FD6588` |
| **`Lyrow-v3.8-win-x64-portable.zip`** | Kurulum gerektirmeyen temiz Taşınabilir Arşiv | ~93.2 MB | `21D75F4D9637C8597422152D2D3BAA5A43C5EF82B7445149916406E856F6CC67` |

---

## 🧪 Test ve Kalite Doğrulaması

- **Otomatik Testler:** 676 / 676 PASS (%100 Başarı)
  - `Lyrow.Core.Tests`: 63 PASS
  - `Lyrow.Audio.Tests`: 53 PASS
  - `Lyrow.Music.Tests`: 120 PASS
  - `Lyrow.App.Tests`: 440 PASS
- **GitHub Actions CI:** SUCCESS
- **Önceki Sürümler:** `v3.5`, `v3.6` ve `v3.7` sürümleri korunmaktadır.
