# Lyrow v2.0 — Windows 11 Native + Lumn-Style Ambient Experience

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml)
[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011%20Native-0078D6?logo=windows)](https://microsoft.com/windows)
[![.NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: WinUI 3 + Windows App SDK](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-0078D4?logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Design: Windows 11 Fluent + Apple Music](https://img.shields.io/badge/Design-Windows%2011%20Mica%20%2B%20Apple-black)](docs/RELEASE_NOTES_v2.0.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Lyrow**, Windows 11 üzerinde müzik dinleme deneyimini görsel bir şölene dönüştüren, **WinUI 3 ve Windows App SDK** mimarisiyle tasarlanmış, **Lumn benzeri 4 kenar ortam aydınlatması (ambient edge lighting)** ve **Apple Music benzeri 60 FPS akıcı dikey merkezlenen şarkı sözü deneyimi** sunan yerel Windows masaüstü müzik arkadaşıdır.

Lyrow iki bağımsız ancak birbiriyle tam senkron çalışan ana modülden oluşur:

- **Glow:** macOS'taki Lumn uygulamasından ilham alan, ekranın dört kenarında (Top, Bottom, Left, Right) sistem ses çıkışının (WASAPI Loopback) ritmine, baslarına, mid/treble bantlarına ve dinamik vuruşlarına (Smoothing → Attack → Hold → Decay) göre bağımsız renk enerjisi yayan, albüm kapağının dominant/vibrant RGBA piksellerinden çıkarılan paletle yumuşak cross-fade geçişler yapan ve diğer uygulamaların kullanımını **kesinlikle engellemeyen (click-through, zero-interference)** ortam aydınlatması.
- **Lyrics:** Sistemde çalan şarkıyı (Spotify, Apple Music, YouTube vb.) Windows GSMTC üzerinden yakalayan, LRCLIB ile senkronize şarkı sözlerini milisaniye hassasiyetinde çeken, **aktif satırı daima dikey olarak merkezleyen**, 60 FPS enterpolasyonlu kaydırma ve hiyerarşik tipografi/opaklık geçişleriyle Apple Music akıcılığı sunan şarkı sözü penceresi.

Ayrıntılı sürüm notları için [Lyrow v2.0 Sürüm Notları](docs/RELEASE_NOTES_v2.0.md)'na göz atın.

---

## ✨ v2.0 Öne Çıkan Yenilikler

- **🌈 Lumn Tarzı 4 Kenar Ambient Glow Motoru (Major Overhaul):** Ayrılmış (dedicated) Win32 STA render thread'i ve 60 FPS `UpdateLayeredWindow` mimarisiyle ekranın 4 kenarında (Top, Bottom, Left, Right) bağımsız enerji hesaplaması. Keskin ON/OFF yerine `Smoothing → Attack → Hold → Decay → Rendered Intensity` zarfıyla sürekli ve akıcı ambiyans ışığı.
- **🎨 Gerçek RGBA Albüm Kapağı → Vibrant Glow Paleti:** WIC `BitmapDecoder` ile albüm kapaklarının gerçek pikselleri örneklenir, HSV doygunluk/parlaklık güçlendirmesi (`BoostVibrancy`) ile mor, mavi, kırmızı, turuncu, yeşil ve pembe gibi canlı renkler çıkarılır ve şarkı değişiminde ~800ms `GlowPalette.Lerp` ile yumuşak cross-fade yapılır.
- **🎵 Apple Music Tarzı 60 FPS Şarkı Sözü & Tam Dikey Merkezleme:** Aktif satır **her zaman görsel olarak tam dikey merkezde** (`viewportHeight / 2`) yer alır. Aktif satır güçlü tipografi ve %100 opaklıkla parlar; yakın satırlar (%46) ve uzak satırlar (%22) kademeli ölçek/opaklık hiyerarşisiyle 60 FPS pürüzsüz geçiş yapar. Seek (ileri/geri sarma) durumunda binary search ile anında doğru satır bulunur ve merkeze yumuşakça kayar.
- **⏯️ Gerçek GSMTC Medya Kontrolleri:** Play, Pause, Toggle Play/Pause, Previous ve Next butonları doğrudan aktif Windows medya oturumuna (Spotify, Apple Music, Tarayıcı vb.) komut gönderir. Parça değiştiğinde metadata, albüm kapağı, şarkı sözleri, zaman çizelgesi ve Glow renk paleti anında senkronize olur.
- **🔊 Gerçek Windows Ses Seviyesi Kontrolü (0% — 100%):** dBFS göstergesi tamamen kaldırıldı. Slider doğrudan aktif medya oturumunun (`ISimpleAudioVolume`) ve Windows ana ses çıkışının (`IAudioEndpointVolume`) gerçek ses seviyesini `0%–100%` aralığında kontrol eder; sessize alma (mute) ve oynatıcı değişimlerini otomatik algılar.
- **🔄 Güvenli Otomatik Güncelleme (Auto Update):** Settings ekranında GitHub Releases üzerinden asenkron `v2.0` sürüm kontrolü, kullanıcı onayı olmadan dosya değiştirmeyen güvenli mimari ve çevrimdışı hata toleransı.
- **🖱️ Kusursuz Kaydırma (Scroll) & Kompakt Kenar Çubuğu:** Fare tekerleği (mouse wheel), touchpad iki parmak kaydırma, `PageUp`/`PageDown`/`Home`/`End` klavye navigasyonu ve daraltılmış modern Windows 11 sol menü (`188px`) ile ferah çalışma alanı.
- **🖥️ PerMonitorV2 Keskin DPI Desteği:** `%100`, `%125`, `%150`, `%175` ve `%200` ölçeklemelerde ve çoklu monitör kurulumlarında bulanıklığı önleyen tam `PerMonitorV2` DPI farkındalığı.

---

## 💻 Sistem Gereksinimleri

| Bileşen | Minimum | Önerilen |
| :--- | :--- | :--- |
| **İşletim Sistemi** | Windows 10 (Build 19041+) 64-bit | **Windows 11 (22H2 / 24H2 64-bit)** |
| **İşlemci (CPU)** | Intel Core i3 / AMD Ryzen 3 | Intel Core i5 / AMD Ryzen 5 veya üstü |
| **Bellek (RAM)** | 4 GB | 8 GB+ |
| **Ekran Kartı (GPU)** | DirectX 11 / DWM uyumlu grafikler | Donanım hızlandırmalı GPU |
| **Ses Aygıtı** | WASAPI uyumlu stereo çıkış | Aktif ses kartı / kulaklık |
| **Çalışma Zamanı** | Bağımsız pakette (.NET gerekmez) | .NET 10 LTS SDK (yalnızca kaynak kod derlemesi için) |

---

## 🚀 Kurulum

### Yöntem 1: Windows Kurulum Paketi (Önerilen)
1. [GitHub Releases](https://github.com/owergungor/Lyrow/releases) sayfasından en son `Lyrow-2.0-Setup-x64.exe` dosyasını indirin.
2. Kurulum sihirbazını çalıştırın. İsteğe bağlı olarak masaüstü kısayolu oluşturun.
3. Kurulum tamamlandığında Lyrow otomatik olarak başlayacaktır.
4. *Not:* Kurulum ve yükseltmelerde kullanıcı ayarlarınız (`%APPDATA%\Lyrow`) silinmez, korunur.

### Yöntem 2: Taşınabilir (Portable) ZIP
1. `Lyrow-v2.0-win-x64-portable.zip` arşivini indirin.
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

# Tüm test paketini çalıştırın
dotnet test Lyrow.sln

# Bağımsız win-x64 paketi üretin
dotnet publish src/Lyrow.App/Lyrow.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

---

## 📄 Lisans

Bu proje [MIT Lisansı](LICENSE) kapsamında lisanslanmıştır.
