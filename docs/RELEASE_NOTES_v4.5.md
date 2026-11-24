# Lyrow v4.5 — Complete Branding Redesign & Release

**Tarih:** 2026-11-24  
**Temel Sürüm:** v4.4 (`c1d0442`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 732 / 732 PASS (%100, deterministik/seri koşu)

---

## Öne Çıkan Yenilikler ve Değişiklikler

### 1. Yeni Lyrow Marka Kimliği ve Çoklu Çözünürlüklü İkonlar
- Yeni 3B şerit yıldız logosu tüm uygulama yüzeylerine, Windows Explorer EXE simgesine, görev çubuğuna (`AppWindow.SetIcon`), Başlat menüsüne, pencere başlığına ve sistem tepsisine entegre edildi.
- 16, 24, 32, 48, 64, 128 ve 256 px katmanlarını içeren gerçek 7 katmanlı 32-bit ARGB `.ico` (`lyrow.ico` / `lyrow-logo.ico`) üretildi.
- Ayarlar > Hakkında ekranına ve sağ üst güncelleme toast bildirimlerine düşük bellek decode boyutlu (`DecodePixelWidth`) yeni Lyrow logosu eklendi.
- Inno Setup kurulum ve kaldırma sihirbazı yeni `wizard-sidebar.bmp` (`164×314`) ve `wizard-small.bmp` (`55×55`) görselleriyle güncellendi.

### 2. Microsoft Store & MSIX Tam Uyumluluk
- Microsoft Partner Center kimliği eksiksiz korundu:
  - **Identity Name:** `crius.Lyrow`
  - **Publisher:** `CN=2F9A5694-FADC-405A-AF51-BB9269D3FFF1`
  - **PublisherDisplayName:** `crius`
  - **DisplayName:** `Lyrow` · **Version:** `4.5.0.0` · **Capability:** `runFullTrust`
- Tüm MSIX görsel varlıkları (`StoreLogo.png`, `Square44x44Logo.png`, `Square71x71Logo.png`, `Square150x150Logo.png`, `Square310x310Logo.png`, `Wide310x150Logo.png`, `SplashScreen.png`) şeffaflık ve en-boy oranı korunarak yeni logodan üretildi.

### 3. Birleşik `v4.5` Sürüm Hizalaması ve Dokümantasyon
- `Directory.Build.props`, `Lyrow.App.csproj` (`FileVersion` & `AssemblyVersion` `4.5.0.0`), `GitHubUpdateService.AppVersion`, `App.CurrentReleaseVersion`, Inno Setup ve MSIX manifest sürüm numaraları `4.5` olarak birleştirildi.
- `README.md` yeni hero banner (`docs/assets/lyrow-hero-banner.png`) ile, GitHub Pages üzerindeki Gizlilik Politikası sayfası (`docs/privacy-policy.html`) yeni marka logosuyla güncellendi.

---

## Dağıtım Paketleri ve SHA-256 Doğrulama Özetleri

| Paket | Dosya Adı | Boyut | İmza Durumu | SHA-256 |
|---|---|---|---|---|
| **Windows Setup EXE** | `Lyrow-4.5-Setup-x64.exe` | 63.81 MB | İmzasız | `7899D339B3A01BA533EF7C92D115A8142959340043EED00124E963BB5DC710C1` |
| **Portable ZIP** | `Lyrow-v4.5-win-x64-portable.zip` | 92.01 MB | — | `3D9EBFB1D659950C7D96C1BDFE4342F1B638C7DE1C8A49F719DE7BA4B725D92A` |
| **MSIX x64 (Store Kimlikli)** | `Lyrow-v4.5-x64.msix` | 94.07 MB | **İmzasız (`NotSigned`)** | `480045FD50C5E4E1B927460CBE47AA43CBE8BDDDCFC446C7D7F7D213E10AC2EE` |

> **Not:** `Lyrow-v4.5-x64.msix` paketi Microsoft Partner Center kimliğiyle (`crius.Lyrow` / `crius`) imzasız (`NotSigned`) olarak sunulmaktadır; Store yüklemesinde Microsoft Store tarafından imzalanır.
