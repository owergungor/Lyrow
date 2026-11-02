# LYROW â€” STAGE 16: SÃœRÃœM Ä°MZALAMA VE AUTHENTICODE HAZIRLIK RAPORU

## 1. YÃ¶netici Ã–zeti ve Mevcut Durum

| Alan | Durum | AÃ§Ä±klama |
| :--- | :--- | :--- |
| **Authenticode Ä°mzalama AltyapÄ±sÄ±** | **`NOT CONFIGURED`** | Depo iÃ§erisinde herhangi bir dijital imzalama sertifikasÄ± (.pfx / .cer) veya Ã¶zel anahtar bulunmamaktadÄ±r ve gÃ¼venlik gereÄŸi bulunmamalÄ±dÄ±r. |
| **YÃ¼kleyici (`Setup.exe`) Ä°mzasÄ±** | **`NOT CONFIGURED`** | Derlenen kurulum dosyasÄ± ÅŸu an imzasÄ±z (unsigned) olarak Ã¼retilmektedir. |
| **Uygulama Ä°kili DosyalarÄ± (`.exe`/`.dll`)** | **`NOT CONFIGURED`** | Win-x64 ikili dosyalarÄ± imzasÄ±zdÄ±r. |
| **SHA-256 Checksum DoÄŸrulamasÄ±** | **`CONFIGURED`** | Dosya bÃ¼tÃ¼nlÃ¼ÄŸÃ¼ iÃ§in SHA-256 saÄŸlama toplamÄ± betiÄŸi ve doÄŸrulama altyapÄ±sÄ± hazÄ±rdÄ±r. |
| **Windows SmartScreen Durumu** | **`AWARENESS / RISK`** | Ä°mzasÄ±z PE ikili dosyalarÄ± ilk indirmelerde Microsoft Defender SmartScreen tarafÄ±ndan "Bilinmeyen YayÄ±ncÄ±" uyarÄ±sÄ± tetikler. |

> [!CAUTION]
> GÃ¼venlik En Ä°yi UygulamalarÄ± (Security Best Practices) uyarÄ±nca, hiÃ§bir zaman Ã¶zel imzalama sertifikalarÄ± (`.pfx`), ÅŸifreleri veya API anahtarlarÄ± Git reposuna commit edilmemelidir. Ä°mzalama iÅŸlemi donanÄ±m HSM belirteÃ§leri (YubiKey vb.) veya bulut tabanlÄ± anahtar kasalarÄ± (Azure Key Vault / Azure Trusted Signing) Ã¼zerinden izole CI ortamÄ±nda gerÃ§ekleÅŸtirilmelidir.

---

## 2. Windows SmartScreen Risk Analizi

1. **Ä°mzasÄ±z YazÄ±lÄ±m Riski:**
   - Windows 10 ve 11 iÅŸletim sistemleri, internetten indirilen ve geÃ§erli bir Authenticode imzasÄ±na sahip olmayan Ã§alÄ±ÅŸtÄ±rÄ±labilir dosyalar (`.exe`) iÃ§in SmartScreen "KorumalÄ± Bilgisayar" uyarÄ±sÄ± gÃ¶sterir.
   - KullanÄ±cÄ±larÄ±n Ã§alÄ±ÅŸtÄ±rmak iÃ§in "Daha fazla bilgi" -> "Yine de Ã§alÄ±ÅŸtÄ±r" butonuna tÄ±klamasÄ± gerekir.
2. **Ä°tibar Edinme (Reputation Building):**
   - **Standart Kod Ä°mzalama SertifikasÄ± (Standard Code Signing):** Sertifika ilk alÄ±ndÄ±ÄŸÄ±nda hemen SmartScreen itibarÄ±na sahip olmaz; zamanla indirilip temiz Ã§alÄ±ÅŸtÄ±kÃ§a itibar kazanÄ±r.
   - **GeniÅŸletilmiÅŸ DoÄŸrulama (EV - Extended Validation) veya Azure Trusted Signing:** DoÄŸrulandÄ±ÄŸÄ± ilk gÃ¼nden itibaren SmartScreen uyarÄ±sÄ± vermeden gÃ¼venli baÅŸlatma saÄŸlar.

---

## 3. GÃ¼venli CI/CD Ä°mzalama Mimarisi (Production Roadmap)

Lyrow'un GitHub Actions CI akÄ±ÅŸÄ±na entegre edilebilecek iki endÃ¼stri standardÄ± yÃ¶ntem bulunmaktadÄ±r:

### YÃ¶ntem A: Azure Trusted Signing (Ã–nerilen Microsoft Ã‡Ã¶zÃ¼mÃ¼)
Microsoft'un Windows App SDK ve modern masaÃ¼stÃ¼ uygulamalarÄ± iÃ§in sunduÄŸu HSM gerektirmeyen, kimlik doÄŸrulamalÄ± bulut imzalama servisidir.

1. **Gereksinimler:**
   - Azure Subscription & Trusted Signing Account.
   - GitHub Actions Secret olarak tanÄ±mlanacak `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_SECRET`.
2. **GitHub Actions Workflow AdÄ±mÄ±:**
   ```yaml
   - name: Sign Binaries with Azure Trusted Signing
     uses: azure/trusted-signing-action@v0.4.1
     with:
       azure-authenticator-type: client-secret
       azure-client-id: ${{ secrets.AZURE_CLIENT_ID }}
       azure-client-secret: ${{ secrets.AZURE_CLIENT_SECRET }}
       azure-tenant-id: ${{ secrets.AZURE_TENANT_ID }}
       endpoint: https://eus.codesigning.azure.net/
       trusted-signing-account-name: lyrow-signing-account
       certificate-profile-name: lyrow-production-profile
       files: |
         publish/win-x64/Lyrow.App.exe
         dist/Lyrow-*-Setup-x64.exe
       timestamp-rfc3161: http://timestamp.digicert.com
       timestamp-digest: SHA256
   ```

### YÃ¶ntem B: GitHub Secrets ile Base64 PFX ve SignTool
Geleneksel PFX sertifikasÄ± kullanÄ±ldÄ±ÄŸÄ±nda:

1. **GitHub Secrets:**
   - `WINDOWS_CERTIFICATE_BASE64`: SertifikanÄ±n Base64 dizesi.
   - `WINDOWS_CERTIFICATE_PASSWORD`: Sertifika parolasÄ±.
2. **SignTool Komutu:**
   ```powershell
   & "C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe" sign /f cert.pfx /p ${{ secrets.WINDOWS_CERTIFICATE_PASSWORD }} /tr http://timestamp.digicert.com /td sha256 /fd sha256 dist/Lyrow-Setup-x64.exe
   ```

---

## 4. SHA-256 Checksum Ãœretme ve DoÄŸrulama ProsedÃ¼rÃ¼

Ä°mza bulunmadÄ±ÄŸÄ± durumlarda veya ek gÃ¼venlik katmanÄ± olarak indirme paketlerinin bÃ¼tÃ¼nlÃ¼ÄŸÃ¼ SHA-256 hash listesi ile garanti edilir.

### A. Otomatik Checksum Ãœretimi (PowerShell)
```powershell
# dist klasÃ¶rÃ¼ndeki paketlerin SHA-256 hash listesini Ã¼retir
Get-ChildItem -Path "dist\*" -Include *.exe, *.zip | ForEach-Object {
    $hash = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLower()
    "$hash  $($_.Name)"
} | Out-File -FilePath "dist\sha256sums.txt" -Encoding ascii
```

### B. KullanÄ±cÄ± DoÄŸrulama AdÄ±mlarÄ± (Windows Terminal)
Son kullanÄ±cÄ± veya test mÃ¼hendisi indirdiÄŸi paketin bÃ¼tÃ¼nlÃ¼ÄŸÃ¼nÃ¼ doÄŸrulamak iÃ§in:
```powershell
# Ä°ndirilen dosyanÄ±n hash'ini hesaplama
Get-FileHash -Path ".\Lyrow-3.5-Setup-x64.exe" -Algorithm SHA256

# sha256sums.txt dosyasÄ±ndaki deÄŸer ile karÅŸÄ±laÅŸtÄ±rma
```

### C. DoÄŸrulama Ã‡Ä±ktÄ±sÄ± FormatÄ± (`sha256sums.txt`)
```
e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  Lyrow-3.5-Setup-x64.exe
b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9  Lyrow-v3.5-win-x64-portable.zip
```
