# Lyrow â€” Sorun Giderme KÄ±lavuzu (Troubleshooting Guide)

**Hedef:** Lyrow masaÃ¼stÃ¼ uygulamasÄ±nda karÅŸÄ±laÅŸÄ±labilecek olasÄ± durumlar, nedenleri ve Ã§Ã¶zÃ¼m adÄ±mlarÄ±.

---

## 1. Medya Oturumu AlgÄ±lanmÄ±yor

### Belirti
Lyrow penceresinde ÅŸarkÄ± adÄ± gÃ¶rÃ¼nmÃ¼yor veya "Medya oturumu bekleniyor" yazÄ±sÄ± kalÄ±yor.

### OlasÄ± Nedenler ve Ã‡Ã¶zÃ¼mler
1. **OynatÄ±cÄ± GSMTC Desteklemiyor:**
   - Lyrow, Windows Global System Media Transport Controls (GSMTC) arabirimini kullanÄ±r. Spotify, Apple Music, Tidal, Deezer, Chrome, Edge ve Firefox GSMTC ile tam uyumludur.
   - TarayÄ±cÄ±da mÃ¼zik dinliyorsanÄ±z, tarayÄ±cÄ±nÄ±zÄ±n arka plan medya entegrasyonunun devrede olduÄŸundan emin olun (Edge/Chrome: `Hardware Media Key Handling` bayraÄŸÄ±).
2. **Uygulama Ä°zinleri:**
   - Windows AyarlarÄ± > Gizlilik ve GÃ¼venlik altÄ±ndan masaÃ¼stÃ¼ uygulamalarÄ±nÄ±n medya kontrollerine eriÅŸiminin aÃ§Ä±k olduÄŸunu doÄŸrulayÄ±n.
3. **Oturumun Yenilenmesi:**
   - OynatÄ±cÄ± uygulamanÄ±zda ÅŸarkÄ±yÄ± duraklatÄ±p tekrar oynatÄ±n veya bir sonraki ÅŸarkÄ±ya geÃ§in.

---

## 2. ÅarkÄ± SÃ¶zleri BulunamÄ±yor veya Senkron DeÄŸil

### Belirti
"ÅarkÄ± sÃ¶zÃ¼ bulunamadÄ±" uyarÄ±sÄ± veya sÃ¶zlerin mÃ¼zikle senkron akmamasÄ±.

### Ã‡Ã¶zÃ¼mler
1. **Ä°nternet BaÄŸlantÄ±sÄ±:**
   - ÅarkÄ± sÃ¶zleri aÃ§Ä±k kaynaklÄ± **LRCLIB** servisi Ã¼zerinden Ã§ekilir. BilgisayarÄ±nÄ±zÄ±n aktif internet baÄŸlantÄ±sÄ± olduÄŸundan emin olun.
2. **DoÄŸru Metadata:**
   - OynatÄ±lan ÅŸarkÄ±nÄ±n etiketlerinde (Artist / Title) parantez iÃ§i remiks veya remaster gibi ek metinler sÃ¶z aramasÄ±nÄ± zorlaÅŸtÄ±rabilir. Lyrow bu ekleri temizlemeye Ã§alÄ±ÅŸÄ±r, ancak veritabanÄ±nda olmayan baÄŸÄ±msÄ±z parÃ§alar iÃ§in sÃ¶z bulunamayabilir.
3. **Zamanlama KaymasÄ± (Offset):**
   - ÅarkÄ± sÃ¶zleri saniyelerle Ã¶nde veya geride kalÄ±yorsa:
     - **Ayarlar > SÃ¶zler** sekmesine gidin.
     - **Zamanlama KaydÄ±rma (Offset)** kaydÄ±rÄ±cÄ±sÄ±nÄ± milisaniye cinsinden (+/- 500 ms) ayarlayarak tam senkronu yakalayÄ±n.

---

## 3. Ambient Glow Ã‡alÄ±ÅŸmÄ±yor veya Ses CihazÄ± DeÄŸiÅŸti

### Belirti
Pencere kenarlarÄ±ndaki dinamik Ä±ÅŸÄ±k efekti yanmÄ±yor veya ses kesildiÄŸinde donuyor.

### Ã‡Ã¶zÃ¼mler
1. **Glow AnahtarÄ±:**
   - Ana sayfadaki **Ambient Glow** anahtarÄ±nÄ±n aÃ§Ä±k olduÄŸunu kontrol edin.
2. **Ses Ã‡Ä±kÄ±ÅŸÄ± DeÄŸiÅŸimi:**
   - KulaklÄ±k veya harici hoparlÃ¶r takÄ±p Ã§Ä±kardÄ±ÄŸÄ±nÄ±zda Windows ses aygÄ±tÄ±nÄ± yeniler. Lyrow `AUDCLNT_E_DEVICE_INVALIDATED` durumunu yakalar ve yeni varsayÄ±lan aygÄ±ta otomatik baÄŸlanÄ±r. 2-3 saniye bekleyin.
3. **AygÄ±t SeÃ§imi:**
   - **Ayarlar > Glow** sekmesinde ses aygÄ±tÄ± olarak "VarsayÄ±lan (Default)" seÃ§ildiÄŸinden emin olun.
4. **Sessizlik Durumu:**
   - MÃ¼zik durduÄŸunda Ambient Glow pÃ¼rÃ¼zsÃ¼z ÅŸekilde sÃ¶nÃ¼mlenir (silence decay). MÃ¼zik Ã§almaya baÅŸladÄ±ÄŸÄ±nda gÃ¶rsel efekt tekrar tetiklenir.

---

## 4. Klavye KÄ±sayollarÄ± (Hotkeys) Ã‡alÄ±ÅŸmÄ±yor

### Belirti
`Win + Shift + L` veya `Ctrl + Shift + G` gibi kÄ±sayollara basÄ±ldÄ±ÄŸÄ±nda Lyrow tepki vermiyor.

### Ã‡Ã¶zÃ¼mler
- BaÅŸka bir uygulama (Ã¶rneÄŸin Discord, OBS veya Windows PowerToys) aynÄ± global kÄ±sayolu Ã¶nceden kaydetmiÅŸ olabilir.
- **Ayarlar > KÄ±sayollar** sekmesinden ilgili kÄ±sayol kombinasyonunu deÄŸiÅŸtirin veya Ã§akÄ±ÅŸan Ã¼Ã§Ã¼ncÃ¼ taraf uygulamanÄ±n kÄ±sayolunu devre dÄ±ÅŸÄ± bÄ±rakÄ±n.

---

## 5. GÃ¼ncelleme HatalarÄ± ve Paket Ä°ndirme

### Belirti
GÃ¼ncelleme kontrolÃ¼ baÅŸarÄ±sÄ±z oluyor veya paket indirilemiyor.

### Ã‡Ã¶zÃ¼mler
- Lyrow gÃ¼ncellemeleri doÄŸrudan resmi GitHub Releases API (`api.github.com`) Ã¼zerinden denetler.
- GÃ¼venlik duvarÄ±nÄ±zÄ±n (firewall) veya ÅŸirket proxy'nizin GitHub API adresine eriÅŸimi engellemediÄŸini doÄŸrulayÄ±n.

---

## 6. Windows Defender SmartScreen "Bilinmeyen YayÄ±ncÄ±" UyarÄ±sÄ±

### Belirti
Kurulum dosyasÄ± (`Setup.exe`) Ã§alÄ±ÅŸtÄ±rÄ±ldÄ±ÄŸÄ±nda mavi renkte "Windows kiÅŸisel bilgisayarÄ±nÄ±zÄ± korudu" ekranÄ± Ã§Ä±kmasÄ±.

### Neden
Lyrow aÃ§Ä±k kaynaklÄ± ve baÄŸÄ±msÄ±z bir projedir. Ticari bir Authenticode sertifikasÄ± ile imzalanmamÄ±ÅŸtÄ±r (`NOT CONFIGURED`). Bu uyarÄ± bir virÃ¼s veya truva atÄ± anlamÄ±na gelmez; Windows'un yeni ve imzasÄ±z yazÄ±lÄ±mlar iÃ§in gÃ¶sterdiÄŸi rutin bir gÃ¼venlik tedbiridir.

### Ã‡Ã¶zÃ¼m
1. Penceredeki **â€œEk bilgiâ€** (More info) linkine tÄ±klayÄ±n.
2. AÃ§Ä±lan alanda **â€œYine de Ã§alÄ±ÅŸtÄ±râ€** (Run anyway) butonuna basarak kuruluma devam edin.
3. Ä°ndirdiÄŸiniz dosyanÄ±n deÄŸiÅŸtirilmediÄŸini doÄŸrulamak iÃ§in PowerShell'de:
   ```powershell
   Get-FileHash -Algorithm SHA256 .\Lyrow-3.5-Setup-x64.exe
   ```
   komutunu Ã§alÄ±ÅŸtÄ±rÄ±p resmi sÃ¼rÃ¼m sayfasÄ±ndaki `sha256sums.txt` ile karÅŸÄ±laÅŸtÄ±rabilirsiniz.

---

## 7. Log DosyalarÄ±nÄ± GÃ¼venli Åekilde PaylaÅŸma

Bir hata bildiriminde (GitHub Issue) geliÅŸtiricilere log iletmek isterseniz:
1. Log dosyasÄ±nÄ±n konumu:
   `%LOCALAPPDATA%\Lyrow\logs\app.log`
2. **Veri Maskeleme:**
   Lyrow'un dahili `LocalDiagnosticLogger` servisi; `Bearer token`, `client_secret`, API anahtarlarÄ± ve kullanÄ±cÄ± profili yollarÄ±nÄ± otomatik olarak `[REDACTED]` ile maskeler.
3. Yine de paylaÅŸmadan Ã¶nce log dosyasÄ±nÄ± bir metin dÃ¼zenleyicide aÃ§arak kiÅŸisel bir veri iÃ§ermediÄŸini gÃ¶zden geÃ§irmeniz Ã¶nerilir.
