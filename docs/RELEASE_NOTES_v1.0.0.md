# Lyrow v1.0.0 — Ambient Audio Visualization & Synchronized Lyrics for Windows

Lyrow'un ilk resmi kararlı sürümü **v1.0.0** yayınlandı! 🎉

Lyrow, masaüstünüzde müzik dinleme deneyimini görsel bir şölene dönüştüren, yüksek performanslı, modern ve Neumorphic tasarımlı bir Windows multimedya platformudur.

---

## 🌟 Öne Çıkan Özellikler

### 1. Glow — Sistem Sesi Frekans Spektrumu & Ambiyans Görselleştirici
- **WASAPI Loopback:** Herhangi bir sanal ses kablosu gerekmeksizin Windows varsayılan ses çıkışını doğrudan dinler.
- **Sıfır-Tahsisli FFT:** Radix-2 Cooley-Tukey algoritması, Hanning pencereli ve önceden hesaplanmış trigonometrik tablolarla sıfır heap tahsisiyle çalışır.
- **Müzikal Frekans Bantları:** Sub-bass, Bass, Mid ve Treble aralıklarını ve 32 logaritmik sütunu dinamik sönümleme (falloff smoothing) ile 60 FPS hızında çizer.
- **Adaptif Güç Koruma:** Müzik durduğunda veya sessizlik algılandığında render hızı 5 FPS'e düşer; GPU ve CPU tüketimi <%1 seviyesine iner.

### 2. Lyrics — Gerçek Zamanlı GSMTC Medya Takibi & Senkronize Şarkı Sözleri
- **Windows GSMTC Desteği:** Spotify, Apple Music, Tidal, Chrome, Edge ve diğer tüm Windows medya oturumlarıyla tam uyumlu çalışır.
- **LRCLIB Entegrasyonu:** Milisaniye hassasiyetinde `.lrc` senkronize sözleri otomatik olarak indirir.
- **$O(\log N)$ İkili Arama (Binary Search):** Şarkı içinde ileri/geri sarma (seek) durumlarında anında doğru satırı bulur.
- **Yerel Disk Önbelleği:** İndirilen tüm sözler `%APPDATA%\Lyrow\LyricsCache` klasöründe atomik olarak saklanır; çevrimdışı durumda anında yüklenir.
- **LRCLIB Rate-Limit Koruması (HTTP 429):** Sunucu istek limitine ulaşıldığında kalan bekleme süresi Dashboard ekranında geri sayımla gösterilir ve yerel önbellek önceliklendirilir.

### 3. Gerçek Win32 Click-Through (`WS_EX_TRANSPARENT`)
- Tek bir anahtarla veya `Ctrl+Shift+C` global kısayoluyla pencereler saydamlaşır ve fare tıklamaları arka plandaki oyunlara, tarayıcılara ve uygulamalara pürüzsüzce geçer.
- Mod kapatıldığında pencereler normal etkileşime ve taşınabilirliğe geri döner.

### 4. Çoklu Monitör & Dinamik Ekran Değişikliği
- Sistemdeki tüm monitörleri dinamik olarak algılar (`Screens.All`).
- 7 farklı ekran bölgesine (`Merkez`, `Sol Üst`, `Üst Orta`, `Sağ Üst`, `Sol Alt`, `Alt Orta`, `Sağ Alt`) DPI ölçeklemesine tam uyumlu hizalanır.
- `WM_DISPLAYCHANGE` kancası sayesinde yeni monitör takıldığında, çıkarıldığında veya çözünürlük/ölçekleme değiştiğinde pencereler otomatik olarak yeniden konumlandırılır.

### 5. Windows System Tray (Sistem Tepsisi Entegrasyonu)
- Görev çubuğu bildirim alanında Lyrow ikonu yer alır.
- Pencerenin `X` butonuna basıldığında uygulama kapanmaz, arka planda müzik deneyimini kesintisiz sürdürmek için tepsiye küçülür (`MinimizeToTrayOnClose`).
- Zengin sağ tık menüsü: *Lyrow'u Göster*, *Glow Aç/Kapat*, *Lyrics Aç/Kapat*, *Click-Through Aç/Kapat*, *Ayarlar* ve *Çıkış*.
- Tepsi menüsünden *Çıkış* seçildiğinde tüm arka plan iş parçacıkları ve Win32 kancaları temiz bir şekilde kapatılır.

### 6. Global Kısayollar (Global Hotkeys)
- Uygulama arka plandayken veya oyundayken çalışan sistem geneli Win32 kısayolları:
  - `Ctrl + Shift + L` : Glow görselleştiricisini aç / kapat
  - `Ctrl + Shift + V` : Lyrics şarkı sözü penceresini aç / kapat
  - `Ctrl + Shift + C` : Click-through saydamlığını aç / kapat
- Uygulama kapanırken veya ayarlar güncellenirken tüm kısayollar güvenle serbest bırakılır (`UnregisterHotKey`).

### 7. Modern Neumorphic Tasarım & Temalar
- Koyu, Açık ve Sistem Teması seçenekleri.
- Yumuşak gölgeler, neon cam parıltıları ve minimalist müzik göstergeleri.

### 8. Windows Başlangıç Desteği
- Tek tıkla Windows açılışında otomatik başlatma seçeneği (`HKCU\Run`).
- Kurulum kaldırılırken Inno Setup `uninsdeletevalue` bayrağı ile sistemde artık anahtar bırakmaz.

---

## 📦 Dağıtım Paketleri

| Paket Türü | Dosya Adı | Boyut | Açıklama |
| :--- | :--- | :--- | :--- |
| **Windows Kurulum Paketi (Installer)** | `Lyrow-1.0.0-Setup-x64.exe` | ~57.0 MB | Inno Setup 6 tabanlı kurulum sihirbazı. Başlat menüsü, masaüstü kısayolları ve temiz kaldırma desteği içerir. |
| **Taşınabilir Sürüm (Portable)** | `Lyrow-win-x64-portable.zip` | ~81.6 MB | Kurulum gerektirmeyen bağımsız paket. Arşivi çıkartıp `Lyrow.App.exe`'yi doğrudan çalıştırabilirsiniz. |

*Her iki paket de self-contained derlenmiştir; bilgisayarınızda önceden .NET Runtime kurulu olması gerekmez.*

---

## 🛠️ Sistem Gereksinimleri
- **İşletim Sistemi:** Windows 10 (Build 19041+) veya Windows 11 (64-bit)
- **Mimari:** x64
- **Donanım:** DirectX 11 / OpenGL 3.3 uyumlu dahili veya harici grafik kartı, WASAPI uyumlu stereo/surround ses çıkışı.

---

## ⚠️ Bilinen Teknik Sınırlamalar
1. **Exclusive Fullscreen Oyunlar:** Windows DWM mimarisi gereği, masaüstü overlay pencereleri (Glow ve Lyrics) "Exclusive Fullscreen" çalışan oyunların üzerinde görüntülenemez. Oyun ayarlarından **"Borderless Windowed" (Kenarlıksız Pencereli)** modunun seçilmesi gerekmektedir.
2. **Korumalı DRM Ses Akışları:** Bazı özel DRM korumalı ses akışları Windows WASAPI Loopback tarafından sessiz olarak bildirilebilir. Standart müzik akış servisleri (Spotify, Apple Music, YouTube vb.) bu durumdan etkilenmez.
3. **Şarkı Sözü Eşleşmesi:** LRCLIB topluluk destekli bir veritabanıdır; metadata bilgisi (Sanatçı & Şarkı Adı) eksik olan yerel dosyalarda veya nadir parçalarda şarkı sözü bulunamayabilir.
