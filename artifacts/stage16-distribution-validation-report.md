# Lyrow â€” Stage 16: DaÄŸÄ±tÄ±m ve Kurulum DoÄŸrulama Raporu (Distribution Validation Report)

**Tarih:** 2026-11-02
**SÃ¼rÃ¼m:** 3.5 (Peeled commit: `2856ee5a9e21207d69fca160d701c49dab689a8d`)
**Durum:** HazÄ±r / OnaylandÄ± (Authenticode Signing: `NOT CONFIGURED`)

---

## 1. Genel BakÄ±ÅŸ

Lyrow Stage 16 kapsamÄ±nda uygulamanÄ±n paketleme, daÄŸÄ±tÄ±m, yÃ¼kleme, yÃ¼kseltme, kaldÄ±rma ve taÅŸÄ±nabilir (portable) kullanÄ±m dinamikleri denetlenmiÅŸtir. Bu rapor, son kullanÄ±cÄ± ortamÄ±nda karÅŸÄ±laÅŸÄ±labilecek daÄŸÄ±tÄ±m senaryolarÄ±nÄ±, sistem etkilerini ve Windows Defender SmartScreen davranÄ±ÅŸÄ±nÄ± ayrÄ±ntÄ±landÄ±rmaktadÄ±r.

---

## 2. DaÄŸÄ±tÄ±m Paketleri Mimarisi

Lyrow iki ana daÄŸÄ±tÄ±m formatÄ±nda sunulmaktadÄ±r:

| DaÄŸÄ±tÄ±m FormatÄ± | Dosya AdlandÄ±rmasÄ± | Hedef Dizin / Kapsam | Ä°zin Gereksinimi |
| :--- | :--- | :--- | :--- |
| **Inno Setup Installer** | `Lyrow-3.5-Setup-x64.exe` | `{autopf}\Lyrow` (`%LOCALAPPDATA%\Programs\Lyrow`) | Standart KullanÄ±cÄ± (`PrivilegesRequired=lowest`) |
| **Portable ZIP** | `Lyrow-3.5-win-x64.zip` | KullanÄ±cÄ± tarafÄ±ndan seÃ§ilen herhangi bir dizin | SÄ±fÄ±r kurulum / Ä°zin gerektirmez |

---

## 3. Kurulum Denetimi (Installer Deep-Dive)

`installer/setup.iss` konfigÃ¼rasyonu doÄŸrulanmÄ±ÅŸ ve aÅŸaÄŸÄ±daki kritik yetenekler teyit edilmiÅŸtir:

### 3.1. User-Based Kurulum (Non-Admin / Per-User)
- **Ayar:** `PrivilegesRequired=lowest`, `DefaultDirName={autopf}\{#MyAppName}`.
- **DavranÄ±ÅŸ:** YÃ¶netici (UAC) yetki yÃ¼kseltmesi gerektirmez. Standart kullanÄ±cÄ± profilinde varsayÄ±lan olarak `%LOCALAPPDATA%\Programs\Lyrow` dizinine kurulur.
- **AvantajÄ±:** Kurumsal veya kÄ±sÄ±tlÄ± kullanÄ±cÄ± hesaplarÄ±nda UAC engeline takÄ±lmadan yÃ¼kleme yapÄ±labilir.

### 3.2. YÃ¼kseltme ve Ã‡alÄ±ÅŸan SÃ¼reÃ§ YÃ¶netimi (Upgrade Scenario)
- **Ayar:**
  - `AppMutex=Lyrow_SingleInstance_f84b12c9-94b1-4c4f-9e63-47a61d15c89f`
  - `CloseApplications=yes`
  - `CloseApplicationsFilter=*.exe`
  - `RestartApplications=no`
- **DavranÄ±ÅŸ:** GÃ¼ncelleme esnasÄ±nda Lyrow aÃ§Ä±ksa, installer sistem dÃ¼zeyindeki GUID mutex'ini algÄ±lar ve dosyalarÄ±n kilitli kalmamasÄ± iÃ§in kullanÄ±cÄ±nÄ±n uygulamayÄ± kapatmasÄ±nÄ± ister veya otomatik olarak kapatÄ±r.
- **DoÄŸrulama:** YÃ¼kseltme sÄ±rasÄ±nda Ã§akÄ±ÅŸma, dosya kilitlenme veya yarÄ±m kurulum riskleri engellenmiÅŸtir.

### 3.3. KaldÄ±rma ve KullanÄ±cÄ± Verisi YÃ¶netimi (Uninstall Scenario)
- **Pascal Script KontrolÃ¼:** `CurUninstallStepChanged(usUninstall)` olayÄ± `installer/setup.iss` iÃ§inde uygulanmÄ±ÅŸtÄ±r.
- **DavranÄ±ÅŸ:**
  1. Ä°nteraktif modda kullanÄ±cÄ±ya 5 dilde (TR, EN, FR, ES, DE) `KeepSettingsPrompt` mesaj kutusu aÃ§Ä±lÄ±r:
     *â€œKiÅŸiselleÅŸtirilmiÅŸ Lyrow ayarlarÄ±nÄ±zÄ± ve ÅŸarkÄ± sÃ¶zÃ¼ Ã¶nbelleÄŸinizi saklamak ister misiniz?â€*
  2. KullanÄ±cÄ± **Evet** derse ayarlar (`%LOCALAPPDATA%\Lyrow`) korunur.
  3. KullanÄ±cÄ± **HayÄ±r** derse tÃ¼m kullanÄ±cÄ± verileri ve ÅŸarkÄ± sÃ¶zÃ¼ Ã¶nbelleÄŸi temizlenir.
  4. Sessiz/otomasyon senaryolarÄ± iÃ§in `/REMOVEUSERDATA` ve `/KEEPUSERDATA` parametreleri desteklenir.
- **Registry TemizliÄŸi:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` altÄ±ndaki otomatik baÅŸlangÄ±Ã§ anahtarÄ± kaldÄ±rma iÅŸlemiyle otomatik olarak silinir (`Flags: uninsdeletevalue`).

---

## 4. TaÅŸÄ±nabilir (Portable) SÃ¼rÃ¼m Analizi

- **Dosya YapÄ±sÄ±:** `Lyrow-3.5-win-x64.zip` arÅŸivi doÄŸrudan Ã§alÄ±ÅŸtÄ±rÄ±labilir self-contained (.NET 10 & WinUI 3) ikililerini iÃ§erir.
- **Ã‡alÄ±ÅŸma Modu:** Herhangi bir dizine aÃ§Ä±larak `Lyrow.App.exe` Ã¼zerinden Ã§alÄ±ÅŸtÄ±rÄ±labilir.
- **Ä°zolasyon Durumu:**
  - Mevcut mimaride kullanÄ±cÄ± ayarlarÄ± ve Ã¶nbellek `%LOCALAPPDATA%\Lyrow` dizininde saklanmaktadÄ±r (`Environment.SpecialFolder.LocalApplicationData`).
  - Sistem kayÄ±t defterine (registry) zorunlu bir kayÄ±t yazÄ±lmaz (yalnÄ±zca kullanÄ±cÄ± Ayarlar'dan "Windows ile BaÅŸlat"Ä± aktif ederse HKCU Run anahtarÄ±na yazar).
- **Gelecek Ä°yileÅŸtirme Ã–nerisi:** Uygulama kÃ¶k dizininde `portable.lock` veya `data/` dizini tespit edildiÄŸinde ayarlarÄ±n uygulamanÄ±n kendi dizininde saklanmasÄ± (true portable mode) mimari yol haritasÄ±na eklenebilir.

---

## 5. SmartScreen Riski ve KullanÄ±cÄ± Deneyimi KÄ±lavuzu

### 5.1. SmartScreen DavranÄ±ÅŸ Analizi
Authenticode sertifikasÄ± bulunmadÄ±ÄŸÄ± iÃ§in (`NOT CONFIGURED`), yeni derlenen installer ve portable ikilileri Windows Defender SmartScreen tarafÄ±ndan baÅŸlangÄ±Ã§ta bilinmeyen yazÄ±lÄ±m olarak sÄ±nÄ±flandÄ±rÄ±lacaktÄ±r:
- KullanÄ±cÄ± dosyayÄ± aÃ§tÄ±ÄŸÄ±nda **â€œWindows kiÅŸisel bilgisayarÄ±nÄ±zÄ± koruduâ€** (Windows protected your PC) mavi uyarÄ± penceresi gÃ¶rÃ¼ntÃ¼lenecektir.
- Bu durum bir hata veya gÃ¼venlik aÃ§Ä±ÄŸÄ± deÄŸil; imzasÄ±z ve yeni daÄŸÄ±tÄ±lan Ã§alÄ±ÅŸtÄ±rÄ±labilir dosyalar iÃ§in Windows'un varsayÄ±lan itibar (reputation) mekanizmasÄ±dÄ±r.

### 5.2. KullanÄ±cÄ± YÃ¶nergesi (README / Release Notes Ä°Ã§eriÄŸi)
KullanÄ±cÄ±lara sunulacak aÃ§Ä±k yÃ¶nergeler:
1. Ä°ndirme sonrasÄ± uyarÄ± penceresinde **â€œEk bilgiâ€** (More info) baÄŸlantÄ±sÄ±na tÄ±klayÄ±n.
2. AÃ§Ä±lan alanda **â€œYine de Ã§alÄ±ÅŸtÄ±râ€** (Run anyway) butonuna basarak kuruluma devam edin.
3. DosyanÄ±n bÃ¼tÃ¼nlÃ¼ÄŸÃ¼nden emin olmak iÃ§in paylaÅŸÄ±lan `sha256sums.txt` dosyasÄ±ndaki hash deÄŸeri ile indirilen dosyanÄ±n hash deÄŸerini karÅŸÄ±laÅŸtÄ±rÄ±n:
   ```powershell
   Get-FileHash -Algorithm SHA256 .\Lyrow-3.5-Setup-x64.exe
   ```

---

## 6. BÃ¼tÃ¼nlÃ¼k ve SaÄŸlama ToplamÄ± (Checksum) DoÄŸrulamasÄ±

DaÄŸÄ±tÄ±m dosyalarÄ±nÄ±n derleme ve paketleme sonrasÄ±nda deÄŸiÅŸtirilmediÄŸini kanÄ±tlamak iÃ§in SHA-256 Ã¶zet dosyasÄ± oluÅŸturulmaktadÄ±r.

- **Otomasyon Scripti:** `installer/generate_checksums.ps1`
- **KullanÄ±m:**
  ```powershell
  pwsh installer/generate_checksums.ps1
  ```
- **Beklenen Ã‡Ä±ktÄ± BiÃ§imi (`dist/sha256sums.txt`):**
  ```text
  <sha256-hash>  Lyrow-3.5-Setup-x64.exe
  <sha256-hash>  Lyrow-3.5-win-x64.zip
  ```

---

## 7. DaÄŸÄ±tÄ±m DoÄŸrulama Matrisi

| DoÄŸrulama Maddesi | Kriter | GerÃ§ekleÅŸen SonuÃ§ | Durum |
| :--- | :--- | :--- | :--- |
| **Non-Admin Kurulum** | YÃ¶netici haklarÄ± istemeden `%LOCALAPPDATA%` kurulumu | `PrivilegesRequired=lowest` aktif | **BAÅARILI** |
| **Ã‡alÄ±ÅŸan SÃ¼reÃ§ KorumasÄ±** | AÃ§Ä±k uygulamanÄ±n kurulum esnasÄ±nda kilitlenmesini engelleme | Mutex tabanlÄ± algÄ±lama ve kapatma | **BAÅARILI** |
| **KullanÄ±cÄ± Verisi Saklama** | KaldÄ±rma esnasÄ±nda veriyi koruma veya silme seÃ§eneÄŸi | 5 dilde prompt ve parametrik destek | **BAÅARILI** |
| **Registry TemizliÄŸi** | Uninstallation sonrasÄ± HKCU Run kaydÄ±nÄ±n kaldÄ±rÄ±lmasÄ± | `uninsdeletevalue` bayraÄŸÄ± aktif | **BAÅARILI** |
| **SmartScreen RehberliÄŸi** | Ä°tiraz/Ã§alÄ±ÅŸtÄ±rma yÃ¶nergelerinin dokÃ¼mante edilmesi | Release & DaÄŸÄ±tÄ±m raporlarÄ±nda aÃ§Ä±klandÄ± | **BAÅARILI** |
| **Authenticode Ä°mzalama** | Resmi kod imzalama sertifikasÄ± ile imzalama | Sertifika bulunmuyor, yol haritasÄ± hazÄ±r | `NOT CONFIGURED` |
| **DonanÄ±m ve Harici Oturumlar**| Fiziksel ses jakÄ±, USB hotplug, Apple Music GSMTC | CI ortamÄ±nda Ã§alÄ±ÅŸtÄ±rÄ±lamaz, manuel kit hazÄ±r | `NOT TESTED` |
