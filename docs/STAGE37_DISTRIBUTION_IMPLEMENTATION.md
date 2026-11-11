# LYROW — STAGE 37: CLEAN DISTRIBUTION PACKAGING & WINDOWS INSTALLER

**Tarih:** 2026-11-11  
**Hedef Sürüm:** Lyrow v3.7  
**Uygulama Modeli:** Temiz Portable ZIP & Inno Setup Kullanıcı Seviyesi Kurulum Paketi  
**Durum:** TAMAMLANDI / BAŞARILI  

---

## 1. Değiştirilen ve Eklenen Dosyalar

| Dosya Yolu | Durum | Açıklama |
|---|:---:|---|
| `src/Lyrow.Core/Services/SettingsManager.cs` | Değiştirildi | `IsDirectoryWritable` kontrolü eklendi; yazma yetkisi olmayan konumlarda `%APPDATA%\Lyrow\settings.json` dizinine güvenli fallback eklendi. |
| `src/Lyrow.Core/Services/LocalDiagnosticLogger.cs` | Değiştirildi | Taşınabilir mod tespiti dizinin yazılabilirliği ile ilişkilendirildi; kısıtlı ortamlarda çökme önlendi. |
| `installer/setup.iss` | Değiştirildi | `OutputBaseFilename=Lyrow-Setup-x64` yapıldı; PDB, log ve ayar dosyalarını hariç tutan `Excludes` filtresi eklendi; Start Menüsü `{autoprograms}` kısayolu tanımlandı. |
| `installer/build_packages.ps1` | Eklendi | Hem Portable ZIP hem de Setup EXE paketlerini aynı Release çıktısından üreten, doğrulama yapan ve SHA-256 hesaplayan deterministik paketleme scripti. |
| `installer/test_distribution_scenarios.ps1` | Eklendi | 10 dağıtım senaryosunu (temiz paket, zip içeriği, log temizliği, setup derleme, kısayol, ayar koruma, yükseltme, checksum) test eden otomasyon betiği. |
| `tests/Lyrow.Core.Tests/LyrowStage37SettingsAndDistributionTests.cs` | Eklendi | Yazma izni, ayar yolu çözümleme ve temiz varsayılan ayar kontrollerini doğrulayan birim testleri. |
| `tests/Lyrow.App.Tests/LyrowStage37DistributionVerificationTests.cs` | Eklendi | `setup.iss` kuralları, paketleme scripti ve ZIP paket yapısı doğrulama testleri. |
| `docs/STAGE36_DISTRIBUTION_AUDIT.md` | Eklendi | Stage 36 denetim raporu. |
| `docs/STAGE37_DISTRIBUTION_IMPLEMENTATION.md` | Eklendi | Bu uygulama ve doğrulama raporu. |

---

## 2. Üretilen Paketlerin Özellikleri ve Yerel Dosya Yolları

Tüm paketler yerel derleme çıktısı olarak `dist/` klasöründe üretilmiştir (GitHub Release'e yüklenmemiştir):

### 2.1. Temiz Taşınabilir Arşiv (Portable ZIP)
- **Yerel Dosya Yolu:** `C:\Users\omer\Projects\Lyrow\dist\Lyrow-v3.7-win-x64-portable.zip`
- **Dosya Boyutu:** 97,725,123 bayt (~93.19 MB)
- **SHA-256 Özeti:** `D99C4C9F87C823AB20DDF4EBB57CB07F5174012EDC1CC4C4643E05ED60B4FD8B`
- **İçerik ve Hijyen Özellikleri:**
  - **Tek Üst Klasör (Enclosed Folder):** Tüm dosyalar `Lyrow-v3.7/` kök klasörü içinde yer alır. Kullanıcı "Buraya çıkart" dediğinde masaüstü kirlenmez.
  - **Sıfır Log Dosyası:** Geliştirme/test günlükleri (`logs/app.log`) tamamen temizlenmiştir (0 adet).
  - **Sıfır Önceden Tanımlı Ayar:** Geliştirici ortamının `settings.json` dosyası paketten çıkarılmıştır. İlk çalıştırmada temiz kullanıcı ayarları oluşturulur.
  - **Sıfır PDB Dosyası:** Üretim paketinde gereksiz sembol dosyaları (`*.pdb`) yer almaz.
  - **`portable` Markörü:** Uygulama kökünde 0-baytlık temiz `portable` markörü bulunur; eğer dizin yazılabilir ise ayarlar yerel klasörde saklanır.

### 2.2. Inno Setup Windows Kurulumcusu (Setup EXE)
- **Yerel Dosya Yolu:** `C:\Users\omer\Projects\Lyrow\dist\Lyrow-Setup-x64.exe`
- **Dosya Boyutu:** 67,737,600 bayt (~64.6 MB)
- **SHA-256 Özeti:** `CD1E5714452AB70A6FCFD0C6BEE34AB0AA677D96E634FFBED5947C9FF5C44288`
- **Kurulumcu Özellikleri:**
  - **Kullanıcı Seviyesi Yetki:** `PrivilegesRequired=lowest` ile yönetici (UAC) parolası gerektirmeden `{autopf}\Lyrow` (`%LOCALAPPDATA%\Programs\Lyrow`) dizinine kurulur.
  - **Kısayollar:** Başlat Menüsü program listesinde (`{autoprograms}`) ve isteğe bağlı Masaüstünde simge oluşturur.
  - **Ayar Koruması:** Kurulum sırasında kullanıcının `%APPDATA%\Lyrow\settings.json` dosyasını kesinlikle ezmez.
  - **Yükseltme (Upgrade):** Mevcut kurulumun üzerine yeni sürüm kurulduğunda çalışan uygulama kilitlerini (`AppMutex`) denetler ve dosyaları temiz biçimde günceller.
  - **Kaldırma (Uninstall):** Windows Program Ekle/Kaldır entegrasyonu sunar; kaldırma sırasında kullanıcı ayarlarını varsayılan olarak saklar (kullanıcı isterse ayarlarını silebilir).

---

## 3. Test ve Doğrulama Sonuçları (10 Senaryo)

`installer/test_distribution_scenarios.ps1` betiği ve tüm birim testleri koşturularak 10 senaryo doğrulanmıştır:

| No | Test Senaryosu | Sonuç | Açıklama / Doğrulama Yöntemi |
|:---:|---|:---:|---|
| **1** | Temiz portable paket üretimi | **PASS** | `Lyrow-v3.7-win-x64-portable.zip` (93.19 MB) başarıyla üretildi. |
| **2** | ZIP içeriğinin doğrulanması | **PASS** | 536 girdi incelendi; tüm girişlerin `Lyrow-v3.7/` altında olduğu ve `Lyrow.App.exe` içerdiği doğrulandı. |
| **3** | Paket içerisinde geliştirici loglarının bulunmaması | **PASS** | `app.log` ve `logs/` araması yapıldı; 0 adet bulundu. |
| **4** | Paket içerisinde geliştirici ayarlarının bulunmaması | **PASS** | `settings.json` ve `*.pdb` dosyalarının pakette yer almadığı doğrulandı. |
| **5** | Portable marker davranışı | **PASS** | `Lyrow-v3.7/portable` mevcudiyeti ve `SettingsManager` yazma izni fallback mekanizması doğrulandı. |
| **6** | Kullanıcı seviyesinde Setup EXE derlenmesi | **PASS** | `ISCC.exe` ile `Lyrow-Setup-x64.exe` (64.6 MB) başarıyla derlendi. |
| **7** | Başlat Menüsü kısayolu | **PASS** | Sessiz kurulum ile izole dizine kuruldu; `{autoprograms}` ve `{group}` tanımları ile `unins000.exe` doğrulandı. |
| **8** | Uygulama kaldırılırken kullanıcı ayarlarının korunması | **PASS** | Kullanıcı ayarları simüle edildi; `/KEEPUSERDATA` ile uygulama kaldırıldığında ayarlar korundu. |
| **9** | Mevcut kurulum üzerine güncelleme | **PASS** | Mevcut kurulum dizini üzerine sessiz yükseltme hatasız tamamlandı. |
| **10** | SHA-256 doğrulaması | **PASS** | `installer/verify_checksums.ps1` ile 39 dağıtım dosyasının tamamı doğrulandı (39/39 OK). |

---

## 4. Çözüm Testleri ve Derleme Özeti

| Test Projesi | Toplam Test | Başarılı | Başarısız | Atlanan | Durum |
|---|:---:|:---:|:---:|:---:|:---:|
| `Lyrow.Core.Tests` | 63 | 63 | 0 | 0 | **PASS** |
| `Lyrow.Audio.Tests` | 53 | 53 | 0 | 0 | **PASS** |
| `Lyrow.Music.Tests` | 120 | 120 | 0 | 0 | **PASS** |
| `Lyrow.App.Tests` | 406 | 406 | 0 | 0 | **PASS** |
| **GENEL TOPLAM** | **642** | **642** | **0** | **0** | **PASS (%100)** |

---

## 5. Sürüm ve Etiket Bütünlüğü

- **v3.5 Tag/Release Commit:** `2c60b2f2282f24b4b9ada9a91dceee97dc49b940` (Değiştirilmedi / Korundu)
- **v3.6 Tag/Release Commit:** `b089c3edc990818507a56b3ca286d31c153ce247` (Değiştirilmedi / Korundu)
- **v3.7 Tag/Release Commit:** `da855057f38cb309ac7e0a98dc9a51008915b46d` (Değiştirilmedi / Korundu)
- **GitHub Release Asset Koruması:** Mevcut `Lyrow-v3.7-win-x64.zip` dosyasına dokunulmamış, yeni paketler yalnızca yerel `dist/` klasöründe üretilmiştir.
- **Dijital İmza Notu:** Kod imzalama sertifikası bulunmadığından paketler imzasız olarak üretilmiş, gerçeğe aykırı imza beyanında bulunulmamıştır.
