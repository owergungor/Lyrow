# Lyrow v3.7 — Desktop Ambient Experience & Synchronized Lyrics

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/release.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/release.yml)
[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011%20%2F%2010%20(19041%2B)-08a1f7?logo=windows)](https://microsoft.com/windows)
[![.NET 10 LTS](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: WinUI 3 + Windows App SDK](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK%201.8-03c1f4?logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Release: v3.7](https://img.shields.io/badge/Release-v3.7-success)](https://github.com/owergungor/Lyrow/releases/tag/v3.7)
[![Tests: 635 Passing](https://img.shields.io/badge/Tests-635%20Passed-brightgreen)](https://github.com/owergungor/Lyrow/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-750014.svg)](LICENSE)

**Lyrow**, Windows 10 ve 11 için geliştirilmiş, müzik dinleme deneyimini görsel bir atmosfere dönüştüren yerel bir masaüstü uygulamasıdır. **WinUI 3 ve Windows App SDK** mimarisi üzerinde çalışan Lyrow; ekranın dört kenarına yayılan **gerçek zamanlı ortam aydınlatması (Ambient Edge Glow)**, **Apple Music benzeri 60 FPS dikey merkezlenen senkronize şarkı sözleri** ve kompakt **Dar Mod (Mini Player)** sunar.

---

## 📥 İndirme (Download v3.7)

En son kararlı sürümü doğrudan GitHub Releases üzerinden edinebilirsiniz:

- 🔗 **GitHub Release:** [Lyrow v3.7 Sürüm Sayfası](https://github.com/owergungor/Lyrow/releases/tag/v3.7)
- 📦 **Doğrudan İndirme:** [Lyrow-v3.7-win-x64.zip (93.3 MB)](https://github.com/owergungor/Lyrow/releases/download/v3.7/Lyrow-v3.7-win-x64.zip)
- 🔑 **SHA-256 Doğrulama Özeti:** `601664444F7D983B05E3F675D220E8F25EDB7701520D4E923EFCD8F4E0A85B73`

> [!NOTE]
> Lyrow bağımsız (self-contained) bir Windows x64 paketidir. Çalışmak için harici bir .NET Runtime kurulumu gerektirmez. Arşivi bir klasöre çıkartıp `Lyrow.App.exe` dosyasını çalıştırmanız yeterlidir.

---

## 🌟 Lyrow Nedir?

Lyrow iki bağımsız ancak birbiriyle tam senkron çalışan ana modülden oluşur:

1. **Glow (Ortam Aydınlatması):**
   Ekranın 4 kenarında (Üst, Alt, Sol, Sağ) sistem ses çıkışının ritmine, 5 frekans bandına (Sub-Bass, Bass, Low-Mid, Mid, High-Mid/Treble) ve gerçek zamanlı Spectral Flux vuruş algılamasına göre bağımsız renk enerjisi yayar. Albüm kapağının renk paletiyle yumuşak cross-fade geçişleri yapar ve diğer pencerelerin kullanımını **kesinlikle engellemez (click-through, zero-interference)**.

2. **Lyrics (Senkronize Şarkı Sözleri & Mini Oynatıcı):**
   Windows GSMTC üzerinden çalan medyayı otomatik dinler, LRCLIB API aracılığıyla senkronize şarkı sözlerini milisaniye hassasiyetinde çeker, aktif satırı dikey olarak odaklar ve soldan sağa akıcı **karaoke dolum animasyonu (word/character fill)** sunar.

---

## 🚀 Temel Özellikler

- **⏱️ Merkezi Playback Timeline Motoru (`IPlaybackTimelineEngine`):**
  Parça süresi, geçen süre (`0:45`) ve kalan süre (`-2:15`) her parça için bağımsız hesaplanır; önceki şarkıların süreleri kesinlikle birbirine eklenmez.
- **🔄 Kusursuz Otomatik Şarkı Geçişleri (`A → B → C`):**
  Şarkı bittiğinde veya atlandığında zaman çizelgesi, sözler ve albüm kapağı atomik olarak sıfırlanır; eski parçanın GSMTC kalıntıları yeni parçayı etkilemez.
- **📱 İki Farklı Görünüm:**
  - **Normal Mod:** Tam boyutta, dikey merkezlenen akıcı şarkı sözleri, yüksek çözünürlüklü kapak ve Neumorphic kontroller.
  - **Dar Mod (Compact Player):** 352×148 px kompakt boyut, tek satır medya kontrolleri, albüm kapağı, açılır şarkı sözü çekmecesi ve kayan ses kontrolü.
- **🔊 Doğrudan Ses Çıkış Aygıtı Seçimi:**
  Lyrics arayüzünden doğrudan varsayılan Windows ses yürütme cihazını (Kulaklık, Hoparlör, Bluetooth) değiştirme ve gerçek zamanlı glif geri bildirimi.
- **🎚️ Kayan Ses Düzeyi Popup'ı:**
  Kompakt modda ses simgesine tıklandığında açılan, sessize alma (mute) butonlu yatay ses kaydırıcısı.
- **🎨 Açık ve Koyu Neumorphism Teması:**
  Yumuşak kabartmalı/içbükey gölgeler, dokunsal buton basma efektleri ve Windows 11 Fluent tasarım uyumu.
- **🖼️ Yüksek Çözünürlüklü ve Animasyonlu Kapaklar:**
  Kapak görselleri orijinal çözünürlüğünde korunur; animasyonlu GIF ve WebP kapakları donanım hızlandırmalı olarak oynatılır.
- **🎯 Tıklama ve Sürükleme ile İlerleme (Seek):**
  İlerleme çubuğunda tıklanan veya sürüklenen noktaya ya da şarkı sözü satırına tıklandığında anında deterministik atlama.

---

## 🎵 Desteklenen Medya Oynatıcıları

Lyrow, Windows'un yerel **Global System Media Transport Controls (GSMTC)** API'sini dinler. Aşağıdaki oynatıcılar kod tabanında ve testlerle doğrudan doğrulanmıştır:

| Oynatıcı | Tür | Desteklenen Yetenekler |
|---|---|---|
| **Spotify Desktop** | Masaüstü Uygulaması | Şarkı Bilgisi, Kapak, Oynat/Duraklat, Önceki/Sonraki, Seek, Söz Senkronizasyonu |
| **Apple Music Desktop** | Masaüstü Uygulaması (Win11) | Şarkı Bilgisi, Sanatçı/Albüm Ayrıştırma, Kapak, Kontroller |
| **Apple Music Web** | Tarayıcı (Edge, Chrome, Firefox) | Şarkı Bilgisi, Web Kapak Desteği, Seek, Tarayıcı Butonu Yönlendirmesi |
| **YouTube Music Web** | Tarayıcı (Edge, Chrome, Firefox) | Şarkı Bilgisi, Otomatik Parça Geçişi, Kapak, Kontroller |
| **Windows Media Player / Diğerleri** | Masaüstü / UWP / PWA | Windows GSMTC arayüzünü destekleyen tüm modern medya oynatıcıları |

---

## ⏱️ Playback Timeline ve Süre Hesaplaması

Lyrow v3.7 ile baştan yazılan **Playback Timeline Engine**, müzik çalarlardaki zamanlama hatalarını kalıcı olarak çözmüştür:

```
[Parça A (3:30)] ─── 0:00 ───────> 3:30 ─── Otomatik Geçiş ───>
[Parça B (3:11)] ─── 0:00 (Yeni) ─> 1:01 [Kalan: -2:10] (Kümülatif toplam: YOK)
```

- **İzole Geçen Süre (Elapsed Time):** Yalnızca o an çalan parçanın başlangıçtan itibaren geçen süresi hesaplanır (`1:01`). Önceki şarkıların süreleri kesinlikle eklenmez.
- **İzole Kalan Süre (Remaining Time):** Her zaman `Parça Süresi - Geçen Süre` formülüyle negatif formatta hesaplanır (`-2:10`).
- **Enterpolasyon Dengelemesi:** Milisaniye düzeyindeki zaman çizelgesi kaymaları yuvarlanarak süre sayacında titreme (jitter) ve sekme (snap-back) önlenir.

---

## 🪟 Normal Mod ve Dar Mod (Compact Player)

Lyrow iki farklı kullanım senaryosuna göre anında boyutlanır:

### 1. Normal Mod (Geniş Görünüm)
Geniş ekranda tam albüm kapağı, dalga formu, büyük kontrol butonları ve merkezlenmiş tam şarkı sözü akışı sunar. Şarkı sözleri ekran boyutuna göre otomatik sarılır (`TextWrapping`). Sol üst alan, sade bir deneyim için başlıksız bırakılmıştır.

### 2. Dar Mod (Compact Player — 352×148 px)
Çalışma masasında minimum yer kaplayacak şekilde tasarlanmıştır:
- Sol tarafta 90×90 px albüm kapağı.
- Sağ tarafta parça adı, sanatçı, ilerleme çubuğu ve kontrol butonları.
- Ses simgesine tıklandığında açılan Neumorphic ses kaydırıcısı.
- Çıkış aygıtı simgesiyle doğrudan ses kartı/kulaklık geçişi.
- `^` butonuyla aşağıya doğru genişleyen kompakt şarkı sözü paneli.

---

## 🎨 Açık ve Koyu Neumorphism Tasarımı

Lyrow, modern düz tasarımların ötesine geçerek derinlikli ve dokunsal **Neumorphism** stilini benimser:
- **Koyu Tema:** Koyu gri zemin üzerine yumuşak ışık ve derin gölge katmanları; göz yormayan gece dinleme deneyimi.
- **Açık Tema:** Temiz, hafif kabartmalı açık tonlar; aydınlık çalışma ortamları için ideal kontrast.
- **Dinamik Arka Plan:** Albüm kapağından çıkarılan dominant tonlarla sinematik parlaklık sınırında (`<= 55 cd/m²`) yumuşak renk geçişi.

---

## 💻 Sistem Gereksinimleri

| Bileşen | Asgari Gereksinim | Önerilen Donanım |
|---|---|---|
| **İşletim Sistemi** | Windows 10 64-bit (Sürüm 2004 / Derleme 19041+) | **Windows 11 64-bit (22H2 veya 24H2)** |
| **İşlemci (CPU)** | 64-bit çift çekirdekli Intel / AMD işlemci | Intel Core i3 / AMD Ryzen 3 veya üstü |
| **Bellek (RAM)** | 4 GB RAM | 8 GB RAM |
| **Grafik Kartı (GPU)** | DirectX 11 / DWM destekli grafik kartı | Donanım hızlandırmalı tüm GPU'lar |
| **Ses Aygıtı** | Standart Windows ses çıkışı (WASAPI Loopback uyumlu) | Kulaklık, Hoparlör veya Bluetooth Ses Cihazı |
| **Çalışma Zamanı** | Bağımsız pakette **hiçbir harici runtime gerekmez** | Geliştirme için: .NET 10 LTS SDK |

---

## 🚀 Kurulum ve Kullanım Talimatları

### Hızlı Kurulum (Taşınabilir ZIP)
1. [Releases](https://github.com/owergungor/Lyrow/releases/tag/v3.7) sayfasından `Lyrow-v3.7-win-x64.zip` dosyasını indirin.
2. ZIP dosyasını dilediğiniz bir klasöre (örn. `C:\Program Files\Lyrow` veya `Belgelerim\Lyrow`) çıkartın.
3. `Lyrow.App.exe` dosyasını çalıştırın.

### Kullanım İpuçları
- **Görünüm Değiştirme:** Pencere başlığındaki mod değiştirme simgesine tıklayarak Normal Mod ile Dar Mod arasında anında geçiş yapabilirsiniz.
- **Şarkı Sözünde Satıra Atlama:** Senkronize şarkı sözlerinde dilediğiniz bir satıra tıklayarak parçayı doğrudan o saniyeye sarabilirsiniz.
- **İlerleme Çubuğu (Seek):** İlerleme çubuğuna tıklayarak veya sürükleyerek parçada dilediğiniz konuma atlayabilirsiniz.
- **Ses Düzeyi:** Dar modda ses simgesine tıklayarak açılır kaydırıcı üzerinden sesi ayarlayabilir veya sessize alabilirsiniz.
- **Aygıt Seçimi:** Cihaz simgesine tıklayarak sistem varsayılan ses çıkışını (Hoparlör, Kulaklık, Bluetooth) anında değiştirebilirsiniz.
- **Ayarlar:** Tepsideki (System Tray) Lyrow simgesine sağ tıklayarak ayarları açabilir, Glow kalınlığını, başlatma tercihlerini ve arayüz temalarını yapılandırabilirsiniz.

---

## 🧪 Test ve Kalite Doğrulaması

Lyrow her aşamada sıkı otomatik testler ve platform doğrulamalarıyla geliştirilmektedir:

- **Birim ve Entegrasyon Testleri:** **635 / 635 Test PASS (%100 Başarı)**
  - `Lyrow.Core.Tests`: 59 test (Modül yaşam döngüsü, olay veri yolu, ayarlar)
  - `Lyrow.Audio.Tests`: 53 test (WASAPI yakalama, FFT matematik motoru, vuruş algılama)
  - `Lyrow.Music.Tests`: 120 test (GSMTC dinleme, metadata normalizasyonu, parça geçiş izolasyonu)
  - `Lyrow.App.Tests`: 403 test (WinUI 3 ViewModel'leri, Dar Mod viewport hesaplamaları, Stage 33 timeline motoru testleri)
- **Sürekli Entegrasyon (CI):** Her commit ve push işlemi [GitHub Actions](https://github.com/owergungor/Lyrow/actions) Windows derleme ortamında doğrulanır.
- **Smoke Test:** Dağıtım öncesinde `Lyrow.App.exe --smoke-test` komutuyla yerel Windows x64 başlatma ve temiz kapanma test edilir.

---

## 📜 Sürüm Geçmişi

### [v3.7] — 2026-11-10 (Güncel)
- **Merkezi Playback Timeline Motoru:** `IPlaybackTimelineEngine` mimarisiyle geçen süre, kalan süre ve parça geçiş izolasyonu tamamen bağımsızlaştırıldı.
- **Kümülatif Süre Hatası Giderildi:** Ardışık şarkı geçişlerinde sürelerin toplanması engellendi.
- **Lyrics Başlık Temizliği:** Lyrics penceresi sol üst başlık metni kaldırılarak görsel alan sadeleştirildi.
- Detaylar için [v3.7 Sürüm Notları](docs/RELEASE_NOTES_v3.7.md)'na göz atın.

### [v3.6] — 2026-11-08
- Tıklama ve sürükleme ile GSMTC seek desteği ve gecikme giderici zaman çizelgesi enterpolasyonu.
- Compact Player genişletilebilir şarkı sözü çekmecesi.
- Detaylar için [v3.6 Sürüm Notları](https://github.com/owergungor/Lyrow/releases/tag/v3.6)'na göz atın.

### [v3.5] — 2026-10-31
- Yatay taşmayan akıcı şarkı sözü sarma (`TextWrapping`).
- Yüksek çözünürlüklü ve animasyonlu kapak desteği (GIF/WebP).
- 24px varsayılan Ambient Edge Bloom.
- Detaylar için [docs/RELEASE_NOTES_v3.5.md](docs/RELEASE_NOTES_v3.5.md)'ye bakın.

*Daha eski sürümler için [CHANGELOG.md](CHANGELOG.md) dosyasına bakabilirsiniz.*

---

## ⚠️ Bilinen Sınırlamalar

1. **Windows SmartScreen Uyarısı:**
   Dağıtım paketleri ticari bir EV/OV kod imzalama sertifikasıyla imzalanmadığından, Windows Defender SmartScreen ilk çalıştırmada koruma uyarısı verebilir. Dosya SHA-256 özetini doğruladıktan sonra *"Ek bilgi -> Yine de çalıştır"* seçeneğiyle güvenle açabilirsiniz.
2. **GSMTC Desteği Gereksinimi:**
   Medya kontrolleri ve şarkı bilgisi alabilmek için kullanılan müzik çaların Windows Medya Kontrolleri'ni (GSMTC) desteklemesi gerekir. Eski Win32 oynatıcılar bu arayüzü desteklemiyorsa metadata algılanamayabilir.

---

## 🤝 Katkıda Bulunma

Lyrow açık kaynaklı bir topluluk projesidir. Hata bildirimleri, özellik önerileri ve kod katkıları memnuniyetle karşılanır. Ayrıntılı yönergeler için lütfen [CONTRIBUTING.md](CONTRIBUTING.md) belgesini inceleyin.

```bash
# Projeyi yerel ortamda çalıştırmak için:
git clone https://github.com/owergungor/Lyrow.git
cd Lyrow
dotnet restore Lyrow.sln
dotnet build Lyrow.sln -c Release
dotnet test Lyrow.sln
```

---

## 📄 Lisans

Lyrow, [MIT Lisansı](LICENSE) kapsamında lisanslanmıştır. Detaylar için `LICENSE` dosyasına göz atabilirsiniz.
