# Lyrow â€” Stage 17: Release Operations Runbook

**DokÃ¼man Versiyonu:** 1.0
**Hedef ÃœrÃ¼n:** Lyrow MasaÃ¼stÃ¼ MÃ¼zik Deneyimi (v3.5)
**Tarih:** 2026-11-02
**Mevcut Ä°mzalama Durumu:** `NOT CONFIGURED`

---

## 1. AmaÃ§ ve Kapsam

Bu runbook, Lyrow masaÃ¼stÃ¼ uygulamasÄ±nÄ±n gÃ¼venilir, tekrarlanabilir ve denetlenebilir bir ÅŸekilde paketlenmesi, doÄŸrulanmasÄ±, daÄŸÄ±tÄ±lmasÄ± ve gerektiÄŸinde geri alÄ±nmasÄ± (rollback) iÃ§in adÄ±m adÄ±m operasyonel kÄ±lavuzdur.

---

## 2. Release HazÄ±rlÄ±k ve DoÄŸrulama AdÄ±mlarÄ±

### AdÄ±m 1: Release Derleme HazÄ±rlÄ±ÄŸÄ±
1. Ã‡alÄ±ÅŸma aÄŸacÄ±nÄ±n temiz olduÄŸunu ve `main` dalÄ±nda bulunulduÄŸunu doÄŸrulayÄ±n:
   ```powershell
   git status
   git branch --show-current
   ```
2. TÃ¼m projelerin Release modunda derlenebilir olduÄŸunu doÄŸrulayÄ±n:
   ```powershell
   dotnet build -c Release
   ```
   *Kabul Kriteri:* 0 Hata, 0 UyarÄ±.

### AdÄ±m 2: Test SÃ¼itinin Ã‡alÄ±ÅŸtÄ±rÄ±lmasÄ±
1. TÃ¼m birim testlerini Release modunda derleme yapmadan Ã§alÄ±ÅŸtÄ±rÄ±n:
   ```powershell
   dotnet test -c Release --no-build
   ```
   *Kabul Kriteri:* TÃ¼m testler (547/547) yeÅŸil (PASS) olmalÄ±dÄ±r.
2. Marka bÃ¼tÃ¼nlÃ¼ÄŸÃ¼ testini Ã§alÄ±ÅŸtÄ±rÄ±n:
   ```powershell
   dotnet test --filter "FullyQualifiedName~ZeroLegacyNames"
   ```
   *Kabul Kriteri:* SÄ±fÄ±r eski ad sÄ±zÄ±ntÄ±sÄ± (5/5 PASS).
3. GÃ¼venlik aÃ§Ä±ÄŸÄ± taramasÄ± yapÄ±n:
   ```powershell
   dotnet list package --vulnerable
   ```
   *Kabul Kriteri:* 0 gÃ¼venlik aÃ§Ä±ÄŸÄ±.

### AdÄ±m 3: DaÄŸÄ±tÄ±m Paketlerinin OluÅŸturulmasÄ±
1. **Self-Contained Publish (win-x64):**
   ```powershell
   dotnet publish src/Lyrow.App/Lyrow.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish/win-x64
   ```
2. **Portable ZIP ArÅŸivi:**
   ```powershell
   Compress-Archive -Path publish/win-x64/* -DestinationPath dist/Lyrow-3.5-win-x64.zip -Force
   ```
3. **Inno Setup Kurulum Paketi:**
   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer/setup.iss
   ```
   *Ã‡Ä±ktÄ±:* `dist/Lyrow-3.5-Setup-x64.exe`

### AdÄ±m 4: Kurulum ve Portable Paketlerinin DoÄŸrulanmasÄ±
- `dist/Lyrow-3.5-Setup-x64.exe` dosyasÄ±nÄ±n boyutunu ve metadata'sÄ±nÄ± kontrol edin.
- `installer/setup.iss` iÃ§indeki `AppMutex`, `PrivilegesRequired=lowest` ve Ã§oklu dil mesajlarÄ±nÄ±n yerinde olduÄŸunu teyit edin.
- Portable pakette gereksiz debug dosyalarÄ±nÄ±n (`.pdb`) bulunmadÄ±ÄŸÄ±ndan emin olun.

---

## 3. SaÄŸlama ToplamÄ± (SHA-256 Checksum) OperasyonlarÄ±

### AdÄ±m 5: Checksum Ãœretimi
`installer/generate_checksums.ps1` betiÄŸini Ã§alÄ±ÅŸtÄ±rarak `dist/sha256sums.txt` dosyasÄ±nÄ± Ã¼retin:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/generate_checksums.ps1 -DistPath dist -OutputFile dist/sha256sums.txt
```
*Beklenen Ã‡Ä±ktÄ± BiÃ§imi:*
```text
<sha256_hash>  Lyrow-3.5-Setup-x64.exe
<sha256_hash>  Lyrow-3.5-win-x64.zip
```

### AdÄ±m 6: Checksum DoÄŸrulamasÄ±
Paketlerin deÄŸiÅŸtirilmediÄŸini teyit etmek iÃ§in `installer/verify_checksums.ps1` betiÄŸini Ã§alÄ±ÅŸtÄ±rÄ±n:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File installer/verify_checksums.ps1 -ChecksumFile dist/sha256sums.txt -BaseDir dist
```
*Kabul Kriteri:* `exit code 0` ve konsolda `All 2 file(s) successfully verified.` mesajÄ±.

---

## 4. Authenticode Ä°mzalama ve Durum YÃ¶netimi

### AdÄ±m 7: Ä°mzalama Durum KontrolÃ¼
- **Mevcut Durum:** `NOT CONFIGURED`
- Depoda herhangi bir Ã¶zel anahtar (.pfx) veya sertifika bulunmamaktadÄ±r ve gÃ¼venlik gereÄŸi eklenmemelidir.
- Sahte / kendinden imzalÄ± sertifika oluÅŸturulmamalÄ±dÄ±r.

### AdÄ±m 8: Ä°mzalama YapÄ±landÄ±rÄ±lmamÄ±ÅŸken Ä°zlenecek Yol
1. Ä°mzalamasÄ±z daÄŸÄ±tÄ±m riski kabul edilir: Microsoft Defender SmartScreen ilk indirmelerde "Bilinmeyen YayÄ±ncÄ±" uyarÄ±sÄ± verir.
2. KullanÄ±cÄ±lara gÃ¼venlik bÃ¼tÃ¼nlÃ¼ÄŸÃ¼nÃ¼ doÄŸrulamalarÄ± iÃ§in `sha256sums.txt` dosyasÄ± resmi GitHub Release sayfasÄ±nda aÃ§Ä±kÃ§a sunulur.
3. README ve Kurulum KÄ±lavuzunda SmartScreen uyarÄ±sÄ±nÄ± geÃ§me adÄ±mlarÄ± ("Ek bilgi" -> "Yine de Ã§alÄ±ÅŸtÄ±r") ÅŸeffaf ÅŸekilde aÃ§Ä±klanÄ±r.
4. Kurumsal veya production ortamÄ±nda Azure Trusted Signing veya Key Vault HSM sertifikasÄ± temin edildiÄŸinde, CI/CD workflow'una `azure/trusted-signing-action` adÄ±mÄ± dahil edilir.

---

## 5. GitHub Actions CI ve Release HazÄ±rlÄ±ÄŸÄ±

### AdÄ±m 9: GitHub Actions CI DoÄŸrulamasÄ±
1. DeÄŸiÅŸiklikleri `main` dalÄ±na push edin:
   ```powershell
   git push origin main
   ```
2. CI iÅŸ akÄ±ÅŸÄ±nÄ± izleyin:
   ```powershell
   gh run list --limit 1
   gh run watch <run_id>
   ```
   *Kabul Kriteri:* TÃ¼m build ve test adÄ±mlarÄ± yeÅŸil (`success`).

### AdÄ±m 10: GitHub Release HazÄ±rlÄ±ÄŸÄ±
1. Yeni bir tag veya release oluÅŸturmadan Ã¶nce staging testlerini yapÄ±n.
2. Release notlarÄ±na aÅŸaÄŸÄ±dakileri ekleyin:
   - Ã–ne Ã§Ä±kan Ã¶zellikler ve iyileÅŸtirmeler.
   - SHA-256 Checksum tablosu.
   - SmartScreen bilgilendirmesi ve `NOT CONFIGURED` imzalama notu.
   - Ä°ndirme baÄŸlantÄ±larÄ± (`Setup-x64.exe` ve `win-x64.zip`).

---

## 6. Release SonrasÄ± DoÄŸrulama ve Rollback

### AdÄ±m 11: Release SonrasÄ± Smoke Test
1. Temiz bir Windows 10/11 x64 sanal makinesinde veya test kullanÄ±cÄ±sÄ±nda `Lyrow-3.5-Setup-x64.exe` kurulumunu Ã§alÄ±ÅŸtÄ±rÄ±n.
2. Welcome Wizard akÄ±ÅŸÄ±nÄ±, tema ve dil seÃ§imini test edin.
3. GSMTC medya yakalamasÄ±nÄ± ve Ambient Glow tepkisini gÃ¶zlemleyin.
4. UygulamayÄ± kaldÄ±rÄ±n ve kullanÄ±cÄ± verisi saklama tercih kutusunun Ã§alÄ±ÅŸtÄ±ÄŸÄ±nÄ± teyit edin.

### AdÄ±m 12: Rollback ve Hata Durumu ProsedÃ¼rÃ¼
EÄŸer release paketinde kritik bir sorun (Ã§Ã¶kme, bellek sÄ±zÄ±ntÄ±sÄ± veya veri bozulmasÄ±) tespit edilirse:
1. **GitHub Release:** Sorunlu release sÃ¼rÃ¼mÃ¼nÃ¼ GitHub Ã¼zerinden derhal "Pre-release" olarak iÅŸaretleyin veya indirme linklerini gizleyin.
2. **KullanÄ±cÄ± Bildirimi:** GitHub Issues veya Discussions Ã¼zerinden kullanÄ±cÄ±lara Ã¶nceki kararlÄ± sÃ¼rÃ¼mÃ¼ kullanmalarÄ± duyurulur.
3. **Hotfix SÃ¼reci:**
   - Sorunun kÃ¶k nedeni izole edilir.
   - DÃ¼zeltme iÃ§in test eklenir ve doÄŸrulanÄ±r.
   - SÃ¼rÃ¼m politikasÄ±na uygun olarak yeni bir commit ile CI Ã¼zerinden tekrar paketlenir.
4. **Asla YapÄ±lmayacaklar:**
   - Eski tag'i zorla silip (`git push --force`) aynÄ± tag adÄ±yla farklÄ± binary yayÄ±nlamayÄ±n.
