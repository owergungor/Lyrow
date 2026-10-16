# Lyrow — Proje Kilometre Taşları (MILESTONES.md)

Bu belge, Lyrow projesinin aşama bazlı kilometre taşlarını, ilerleme durumunu ve kontrol noktalarını özetler.

---

## Kilometre Taşları Tablosu

| Kilometre Taşı | Aşama Adı | Hedef Kapsam | Durum |
| :--- | :--- | :--- | :--- |
| **M1** | **Aşama 1: Teknik Keşif ve Mimari Kararlar** | Ortam analizi, SDK uyumluluk doğrulaması, mimari tasarım, risk analizi, temel dokümantasyon (`ARCHITECTURE.md`, `DEVELOPMENT_PLAN.md`, `PERFORMANCE.md`, `README.md`). | **TAMAMLANDI** |
| **M2** | **Aşama 2: Çekirdek Altyapı ve Solution Yapılandırması** | .NET 10 LTS SDK kurulumu ve hizalaması, `Lyrow.Core`, modül yaşam döngüsü (`ILyrowModule`), 4 çalışma durumunu yöneten `ModuleManager`, `InMemoryEventBus`, `SettingsManager`, Avalonia Neumorphic ana pencere ve 17 adet xUnit testi. | **TAMAMLANDI** |
| **M3** | **Aşama 3: Lumn Modülü (Ses Analizi & Görselleştirme)** | WASAPI Loopback sistem sesi yakalama, sıfır-tahsisli FFT analiz motoru, RMS/dBFS seviye algılama, logaritmik frekans bantları, Avalonia GPU hızlandırmalı dinamik spektrum penceresi (`LumnWindow`), 19 xUnit testi. | **TAMAMLANDI** |
| **M4** | **Aşama 4: Verci Modülü (Metadata & Senkronize Sözler)** | Windows GSMTC entegrasyonu, LRCLIB API istemcisi, LRC ayrıştırıcı, yüzen şarkı sözü penceresi. | *Sıradaki Aşama* |
| **M5** | **Aşama 5: Görsel Entegrasyon, Ayarlar & Overlay** | Win32 Click-through adaptörü, Neumorphism/Glassmorphism tema ayarları, kısayol tuşları. | *Planlandı* |
| **M6** | **Aşama 6: Performans Profili & Kararlılık** | RAM ve CPU ölçümleri, bellek sızıntısı testleri, uzun süreli stres testi, hata kurtarma doğrulaması. | *Planlandı* |
| **M7** | **Aşama 7: Dağıtım, Kurulum & CI/CD** | Self-contained tek dosya derleme, Windows yükleyici (Installer), GitHub Actions iş akışı. | *Planlandı* |

---

## Aşama 3 Doğrulama Notları

- **WASAPI Loopback:** `WasapiAudioCaptureService` ile Windows varsayılan çıkış aygıtı doğrudan sürücü katmanından dinlenir. Aygıt kaybı (`AUDCLNT_E_DEVICE_INVALIDATED`) durumunda 500 ms içinde otomatik yeniden bağlanma döngüsü test edildi.
- **Sıfır Tahsisli FFT:** `FftAnalyzer` önceden hesaplanmış Hanning penceresi ve twiddle trigonometri tabloları ile sıfır heap tahsisiyle çalışır.
- **Müzikal Frekans Ayrıştırma:** Sub-bass (20-60 Hz), Bass (60-250 Hz), Mid (250-4000 Hz), Treble (4000-20000 Hz) ve 32 logaritmik çubuk `FrequencyBandProcessor` tarafından hesaplanır.
- **Görselleştirici:** Avalonia 12 üzerinde donanım hızlandırmalı GPU çizimi (`LumnVisualizerControl`) ve bağımsız spektrum penceresi (`LumnWindow`) 60 FPS hızında test edildi. Sessizlik anında CPU tüketimi %0 seviyesine inmektedir.
- **Test Kapsamı:** `Lyrow.Audio.Tests` altında 19 test ve `Lyrow.Core.Tests` altında 17 test olmak üzere toplam 36 xUnit testi Release konfigürasyonunda %100 başarıyla geçti.
- **Bağımsızlık:** Lumn modülü Verci olmadan tek başına başlatılıp durdurulabilir; modüller arası izolasyon prensibi korundu.
