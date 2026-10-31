# Lyrow v3.4 — Responsive Player, Lyrics Sync & Advanced Beat Reactivity

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/release.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/release.yml)
[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011%20Native-0078D6?logo=windows)](https://microsoft.com/windows)
[![.NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: WinUI 3 + Windows App SDK](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-0078D4?logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Design: Windows 11 Fluent + Apple Music](https://img.shields.io/badge/Design-Windows%2011%20Mica%20%2B%20Apple-black)](docs/RELEASE_NOTES_v3.4.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Lyrow**, Windows 11 üzerinde müzik dinleme deneyimini görsel bir şölene dönüştüren, **WinUI 3 ve Windows App SDK** mimarisiyle tasarlanmış, **4 kenar ortam aydınlatması (ambient edge lighting)** ve **Apple Music benzeri 60 FPS akıcı dikey merkezlenen karaoke şarkı sözü deneyimi** sunan yerel Windows masaüstü müzik arkadaşıdır.

Lyrow iki bağımsız ancak birbiriyle tam senkron çalışan ana modülden oluşur:

- **Glow:** Ekranın dört kenarında (Top, Bottom, Left, Right) sistem ses çıkışının (WASAPI Loopback) ritmine, 5 frekans bandına (Sub/Bass, Low-Mid, Mid, High-Mid, Treble), gerçek zamanlı Spectral Flux onset ve tempo/beat-phase algılamasına göre bağımsız renk enerjisi yayan, albüm kapağının dominant/vibrant RGBA piksellerinden çıkarılan paletle yumuşak cross-fade geçişler yapan ve diğer uygulamaların kullanımını **kesinlikle engellemeyen (click-through, zero-interference)** ortam aydınlatması.
- **Lyrics:** Sistemde çalan şarkıyı (Spotify, Apple Music, YouTube vb.) Windows GSMTC üzerinden yakalayan, LRCLIB ile senkronize şarkı sözlerini milisaniye hassasiyetinde çeken, **aktif satırı daima dikey olarak merkezleyen**, çok satırlı bölümlerde satırları **sırayla beyazlatan** ve soldan sağa akıcı **karakter/kelime dolum animasyonuna (karaoke fill)** sahip 60 FPS duyarlı (responsive) şarkı sözü penceresi.

Ayrıntılı sürüm notları için [Lyrow v3.4 Sürüm Notları](docs/RELEASE_NOTES_v3.4.md)'na göz atın.

---

## ✨ v3.4 Öne Çıkan Yenilikler

- **🚀 Güncelleme Sonrası Otomatik Yeniden Başlatma:** İndirme ve kurulum tamamlandığında paket bütünlüğü doğrulanır, single-instance kilidi temizlenir ve yeni `Lyrow.App.exe` otomatik olarak başlatılır.
- **📐 Oranlı ve Hizalı Responsive Pencere Ölçekleme:** Pencere büyütüldüğünde veya küçültüldüğünde albüm kapağı (1:1), başlıklar, progress bar, ses çubuğu, kontrol butonları ve şarkı sözü fontları birbiriyle tam hizalı ve oranlı şekilde büyür/küçülür.
- **🎛️ Yenilenen Dar Mod (Compact Mode) ve Genişletilmiş Dikey Boyut:** Dar modun dikey yüksekliği ferahlatıldı; alt sağdaki ses butonu yerine doğrudan yatay 50/50 görünüme geçiş sağlayan **Normal Mode** butonu eklendi ve pencere genişletildiğinde otomatik normal moda dönüş (histerezis) sağlandı.
- **🎯 Progress Bar Üzerinden Tıklama ve Sürükleme ile Seek:** İlerleme çubuğu üzerinde istediğiniz noktaya tıklayarak veya sürükleyerek şarkıyı o saniyeye sarabilir, sözlerin ve vurguların anında yeni konuma kilitlenmesini sağlayabilirsiniz.
- **📝 Çok Satırlı Şarkı Sözlerinde Sıralı (Sequential) Beyazlaşma:** Çok satırlı söz bloklarında tüm satırların aynı anda beyazlaşması yerine önce 1. satır, ardından 2. satır ve 3. satır sırayla vurgulanır.
- **🔄 Otomatik Şarkı Geçişlerinde (`A → B → C → D`) Kesintisiz Söz, Arkaplan ve Glow Renk Yenilemesi:** Gecikmeli GSMTC metadata veya kapak görseli bildirimlerinde dahi her yeni şarkının sözleri eksiksiz yüklenir; hem Lyrics arkaplanı hem de 4 kenar Glow renkleri yeni albüm kapağına yumuşakça geçiş yapar.
- **🥁 Gelişmiş Gerçek Zamanlı Beat, Tempo ve Transient Algılama:** Spectral Flux, 5 bantlı enerji ayrımı ve kısa süreli otokorelasyon tabanlı tempo/faz takibi ile gerçek davul/bas vuruşlarına ve hi-hat/snare transient'lerine yüksek doğrulukla tepki verir.

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
1. [GitHub Releases](https://github.com/owergungor/Lyrow/releases) sayfasından en son `Lyrow-3.4-Setup-x64.exe` dosyasını indirin.
2. Kurulum sihirbazını çalıştırın. İsteğe bağlı olarak masaüstü kısayolu oluşturun.
3. Kurulum tamamlandığında Lyrow otomatik olarak başlayacaktır.
4. *Not:* Kurulum ve yükseltmelerde kullanıcı ayarlarınız (`%APPDATA%\Lyrow`) silinmez, korunur.

### Yöntem 2: Taşınabilir (Portable) ZIP
1. `Lyrow-v3.4-win-x64-portable.zip` arşivini indirin.
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
