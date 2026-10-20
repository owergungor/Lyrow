# Lyrow v2.3 — Apple Music Motion, Self-Update & Multi-Language Polish

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml)
[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011%20Native-0078D6?logo=windows)](https://microsoft.com/windows)
[![.NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: WinUI 3 + Windows App SDK](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-0078D4?logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Design: Windows 11 Fluent + Apple Music](https://img.shields.io/badge/Design-Windows%2011%20Mica%20%2B%20Apple-black)](docs/RELEASE_NOTES_v2.3.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Lyrow**, Windows 11 üzerinde müzik dinleme deneyimini görsel bir şölene dönüştüren, **WinUI 3 ve Windows App SDK** mimarisiyle tasarlanmış, **Lumn benzeri 4 kenar ortam aydınlatması (ambient edge lighting)** ve **Apple Music benzeri 60 FPS akıcı dikey merkezlenen şarkı sözü deneyimi** sunan yerel Windows masaüstü müzik arkadaşıdır.

Lyrow iki bağımsız ancak birbiriyle tam senkron çalışan ana modülden oluşur:

- **Glow:** macOS'taki Lumn uygulamasından ilham alan, ekranın dört kenarında (Top, Bottom, Left, Right) sistem ses çıkışının (WASAPI Loopback) ritmine, baslarına, mid/treble bantlarına ve dinamik vuruşlarına (Smoothing → Attack → Hold → Decay) göre bağımsız renk enerjisi yayan, albüm kapağının dominant/vibrant RGBA piksellerinden çıkarılan paletle yumuşak cross-fade geçişler yapan ve diğer uygulamaların kullanımını **kesinlikle engellemeyen (click-through, zero-interference)** ortam aydınlatması.
- **Lyrics:** Sistemde çalan şarkıyı (Spotify, Apple Music, YouTube vb.) Windows GSMTC üzerinden yakalayan, LRCLIB ile senkronize şarkı sözlerini milisaniye hassasiyetinde çeken, **aktif satırı daima dikey olarak merkezleyen**, 60 FPS enterpolasyonlu kaydırma ve hiyerarşik tipografi/opaklık geçişleriyle Apple Music akıcılığı sunan şarkı sözü penceresi.

Ayrıntılı sürüm notları için [Lyrow v2.3 Sürüm Notları](docs/RELEASE_NOTES_v2.3.md)'na göz atın.

---

## ✨ v2.3 Öne Çıkan Yenilikler

- **🎵 Apple Music Tarzı Canlı Şarkı Sözü Animasyonu (Motion Pipeline):** Aktif ve inaktif satırlar arasında 60 FPS akıcı ölçek (scale), opaklık (opacity) ve dikey konum enterpolasyonu. Aktif satır daima dikey merkezdedir (hardcoded auto-center).
- **🖱️ Tıklanan Satıra Anında Atlama (Click-to-Seek):** Şarkı sözleri penceresindeki herhangi bir satıra tıklandığında müzik çalarda tam o zamana seek yapılır ve dikey merkezleme pürüzsüzce devam eder.
- **🔊 Şarkı Sözü Penceresinde Dahili Ses Seviyesi:** Lyrics penceresi alt araç çubuğuna entegre edilen oturum ses seviyesi slider'ı ile ana pencereye geçmeden ses ayarlanabilir.
- **🔄 Gerçek Zamanlı Çift Yönlü Sistem Ses Senkronizasyonu:** Windows ses seviyesi klavye veya harici yoldan değiştiğinde Lyrow arayüzü anında güncellenir; Lyrow'dan değiştirildiğinde Windows donanım çıkışı anında yanıt verir.
- **📦 Dahili Kendi Kendine Güncelleme (In-App Self Update):** Tarayıcı açılmadan GitHub Releases üzerinden yeni sürüm indirilir, doğrulanır ve kullanıcı onayıyla doğrudan uygulanıp uygulama yeniden başlatılır.
- **🛡️ Katı Yüzeyli Mac Tarzı Yüzen Bildirimler (Opaque Toasts):** Bildirim kartları artık masaüstü geçirgenliği olmadan koyu ve açık temayla uyumlu tam opak ve gölgeli yüzeyde gösterilir.
- **⏸️ Akıcı Glow Pause Sönmesi & Resume Canlanması:** Müzik duraklatıldığında donup kalmak yerine yumuşakça kararır, devam ettiğinde pürüzsüzce geri gelir.
- **🌍 5 Dilli Yerelleştirme (Localization):** English, Français, Español, Deutsch ve Türkçe dil desteği.
- **⚙️ Kategorize Edilmiş Düzenli Ayarlar (Reorganized Settings):** General, Playback, Lyrics, Glow, Updates ve Shortcuts olarak ayrılmış ferah bilgi mimarisi.

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
1. [GitHub Releases](https://github.com/owergungor/Lyrow/releases) sayfasından en son `Lyrow-2.3-Setup-x64.exe` dosyasını indirin.
2. Kurulum sihirbazını çalıştırın. İsteğe bağlı olarak masaüstü kısayolu oluşturun.
3. Kurulum tamamlandığında Lyrow otomatik olarak başlayacaktır.
4. *Not:* Kurulum ve yükseltmelerde kullanıcı ayarlarınız (`%APPDATA%\Lyrow`) silinmez, korunur.

### Yöntem 2: Taşınabilir (Portable) ZIP
1. `Lyrow-v2.3-win-x64-portable.zip` arşivini indirin.
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
