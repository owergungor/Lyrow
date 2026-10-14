# Lyrow — Proje Kilometre Taşları (MILESTONES.md)

Bu belge, Lyrow projesinin aşama bazlı kilometre taşlarını, ilerleme durumunu ve kontrol noktalarını özetler.

---

## Kilometre Taşları Tablosu

| Kilometre Taşı | Aşama Adı | Hedef Kapsam | Durum |
| :--- | :--- | :--- | :--- |
| **M1** | **Aşama 1: Teknik Keşif ve Mimari Kararlar** | Ortam analizi, SDK uyumluluk doğrulaması, mimari tasarım, risk analizi, temel dokümantasyon (`ARCHITECTURE.md`, `DEVELOPMENT_PLAN.md`, `PERFORMANCE.md`, `README.md`). | **TAMAMLANDI** |
| **M2** | **Aşama 2: Çekirdek Altyapı ve Solution Yapılandırması** | .NET 10 LTS SDK kurulumu ve hizalaması, `Lyrow.Core`, modül yaşam döngüsü (`ILyrowModule`), 4 çalışma durumunu yöneten `ModuleManager`, `InMemoryEventBus`, `SettingsManager`, Avalonia Neumorphic ana pencere ve 17 adet xUnit testi. | **TAMAMLANDI** |
| **M3** | **Aşama 3: Lumn Modülü (Ses Analizi & Görselleştirme)** | WASAPI Loopback sistem sesi yakalama, sıfır-tahsisli FFT analiz motoru, Avalonia dinamik spektrum penceresi. | *Sıradaki Aşama* |
| **M4** | **Aşama 4: Verci Modülü (Metadata & Senkronize Sözler)** | Windows GSMTC entegrasyonu, LRCLIB API istemcisi, LRC ayrıştırıcı, yüzen şarkı sözü penceresi. | *Planlandı* |
| **M5** | **Aşama 5: Görsel Entegrasyon, Ayarlar & Overlay** | Win32 Click-through adaptörü, Neumorphism/Glassmorphism tema ayarları, kısayol tuşları. | *Planlandı* |
| **M6** | **Aşama 6: Performans Profili & Kararlılık** | RAM ve CPU ölçümleri, bellek sızıntısı testleri, uzun süreli stres testi, hata kurtarma doğrulaması. | *Planlandı* |
| **M7** | **Aşama 7: Dağıtım, Kurulum & CI/CD** | Self-contained tek dosya derleme, Windows yükleyici (Installer), GitHub Actions iş akışı. | *Planlandı* |

---

## Aşama 2 Doğrulama Notları

- **.NET 10 LTS Durumu:** .NET SDK 10.0.402 başarıyla yerel ortama entegre edildi. Tüm projeler (`Lyrow.App`, `Lyrow.Core`, `Lyrow.Core.Tests`) `net10.0` hedefine yükseltildi.
- **Birim Testleri:** 17 xUnit birim testinin tamamı Release modunda başarıyla geçti (%100 başarı).
- **Arayüz:** Avalonia UI üzerinde 4 modlu kontrol, Lumn/Verci toggle butonları, Neumorphic kartlar ve tema desteği (System / Dark / Light) tamamlandı.
- **GitHub Durumu:** Özel GitHub deposu oluşturuldu (`https://github.com/owergungor/Lyrow`) ve GitHub Actions CI iş akışı hazırlandı.
