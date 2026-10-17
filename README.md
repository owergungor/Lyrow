# Lyrow v1.2

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?logo=windows)](https://microsoft.com/windows)
[![.NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: Avalonia 12](https://img.shields.io/badge/UI-Avalonia%2012-8A2BE2)](https://avaloniaui.net/)
[![Design: Vercel + Apple](https://img.shields.io/badge/Design-Vercel%20%2B%20Apple-black)](https://21st.dev/@serafimcloud/themes/vercel)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Lyrow**, masaüstünüzde müzik dinleme deneyimini görsel bir şölene dönüştüren, yüksek performanslı, Vercel ve Apple tasarım estetiğiyle yenilenmiş modern bir Windows multimedya platformudur.

Lyrow iki bağımsız ancak birbiriyle uyum içinde çalışan ana modülden oluşur:

- **Glow:** Sistem ses çıkışını (WASAPI Loopback) anlık yakalayan, sıfır-tahsisli FFT ve geçiş/ritim (transient beat) algılama motoruyla ekranın dört kenarında ortama yayılan ambient kenar aydınlatması sunan dinamik görselleştirici.
- **Lyrics:** Sistemde çalan müziği (Spotify, Apple Music, YouTube vb.) Windows GSMTC üzerinden yakalayan, albüm kapağını (artwork) asenkron çözen, LRCLIB ile senkronize şarkı sözlerini milisaniye hassasiyetinde çeken ve çalan satırı ekranın dikey merkezinde tutan modern şarkı sözü penceresi.

---

## 📸 v1.2 Temel Özellikleri

- **🎨 Vercel & Apple İlhamlı Modern Tasarım:** Temiz koyu ve açık yüzeyler, 1px zarif sınırlar, akıcı Apple tarzı anahtarlar (ToggleSwitch) ve yüksek okunabilirlikli tipografi.
- **⚡ Merkezlenmiş Şarkı Sözü Akışı (Lyrics Centering):** Çalan satır her zaman viewport'un görsel merkezinde konumlanır; ileri/geri aramalarda (seek) anında yumuşak merkezleme yapılır; duraklatma/sürdürmede konum bozulmaz.
- **🖼️ Asenkron Albüm Kapağı Hattı (Artwork Pipeline):** Windows GSMTC üzerinden albüm kapakları UI thread'i bloklamadan okunur, bellek içi önbelleğe (cache) alınır ve yüksek çözünürlüklü zarif yuvarlatılmış kartlarda gösterilir.
- **✨ Ambient Glow — 4 Kenarlı Ekran Kenar Aydınlatması:** Ekran sınırlarında bas, orta ve tiz frekans enerjisine ve ritim transient'larına tepki veren, titreşimsiz, GPU hızlandırmalı yumuşak kenar ışıması.
- **⌨️ Sıfır Varsayılanlı & Özelleştirilebilir Global Kısayollar:** İlk kurulumda 0 varsayılan kısayol rezerve edilir. Kullanıcı ayarlar kartından kendi tuş kombinasyonlarını belirleyebilir, kaldırabilir; dahili ve Win32 sistem çakışmaları anında tespit edilip engellenir (Win32 1408 thread güvenliği korunmuştur).
- **📐 Sabit Dashboard Kart Geometrisi:** Şarkı adı, sanatçı veya durum metinleri ne kadar uzun olursa olsun kart ölçüleri (`76px`, `104px`, `200px`) deterministik kalır; metinler zarif şekilde kısaltılır (ellipsis + tooltip).
- **🍞 Modern Sağ Üst Toast Sistemi:** Kalıcı alt durum metinleri kaldırıldı; sağ üst köşede kaybolan, anti-spam korumalı, yarı saydam ve hafif gölgeli toast bildirimleri eklendi.
- **🖱️ Gerçek Win32 Click-Through:** Tek bir anahtarla veya kullanıcı kısayoluyla (`WS_EX_LAYERED` & `WS_EX_TRANSPARENT`) overlay pencereleri saydamlaştırılır ve tüm fare tıklamaları arka plandaki oyunlara ve uygulamalara pürüzsüzce geçer.
- **🔔 Windows System Tray (Sistem Tepsisi):** Bildirim alanı simgesi üzerinden Lyrow'u tek tıkla arka plana alabilir, 'X' butonu ile tepsiye küçültebilir ve sağ tık menüsüyle modülleri yönetebilirsiniz.
- **🚀 Tekil Örnek (Single-Instance) Güvencesi:** Uygulama tekrar çalıştırıldığında yeni bir kopya açılmaz; mevcut pencere anında öne getirilir.
- **🖥️ Akıllı Çoklu Monitör & Dinamik Ekran Değişikliği:** Sistemdeki tüm ekranlar algılanır (`Screens.All`). Monitör takılıp çıkarıldığında veya DPI değiştiğinde overlay'ler otomatik olarak yeniden konumlandırılır.

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
1. [GitHub Releases](https://github.com/owergungor/Lyrow/releases) sayfasından en son `Lyrow-1.2-Setup-x64.exe` dosyasını indirin.
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

# Tüm test paketini çalıştırın (140 test)
dotnet test Lyrow.sln

# Bağımsız win-x64 paketi üretin
dotnet publish src/Lyrow.App/Lyrow.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

---

## 📄 Lisans

Bu proje [MIT Lisansı](LICENSE) kapsamında lisanslanmıştır.
