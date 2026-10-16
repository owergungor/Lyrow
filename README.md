# Lyrow

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?logo=windows)](https://microsoft.com/windows)
[![.NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: Avalonia](https://img.shields.io/badge/UI-Avalonia%2012-8A2BE2)](https://avaloniaui.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Lyrow**, masaüstünüzde müzik dinleme deneyimini görsel bir şölene dönüştüren, yüksek performanslı, modern ve Neumorphic tasarımlı bir Windows multimedya platformudur.

Lyrow iki bağımsız ancak birbiriyle uyum içinde çalışabilen ana modülden oluşur:

- **Lumn:** Sistem ses çıkışını (WASAPI Loopback) anlık yakalayan, sıfır-tahsisli FFT frekans analiziyle donanım hızlandırmalı ritim ve spektrum dalgaları oluşturan dinamik ses görselleştiricisi.
- **Verci:** Sistemde çalan müziği (Spotify, Apple Music, YouTube vb.) Windows GSMTC üzerinden yakalayan, LRCLIB ile senkronize şarkı sözlerini milisaniye hassasiyetinde çeken ve pürüzsüz kaydırma ile masaüstünüzde sergileyen yüzen şarkı sözü penceresi.

---

## 📸 Temel Özellikler

- **🖱️ Gerçek Win32 Click-Through:** Tek bir anahtarla veya global kısayolla (`WS_EX_LAYERED` & `WS_EX_TRANSPARENT`) overlay pencereleri saydamlaştırılır ve tüm fare tıklamaları arka plandaki oyunlara, tarayıcılara ve kod editörlerine pürüzsüzce geçer.
- **🔔 Windows System Tray (Sistem Tepsisi):** Bildirim alanı ikonu üzerinden Lyrow'u tek tıkla arka plana alabilir, 'X' butonu ile tepsiye küçültebilir ve sağ tık menüsüyle modülleri yönetebilirsiniz.
- **⌨️ Global Win32 Kısayolları (Hotkeys):** Uygulama arka plandayken veya oyundayken dahi çalışan sistem geneli kısayollar (`Ctrl+Shift+L`, `Ctrl+Shift+V`, `Ctrl+Shift+C`).
- **🖥️ Akıllı Çoklu Monitör & Dinamik Ekran Değişikliği:** Sistemdeki tüm ekranlar algılanır (`Screens.All`). `WM_DISPLAYCHANGE` kancası sayesinde monitör takılıp çıkarıldığında veya çözünürlük/DPI değiştiğinde overlay'ler otomatik olarak yeniden konumlandırılır.
- **🛡️ Akıllı LRCLIB Rate-Limit Yönetimi:** HTTP 429 durumlarında `Retry-After` süresi UI'da canlı geri sayımla gösterilir; gereksiz API sorguları önlenir ve çevrimdışı yerel önbellek önceliklendirilir.
- **🚀 Windows Başlangıç Entegrasyonu:** Tek tıklamayla Windows açılışında otomatik başlatma desteği (`HKCU\Run`); kaldırma esnasında hiçbir artık kayıt bırakılmaz.
- **🎨 Kusursuz Neumorphism Teması:** Açık, Koyu ve Sistem Teması modları ile göz yormayan, derinlikli cam efektleri ve yumuşak gölgeler.
- **⚡ Sıfır Arka Plan Yükü:** Pencereler gizlendiğinde veya simge durumuna küçültüldüğünde GPU render döngüsü ve zamanlayıcılar otomatik olarak durur; boşta CPU tüketimi <%1 kalır.
- **💾 Atomik Kalıcı Ayarlar:** Tüm kullanıcı tercihleri `%APPDATA%\Lyrow\settings.json` altında atomik olarak saklanır; yükseltmelerde veya yeniden başlatmalarda korunur.


---

## 💻 Sistem Gereksinimleri

| Bileşen | Minimum | Önerilen |
| :--- | :--- | :--- |
| **İşletim Sistemi** | Windows 10 (Build 19041+) 64-bit | Windows 11 (64-bit) |
| **İşlemci (CPU)** | Intel Core i3 / AMD Ryzen 3 | Intel Core i5 / AMD Ryzen 5 veya üstü |
| **Bellek (RAM)** | 4 GB | 8 GB+ |
| **Ekran Kartı (GPU)** | DirectX 11 / OpenGL 3.3 uyumlu dahili grafik | Donanım hızlandırmalı harici GPU |
| **Ses Aygıtı** | WASAPI uyumlu stereo/surround ses çıkışı | Aktif ses kartı / kulaklık |
| **Çalışma Zamanı** | Bağımsız pakette (.NET gerekmez) | .NET 10 LTS SDK (yalnızca kaynak kod derlemesi için) |

---

## 🚀 Kurulum

### Yöntem 1: Windows Kurulum Paketi (Önerilen)
1. [GitHub Releases](https://github.com/owergungor/Lyrow/releases) sayfasından en son `Lyrow-1.0.0-Setup-x64.exe` dosyasını indirin.
2. Kurulum sihirbazını çalıştırın. İsteğe bağlı olarak masaüstü kısayolu oluşturun.
3. Kurulum tamamlandığında Lyrow otomatik olarak başlayacaktır.
4. *Not:* Kurulum ve yükseltmelerde kullanıcı ayarlarınız (`%APPDATA%\Lyrow`) silinmez, korunur.

### Yöntem 2: Taşınabilir (Portable) ZIP
1. `Lyrow-win-x64-portable.zip` arşivini indirin.
2. Dilediğiniz bir klasöre çıkartın.
3. `Lyrow.App.exe` dosyasını doğrudan çalıştırın.

---

## 🎯 İlk Çalıştırma ve Kullanım

1. **Dashboard'u Açın:** Uygulama başladığında **Lyrow — Multimedya Deneyim Merkezi** açılır.
2. **Medya Oynatın:** Bilgisayarınızda Spotify, YouTube veya herhangi bir oynatıcıda bir şarkı başlatın.
3. **Canlı Göstergeler:**
   - Üst bilgi çubuğunda şarkı adı, sanatçı ve albüm anında görünür.
   - Lumn ses seviyesi göstergesi (dB) müzikle birlikte dalgalanmaya başlar.
   - Verci durumu sözlerin LRCLIB'den başarıyla yüklendiğini veya yerel önbellekten okunduğunu rozetle gösterir.
4. **Modülleri Başlatın:**
   - **Lumn Penceresini Aç** butonuna basarak dinamik spektrum penceresini görüntüleyin.
   - **Verci Penceresini Aç** butonuna basarak şarkı sözü penceresini açın.

---

## 🎛️ Modül ve Ayar Kılavuzu

### 1. Lumn (Görsel Efekt & Spektrum)
- **Hassasiyet:** Lumn ses giriş kazancını `%50` ile `%200` arasında ayarlayarak düşük ses seviyelerinde bile görsel efektlerin canlı kalmasını sağlayabilirsiniz.
- **Sessizlik Modu:** Ses durduğunda veya müzik bittiğinde Lumn otomatik olarak 5 FPS güç koruma moduna geçer; GPU'nuzu yormaz.

### 2. Verci (Senkronize Şarkı Sözleri)
- **Zaman Kaydırma (Offset):** Medya oynatıcınız ile şarkı sözleri arasında gecikme varsa, Verci penceresindeki `+0.5s` veya `-0.5s` butonlarıyla veya ana ayarlar panelinden ofseti milisaniye bazında ayarlayabilirsiniz.
- **Otomatik Söz Çekme:** Şarkı değiştiğinde LRCLIB üzerinden otomatik arama yapılmasını sağlar.
- **Yerel Disk Önbelleği:** İndirilen sözler `%APPDATA%\Lyrow\LyricsCache` klasörüne kaydedilir; böylece aynı şarkı tekrar çalındığında internet bağlantısı olmasa bile anında ekrana gelir.

### 3. Click-Through (Tıklama Geçirgenliği)
- **Nasıl Çalışır:** Ayarlar panelinden **"Fare Tıklamalarını Arkaya Geçir (Click-Through)"** seçeneğini açtığınızda veya `Ctrl+Shift+C` kısayoluna bastığınızda, pencerelerin arka planı saydamlaşır ve üzerlerine tıkladığınızda fare imleci arkadaki pencereye erişir.
- **Tekrar Düzenleme:** Pencereleri taşımak veya boyutlandırmak istediğinizde ana dashboard üzerinden veya `Ctrl+Shift+C` ile modu kapatmanız yeterlidir.

### 4. Çoklu Monitör ve Konumlandırma
- **Monitör Seçimi:** Sisteminizde takılı olan ekranlar (Örn: *1: Monitör 1 [1920x1080]* ve *2: Monitör 2 [2560x1440]*) açılır listede görünür.
- **Ekran Bölgesi:** Pencerelerin ekranın neresinde duracağını seçin (*Merkez*, *Sağ Alt*, *Üst Orta* vb.).
- **Dinamik Ekran Adaptasyonu:** Yeni bir monitör bağlandığında veya çözünürlük/ölçekleme değiştiğinde Lyrow pencereleri anında otomatik hizalar. İkincil monitör sökülürse birincil ekrana güvenle toparlanır.

### 5. Sistem Tepsisi (System Tray) & Global Kısayollar
- **Tepsiye Küçültme:** Ana pencerenin `X` butonuna bastığınızda uygulama kapanmaz, sistem tepsisine (`Tray`) küçülür.
- **Gerçek Çıkış:** Uygulamayı tamamen kapatmak için sistem tepsisindeki Lyrow ikonuna sağ tıklayıp **"Çıkış"** seçeneğini seçin.
- **Varsayılan Global Kısayollar:**
  - `Ctrl + Shift + L` : Lumn görselleştiricisini aç / kapat
  - `Ctrl + Shift + V` : Verci şarkı sözü penceresini aç / kapat
  - `Ctrl + Shift + C` : Click-through saydamlığını aç / kapat

---

## 🛠️ Sorun Giderme

| Sorun | Olası Neden | Çözüm |
| :--- | :--- | :--- |
| **Lumn spektrumu hareket etmiyor** | Ses aygıtı sessizde veya aktif ses çıkışı yok | Bilgisayarınızda ses çaldığından emin olun. Kulaklık/hoparlör takıldığında Lumn otomatik olarak yeni varsayılan çıkışa bağlanacaktır. |
| **Verci sözleri bulamıyor** | Şarkı LRCLIB veritabanında yok veya enstrümantal | Şarkının metadata etiketlerinin (Sanatçı ve Başlık) doğru olduğunu kontrol edin. Yerel şarkılar için LRCLIB üzerinde eşleşme olmayabilir. |
| **LRCLIB Rate Limit Uyarısı** | Kısa sürede çok fazla istek gönderildi | LRCLIB sunucu limitine ulaşıldığında kalan bekleme süresi Dashboard üzerinde geri sayımla gösterilir; süre bitene kadar yerel önbellek kullanılır. |
| **Overlay oyunların arkasında kalıyor** | Oyun "Exclusive Fullscreen" modunda | Windows mimarisi gereği masaüstü overlay'leri için oyun ayarlarından **"Borderless Windowed" (Kenarlıksız Pencereli)** modunu seçin. |
| **Pencereyi taşıyamıyorum** | Click-Through modu aktif | `Ctrl+Shift+C` kısayoluna basın veya Dashboard ekranından Click-Through anahtarını kapatın. |

---

## 👨‍💻 Geliştirici Kılavuzu

### Gereksinimler
- Windows 10/11 64-bit
- .NET 10.0 SDK (veya .NET 9.0 SDK)
- PowerShell 7+

### Çözümü Klonlama ve Derleme
```powershell
git clone https://github.com/owergungor/Lyrow.git
cd Lyrow

# Bağımlılıkları geri yükleme
dotnet restore Lyrow.sln

# Release konfigürasyonunda derleme
dotnet build Lyrow.sln -c Release

# Test paketini çalıştırma (95 test)
dotnet test Lyrow.sln -c Release --no-build
```

### Self-Contained Windows Dağıtımı Üretme
```powershell
# win-x64 Release publish
dotnet publish src/Lyrow.App/Lyrow.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64

# Inno Setup ile kurulum paketi oluşturma
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/setup.iss
```

---

## 🚀 GitHub Actions & Sürüm Süreci

- **Sürekli Entegrasyon (CI):** Her `main` dalı commit'inde ve Pull Request'te `.github/workflows/ci.yml` çalışır; çözümü derler ve 95 testin tamamını doğrular.
- **Resmi Sürüm Dağıtımı (Release):** `v*` (örn. `v1.0.0`) etiketi push edildiğinde `.github/workflows/release.yml` devreye girer:
  1. Testleri çalıştırır.
  2. Self-contained `win-x64` paketini derler.
  3. Inno Setup ile `Lyrow-1.0.0-Setup-x64.exe` oluşturur.
  4. `Lyrow-win-x64-portable.zip` oluşturur.
  5. GitHub Releases üzerinde otomatik sürüm notlarıyla birlikte indirme varlıklarını yayımlar.

---

## 📄 Lisans

Bu proje **MIT Lisansı** altında lisanslanmıştır. Detaylar için [LICENSE](LICENSE) dosyasına göz atabilirsiniz.
