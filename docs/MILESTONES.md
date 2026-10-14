# Lyrow — Proje Kilometre Taşları (MILESTONES.md)

Bu belge, Lyrow projesinin aşama bazlı kilometre taşlarını, ilerleme durumunu ve kontrol noktalarını özetler.

---

## Kilometre Taşları Tablosu

| Kilometre Taşı | Aşama Adı | Hedef Kapsam | Durum |
| :--- | :--- | :--- | :--- |
| **M1** | **Aşama 1: Teknik Keşif ve Mimari Kararlar** | Ortam analizi, SDK uyumluluk doğrulaması, mimari tasarım, risk analizi, temel dokümantasyon (`ARCHITECTURE.md`, `DEVELOPMENT_PLAN.md`, `PERFORMANCE.md`, `README.md`). | **TAMAMLANDI** |
| **M2** | **Aşama 2: Çekirdek Altyapı ve Çözüm Yapılandırması** | `net9.0` geçişi, `Lyrow.Core`, modül yaşam döngüsü (`ILyrowModule`), `IEventBus`, ayar yönetimi ve birim testler. | *Beklemede (Onay Bekliyor)* |
| **M3** | **Aşama 3: Lumn Modülü (Ses Analizi & Görselleştirme)** | WASAPI Loopback sistem sesi yakalama, sıfır-tahsisli FFT analiz motoru, Avalonia dinamik spektrum penceresi. | *Planlandı* |
| **M4** | **Aşama 4: Verci Modülü (Metadata & Senkronize Sözler)** | Windows GSMTC entegrasyonu, LRCLIB API istemcisi, LRC ayrıştırıcı, yüzen şarkı sözü penceresi. | *Planlandı* |
| **M5** | **Aşama 5: Görsel Entegrasyon, Ayarlar & Overlay** | Win32 Click-through adaptörü, Neumorphism/Glassmorphism tema ayarları, kısayol tuşları. | *Planlandı* |
| **M6** | **Aşama 6: Performans Profili & Kararlılık** | RAM ve CPU ölçümleri, bellek sızıntısı testleri, uzun süreli stres testi, hata kurtarma doğrulaması. | *Planlandı* |
| **M7** | **Aşama 7: Dağıtım, Kurulum & CI/CD** | Self-contained tek dosya derleme, Windows yükleyici (Installer), GitHub Actions iş akışı. | *Planlandı* |

---

## Aşama 1 Doğrulama Notları

- **Tespit Edilen SDK:** .NET SDK 9.0.318 (Ortamda .NET 10 kurulu değil).
- **Proje Durumu:** `Lyrow.App.csproj` `net10.0` hedeflendiğinde `NETSDK1045` hatası oluşmaktadır; Aşama 2'de `net9.0` sürümüne güncellenmesi kararlaştırılmıştır.
- **Kütüphaneler:** Avalonia UI (12.1.4 / 11.2+), NAudio.Wasapi (MIT), xUnit (Apache 2.0).
- **Sıradaki Adım:** Kullanıcı onayının ardından Aşama 2'nin başlatılması.
