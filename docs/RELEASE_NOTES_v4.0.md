# Lyrow v4.0 — Auto-Open Lyrics, Lyrics Glow Toggle, Modal Settings & Compact Icon Refresh

**Sürüm:** `v4.0`  
**Tarih:** 19 Kasım 2026  
**Dağıtım Bağlantısı:** [GitHub Release v4.0](https://github.com/owergungor/Lyrow/releases/tag/v4.0)  
**Kaynak Kodu:** [owergungor/Lyrow](https://github.com/owergungor/Lyrow)  

---

## 🎯 v4.0 Genel Bakış

Lyrow v4.0 sürümü, uygulama açılışında doğrudan senkronize şarkı sözleri deneyimine geçişi, şarkı sözleri üzerindeki parlama (Glow) efektinin anında açılıp kapatılabilmesini, pencereyi bozmadan merkezde açılan yarı saydam karartmalı ve bağımsız kaydırılabilen yeni **Modal Ayarlar** deneyimini ve Dar Mod (Kompakt Görünüm) ikon iyileştirmesini sunar.

---

## 🚀 Temel Yenilikler ve İyileştirmeler

### 1. Uygulama Açılışında Otomatik Lyrics Penceresi
- Lyrow başlatıldığında Lyrics penceresi otomatik olarak açık gelir; kullanıcı doğrudan şarkı sözleri arayüzü ile karşılaşır.
- Tekil pencere koruması sayesinde aynı pencerenin birden fazla kez oluşturulması engellenir; mevcut pencere yönetimi, sistem tepsisi ve yaşam döngüsü eksiksiz korunur.

### 2. Şarkı Sözü Parlama (Glow) Açma / Kapama Butonu
- **Şarkı Sözlerini Göster/Gizle** butonunun soluna (hem Normal Modda hem de Kompakt Modda) neumorphism tasarım diline uygun yeni **Glow** butonu eklendi.
- Açık (`\uE706`) ve kapalı (`\uE708`) durumlar farklı ikonlar, vurgu çerçevesi ve yerelleştirilmiş ipuçlarıyla açıkça ayırt edilir.
- GPU hızlandırmalı, sıfır bellek tahsisli (zero-allocation) parlama katmanı kapalıyken tamamen devre dışı kalır (`Visibility.Collapsed`) ve mevcut Şarkı Sözlerini Göster/Gizle işlevini etkilemez.

### 3. Sol Üst Üç Nokta (`...`) Ghost Button & Merkezlenmiş Modal Ayarlar Penceresi
- Lyrics penceresinin en sol üst köşesine normal durumda dikkat çekmeyen, hover ve basılı durumları belirgin üç nokta (`\uE712`) Ghost Button eklendi.
- Tıklandığında ana pencerenin boyutunu ve konumunu değiştirmeden pencere üzerinde merkezlenmiş, arka planı yarı saydam koyu bir katmanla karartan ve arka plan etkileşimini engelleyen **Modal Ayarlar** penceresi açılır.
- Ayarlar içeriği arka plandaki sözlerden bağımsız olarak kaydırılabilir; tüm mevcut Lyrow ayarları (Görünüm, Dil, Tema, Parlama, Albüm Kapağı, Ses/Ambient Glow, Güncellemeler, Kısayollar, Hakkında) bu modal içinden yönetilip kaydedilebilir.
- `ESC` tuşu, kapatma butonu (`×`) veya karartılmış arka plana tıklanarak yumuşak animasyonla kapatılabilir.

### 4. Dar Mod (Kompakt Görünüm) Buton İkonunun Yenilenmesi
- Dar Mod butonunun mevcut ikonu, kompakt/dar görünüme geçişi açıkça temsil eden Windows 11 Segoe Fluent Icons `BackToWindow` (`\uE73F`) ikonu ile değiştirildi.
- Normal Moda dönüş butonu ikonu (`\uE740`) ile tam görsel simetri sağlandı.

---

## 📦 Dağıtım Dosyaları ve SHA-256 Doğrulama Özetleri

Lyrow v4.0 bağımsız (self-contained) bir Windows x64 uygulamasıdır. Çalışmak için harici bir .NET Runtime veya Windows App SDK kurulumu gerektirmez.

| Dosya Adı | Açıklama | Boyut | SHA-256 Checksum |
| :--- | :--- | :--- | :--- |
| **`Lyrow-4.0-Setup-x64.exe`** | Kullanıcı seviyesinde Inno Setup Kurulum Sihirbazı | ~64.8 MB | `C73E106C56193BE3FC7A1688C73A4F2C69E5F8865CEB4E9CD9DBD7F3FF407629` |
| **`Lyrow-v4.0-win-x64-portable.zip`** | Kurulum gerektirmeyen temiz Taşınabilir Arşiv | ~93.5 MB | `413DEBE63E18B9C91EF71453898C2FC673683F10214A1B483B716EF6FB7D5866` |

---

## 🧪 Test ve Kalite Doğrulaması

- **Otomatik Testler:** 684 / 684 PASS (%100 Başarı)
  - `Lyrow.Core.Tests`: 63 PASS
  - `Lyrow.Audio.Tests`: 53 PASS
  - `Lyrow.Music.Tests`: 120 PASS
  - `Lyrow.App.Tests`: 448 PASS (v4.0 için 8 yeni doğrulama testi dahil)
- **Önceki Sürümler:** `v3.5`, `v3.6`, `v3.7` ve `v3.8` sürümleri korunmaktadır.
