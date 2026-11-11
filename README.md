# Lyrow

> A native Windows music companion with synchronized lyrics, playback controls, and a compact desktop experience.

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/release.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/release.yml)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20Native-08a1f7?logo=windows)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0%20LTS-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Windows%20App%20SDK%201.8-03c1f4?logo=windows)](https://learn.microsoft.com/windows/apps/winui/winui3/)
[![Release](https://img.shields.io/badge/Release-v3.7-success)](https://github.com/owergungor/Lyrow/releases/tag/v3.7)
[![Tests](https://img.shields.io/badge/Tests-635%20Passed-brightgreen)](https://github.com/owergungor/Lyrow/actions)
[![License](https://img.shields.io/badge/License-MIT-750014.svg)](LICENSE)

**Lyrow**, Windows 10 ve 11 için geliştirilmiş, müzik dinleme deneyimini görsel bir atmosfere dönüştüren yerel bir masaüstü uygulamasıdır. **WinUI 3 ve Windows App SDK** mimarisi üzerinde çalışan Lyrow; ekranın dört kenarına yayılan gerçek zamanlı ortam aydınlatması (Ambient Edge Glow), Apple Music benzeri 60 FPS dikey merkezlenen senkronize şarkı sözleri ve kompakt Dar Mod (Mini Player) sunar.

---

## 📥 İndirme ve Kurulum

### Güncel Sürüm: Lyrow v3.7 (Önerilen)

| Kaynak | Dosya | Boyut | SHA-256 Doğrulama Özeti |
|---|---|---|---|
| **GitHub Release** | [Lyrow v3.7 Sürüm Sayfası](https://github.com/owergungor/Lyrow/releases/tag/v3.7) | — | — |
| **Taşınabilir Paket** | [Lyrow-v3.7-win-x64.zip](https://github.com/owergungor/Lyrow/releases/download/v3.7/Lyrow-v3.7-win-x64.zip) | ~93.3 MB | `601664444F7D983B05E3F675D220E8F25EDB7701520D4E923EFCD8F4E0A85B73` |

> [!NOTE]
> Lyrow bağımsız (self-contained) bir Windows x64 paketidir. Çalışmak için harici bir .NET Runtime kurulumu gerektirmez. Arşivi bir klasöre çıkartıp `Lyrow.App.exe` dosyasını çalıştırmanız yeterlidir.

### Alternatif ve Önceki Kararlı Sürümler (Lyrow v3.5)
Klasik kurulum sihirbazını veya önceki paketleri tercih eden kullanıcılar için:
- **Kurulum Sihirbazı (Installer):** [Lyrow-3.5-Setup-x64.exe](https://github.com/owergungor/Lyrow/releases/tag/v3.5)
- **Taşınabilir Arşiv (Portable):** [Lyrow-v3.5-win-x64-portable.zip](https://github.com/owergungor/Lyrow/releases/tag/v3.5)

---

## 🌟 Mimari ve Temel Modüller

Lyrow, iki bağımsız ancak birbiriyle tam senkron çalışan ana modülden oluşur:

```
┌────────────────────────────────────────────────────────┐
│                        Lyrow                           │
│  ┌──────────────────────────┐┌──────────────────────┐  │
│  │   Ambient Glow Modülü    ││ Lyrics & Mini Player │  │
│  │ - 4 Kenar Ekran Işıması  ││ - Senkronize Sözler  │  │
│  │ - WASAPI Loopback Capture││ - GSMTC Entegrasyonu │  │
│  │ - 5 Bant FFT Spektrumu   ││ - Dar Mod (352x148)  │  │
│  └─────────────┬────────────┘└──────────┬───────────┘  │
│                │                        │              │
│                ▼                        ▼              │
│  ┌──────────────────────────────────────────────────┐  │
│  │   Merkezi Playback Timeline Motoru (Lyrow.Core)  │  │
│  │   - İzole Parça Süresi, Geçen ve Kalan Zaman     │  │
│  └──────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────┘
```

1. **Ambient Edge Glow (Ortam Aydınlatması):**
   Ekranın 4 kenarında (Üst, Alt, Sol, Sağ) sistem ses çıkışının ritmine, 5 frekans bandına (Sub-Bass, Bass, Low-Mid, Mid, High-Mid/Treble) ve Spectral Flux vuruş algılamasına göre bağımsız renk enerjisi yayar. Albüm kapağının renk paletiyle yumuşak cross-fade geçişleri yapar ve diğer pencerelerin kullanımını **kesinlikle engellemez (click-through, zero-interference)**.

2. **Lyrics & Compact Player (Şarkı Sözleri ve Mini Oynatıcı):**
   Windows GSMTC üzerinden çalan medyayı otomatik algılar, LRCLIB API aracılığıyla senkronize şarkı sözlerini milisaniye hassasiyetinde çeker, aktif satırı dikey olarak odaklar ve soldan sağa akıcı **karaoke dolum animasyonu (word/character fill)** sunar.

---

## 🚀 Temel Yetenekler

### 1. Zaman Çizelgesi ve Süre Doğruluğu
- **Merkezi Playback Timeline Motoru (`IPlaybackTimelineEngine`):** Geçen süre (`0:45`) ve kalan süre (`-2:15`) her parça için bağımsız hesaplanır; önceki şarkıların süreleri kesinlikle birbirine eklenmez.
- **Otomatik Parça Geçiş İzolasyonu (`A → B → C`):** Şarkı bittiğinde veya atlandığında zaman çizelgesi, sözler ve albüm kapağı atomik olarak sıfırlanır; eski parçanın GSMTC kalıntıları yeni parçanın zaman çizelgesini bozamaz.
- **Deterministik İlerleme (Seek):** İlerleme çubuğunda tıklanan veya sürüklenen noktaya ya da şarkı sözü satırına tıklandığında anında deterministik atlama yapılır.

### 2. Arayüz ve Görünüm Modları
- **Normal Mod:** Tam boyutta, dikey merkezlenen akıcı şarkı sözleri, yüksek çözünürlüklü kapak ve Neumorphic kontroller. Şarkı sözleri pencere sınırlarına göre otomatik sarılır (`TextWrapping`).
- **Dar Mod (Compact Player — 352×148 px):** Masaüstünde minimum alan kaplayan yatay düzen: 90×90 px albüm kapağı, parça bilgisi, ilerleme çubuğu, açılır şarkı sözü çekmecesi ve kayan ses kontrolü.
- **Açık ve Koyu Neumorphism Teması:** Yumuşak içbükey ve kabartmalı gölge katmanları, dokunsal buton basma efektleri ve Windows 11 Fluent tasarım uyumu.
- **Yüksek Çözünürlüklü & Animasyonlu Kapaklar:** Kapak görselleri ham kalitesinde korunur; animasyonlu GIF ve WebP kapakları donanım hızlandırmalı oynatılır.

### 3. Sistem ve Ses Entegrasyonu
- **Doğrudan Ses Çıkış Aygıtı Seçimi:** Lyrics arayüzündeki aygıt menüsünden Windows varsayılan ses yürütme cihazı (Kulaklık, Hoparlör, Bluetooth) anında değiştirilir ve ilgili glif simgesi canlı güncellenir.
- **Kayan Ses Düzeyi Popup'ı:** Kompakt modda ses simgesine tıklandığında açılan, sessize alma (mute) butonlu yatay ses kaydırıcısı.

---

## 🎵 Desteklenen Medya Platformları

Lyrow, Windows'un yerel **Global System Media Transport Controls (GSMTC)** API'sini dinler. Aşağıdaki platformlar kod tabanında ve test senaryolarıyla doğrulanmıştır:

| Platform | Ortam | Doğrulanan Yetenekler |
|---|---|---|
| **Spotify Desktop** | Masaüstü İstemcisi | Şarkı Bilgisi, Albüm Kapağı, Oynat/Duraklat, Önceki/Sonraki, Seek, Söz Senkronizasyonu |
| **Apple Music Desktop** | Windows 11 Uygulaması | Şarkı Bilgisi, Sanatçı/Albüm Ayrıştırması, Kapak, Oynatma Kontrolleri |
| **Apple Music Web** | Edge, Chrome, Firefox | Şarkı Bilgisi, Web Kapak Çözümlemesi, Seek, Tarayıcı Yönlendirmesi |
| **YouTube Music Web** | Edge, Chrome, Firefox | Şarkı Bilgisi, Otomatik Parça Geçişi, Kapak Çözümlemesi, Oynatma Kontrolleri |
| **Windows Media Player / Diğerleri** | Masaüstü / UWP / PWA | Windows GSMTC arayüzünü destekleyen tüm modern medya oynatıcıları |

---

## ⏱️ Playback Timeline Mekanizması

Lyrow v3.7 ile baştan yazılan **Playback Timeline Engine**, müzik çalarlardaki zamanlama hatalarını kökten çözer:

```
[Parça A (3:30)] ─── 0:00 ───────> 3:30 ─── Otomatik Geçiş ───>
[Parça B (3:11)] ─── 0:00 (Yeni) ─> 1:01 [Kalan: -2:10] (Kümülatif toplam: YOK)
```

- **İzole Geçen Süre (Elapsed Time):** Yalnızca o an çalan parçanın başlangıçtan itibaren geçen süresi hesaplanır (`1:01`).
- **İzole Kalan Süre (Remaining Time):** Her zaman `Parça Süresi - Geçen Süre` formülüyle negatif formatta hesaplanır (`-2:10`).
- **Enterpolasyon Dengelemesi:** Milisaniye düzeyindeki zaman çizelgesi kaymaları yuvarlanarak süre sayacında titreme (jitter) ve sekme (snap-back) önlenir.

---

## 💻 Sistem Gereksinimleri

| Bileşen | Asgari Gereksinim | Önerilen Donanım |
|---|---|---|
| **İşletim Sistemi** | Windows 10 64-bit (Sürüm 2004 / Derleme 19041+) | **Windows 11 64-bit (22H2 veya 24H2)** |
| **İşlemci (CPU)** | 64-bit çift çekirdekli Intel / AMD işlemci | Intel Core i3 / AMD Ryzen 3 veya üstü |
| **Bellek (RAM)** | 4 GB RAM | 8 GB RAM |
| **Grafik Kartı (GPU)** | DirectX 11 / DWM destekli grafik kartı | Donanım hızlandırmalı modern GPU |
| **Ses Aygıtı** | Standart Windows ses çıkışı (WASAPI Loopback uyumlu) | Kulaklık, Hoparlör veya Bluetooth Ses Cihazı |
| **Çalışma Zamanı** | Bağımsız pakette **harici runtime gerekmez** | Geliştirme için: .NET 10 LTS SDK |

---

## 🚀 Kullanım Talimatları

### Hızlı Başlangıç
1. [Releases](https://github.com/owergungor/Lyrow/releases/tag/v3.7) sayfasından `Lyrow-v3.7-win-x64.zip` dosyasını indirin.
2. ZIP dosyasını dilediğiniz bir klasöre çıkartın.
3. `Lyrow.App.exe` dosyasını çalıştırın.

### Arayüz Kısayolları ve İpuçları
- **Görünüm Değiştirme:** Pencere başlığındaki mod simgesine tıklayarak Normal Mod ile Dar Mod arasında geçiş yapabilirsiniz.
- **Şarkı Sözünde Satıra Atlama:** Senkronize şarkı sözlerinde dilediğiniz bir satıra tıklayarak parçayı doğrudan o konuma sarabilirsiniz.
- **İlerleme Çubuğu (Seek):** İlerleme çubuğuna tıklayarak veya sürükleyerek parçada dilediğiniz konuma atlayabilirsiniz.
- **Ses Düzeyi:** Dar modda ses simgesine tıklayarak açılır kaydırıcı üzerinden sesi ayarlayabilir veya sessize alabilirsiniz.
- **Aygıt Seçimi:** Cihaz simgesine tıklayarak sistem varsayılan ses çıkışını (Hoparlör, Kulaklık, Bluetooth) değiştirebilirsiniz.
- **Ayarlar:** Sistem tepsisindeki Lyrow simgesine sağ tıklayarak ayarları açabilir, Glow kalınlığını, başlatma tercihlerini ve temayı yapılandırabilirsiniz.

---

## 🧪 Kalite Doğrulaması ve Testler

Lyrow her aşamada sıkı otomatik testler ve platform doğrulamalarıyla geliştirilmektedir:

- **Birim ve Entegrasyon Testleri:** **635 / 635 Test PASS (%100 Başarı)**
  - `Lyrow.Core.Tests`: 59 test (Modül yaşam döngüsü, olay veri yolu, ayarlar)
  - `Lyrow.Audio.Tests`: 53 test (WASAPI yakalama, FFT matematik motoru, vuruş algılama)
  - `Lyrow.Music.Tests`: 120 test (GSMTC dinleme, metadata normalizasyonu, parça geçiş izolasyonu)
  - `Lyrow.App.Tests`: 403 test (WinUI 3 ViewModel'leri, Dar Mod viewport hesaplamaları, timeline motoru testleri)
- **Sürekli Entegrasyon (CI):** Her commit ve push işlemi [GitHub Actions](https://github.com/owergungor/Lyrow/actions) Windows derleme ortamında test edilir.
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

*Tüm geçmiş sürümler ve değişiklik ayrıntıları için [CHANGELOG.md](CHANGELOG.md) dosyasına bakabilirsiniz.*

---

## ⚠️ Bilinen Sınırlamalar

1. **Windows SmartScreen Uyarısı:**
   Dağıtım paketleri ticari bir EV/OV kod imzalama sertifikasıyla imzalanmadığından, Windows Defender SmartScreen ilk çalıştırmada koruma uyarısı verebilir. Dosya SHA-256 özetini doğruladıktan sonra *"Ek bilgi -> Yine de çalıştır"* seçeneğiyle güvenle açabilirsiniz.
2. **GSMTC Desteği Gereksinimi:**
   Medya kontrolleri ve şarkı bilgisi alabilmek için kullanılan müzik çaların Windows Medya Kontrolleri'ni (GSMTC) desteklemesi gerekir.

---

## 🤝 Katkıda Bulunma

Lyrow açık kaynaklı bir topluluk projesidir. Hata bildirimleri, özellik önerileri ve kod katkıları memnuniyetle karşılanır. Ayrıntılı yönergeler için lütfen [CONTRIBUTING.md](CONTRIBUTING.md) belgesini inceleyin.

```bash
# Projeyi klonlama ve derleme:
git clone https://github.com/owergungor/Lyrow.git
cd Lyrow
dotnet restore Lyrow.sln
dotnet build Lyrow.sln -c Release
dotnet test Lyrow.sln
```

---

## 📄 Lisans

Lyrow, [MIT Lisansı](LICENSE) kapsamında lisanslanmıştır. Detaylar için `LICENSE` dosyasına göz atabilirsiniz.
