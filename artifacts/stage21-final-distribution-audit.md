# Lyrow — Stage 21: Final Distribution Audit & User Acceptance Report

> **Değerlendirme Tarihi:** 2026-11-03
> **Hedef Sürüm:** Lyrow v3.5 (Tag: `2c60b2f2282f24b4b9ada9a91dceee97dc49b940`, Commit: `2856ee5a9e21207d69fca160d701c49dab689a8d`)
> **Temel İstikrar Commit:** `267290e` (`chore(qa): execute real-world validation`)
> **Nihai Dağıtım Kararı:** **READY WITH LIMITATIONS** (Kısıtlamalarla Dağıtıma Hazır)

---

## 1. Dağıtım Paketleri Denetimi ve Bütünlük Doğrulaması

`dist/` dizinindeki v3.5 dağıtım ikili dosyaları ve arşivleri ayrıntılı olarak incelenmiş, dosya boyutları, SHA-256 özetleri ve bileşen bütünlükleri doğrulanmıştır.

### 1.1. Paket Listesi ve SHA-256 Değerleri

| Paket Türü | Dosya Adı | Boyut (Bayt / MB) | SHA-256 Checksum Değeri | Durum |
|---|---|---|---|:---:|
| **Kurulum Paketi (Installer)** | `Lyrow-3.5-Setup-x64.exe` | 67,790,366 B (64.65 MB) | `caa6013056f82629c7c4c3f81f1740efb90c69aa76cdf6aa986d3ba93adc623d` | `PASS` |
| **Taşınabilir Arşiv (Portable)** | `Lyrow-v3.5-win-x64-portable.zip` | 97,809,524 B (93.28 MB) | `6c71c7bcfda6a70bd3a344579d8ba608045d3da6ba51bf71411cf9f2583f5df5` | `PASS` |

### 1.2. Checksum Betikleri Otomasyon Denetimi
- `installer/generate_checksums.ps1` betiği v3.5 paketleri üzerinde çalıştırılmış, `ExitCode: 0` ile standart SHA-256 listesi üretilmiştir.
- `installer/verify_checksums.ps1` betiği ile doğrulanmış:
  ```text
  Lyrow-3.5-Setup-x64.exe : OK
  Lyrow-v3.5-win-x64-portable.zip : OK
  Verification Summary: Total: 2 | Passed: 2 | Failed: 0 | Missing: 0
  All 2 file(s) successfully verified.
  ```

### 1.3. Paket İçerik ve Bileşen Uyumu
- **Toplam Dosya Sayısı:** Portable arşivde 540 dosya (Installer hedefinde 541 dosya, 1 uninstaller eklenmektedir).
- **Yürütülebilir Dosyalar:** `Lyrow.App.exe`, `createdump.exe`, `RestartAgent.exe` eksiksiz mevcuttur.
- **Çalışma Zamanı ve Kaynaklar:** WinUI 3 runtime, WebView2Loader, DirectWriteForwarder, dil kaynakları (`tr`, `en`, `de`, `fr`, `es` vb.) ve `Assets` eksiksiz yer almaktadır.
- **Hata Ayıklama Sızıntı Denetimi:** Test DLL'leri (`*Tests.dll`) ve gereksiz XML dokümantasyon dosyaları paketlerde yer almamaktadır (0 adet). Yalnızca üretim kütüphanelerine ait 4 adet sembol dosyası (`Lyrow.App.pdb`, `Lyrow.Audio.pdb`, `Lyrow.Core.pdb`, `Lyrow.Music.pdb`) yerel hata tanılamasını desteklemek üzere bulunmaktadır.

---

## 2. Dokümantasyon Denetimi ve Düzeltmeler

Aşağıdaki 5 doküman tutarlılık, terminoloji, dosya adları ve SmartScreen talimatları açısından incelenmiştir:
1. `artifacts/stage17-user-installation-guide.md`
2. `artifacts/stage17-troubleshooting-guide.md`
3. `artifacts/stage19-final-release-gate-report.md`
4. `artifacts/stage19-real-world-validation-plan.md`
5. `artifacts/stage20-real-world-validation-results.md`

### Gerçekleştirilen İyileştirmeler:
- **Karakter Kodlaması ve Okunabilirlik:** `stage17-user-installation-guide.md` ve `stage17-troubleshooting-guide.md` dosyalarındaki UTF-8 çift kodlama (mojibake) sorunları düzeltildi; tüm Türkçe karakterler ve başlıklar temizlendi.
- **Dosya Adı Uyumu:** Kurulum kılavuzundaki taşınabilir arşiv adı `Lyrow-v3.5-win-x64-portable.zip` olarak güncellendi.
- **Veri Yolu Düzeltmesi:** Kullanıcı ayar verilerinin `%LOCALAPPDATA%\Lyrow` yerine doğru Inno Setup `{userappdata}` karşılığı olan `%APPDATA%\Lyrow` klasöründe tutulduğu netleştirildi.
- **Bütünlük Kontrolü:** Kurulum kılavuzuna PowerShell `Get-FileHash` ile SHA-256 doğrulama adımları eklendi.

---

## 3. Manuel Kullanıcı Kabul Testleri Durumu

Kullanıcının kendi fiziksel Windows bilgisayarında uygulayabilmesi için `stage21-user-acceptance-checklist.md` kontrol listesi yayınlanmıştır. Gerçek donanım gerektiren durumlar objektif biçimde korunmuştur:

| Test Senaryosu | Otomasyon / Statik Durum | Fiziksel Son Kullanıcı Durumu |
|---|---|:---:|
| **SmartScreen Gözlemi** | İmzalanmamış (`NotSigned`) doğrulaması `PASS` | Son kullanıcı gözlemi bekleniyor |
| **Apple Music GSMTC Medya Oturumu** | Servis mock testleri `PASS` | `NOT TESTED` (Canlı oturum gerektirir) |
| **Hızlı Şarkı Değişimi & Metadata** | Servis yaşam döngüsü testleri `PASS` | `NOT TESTED` (Canlı oturum gerektirir) |
| **Ambient Glow Gerçek Ses Tepkisi** | Sentetik ses yakalama testleri `PASS` | `NOT TESTED` (Hoparlör/kulaklık sesi gerektirir) |
| **3.5 mm Ses Jakı Hotplug** | WASAPI hata yönetimi testleri `PASS` | `NOT TESTED` (Fiziksel kablo çıkarma gerektirir) |
| **USB Ses Aygıtı Hotplug** | Yeniden bağlanma lojiği `PASS` | `NOT TESTED` (Fiziksel USB aygıtı gerektirir) |
| **Windows Narrator İşitsel Kontrolü** | AutomationProperties testleri `PASS` | `NOT TESTED` (İşitsel dinleme gerektirir) |
| **Sistem Tepsisi & Global Kısayollar** | Hook / Tray birim testleri `PASS` | `NOT TESTED` (Masaüstü etkileşimi gerektirir) |
| **Yeniden Başlatma Kalıcılığı** | Ayar şeması ve yükleme testi `PASS` | Son kullanıcı doğrulaması bekleniyor |

---

## 4. Kalan Teknik ve Kullanıcı Deneyimi Riskleri

1. **Kod İmzalama (Authenticode Signing):**
   - *Durum:* `NOT CONFIGURED`.
   - *Etki:* Windows Defender SmartScreen "Bilinmeyen Yayıncı" uyarısı verir.
   - *Çözüm:* Kullanıcı kılavuzunda "Ek bilgi -> Yine de çalıştır" prosedürü net biçimde dokümante edilmiştir.
2. **Harici Şarkı Sözü Sağlayıcı (LRCLIB):**
   - *Durum:* Canlı API sorgusu test edilmiş ve başarılı bulunmuştur.
   - *Etki:* Servis kesintilerinde şarkı sözü bulunamayabilir.
   - *Çözüm:* Yerel SQLite ve JSON dosya önbelleği (`%APPDATA%\Lyrow\LyricsCache`) önceden çekilen sözleri çevrimdışı kullanım için saklar.

---

## 5. Nihai Dağıtım Kararı (Final Release Decision)

### **READY WITH LIMITATIONS** *(Kısıtlamalarla Dağıtıma Hazır)*

#### Kararın Gerekçesi:
- Lyrow v3.5 dağıtım paketleri (`Lyrow-3.5-Setup-x64.exe` ve `Lyrow-v3.5-win-x64-portable.zip`) eksiksizdir, SHA-256 bütünlükleri doğrulanmıştır.
- Release derlemesi 0 hata ve 0 uyarı ile tamamlanmaktadır. 551 testin tamamı (%100) yeşildir.
- Güvenlik açığı (0 CVE) ve eski marka ismi (0 Legacy Name) kalıntısı bulunmamaktadır.
- Dokümantasyonlar güncellenmiş ve kurulum kılavuzları paketlerle %100 uyumlu hale getirilmiştir.
- Açık kaynak ve GitHub Release dağıtımı için hiçbir teknik engel bulunmamaktadır.

---

## 6. Sürüm ve Tag Bütünlüğü Beyanı

- `v3.5` Tag Object SHA: `2c60b2f2282f24b4b9ada9a91dceee97dc49b940`
- `v3.5` Peeled Commit SHA: `2856ee5a9e21207d69fca160d701c49dab689a8d`
- **Garanti:** v3.5 etiketi ve commit'i hiçbir koşulda değiştirilmemiş, yeni Git tag veya GitHub Release oluşturulmamıştır.
