# LYROW â€” STAGE 16: MANUEL DONANIM VE KULLANICI DOÄRULAMA KÄ°TÄ°

Bu dokÃ¼man, Lyrow masaÃ¼stÃ¼ uygulamasÄ±nÄ±n otomatik test ortamlarÄ±nda (CI / headless runner) tam olarak simÃ¼le edilemeyen fiziksel donanÄ±m, harici medya oturumlarÄ± ve sesli ekran okuyucu etkileÅŸimleri iÃ§in hazÄ±rlanmÄ±ÅŸ adÄ±m adÄ±m **Manuel DoÄŸrulama ProsedÃ¼rlerini** iÃ§erir.

> [!IMPORTANT]
> Otomatik test veya sanal sunucu ortamÄ±nda gerÃ§ek 3.5 mm jak, fiziksel USB ses kartÄ±, canlÄ± insan kulaÄŸÄ± ile Windows Narrator dinlemesi ve lisanslÄ± Apple Music DRM oturumu test edilemediÄŸinden, bu testler **`NOT TESTED`** olarak sÄ±nÄ±flandÄ±rÄ±lmÄ±ÅŸtÄ±r. GerÃ§ek donanÄ±m testi yapÄ±ldÄ±ÄŸÄ±nda ilgili test personeli tarafÄ±ndan PASS/FAIL olarak gÃ¼ncellenmelidir.

---

## 1. Manuel Test SenaryolarÄ± Matrisi

| Test ID | Test TanÄ±mÄ± | Odak AlanÄ± | Durum |
| :--- | :--- | :--- | :--- |
| **MAN-AUDIO-01** | Oynatma esnasÄ±nda 3.5 mm ses jakÄ±nÄ±n Ã§Ä±karÄ±lmasÄ± ve yeniden takÄ±lmasÄ± | Ses Ã‡Ä±kÄ±ÅŸ DeÄŸiÅŸimi | `NOT TESTED` |
| **MAN-AUDIO-02** | USB ses kartÄ±/kulaklÄ±ÄŸÄ±nÄ±n Ã§Ä±karÄ±lmasÄ± ve yeniden baÄŸlanmasÄ± | USB Hot-plug & Enum | `NOT TESTED` |
| **MAN-GLOW-01** | Ambient Glow aÃ§Ä±kken ses aygÄ±tÄ±nÄ±n aniden kaybolmasÄ± (`DEVICE_INVALIDATED`) | WASAPI DÃ¶ngÃ¼ Kurtarma | `NOT TESTED` |
| **MAN-A11Y-01** | Windows Narrator ile Lyrow ana pencere ve sÃ¶zlÃ¼k arayÃ¼zÃ¼nÃ¼n taranmasÄ± | EriÅŸilebilirlik (a11y) | `NOT TESTED` |
| **MAN-GSMTC-01** | GerÃ§ek Apple Music Windows uygulamasÄ±nda GSMTC oturumunun yakalanmasÄ± | Medya Entegrasyonu | `NOT TESTED` |
| **MAN-GSMTC-02** | Apple Music ÅŸarkÄ± deÄŸiÅŸimi, kapak gÃ¶rseli ve ÅŸarkÄ± sÃ¶zÃ¼ senkronizasyonu | Metadata & Senkron | `NOT TESTED` |
| **MAN-LIFECYCLE-01** | Uygulama kapatÄ±lÄ±p aÃ§Ä±lÄ±rken kiÅŸiselleÅŸtirilmiÅŸ ayarlarÄ±n korunmasÄ± | Ayar KalÄ±cÄ±lÄ±ÄŸÄ± | `NOT TESTED` |

---

## 2. DetaylÄ± Test ProsedÃ¼rleri

### TEST ID: MAN-AUDIO-01
- **BaÅŸlÄ±k:** 3.5 mm Ses JakÄ±nÄ±n Oynatma EsnasÄ±nda Ã‡Ä±karÄ±lmasÄ± ve Yeniden TakÄ±lmasÄ±
- **Ã–n KoÅŸullar:**
  1. Windows 10/11 x64 sistem Ã¼zerinde Lyrow kurulu veya portable olarak aÃ§Ä±k.
  2. 3.5 mm analog kulaklÄ±k/hoparlÃ¶r anakart veya Ã¶n panel jakÄ±na takÄ±lÄ±.
  3. Sistemde mÃ¼zik Ã§alÄ±yor ve varsayÄ±lan ses Ã§Ä±kÄ±ÅŸÄ± 3.5 mm cihazÄ±.
  4. Ambient Glow aktif.
- **Uygulama AdÄ±mlarÄ±:**
  1. Lyrow ana penceresinde Ambient Glow anahtarÄ±nÄ± aÃ§Ä±k (`ON`) konuma getirin.
  2. MÃ¼zik oynatÄ±lÄ±rken 3.5 mm ses jakÄ±nÄ± fiziksel olarak bilgisayardan Ã§ekin.
  3. Windows'un dahili hoparlÃ¶re veya ikincil cihaza geÃ§mesini bekleyin (yaklaÅŸÄ±k 2 saniye).
  4. Lyrow'un kilitlenip kilitlenmediÄŸini ve CPU kullanÄ±mÄ±nÄ± GÃ¶rev YÃ¶neticisi'nden kontrol edin.
  5. 3.5 mm ses jakÄ±nÄ± tekrar yerine takÄ±n.
- **Beklenen SonuÃ§:**
  - Jak Ã§ekildiÄŸinde uygulama kilitlenmemeli (crash veya ANR oluÅŸmamalÄ±).
  - Ambient Glow, ses kesildiÄŸinde yumuÅŸak sÃ¶nÃ¼mleme (silence decay) ile 0 yoÄŸunluÄŸa inmelidir.
  - Jak yeniden takÄ±ldÄ±ÄŸÄ±nda WASAPI capture servisi yeni varsayÄ±lan aygÄ±tÄ± otomatik algÄ±lamalÄ± ve gÃ¶rsel parÄ±ltÄ± yeniden baÅŸlamalÄ±dÄ±r.
- **Test Sonucu:** `NOT TESTED`
- **KanÄ±t / Notlar:** Otomasyon ortamÄ±nda fiziksel jak sÃ¶kme/takma iÅŸlemi yapÄ±lamamaktadÄ±r.

---

### TEST ID: MAN-AUDIO-02
- **BaÅŸlÄ±k:** USB Ses KartÄ± / KulaklÄ±ÄŸÄ±n Ã‡Ä±karÄ±lmasÄ± ve Yeniden BaÄŸlanmasÄ±
- **Ã–n KoÅŸullar:**
  1. USB DAC, USB kulaklÄ±k veya harici USB ses kartÄ± sisteme baÄŸlÄ±.
  2. Windows varsayÄ±lan Ã§Ä±kÄ±ÅŸ aygÄ±tÄ± olarak bu USB cihazÄ± seÃ§ili.
  3. Lyrow Ã§alÄ±ÅŸÄ±r durumda ve medya oturumu aktif.
- **Uygulama AdÄ±mlarÄ±:**
  1. Lyrow Settings > Glow sekmesinde ses cihazÄ± seÃ§iminin varsayÄ±lan (`Default`) olduÄŸunu doÄŸrulayÄ±n.
  2. MÃ¼zik Ã§alarken USB ses cihazÄ±nÄ± USB portundan Ã§Ä±karÄ±n.
  3. 5 saniye bekleyin; `%APPDATA%\Lyrow\logs\app.log` dosyasÄ±nÄ± aÃ§Ä±p hata kaydÄ±nÄ± inceleyin.
  4. USB ses cihazÄ±nÄ± tekrar aynÄ± veya farklÄ± bir USB portuna takÄ±n.
  5. MÃ¼ziÄŸi devam ettirin.
- **Beklenen SonuÃ§:**
  - `WasapiAudioCaptureService` donanÄ±m kesintisini yakalamalÄ±, `AUDCLNT_E_DEVICE_INVALIDATED` hatasÄ±nÄ± ele almalÄ± ve Ã§Ã¶kme yaÅŸanmamalÄ±dÄ±r.
  - Cihaz geri takÄ±ldÄ±ÄŸÄ±nda ses akÄ±ÅŸÄ± kesintisiz olarak dinlenmeye devam etmelidir.
  - Log dosyasÄ±nda `Bearer` veya kullanÄ±cÄ± yolu gibi hassas veriler maskelenmiÅŸ olmalÄ±dÄ±r.
- **Test Sonucu:** `NOT TESTED`
- **KanÄ±t / Notlar:** Otomasyon ve sanal sunucu ortamÄ±nda USB donanÄ±m takÄ±p Ã§Ä±karma iÅŸlemi yapÄ±lamamaktadÄ±r.

---

### TEST ID: MAN-GLOW-01
- **BaÅŸlÄ±k:** Ambient Glow AÃ§Ä±kken Ses AygÄ±tÄ±nÄ±n Aniden KaybolmasÄ± (Device Invalidation)
- **Ã–n KoÅŸullar:**
  1. Lyrow tam ekran veya pencereli modda Ã§alÄ±ÅŸÄ±yor.
  2. Ambient Glow etkin (YoÄŸunluk: %80, Hassasiyet: 1.0).
  3. Akustik profil `BassBoost` olarak ayarlanmÄ±ÅŸ.
- **Uygulama AdÄ±mlarÄ±:**
  1. Windows Ses AyarlarÄ± > Ã‡Ä±kÄ±ÅŸ AygÄ±tlarÄ± bÃ¶lÃ¼mÃ¼nden aktif oynatma aygÄ±tÄ±nÄ± "Devre DÄ±ÅŸÄ± BÄ±rak" (Disable) yapÄ±n.
  2. Lyrow UI'sÄ±nÄ± gÃ¶zlemleyin: ParÄ±ltÄ±nÄ±n aniden donup donmadÄ±ÄŸÄ±nÄ± kontrol edin.
  3. AygÄ±tÄ± tekrar "EtkinleÅŸtir" (Enable) yapÄ±n.
- **Beklenen SonuÃ§:**
  - AygÄ±t devre dÄ±ÅŸÄ± bÄ±rakÄ±ldÄ±ÄŸÄ±nda Glow kenar Ã§izgileri siyah/ÅŸeffaf duruma yumuÅŸakÃ§a solmalÄ±dÄ±r (abrupt visual cut off olmamalÄ±).
  - AygÄ±t geri geldiÄŸinde `MMDeviceEnumerator` aygÄ±t deÄŸiÅŸim bildirimini almalÄ± ve gÃ¶rselleÅŸtirici otomatik olarak yeniden canlanmalÄ±dÄ±r.
- **Test Sonucu:** `NOT TESTED`
- **KanÄ±t / Notlar:** Windows Ses YÃ¶netim Denetleyicisi donanÄ±m emÃ¼lasyonu CI ortamÄ±nda desteklenmemektedir.

---

### TEST ID: MAN-A11Y-01
- **BaÅŸlÄ±k:** Windows Narrator ile Lyrow ArayÃ¼zÃ¼nÃ¼n TaranmasÄ±
- **Ã–n KoÅŸullar:**
  1. Windows Narrator (Ekran Okuyucusu) aÃ§Ä±k (`Win + Ctrl + Enter`).
  2. Lyrow aÃ§Ä±k ve ana pencere odakta.
- **Uygulama AdÄ±mlarÄ±:**
  1. `Tab` ve `Shift + Tab` tuÅŸlarÄ±yla ana sayfadaki kontrollere (Glow toggle, Lyrics toggle, Ayarlar ikonu) sÄ±rayla gidin.
  2. Her kontrolde Narrator'Ä±n okuduÄŸu metni dinleyin.
  3. ÅarkÄ± sÃ¶zÃ¼ penceresini aÃ§Ä±n; klavye kÄ±sayollarÄ±nÄ± (`Space`, `F11`, `Sol/SaÄŸ Ok`) deneyin.
  4. Ayarlar sekmesindeki kaydÄ±rÄ±cÄ±larÄ± (`SliderGlowIntensity`, `SliderLyricsOffset`) klavye ok tuÅŸlarÄ±yla deÄŸiÅŸtirin ve Narrator okumasÄ±nÄ± kontrol edin.
- **Beklenen SonuÃ§:**
  - TÃ¼m butonlar ve anahtarlar `AutomationProperties.Name` deÄŸerini net ÅŸekilde seslendirmelidir (Ã–rn: "Toggle Ambient Glow, anahtar, aÃ§Ä±k").
  - Klavye odak halkasÄ± (focus rectangle) tÃ¼m temalarda (AÃ§Ä±k ve Koyu) belirgin ve gÃ¶rÃ¼nÃ¼r kalmalÄ±dÄ±r.
  - HiÃ§bir kontrol etiketsiz veya "isimsiz dÃ¼ÄŸme" olarak okunmamalÄ±dÄ±r.
- **Test Sonucu:** `NOT TESTED`
- **KanÄ±t / Notlar:** Kod dÃ¼zeyindeki `AutomationProperties.Name` tanÄ±mlarÄ± test edilmiÅŸtir; ancak canlÄ± TTS insan kulaÄŸÄ± dinlemesi otomatikleÅŸtirilemez.

---

### TEST ID: MAN-GSMTC-01
- **BaÅŸlÄ±k:** GerÃ§ek Apple Music Windows MasaÃ¼stÃ¼ Oturumunda Medya Takibi
- **Ã–n KoÅŸullar:**
  1. Microsoft Store Ã¼zerinden resmi Apple Music for Windows yÃ¼klÃ¼ ve aktif bir Apple Music aboneliÄŸi ile giriÅŸ yapÄ±lmÄ±ÅŸ.
  2. Lyrow arka planda Ã§alÄ±ÅŸÄ±yor.
- **Uygulama AdÄ±mlarÄ±:**
  1. Apple Music uygulamasÄ±ndan herhangi bir ÅŸarkÄ±yÄ± baÅŸlatÄ±n.
  2. Lyrow ana penceresinde ve kompakt oynatÄ±cÄ±da ÅŸarkÄ± adÄ±nÄ±n, sanatÃ§Ä±sÄ±nÄ±n ve albÃ¼mÃ¼nÃ¼n gÃ¶rÃ¼nÃ¼p gÃ¶rÃ¼nmediÄŸini kontrol edin.
  3. Lyrow arayÃ¼zÃ¼ndeki "Duraklat" butonuna tÄ±klayÄ±n; Apple Music'in durduÄŸunu doÄŸrulayÄ±n.
  4. Lyrow arayÃ¼zÃ¼ndeki "Oynat" butonuna tÄ±klayÄ±n; Apple Music'in devam ettiÄŸini doÄŸrulayÄ±n.
  5. Ä°lerleme Ã§ubuÄŸuna tÄ±klayarak seek yapmayÄ± deneyin.
- **Beklenen SonuÃ§:**
  - ÅarkÄ± baÅŸlÄ±ÄŸÄ± ve sanatÃ§Ä± Apple Music oturumundan 100 ms iÃ§inde Ã§ekilmelidir.
  - Apple Music Windows API'sinde seek desteÄŸi kÄ±sÄ±tlÄ±ysa kullanÄ±cÄ±ya nazik bir toast bildirim (`Lyrics_SeekFailed`) gÃ¶sterilmeli, uygulama kilitlenmemelidir.
  - Play/Pause komutlarÄ± fiziksel oynatÄ±cÄ±ya anÄ±nda yansÄ±malÄ±dÄ±r.
- **Test Sonucu:** `NOT TESTED`
- **KanÄ±t / Notlar:** CI sunucusunda canlÄ± Apple Music DRM ve GUI oturumu aÃ§Ä±lamamaktadÄ±r.

---

### TEST ID: MAN-GSMTC-02
- **BaÅŸlÄ±k:** Apple Music ParÃ§a DeÄŸiÅŸimi, Kapak GÃ¶rseli ve ÅarkÄ± SÃ¶zÃ¼ Senkronu
- **Ã–n KoÅŸullar:**
  1. Apple Music Ã¼zerinde bir Ã§alma listesi Ã§alÄ±yor.
  2. Lyrow ÅŸarkÄ± sÃ¶zÃ¼ penceresi (`LyricsWindow`) aÃ§Ä±k.
- **Uygulama AdÄ±mlarÄ±:**
  1. Apple Music'te ÅŸarkÄ±nÄ±n bitmesini bekleyin veya "Sonraki ÅarkÄ±"ya basÄ±n.
  2. Lyrow'un eski ÅŸarkÄ±nÄ±n sÃ¶zlerini hemen temizleyip temizlemediÄŸini gÃ¶zlemleyin.
  3. Yeni ÅŸarkÄ±nÄ±n albÃ¼m kapaÄŸÄ±nÄ±n ve LRCLIB Ã¼zerinden gelen senkronize sÃ¶zlerin yÃ¼klenmesini izleyin.
  4. ÅarkÄ± Ã§alarken aktif sÃ¶z satÄ±rÄ±nÄ±n merkeze kayÄ±p kaymadÄ±ÄŸÄ±nÄ± ve karaoke dolgusunu kontrol edin.
- **Beklenen SonuÃ§:**
  - ParÃ§a deÄŸiÅŸtiÄŸi anda eski sÃ¶zler ekrandan kaybolmalÄ±, bayat sÃ¶zler gÃ¶sterilmemelidir.
  - Kapak gÃ¶rseli arka plana dinamik akrilik/blur olarak yansÄ±malÄ±dÄ±r.
  - Aktif sÃ¶z satÄ±rÄ± dikey eksende merkezlenmeli ve zaman Ã§izelgesiyle uyumlu akmalÄ±dÄ±r.
- **Test Sonucu:** `NOT TESTED`
- **KanÄ±t / Notlar:** LisanslÄ± DRM akÄ±ÅŸÄ± ve gerÃ§ek Apple Music masaÃ¼stÃ¼ oturumu gerektirir.

---

### TEST ID: MAN-LIFECYCLE-01
- **BaÅŸlÄ±k:** Uygulama KapatÄ±lÄ±p Yeniden AÃ§Ä±lÄ±rken AyarlarÄ±n KorunmasÄ±
- **Ã–n KoÅŸullar:**
  1. Lyrow varsayÄ±lan ayarlarla aÃ§Ä±k.
- **Uygulama AdÄ±mlarÄ±:**
  1. Dili TÃ¼rkÃ§e (`tr`), temayÄ± Koyu (`Dark`) yapÄ±n.
  2. Ambient Glow akustik profilini `BassBoost`, ÅarkÄ± sÃ¶zÃ¼ satÄ±r aralÄ±ÄŸÄ±nÄ± `28` yapÄ±n.
  3. Lyrow'u sistem tepsisinden veya pencere kapatma butonuyla tamamen kapatÄ±n (`Exit`).
  4. GÃ¶rev YÃ¶neticisi'nde `Lyrow.App.exe` sÃ¼recinin kalmadÄ±ÄŸÄ±nÄ± doÄŸrulayÄ±n.
  5. Lyrow'u yeniden baÅŸlatÄ±n.
- **Beklenen SonuÃ§:**
  - Dil, tema, akustik profil ve satÄ±r aralÄ±ÄŸÄ± ayarlarÄ± tam olarak kaydedilmiÅŸ ÅŸekilde geri yÃ¼klenmelidir.
  - `%APPDATA%\Lyrow\settings.json` dosyasÄ±nda tÃ¼m deÄŸerler geÃ§erli JSON formatÄ±nda bulunmalÄ±dÄ±r.
- **Test Sonucu:** `NOT TESTED` (Manuel kullanÄ±cÄ± senaryosu olarak; birim test dÃ¼zeyinde `WelcomeWizard_FirstRun_FlowAndSettingsPersistence` PASS almÄ±ÅŸtÄ±r).
- **KanÄ±t / Notlar:** SÃ¼reÃ§ kapatma ve yeniden baÅŸlatma masaÃ¼stÃ¼ ortamÄ±nda doÄŸrulanmalÄ±dÄ±r.
