# Lyrow v1.1

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?logo=windows)](https://microsoft.com/windows)
[![.NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: Avalonia 12](https://img.shields.io/badge/UI-Avalonia%2012-8A2BE2)](https://avaloniaui.net/)
[![Design: Vercel + Apple](https://img.shields.io/badge/Design-Vercel%20%2B%20Apple-black)](https://21st.dev/@serafimcloud/themes/vercel)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Lyrow**, masaüstünüzde müzik dinleme deneyimini görsel bir şölene dönüştüren, yüksek performanslı, Vercel ve Apple tasarım estetiğiyle yenilenmiş modern bir Windows multimedya platformudur.

Lyrow iki bağımsız ancak birbiriyle uyum içinde çalışan ana modülden oluşur:

- **Glow:** Sistem ses çıkışını (WASAPI Loopback) anlık yakalayan, sıfır-tahsisli FFT frekans analiziyle donanım hızlandırmalı ritim ve spektrum dalgaları oluşturan dinamik ses görselleştiricisi.
- **Lyrics:** Sistemde çalan müziği (Spotify, Apple Music, YouTube vb.) Windows GSMTC üzerinden yakalayan, LRCLIB ile senkronize şarkı sözlerini milisaniye hassasiyetinde çeken ve pürüzsüz kaydırma ile masaüstünüzde sergileyen yüzen şarkı sözü penceresi.

---

## 📸 Temel Özellikler

- **🎨 Vercel & Apple İlhamlı Modern Tasarım:** Temiz koyu ve açık yüzeyler, 1px zarif sınırlar, akıcı Apple tarzı anahtarlar (ToggleSwitch) ve yüksek okunabilirlikli tipografi.
- **⚡ Kusursuz Şarkı Sözü Senkronizasyonu:** Gerçek zamanlı interpolasyon ve ikili arama (Binary Search) ile anında doğru satır vurgusu ve otomatik merkezleme kaydırması.
- **🖱️ Gerçek Win32 Click-Through:** Tek bir anahtarla veya global kısayolla (`WS_EX_LAYERED` & `WS_EX_TRANSPARENT`) overlay pencereleri saydamlaştırılır ve tüm fare tıklamaları arka plandaki oyunlara ve uygulamalara pürüzsüzce geçer.
- **🔔 Windows System Tray (Sistem Tepsisi):** Bildirim alanı simgesi üzerinden Lyrow'u tek tıkla arka plana alabilir, 'X' butonu ile tepsiye küçültebilir ve sağ tık menüsüyle modülleri yönetebilirsiniz.
- **⌨️ Güvenilir Global Kısayollar (Hotkeys):** Dedicated mesaj döngüsü iş parçacığı ile Win32 çakışmalarından arındırılmış global kısayollar (`Ctrl+Shift+L` : Glow, `Ctrl+Shift+V` : Lyrics, `Ctrl+Shift+C` : Click-through).
- **🚀 Tekil Örnek (Single-Instance) Güvencesi:** Uygulama tekrar çalıştırıldığında yeni bir kopya açılmaz; mevcut pencere anında öne getirilir.
- **🖥️ Akıllı Çoklu Monitör & Dinamik Ekran Değişikliği:** Sistemdeki tüm ekranlar algılanır (`Screens.All`). Monitör takılıp çıkarıldığında veya DPI değiştiğinde overlay'ler otomatik olarak yeniden konumlandırılır.
- **🛡️ Akıllı LRCLIB Rate-Limit Yönetimi:** HTTP 429 durumlarında `Retry-After` süresi UI'da canlı geri sayımla gösterilir; çevrimdışı yerel önbellek önceliklendirilir.
- **💾 Yerel Tanı Günlüğü (Zero-Telemetry):** `%APPDATA%\Lyrow\logs\app.log` altında güvenli yerel hata günlüğü tutulur; hiçbir telemetri verisi dışarıya iletilmez.

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
1. [GitHub Releases](https://github.com/owergungor/Lyrow/releases) sayfasından en son `Lyrow-1.1-Setup-x64.exe` dosyasını indirin.
2. Kurulum sihirbazını çalıştırın. İsteğe bağlı olarak masaüstü kısayolu oluşturun.
3. Kurulum tamamlandığında Lyrow otomatik olarak başlayacaktır.
4. *Not:* Kurulum ve yükseltmelerde kullanıcı ayarlarınız (`%APPDATA%\Lyrow`) silinmez, korunur.

### Yöntem 2: Taşınabilir (Portable) ZIP
1. `Lyrow-win-x64-portable.zip` arşivini indirin.
2. Dilediğiniz bir klasöre çıkartın.
3. `Lyrow.App.exe` dosyasını doğrudan çalıştırın.

---

## 🛠️ Kaynak Koddan Derleme

```bash
# Depoyu klonlayın
git clone https://github.com/owergungor/Lyrow.git
cd Lyrow

# Bağımlılıkları geri yükleyin ve derleyin
dotnet restore Lyrow.sln
dotnet build Lyrow.sln -c Release

# Tüm test paketini çalıştırın (108 test)
dotnet test Lyrow.sln

# Bağımsız win-x64 paketi üretin
dotnet publish src/Lyrow.App/Lyrow.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

---

## 📄 Lisans

Bu proje [MIT Lisansı](LICENSE) kapsamında lisanslanmıştır.
