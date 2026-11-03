# Lyrow v3.5 — Fluid Lyrics, High-Res Artwork & Advanced Beat Reactivity

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/release.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/release.yml)
[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011%20Native-08a1f7?logo=windows)](https://microsoft.com/windows)
[![.NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: WinUI 3 + Windows App SDK](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-03c1f4?logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Design: Windows 11 Fluent + Apple Music](https://img.shields.io/badge/Design-Windows%2011%20Mica%20%2B%20Apple-black)](docs/RELEASE_NOTES_v3.5.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-750014.svg)](LICENSE)

**Lyrow**, Windows 11 üzerinde müzik dinleme deneyimini görsel bir şölene dönüştüren, **WinUI 3 ve Windows App SDK** mimarisiyle tasarlanmış, **4 kenar ortam aydınlatması (ambient edge lighting)** ve **Apple Music benzeri 60 FPS akıcı dikey merkezlenen karaoke şarkı sözü deneyimi** sunan yerel Windows masaüstü müzik arkadaşıdır.

Lyrow iki bağımsız ancak birbiriyle tam senkron çalışan ana modülden oluşur:

- **Glow:** Ekranın dört kenarında (Top, Bottom, Left, Right) sistem ses çıkışının (WASAPI Loopback) ritmine, 5 frekans bandına (Sub/Bass, Low-Mid, Mid, High-Mid, Treble), gerçek zamanlı Spectral Flux onset ve tempo/beat-phase algılamasına göre bağımsız renk enerjisi yayan, albüm kapağının dominant/vibrant RGBA piksellerinden çıkarılan paletle yumuşak cross-fade geçişler yapan ve diğer uygulamaların kullanımını **kesinlikle engellemeyen (click-through, zero-interference)** ortam aydınlatması.
- **Lyrics:** Sistemde çalan şarkıyı (Spotify, Apple Music, YouTube vb.) Windows GSMTC üzerinden yakalayan, LRCLIB ile senkronize şarkı sözlerini milisaniye hassasiyetinde çeken, **aktif satırı daima dikey olarak merkezleyen**, çok satırlı bölümlerde satırları **sırayla beyazlatan** ve soldan sağa akıcı **karakter/kelime dolum animasyonuna (karaoke fill)** sahip 60 FPS duyarlı (responsive) şarkı sözü penceresi.

Ayrıntılı sürüm notları için [Lyrow v3.5 Sürüm Notları](docs/RELEASE_NOTES_v3.5.md)'na göz atın.

---

## ✨ v3.5 Öne Çıkan Yenilikler

- **📐 Yatay Taşma ve Kesilmeyen Akıcı Lyrics:** Uzun ve çok kelimeli şarkı sözleri pencere sınırlarına göre akıllıca sarılır (`TextWrapping`), aktif satır her boyutta ve tam ekranda tam görünür kalır.
- **🖼️ Yüksek Çözünürlüklü Albüm Kapağı & Animasyon Desteği:** En yüksek çözünürlüklü kapak kaynağı korunur, decode kalitesi optimize edilir ve animasyonlu (animated GIF/WebP) kapaklar donanım hızlandırmalı akıcı oynatılır.
- **💡 24px Varsayılan Edge Bloom:** Glow parlama kalınlığı ilk kurulumda ve ayarlarda dengeli ve estetik 24px değerine ayarlandı.
- **🎯 Kesin Zamanlı Progress Bar Seek:** İlerleme çubuğunda tıklanan veya sürüklenen tam saniyeye deterministik atlama ve anında söz senkronizasyonu.
- **🔄 Otomatik Şarkı Geçişlerinde (`A → B → C → D → E`) Kesintisiz Söz ve Renk Yenilemesi:** Şarkı otomatik değiştiğinde eski şarkının tüm verileri atomik temizlenir, yeni sözler aranır ve arka plan/Glow renkleri yeni kapağa pürüzsüz cross-fade yapar.
- **🥁 Gelişmiş Gerçek Zamanlı Beat, Tempo ve Transient Algılama:** Spectral Flux, 5 bantlı enerji ayrımı ve otokorelasyon tempo takibi ile gerçek müzik ritmine yüksek doğrulukla uyum.

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
1. [GitHub Releases](https://github.com/owergungor/Lyrow/releases) sayfasından en son `Lyrow-3.5-Setup-x64.exe` dosyasını indirin.
2. Kurulum sihirbazını çalıştırın. İsteğe bağlı olarak masaüstü kısayolu oluşturun.
3. Kurulum tamamlandığında Lyrow otomatik olarak başlayacaktır.
4. *Not:* Kurulum ve yükseltmelerde kullanıcı ayarlarınız (`%APPDATA%\Lyrow`) silinmez, korunur.

### Yöntem 2: Taşınabilir (Portable) ZIP
1. `Lyrow-v3.5-win-x64-portable.zip` arşivini indirin.
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
