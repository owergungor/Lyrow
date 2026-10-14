# Lyrow

[![CI](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml/badge.svg)](https://github.com/owergungor/Lyrow/actions/workflows/ci.yml)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?logo=windows)](https://microsoft.com/windows)
[![.NET 9 / 10](https://img.shields.io/badge/.NET-9.0%20(Targeting%2010%20LTS)-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI: Avalonia](https://img.shields.io/badge/UI-Avalonia%2012-8A2BE2)](https://avaloniaui.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

**Lyrow**, müzik deneyimini masaüstünde görsel bir şölene dönüştüren iki bağımsız modülü tek bir zarif Windows uygulamasında birleştiren modern bir multimedya platformudur:

- **Lumn:** Sistem ses çıkışını (WASAPI Loopback) anlık analiz eden, donanım hızlandırmalı ritim ve frekans tepkili ışık/spektrum efektleri sunan modül.
- **Verci:** Sistemde çalan müziğin (Spotify, Apple Music, YouTube vb.) metadata bilgilerini ve senkronize şarkı sözlerini (LRC) pürüzsüz yüzen overlay veya tam ekran pencerelerde sunan modül.

---

## 🌟 Temel Ürün İlkeleri (Product Invariants)

1. **Bağımsız Modülerlik:** Lumn, Verci olmadan çalışabilir; Verci, Lumn olmadan çalışabilir. İki modül birbirine sıkı bağımlı değildir.
2. **Eşzamanlı Çalışma:** İki modül aynı anda çalışabilir ve kullanıcı tercihine bağlı olarak ortak ritim ve renk paletiyle görsel entegrasyon kurabilir.
3. **Tek Süreç İlkesi:** Modüller ayrı exe/process süreçleri olarak değil, tek bir `Lyrow.exe` süreci altında yönetilen izole servisler olarak çalışır.
4. **Hata İzolasyonu:** Bir modülde meydana gelen hata (örneğin şarkı sözü API kesintisi) diğer modülün veya ana uygulamanın çalışmasını engellemez.
5. **Sıfır Arka Plan Yükü (Zero Inactive Overhead):** Kapatılan veya gizlenen modüllerin tüm ses yakalama, render döngüleri ve zamanlayıcıları derhal durdurulur ve kaynaklar serbest bırakılır.
6. **Kalıcı Tercihler:** Modül açık/kapalı durumları, pencere koordinatları ve kullanıcı ayarları yeniden başlatmalar arasında `%APPDATA%\Lyrow\settings.json` altında korunur.

---

## 🚀 Özellik Durumu (Gerçekleştirilen vs. Planlanan)

### Mevcut (Aşama 1 & Aşama 2)
- [x] Mimari tasarım ve bileşen sınırlarının belirlenmesi.
- [x] Temel modül arayüzleri (`ILyrowModule`, `ModuleState`, `ModuleMetadata`).
- [x] Modül yaşam döngüsü yöneticisi (`IModuleManager`, `ModuleManager`) ile 4 çalışma durumunun desteklenmesi:
  - 1. İki modül kapalı (Yalnızca Tray / Ayarlar)
  - 2. Yalnızca Lumn açık
  - 3. Yalnızca Verci açık
  - 4. İki modül birlikte açık
- [x] Gevşek bağlı olay veri yolu (`IEventBus`, `InMemoryEventBus`).
- [x] Güvenli ve atomik JSON ayar yöneticisi (`ISettingsManager`, `SettingsManager`).
- [x] Dependency Injection (`Microsoft.Extensions.DependencyInjection`) tabanlı altyapı.
- [x] Avalonia UI tabanlı ana pencere ve Neumorphic modern tasarım token'ları.
- [x] Açık, Koyu ve Sistem temasını izleme (`ThemeVariant.Default`, `ThemeVariant.Dark`, `ThemeVariant.Light`).
- [x] Yaşam döngüsü, ayar kalıcılığı ve olay dağıtımı için kapsamlı xUnit birim testleri.
- [x] GitHub Actions CI iş akışı.

### Planlanan (Aşama 3 - Aşama 7)
- [ ] **Lumn (Aşama 3):** WASAPI Loopback sistem sesi yakalama (`NAudio.Wasapi`), sıfır-tahsisli FFT frekans analizi, donanım hızlandırmalı dinamik ışık efektleri.
- [ ] **Verci (Aşama 4):** Windows GSMTC medya izleyicisi, LRCLIB REST API istemcisi, senkronize LRC şarkı sözü zamanlayıcısı ve kaydırma animasyonu.
- [ ] **Görsel Entegrasyon (Aşama 5):** Win32 `WS_EX_TRANSPARENT` Click-Through adaptörü, Neumorphism ambiyans ışık entegrasyonu.
- [ ] **Performans Optimizasyonu (Aşama 6):** Canlı bellek ve CPU ölçümleri, 3 saatlik stres testi.
- [ ] **Windows Dağıtımı (Aşama 7):** Self-contained tek dosya derlemesi ve Windows yükleyici paketi.

---

## 🛠️ Teknoloji Yığını

- **Dil & Çalışma Zamanı:** C# 13 / .NET 9.0 (Geliştirme ortamı `9.0.318`, gelecekte `.NET 10 LTS` uyumlu)
- **Arayüz Çatısı:** Avalonia UI (v12.1.4 / v11.2+) — XAML, SkiaSharp donanım hızlandırmalı
- **Ses Yakalama:** Windows Core Audio / WASAPI Loopback (`NAudio.Wasapi` - MIT)
- **Ses Analizi:** Sıfır-tahsisli Cooley-Tukey FFT motoru
- **Medya Metadata:** Windows GSMTC (`GlobalSystemMediaTransportControlsSessionManager`)
- **Şarkı Sözleri:** LRCLIB REST API & Yerel Disk Önbelleği
- **Overlay Entegrasyonu:** Windows Win32 P/Invoke (`User32.dll`)
- **Test Çerçevesi:** xUnit 2.9.2, FluentAssertions 8.1.1, Microsoft.NET.Test.Sdk 17.12.0

---

## 🏛️ Proje Mimarisi

```
Lyrow/
├── .github/workflows/         # GitHub Actions CI iş akışları
├── docs/                      # Detaylı mimari ve plan belgeleri
│   ├── ARCHITECTURE.md        # Mimari tasarım, veri akışları ve güvenlik
│   ├── DEVELOPMENT_PLAN.md    # Aşama 1'den 7'ye geliştirme yol haritası
│   ├── PERFORMANCE.md         # RAM/CPU hedefleri ve ölçüm metodolojisi
│   └── MILESTONES.md          # Kilometre taşları ve aşama durumları
├── src/
│   ├── Lyrow.App/             # Avalonia ana masaüstü host uygulaması, pencereler, temalar
│   ├── Lyrow.Core/            # Modül yaşam döngüsü, EventBus, ayarlar ve temel arayüzler
│   ├── Lyrow.Audio/           # (Aşama 3) WASAPI ses yakalama ve FFT analiz motoru
│   ├── Lyrow.Music/           # (Aşama 4) GSMTC medya izleyici ve LRCLIB söz istemcisi
│   ├── Lyrow.Modules.Lumn/    # (Aşama 3) Lumn görsel efekt motoru
│   ├── Lyrow.Modules.Verci/   # (Aşama 4) Verci şarkı sözü yüzen penceresi
│   └── Lyrow.Platform.Windows/# (Aşama 5) Win32 click-through p/invoke adaptörleri
├── tests/
│   ├── Lyrow.Core.Tests/      # Yaşam döngüsü, ayarlar ve eventbus testleri
│   ├── Lyrow.Audio.Tests/     # (Aşama 3) FFT matematiksel testleri
│   └── Lyrow.Music.Tests/     # (Aşama 4) LRC parsing ve senkronizasyon testleri
├── .gitignore                 # .NET, Avalonia ve Windows için yapılandırılmış kurallar
├── LICENSE                    # MIT Lisansı
├── Lyrow.sln                  # Visual Studio / .NET Solution
└── README.md                  # Proje ana sayfası
```

---

## 💻 Geliştirme Ortamı Gereksinimleri

- **İşletim Sistemi:** Windows 10 (Build 19041+) veya Windows 11 (64-bit)
- **.NET SDK:** .NET 9.0.300+ veya .NET 10.0+ SDK
- **Geliştirme Araçları:** Visual Studio 2022 (v17.12+), JetBrains Rider veya VS Code (C# Dev Kit eklentisiyle)

---

## 🔨 Derleme ve Test Talimatları

### Bağımlılıkları Geri Yükleme
```bash
dotnet restore Lyrow.sln
```

### Projeyi Derleme (Debug / Release)
```bash
dotnet build Lyrow.sln --configuration Release
```

### Birim Testlerini Çalıştırma
```bash
dotnet test Lyrow.sln --configuration Release --verbosity normal
```

### Uygulamayı Çalıştırma
```bash
dotnet run --project src/Lyrow.App/Lyrow.App.csproj
```

---

## ⚡ Performans Hedefleri

| Çalışma Durumu | Hedeflenen RAM | Hedeflenen CPU (Ortalama) |
| :--- | :--- | :--- |
| **Boşta (Modüller Kapalı, Tray)** | **≤ 100 – 150 MB** | **< %1** |
| **Yalnızca Lumn Açık** | **150 – 250 MB** | **%4 – %8** |
| **Yalnızca Verci Açık** | **150 – 250 MB** | **%1 – %3** |
| **İki Modül Birlikte Açık** | **200 – 350 MB** | **%5 – %10** |

> *Ayrıntılı test senaryoları ve optimizasyon ilkeleri için [docs/PERFORMANCE.md](docs/PERFORMANCE.md) belgesine bakınız.*

---

## ⚠️ Bilinen Kısıtlamalar

1. **Exclusive Fullscreen Oyunlar:** Windows DWM mimarisi nedeniyle, pencereli olmayan "Özel Tam Ekran" (Exclusive Fullscreen) oyunlarda hiçbir masaüstü overlay'i görüntülenemez. Oyunların "Kenarlıksız Pencereli" (Borderless Windowed) modda çalışması gerekir.
2. **Ses Cihazı:** WASAPI Loopback yalnızca aktif ses çıkışında veri üretir. Kulaklık/hoparlör takılı olmadığında veya ses çalınmadığında motor otomatik olarak düşük güç moduna geçer.
3. **Platform Kapsamı:** İlk sürüm Windows odaklıdır; ses ve overlay katmanları modüler tasarlandığından ileride Linux (PulseAudio/PipeWire) ve macOS desteği eklenebilecektir.

---

## 📄 Lisans ve Katkı

Bu proje [MIT Lisansı](LICENSE) kapsamında lisanslanmıştır. Katkıda bulunmak için lütfen bir issue açın veya pull request gönderin.
