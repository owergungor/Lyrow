# LYROW — STAGE 36: WINDOWS DISTRIBUTION & INSTALLATION AUDIT

**Denetim Tarihi:** 2026-11-11  
**Hedef Sürüm:** Lyrow v3.7 (`v3.7`)  
**Denetlenen Dağıtım Arşivi:** `Lyrow-v3.7-win-x64.zip`  
**Denetim Kapsamı:** Windows Dağıtım Modeli, Arşiv Bütünlüğü, Kurulum/Kaldırma Deneyimi, Güvenlik Uyarıları ve Dağıtım Alternatifleri  
**Denetim Durumu:** TAMAMLANDI (Salt Denetim ve Raporlama)  

---

## 1. Yönetici Özeti

Lyrow v3.7 sürümünün Windows son kullanıcılarına ulaştırılma süreci; GitHub Release varlıkları, arşiv içerik bütünlüğü, işletim sistemi bağımlılıkları, kurulum, ilk çalıştırma, veri kalıcılığı ve kaldırma senaryoları açısından kapsamlı bir denetimden geçirilmiştir.

### Temel Doğrulanmış Bulgular:
1. **Bağımsız Çalışma Yetkinliği (Self-Contained):** Lyrow v3.7, .NET 10 CLR ve Windows App SDK (WinUI 3) çalışma zamanlarını paket içinde barındıran tam self-contained (`win-x64`) bir mimariye sahiptir. Temiz bir Windows 10/11 ortamında harici bir .NET Runtime veya Windows App SDK Runtime kurulumuna ihtiyaç duyulmamaktadır.
2. **Sağlama Özeti (SHA-256) Doğrulaması:** GitHub Release üzerindeki resmî sağlama özeti olan `601664444F7D983B05E3F675D220E8F25EDB7701520D4E923EFCD8F4E0A85B73` değeri bayt bayt doğrulanmıştır.
3. **Paket Temizliği Açığı (Kritik Bulgu):** Dağıtım ZIP arşivi içerisinde geliştirici/inşa ortamına ait `logs\app.log` (93.9 KB) ve yerel test makinesine ait `settings.json` ile `portable` markörünün paketlendiği tespit edilmiştir. Bu durum, arşivin doğrudan taşınabilir modda çalışmasına ve ilk kurulum ekranlarının atlanmasına yol açmaktadır.
4. **Dijital İmza Eksikliği ve SmartScreen Bariyeri:** Yürütülebilir dosyaların (`Lyrow.App.exe`) Authenticode dijital sertifikası ile imzalanmamış olması (`NotSigned`) ve internetten indirme kaynaklı Mark-of-the-Web (MOTW) özniteliği nedeniyle, Windows Defender SmartScreen mavi güvenlik engelini tetiklemektedir.
5. **Kök Dizin Kirliliği (Root Pollution):** Arşiv içeriği kök seviyede bir ana klasör içermeksizin düz (flat) biçimde 542 dosya ve 80+ alt klasör barındırmaktadır. "Buraya çıkart" diyen kullanıcının masaüstü veya indirme klasörü kirlenmektedir.

---

## 2. Release Arşivi İncelemesi (v3.7 Dağıtımı)

### 2.1. Paket Özellikleri ve Bütünlük

| Parametre | Doğrulanan Değer | Notlar |
|---|---|---|
| **Dosya Adı** | `Lyrow-v3.7-win-x64.zip` | GitHub Release v3.7 ana dağıtım varlığı |
| **Sıkıştırılmış Boyut** | 97,863,708 bayt (~93.33 MB) | Standart ZIP sıkıştırması |
| **Açılmış Boyut** | ~278 MB | Tam açılmış disk kaplama alanı |
| **Toplam Girdi Sayısı** | 542 dosya / dizin | Arşiv kökünde düz yerleşim |
| **SHA-256 Özeti** | `601664444F7D983B05E3F675D220E8F25EDB7701520D4E923EFCD8F4E0A85B73` | GitHub Release & README ile %100 uyumlu (PASS) |
| **Arşiv Bütünlüğü** | Tam / Bozulma Yok | ZIP başlıkları ve CRC testleri başarılı |

### 2.2. Yürütülebilir Dosyalar ve Bağımlılıklar

- **Ana Yürütülebilir:** `Lyrow.App.exe` (675,328 bayt) arşiv kök dizininde mevcuttur.
- **Uygulama Modülleri:** `Lyrow.App.dll`, `Lyrow.Core.dll`, `Lyrow.Music.dll`, `Lyrow.Audio.dll` eksiksiz yer almaktadır.
- **Çalışma Zamanı Çekirdeği:** `coreclr.dll`, `clrjit.dll`, `System.Private.CoreLib.dll` ve ilgili .NET 10 kütüphaneleri mevcuttur.
- **WinUI 3 / Windows App SDK:** `Microsoft.WindowsAppRuntime.Bootstrap.dll`, `Microsoft.WindowsAppRuntime.dll`, `Microsoft.ui.xaml.dll`, `DWriteCore.dll`, `resources.pri`, `Lyrow.App.pri` eksiksiz bulunmaktadır.
- **Web ve Medya Bileşenleri:** `WebView2Loader.dll` ve yerel varlıklar (`Assets\lyrow.ico`) mevcuttur.

### 2.3. .NET Runtime ve Windows App SDK Gereksinimleri

Proje yapılandırması (`src/Lyrow.App/Lyrow.App.csproj`) incelendiğinde:
```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
```
Bu konfigürasyon sayesinde:
- **.NET 10 Runtime İhtiyacı: YOK.** Çalışma zamanı derleme çıktısına gömülüdür (Self-Contained). Kullanıcının işletim sistemine .NET SDK veya .NET Desktop Runtime kurması gerekmez.
- **Windows App SDK Runtime İhtiyacı: YOK.** `WindowsAppSDKSelfContained=true` ayarı ile WinUI 3 bileşenleri ve bootstrap ikilileri doğrudan uygulama dizinindedir. Harici `WindowsAppRuntimeInstall.exe` çalıştırılmasına gerek yoktur.

### 2.4. Temiz Windows Ortamında Çalışma Koşulları

Uygulamanın sıfır/temiz bir Windows kurulumunda hatasız çalışması için gereken sistem koşulları doğrulanmıştır:
1. **İşletim Sistemi:** Windows 10 Sürüm 1809 (Build 17763) veya üzeri, Windows 11 (Önerilen).
2. **Mimari:** 64-bit x64 (AMD64 / Intel 64-bit).
3. **Grafik ve Render Desteği:** WinUI 3 / DWriteCore render motoru için DirectX 11 uyumlu GPU sürücüsü.
4. **Ses Donanımı:** Windows CoreAudio / WASAPI uç noktası (akış yakalama ve ortam aydınlatması için aktif bir ses aygıtı).
5. **Medya Kontrol Servisi:** Windows Global System Media Transport Controls (GSMTC) servisinin aktif olması (Windows varsayılanında açıktır).
6. **Web Tarayıcısı:** Apple Music Web oynatma/seek kontrolleri için varsayılan bir modern tarayıcı (Microsoft Edge, Google Chrome vb.).

### 2.5. Gereksiz ve Riskli Dosyalar İncelemesi

Arşiv analizi sonucunda dağıtımda bulunmaması gereken unsurlar tespit edilmiştir:

1. **İnşa ve Smoke-Test Günlüğü (`logs\app.log` - 93.9 KB):**
   - 8 Kasım 2026 tarihli derleme makinesinde gerçekleştirilen `--smoke-test` çalıştırmalarının çıktısını içermektedir.
   - Dosya içerisinde yerel geliştirici dosya yolları (`C:\Users\***\Projects\Lyrow\publish\win-x64...`) ve test sırasında yakalanmış istisna yığın izleri (stack trace) bulunmaktadır.
   - Dağıtım paketlerine çalışma zamanı log dizinleri asla dahil edilmemelidir.
2. **Önceden Doldurulmuş Ayar Dosyası (`settings.json` - 1.7 KB):**
   - İnşa ortamına ait ses aygıtı GUID'i (`PreferredDeviceId: "{0.0.0.00000000}.{5a68be4b-d591-4dcb-8f08-113f680422af}"`), pencere koordinatları (`WindowX: 342, WindowY: 380`) ve `HasCompletedFirstRun: true` değerlerini içermektedir.
   - Bu durum, yeni kullanıcının temiz ilk çalıştırma (first-run) deneyimini bozmaktadır.
3. **Taşınabilir Mod Markörü (`portable` - 0 bayt):**
   - Arşiv kökünde yer alması nedeniyle, uygulama varsayılan olarak kullanıcı profili (`%APPDATA%`) yerine kendi çalışma dizinine yazmaya zorlanmaktadır.
4. **Hata Ayıklama Sembolleri (`*.pdb` - 4 adet, ~1.7 MB):**
   - `Lyrow.App.pdb` (1.4 MB), `Lyrow.Audio.pdb` (70 KB), `Lyrow.Core.pdb` (80 KB), `Lyrow.Music.pdb` (200 KB).
   - Üretim dağıtımında paket boyutunu artırır; semboller ayrı bir paket (symbols package) olarak sunulmalıdır.
5. **80+ Uydu Dil Klasörü (Satellite Assemblies):**
   - Windows App SDK tarafından otomatik üretilen onlarca dil klasörü (`af-ZA`, `ca-ES`, `fil-PH`, `zu-ZA` vb.) kök dizini aşırı kalabalıklaştırmaktadır.

---

## 3. Kullanıcı Kurulum Deneyimi Analizi

### 3.1. GitHub Release'den ZIP İndirme
- Kullanıcı GitHub Release sayfasından `Lyrow-v3.7-win-x64.zip` dosyasını indirir (~93.3 MB).
- İndirme tamamlandığında Windows, dosya sistemine bir NTFS Alternate Data Stream (ADS) ekleyerek dosyayı **Mark-of-the-Web (MOTW)** ile işaretler (`ZoneId=3 - Internet`).

### 3.2. ZIP Arşivini Çıkarma
- **Kök Klasörsüz Arşiv Sorunu:** Arşiv bir üst kapsayıcı klasör (örneğin `Lyrow\`) içermemektedir. Kullanıcı Windows Gezgini'nde "Buraya Ayıkla" (Extract Here) seçeneğini kullandığında, 542 dosya ve 80'den fazla klasör doğrudan kullanıcının Masaüstüne veya İndirilenler klasörüne saçılmaktadır.
- **MOTW Yayılımı:** Windows Gezgini ile arşiv çıkarıldığında, çıkarılan tüm `.exe` ve `.dll` dosyaları internet kaynağı etiketini devralır.

### 3.3. Lyrow.App.exe Çalıştırma ve Windows SmartScreen Engeli
- **Dijital Sertifika Durumu:** `Get-AuthenticodeSignature` incelemesinde `Lyrow.App.exe` dosyasının imzasız (`NotSigned`) olduğu doğrulanmıştır.
- **SmartScreen Uyarısı:** İmzalanmamış ve internetten indirilmiş bir ikili çalıştırıldığında, Microsoft Defender SmartScreen tam ekran mavi uyarı penceresi açar:
  > *"Windows kişisel bilgisayarınızı korudu / Microsoft Defender SmartScreen tanınmayan bir uygulamanın başlatılmasını engelledi."*
- **Kullanıcı Reaksiyonu:** Kullanıcı "Ek bilgi" (More info) linkine tıklayıp ardından "Yine de çalıştır" (Run anyway) butonuna basmak zorundadır. Bu durum teknik olmayan kullanıcılarda güvensizlik yaratmakta ve terk oranını artırmaktadır.

### 3.4. Başlatma Hataları ve İzin Kısıtları (Program Files Riski)
- Arşivde `portable` dosyası ve `settings.json` bulunduğu için uygulama taşınabilir modda başlatılır.
- Eğer kullanıcı arşivi yönetici ayrıcalığı gerektiren bir dizine (`C:\Program Files\Lyrow\` gibi) açarsa ve uygulamayı standart kullanıcı olarak çalıştırırsa:
  - Uygulama `settings.json` ve `logs\app.log` dosyalarına yazmaya çalışırken `UnauthorizedAccessException` hatası alır.
  - Bu durum uygulamanın çökmesine veya ayarların hiçbir zaman kaydedilememesine neden olur.

### 3.5. Uygulamayı Kaldırma (Uninstall) Süreci
- Taşınabilir ZIP formatında dağıtıldığı için Windows "Yüklü Uygulamalar" (Installed Apps) veya `appwiz.cpl` listesinde Lyrow yer almaz.
- Kaldırma işlemi kullanıcının çıkarılan klasörü manuel olarak bulup çöp kutusuna atmasına dayanır.
- Eğer kullanıcı `portable` dosyasını silmiş veya standart moda geçmişse, `%APPDATA%\Lyrow` dizininde biriken ayarlar, loglar ve şarkı sözü önbellekleri sistemde yetim (orphan) veri olarak kalır.

### 3.6. Kullanıcı Ayarları ve Verilerinin Saklanması (Güncelleme Tehlikesi)
- `SettingsManager.cs` kod yapısı incelendiğinde:
  - `portable` veya `settings.json` uygulama kök dizinindeyse -> `<BaseDir>\settings.json`
  - Aksi halde -> `%APPDATA%\Lyrow\settings.json`
- **Sürüm Güncelleme Riski:** Kullanıcı v3.7'den sonra çıkacak bir v3.8 ZIP arşivini mevcut klasörünün üzerine açtığında, yeni ZIP içindeki varsayılan `settings.json` kullanıcının mevcut kişiselleştirilmiş ayarlarını ve pencere konumlarını ezecektir (overwrite).

---

## 4. Dağıtım Alternatiflerinin Karşılaştırması

Windows ekosisteminde Lyrow için değerlendirilebilecek 4 dağıtım yöntemi karşılaştırılmıştır:

| Değerlendirme Kriteri | 1. Portable ZIP (Mevcut) | 2. Setup.exe (Inno Setup) | 3. MSI (WiX Toolset) | 4. MSIX / Windows Store |
|---|---|---|---|---|
| **Kullanıcı Deneyimi (UX)** | **Düşük / Orta** (Manuel çıkarma, kısayol yok, kök kirliliği) | **Çok Yüksek** (Modern sihirbaz, masaüstü/başlat menüsü simgeleri, açılış seçeneği) | **Yüksek** (Standart kurumsal kurulum adımları) | **En Yüksek** (Tek tıkla modern yükleme, sıfır kalıntı) |
| **Yönetici İzni (UAC / Admin)** | Çıkarılan yere bağlı (Kullanıcı klasöründe gerekmez) | **Gerekmez** (`PrivilegesRequired=lowest` ile kullanıcı profiline kurulabilir) | Genellikle yönetici izni ister (Per-machine) | **Gerekmez** (Windows AppModel per-user sandbox) |
| **Windows Güvenlik Uyarıları** | İmzasızsa **SmartScreen engeli** tam devrede | İmzasızsa SmartScreen uyarısı; İmzalıysa hızla itibar kazanır | İmzasızsa SmartScreen; İmzalıysa sorunsuz | Store üzerinden **sıfır uyarı**; Yan yüklemede güvenilir CA gerekir |
| **Güncelleme Kolaylığı** | **Zor** (Manuel indirme, ayar ezilme riski) | **Kolay** (Yeni Setup çalıştırılır, ayarlar korunur) | **Orta / İyi** (MajorUpgrade ile versiyon geçişi) | **Mükemmel** (Arka planda otomatik sessiz güncelleme) |
| **Kaldırma (Clean Uninstall)** | **Yok** (Manuel silme, geride yetim veri riski) | **Mükemmel** (`unins000.exe`, kullanıcı ayarlarını koruma/silme seçeneği) | **Mükemmel** (Windows standart kaldırma) | **Mükemmel** (İşletim sistemi tüm paket verisini temizler) |
| **Dağıtım Karmaşıklığı** | **En Düşük** (Tek ZIP çıktısı) | **Düşük** (Inno Setup betiği `setup.iss` depoda hazır) | **Orta / Yüksek** (WiX XML ve 540+ dosya yönetimi) | **Yüksek** (Unpackaged WinUI 3 için manifest ve bildirim karmaşıklığı) |
| **Bakım & Geliştirme Maliyeti**| **Sıfır** (Fakat kullanıcı destek maliyeti yüksek) | **Çok Düşük** (Mevcut `installer/setup.iss` zaten mevcut ve test edilmiş) | **Orta** (WiX derleme zinciri gerektirir) | **Yüksek** (Store hesabı, sertifika yenileme, packaging pipeline) |

---

## 5. Önerilen Dağıtım Yöntemi

Lyrow için en optimum kullanıcı deneyimi, güvenlik ve operasyonel verimlilik sağlayan strateji **İkili Dağıtım Modeli (Dual Distribution Model)** yaklaşımıdır:

### Birincil Tavsiye: Modern Setup.exe (Inno Setup Tabanlı)
- **Neden?** Depoda zaten hazır ve test edilmiş olan `installer/setup.iss` betiği bulunmaktadır.
- **Kullanıcı Modu:** `PrivilegesRequired=lowest` ayarı ile UAC şifresi sormadan `{autopf}` veya `{localappdata}\Programs\Lyrow` dizinine kurulur.
- **Kısayollar:** Başlat Menüsü ve Masaüstü kısayollarını otomatik oluşturur.
- **Ayar Güvenliği:** Kullanıcı ayarlarını `%APPDATA%\Lyrow` dizininde izole tutar; güncellemelerde ayarlar ezilmez.
- **Temiz Kaldırma:** Program Ekle/Kaldır entegrasyonu ve kaldırma sırasında kullanıcıya ayarlarını saklamak isteyip istemediğini soran çok dilli diyalog sunar.

### İkincil Destek: Temizlenmiş Portable ZIP
- Kurulum yapmak istemeyen, USB bellekten çalıştıran veya kısıtlı sistemlerdeki kullanıcılar için taşınabilir arşiv seçeneği sunulmaya devam edilmelidir.
- Ancak arşiv kökünde tek bir `Lyrow\` klasörü bulunmalı ve inşa günlükleri (`logs\app.log`) ile test ayarları paketten kesinlikle çıkarılmalıdır.

---

## 6. Önceliklendirilmiş İyileştirme Listesi (Backlog)

Gelecek aşamalar için doğrulanmış ve önceliklendirilmiş aksiyon planı:

| Öncelik | Kategori | İyileştirme Maddesi | Beklenen Fayda |
|:---:|---|---|---|
| **P0** | **Paket Temizliği** | Dağıtım ZIP'inden `logs\app.log` dosyasının çıkarılması. | Gizlilik ve paket hijyeni; yerel dosya yollarının ve hata izlerinin sızmasını engeller. |
| **P0** | **Paket Temizliği** | Dağıtım ZIP'inden `settings.json` ve `portable` dosyalarının kaldırılması (veya temiz şablon konulması). | İlk çalıştırma (First-run) sihirbazının yeni kullanıcıda doğru çalışması ve donanım GUID çakışmasının önlenmesi. |
| **P0** | **Arşiv Yapısı** | ZIP içeriğinin `Lyrow-v3.7\` ana klasörü içerisine alınması. | "Buraya ayıkla" yapıldığında kullanıcının masaüstünün veya klasörünün 540 dosyayla kirlenmesini önler. |
| **P1** | **Kurulumcu Entegrasyonu**| GitHub Release'e resmî `Lyrow-3.7-Setup-x64.exe` kurulum paketinin eklenmesi. | Standart Windows kurulum deneyimi, Başlat Menüsü simgesi ve sorunsuz sürüm yükseltme. |
| **P1** | **Sembol Ayrıştırma** | `*.pdb` dosyalarının ana paketten çıkarılıp bağımsız bir `Lyrow-v3.7-Symbols.zip` haline getirilmesi. | Dağıtım paketi boyutunu düşürür, gereksiz dosya kalabalığını önler. |
| **P2** | **Kod İmzalama** | İkili dosyaların ve kurulumcunun geçerli bir Authenticode sertifikası ile imzalanması. | Windows Defender SmartScreen engelini tamamen ortadan kaldırır. |
| **P2** | **Otomatik Güncelleme** | Uygulama içi GitHub Releases tabanlı güncelleme denetleyicisinin kullanıcıya kurulum paketini indirme bildirimi sunması. | Kullanıcıların güncel ve hatasız sürümlere zahmetsizce geçmesini sağlar. |

---

## 7. Test ve Doğrulama Sonuçları Özeti

- [x] **Release Varlığı İncelemesi:** `Lyrow-v3.7-win-x64.zip` indirildi, SHA-256 doğrulandı (`601664444F7D983B05E3F675D220E8F25EDB7701520D4E923EFCD8F4E0A85B73`).
- [x] **İkili Bütünlük:** `Lyrow.App.exe` ve bağımlı WinUI 3 / .NET 10 ikilileri eksiksiz mevcut.
- [x] **Sertifika Denetimi:** `Lyrow.App.exe` dosyası `NotSigned` durumundadır; SmartScreen uyarısı doğrulanmıştır.
- [x] **Paket İçi Sızıntı Denetimi:** `logs\app.log` ve `settings.json` varlığı tespit edilip raporlanmıştır.
- [x] **Gereksinimler:** Temiz Windows 10/11 x64 için ek .NET / WinAppSDK kurulumu gerektirmediği doğrulanmıştır.
- [x] **Koruma Kuralları:** Uygulama kaynak koduna, ürün sürümlerine, mevcut tag/release'lere dokunulmamış; salt denetim ve belgeleme gerçekleştirilmiştir.
