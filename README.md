# Lyrow v1.3 — Windows 11 Native + Lumn-Style Ambient Experience

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml)
[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011%20Native-0078D6?logo=windows)](https://microsoft.com/windows)
[![.NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: WinUI 3 + Windows App SDK](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK-0078D4?logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Design: Windows 11 Fluent + Apple Music](https://img.shields.io/badge/Design-Windows%2011%20Mica%20%2B%20Apple-black)](docs/WINUI_MIGRATION.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Lyrow**, Windows 11 üzerinde müzik dinleme deneyimini görsel bir şölene dönüştüren, **WinUI 3 ve Windows App SDK** ile baştan sona yeniden yazılmış, **Lumn benzeri ortam kenar aydınlatması (ambient edge lighting)** ve **Apple Music benzeri akıcı dikey merkezlenen şarkı sözü deneyimi** sunan yeni nesil yerel Windows masaüstü müzik arkadaşıdır.

Lyrow iki bağımsız ancak birbiriyle tam senkron çalışan ana modülden oluşur:

- **Glow:** macOS'taki Lumn uygulamasından ilham alan, ekranın dört kenarında (üst, alt, sol, sağ) sistem ses çıkışının (WASAPI Loopback) ritmine, baslarına ve dinamik geçişlerine (transient beat) göre yumuşak renk enerjisi yayan, albüm kapağının dominant/vibrant renklerine yumuşak cross-fade geçişlerle bürünen ve diğer uygulamaların kullanımını **kesinlikle engellemeyen (click-through, zero-interference)** ortam aydınlatması.
- **Lyrics:** Sistemde çalan şarkıyı (Spotify, Apple Music, YouTube vb.) Windows GSMTC üzerinden yakalayan, LRCLIB ile senkronize şarkı sözlerini milisaniye hassasiyetinde çeken, **aktif satırı daima dikey olarak merkezleyen**, donanım hızlandırmalı yumuşak kaydırma ve şeffaflık geçişleriyle Apple Music akıcılığı sunan şarkı sözü penceresi.

Ayrıntılı mimari ve Avalonia'dan WinUI 3'e geçiş detayları için [WinUI Migration Raporu](docs/WINUI_MIGRATION.md)'na göz atın.

---

## ✨ v1.3 Öne Çıkan Yenilikler

- **🪟 Gerçek Windows 11 Native WinUI 3:** Avalonia UI tamamen kaldırıldı. Gerçek Windows 11 tasarım dili: **Mica backdrop**, modern başlık çubuğu (Custom TitleBar), `NavigationView`, yerel `ToggleSwitch`, `Slider`, `ComboBox`, `InfoBar` ve akıcı tipografi.
- **🛡️ Sıfır Etkileşim Engeli (Zero-Interference Click-Through Overlay):** Glow penceresi diğer uygulamaların (Chrome, VS Code, oyunlar, Explorer) üzerine fiziksel bir pencere gibi oturmaz. `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW` ve `WM_NCHITTEST -> HTTRANSPARENT` mimarisiyle tüm fare tıklamaları ve klavye girdileri arkadaki pencerelere doğrudan geçer. Alt+Tab ve Görev Çubuğu davranışları asla bozulmaz. Glow kapatıldığında overlay tamamen kapanır.
- **🌈 Lumn Tarzı 4 Kenar Ambient Kenar Aydınlatması:** Düz çizgiler yerine bloom, soft gradient ve müzikal enerji akışı. Bas ve ritim vurduğunda yumuşak bir parlama (pulse); müzik durduğunda ani kesilme olmaksızın pürüzsüz sakinleşme (decay).
- **🎨 Albüm Kapağı → Glow Renk Entegrasyonu:** Albüm kapağındaki dominant ve canlı vurgu renkleri asenkron olarak çözülür. Şarkı değiştiğinde renkler aniden değişmek yerine ~800ms boyunca yumuşak cross-fade (`GlowPalette.Lerp`) ile geçiş yapar.
- **🎵 Apple Music Hissiyatında Şarkı Sözü Akışı:** Aktif satır **her zaman görsel olarak dikey merkezde** kalır. `ScrollViewer.ChangeView` donanım hızlandırmalı pürüzsüz animasyonla kayar. Aktif satır %100 opaklık ve belirgin tipografiyle öne çıkarken inaktif satırlar %35 opaklıkta sakin durur. İleri/geri sarma (seek) anında yeni aktif satırı merkeze alır; duraklatma/sürdürmede konumunu korur.
- **🔊 0% — 100% Yerel Ses Düzeyi:** Teknik dBFS göstergesi UI'dan tamamen kaldırıldı. Kullanıcıya Windows yerel Slider'ı ve yüzde göstergesiyle doğrudan 0%–100% lineer ses seviyesi sunulur.
- **⏱️ Deterministik Başlangıç Durumu:** Uygulama ilk açıldığında Glow = OFF ve Lyrics = OFF olarak başlar. UI anahtarları gerçek modül durumunu eksiksiz yansıtır; yarış durumu (race condition) veya yanlış aktif göstergesi engellenmiştir.
- **🖥️ Akıllı Çoklu Monitör & DPI Desteği:** Sistemdeki tüm monitörler (`EnumDisplayMonitors`) algılanır. Ekran çözünürlüğü, ölçekleme veya monitör ekleme/çıkarma durumunda overlay'ler otomatik olarak yeniden hesaplanır. İstenen ekranlar ayarlar üzerinden seçilebilir.
- **⌨️ Sıfır Varsayılanlı Güvenli Kısayollar:** İlk kurulumda hiçbir küresel kısayol işgal edilmez. Kullanıcı istediği tuşları kendisi tanımlar; Win32 1408 iş parçacığı çakışma koruması devrededir.

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
1. [GitHub Releases](https://github.com/owergungor/Lyrow/releases) sayfasından en son `Lyrow-1.3-Setup-x64.exe` dosyasını indirin.
2. Kurulum sihirbazını çalıştırın. İsteğe bağlı olarak masaüstü kısayolu oluşturun.
3. Kurulum tamamlandığında Lyrow otomatik olarak başlayacaktır.
4. *Not:* Kurulum ve yükseltmelerde kullanıcı ayarlarınız (`%APPDATA%\Lyrow`) silinmez, korunur.

### Yöntem 2: Taşınabilir (Portable) ZIP
1. `Lyrow-v1.3-win-x64-portable.zip` arşivini indirin.
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

# Tüm test paketini çalıştırın (157 test)
dotnet test Lyrow.sln

# Bağımsız win-x64 paketi üretin
dotnet publish src/Lyrow.App/Lyrow.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

---

## 📄 Lisans

Bu proje [MIT Lisansı](LICENSE) kapsamında lisanslanmıştır.
