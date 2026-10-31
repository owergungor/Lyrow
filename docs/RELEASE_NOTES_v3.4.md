# Lyrow v3.4 — Responsive Player, Lyrics Sync & Advanced Beat Reactivity

**Sürüm:** `v3.4` (`ProductVersion: 3.4`, `FileVersion: 3.4.0.0`)  
**Taban Sürüm:** `v3.3` (`083d76caf33f4fb01fb18f9784cfcf4152d665d5`)  
**Platform:** Windows 10 (19041+) / Windows 11 Native (`C#` / `.NET 10 LTS` / `WinUI 3` / `Windows App SDK` / `XAML` / `MVVM`)

---

## 🎯 v3.4 Özet

Lyrow v3.4, oynatıcı penceresinin her çözünürlükte oranlı ve hizalı büyümesini (Responsive Scaling), Dar Mod (Compact Mode) kontrollerinin ve yüksekliğinin iyileştirilmesini, çok satırlı şarkı sözlerinde sıralı (sequential) vurgulama desteğini, ilerleme çubuğu üzerinden tıklama/sürükleme ile anında sarma (seek) özelliğini, otomatik parça geçişlerinde (`A → B → C → D`) eksiksiz söz/arkaplan/Glow paleti yenilemesini, güncelleme tamamlandığında uygulamanın otomatik yeniden başlatılmasını ve gerçek zamanlı Spectral Flux + Tempo tabanlı gelişmiş ritim algılamasını içerir.

---

## 🚀 12 Temel Geliştirme ve Çözüm

### 1. Güncelleme Sonrası Otomatik Yeniden Başlatma (`Auto-Relaunch`)
- İndirilen kurulum paketi (`Lyrow-3.4-Setup-x64.exe`) çalıştırılmadan önce dosya varlığı, boyutu ve Windows PE (`MZ`) imzası `VerifyDownloadedPackage` ile doğrulanır.
- Kurulum tamamlandığında single-instance mutex kilidi (`ReleaseSingleInstanceLock`) güvenli biçimde serbest bırakılır ve yeni `Lyrow.App.exe` otomatik olarak başlatılır.
- Başlatma veya doğrulama hatasında kullanıcıya `Tekrar Dene (Retry)` aksiyonlu hata bildirimi sunulur.

### 2. Duyarlı (Responsive) Pencere Ölçekleme
- `LyricsViewportCalculator.CalculateResponsiveMetrics` ile pencere boyutu (`SizeChanged`) değiştikçe:
  - Albüm kapağı **1:1 kare** oranını koruyarak büyür/küçülür.
  - Şarkı başlığı, sanatçı/albüm metni, zaman etiketleri ve cihaz bilgisi oranlı ölçeklenir.
  - Progress bar ve ses çubuğu genişlikleri her boyutta albüm kapağı genişliğiyle birebir hizalı kalır.
  - Oynatma ve yardımcı kontrol butonları ile sağ paneldeki şarkı sözü font boyutları/satır aralıkları pencere boyutuna göre akıcı biçimde ölçeklenir.

### 3. Dar Mod (Compact Mode) Kontrollerinin Yeniden Düzenlenmesi
- Dar modun sağ alt köşesindeki ses/mute butonu kaldırılarak yerine doğrudan yatay 50/50 görünüme geçiş yapan **Normal Mode (`BtnNormalModeCompact`)** butonu eklendi (`Tooltip: "Normal mode"`).
- Dar modda pencere genişliği kullanıcı tarafından `>= 640 px` değerine büyütüldüğünde histerezis kontrolüyle otomatik olarak yatay normal moda geçiş yapılır.

### 4. Dar Mod Dikey Boyutunun Artırılması
- Dar mod pencere yüksekliği ve iç dolguları (`500 × 232` mantıksal piksel, `76 × 76` albüm kapağı) artırılarak başlık, ilerleme çubuğu ve alt kontrol satırının ferah ve dengeli görünmesi sağlandı.

### 5. Kök Nedenli Lyrics Senkronizasyonu
- Tek zaman kaynağı (`PlaybackPosition` + `SyncOffsetMs`) üzerinden `0ms`, `10ms`, `50ms`, `100ms`, `250ms` ve `500ms` sınırlarında deterministik satır ve dolum hesaplaması doğrulandı.

### 6. Çok Satırlı Şarkı Sözlerinde Sıralı (Sequential) Beyazlaşma
- `LyricsFillProgressCalculator.SplitIntoSequentialSubLines` ve `CalculateSequentialSubLineProgresses` ile çok satırlı söz bloklarında tüm satırların aynı anda beyazlaşması önlendi.
- Önce 1. alt satır `[0.0 → 1.0]` aralığında beyazlaşır; 1. satır tamamlandığında 2. alt satır beyazlaşmaya başlar, 3. alt satır ise sırası gelene kadar ikincil (dim) renkte bekler.

### 7. Otomatik Şarkı Geçişlerinde (`A → B → C → D`) Söz Arama ve Yükleme
- GSMTC'nin otomatik parça geçişlerinde önce başlık/sanatçı, birkaç milisaniye sonra albüm kapağı veya süre göndermesi durumunda aktif LRCLIB sorgusunun iptal edilmesi (`CancellationTokenSource` çakışması) engellendi.
- `LrclibClient` içerisine süre filtresiz yedek sorgu ve ortak yayın eklerini (`- Remastered`, `(feat. ...)`) temizleyen akıllı normalize sorgu yedeklemesi eklendi.

### 8. Progress Bar Üzerinden Tıklama ve Sürükleme ile Seek
- Yatay ve Dar Mod ilerleme çubuklarına (`GridProgressHitBox`, `GridProgressCompactHitBox`) tıklama ve sürükleme (`PointerPressed`, `PointerMoved`, `PointerReleased`) desteği eklendi.
- `CalculateSeekTimeFromPointer` ile tıklanan noktanın oranı `[0.0, 1.0]` aralığında hesaplanarak `TrySeekAsync` çağrılır ve şarkı sözleri anında yeni konuma kilitlenir.

### 9. Otomatik Geçişte Eski Sözlerin Temizlenmesi ve Tüm Satırların Yüklenmesi
- Yeni şarkıya geçildiğinde eski şarkının tüm görsel satırları atomik olarak temizlenir ve yeni şarkının tüm satırları eksiksiz yüklenerek mevcut oynatma konumuna (`EvaluatePosition`) göre aktif satır hemen merkezlenir.

### 10. Otomatik Geçişte Arkaplan Renklerinin Yenilenmesi
- v3.3'teki `ArtworkPaletteExtractor` algoritması, `<= 55.0` parlaklık sınırı ve sinematik vignette yapısı korunarak, `ComputeArtworkSignature` ile gecikmeli gelen kapak görsellerinde dahi yeni şarkının renklerinin arkaplana yumuşak cross-fade ile uygulanması sağlandı.

### 11. Otomatik Geçişte Glow Renklerinin Yenilenmesi
- Otomatik şarkı geçişlerinde `MainWindow` ve `ArtworkBitmapManager`, yeni şarkının kapak imzasını (`ComputeArtworkSignature`) ve `TrackSequence` kimliğini doğrulayarak 4 kenar `GlowPalette` renklerini anında günceller ve `GlowOverlayManager.ResetBeatEngines()` ile ritim geçmişini sıfırlar.

### 12. Gelişmiş Gerçek Zamanlı Beat, Tempo ve Transient Algılama
- `FrequencyBandProcessor` ve `GlowBeatEngine` içerisine:
  - **5 Bantlı Enerji ve Akı Ayrımı:** Sub/Bass (`20–160 Hz`), Low-Mid (`160–500 Hz`), Mid (`500–2000 Hz`), High-Mid (`2000–6000 Hz`), Treble (`6000–16000 Hz`).
  - **Yarım Dalga Doğrultulmuş Spectral Flux (`SF(t)`):** Adaptif ortalama + standart sapma eşiği ile gerçek vuruş başlangıçlarının (onset) tespiti.
  - **Kısa Süreli Tempo (BPM) ve Faz Takibi (`EstimatedBpm`, `TempoConfidence`, `BeatPhase`):** Vuruş aralıklarından (`60–180 BPM`) tempo tahmini ve faz uyumu.
  - **Hızlı Ataklı Bağımsız Transient Dedektörü (`IsTransientDetected`):** Kick/bass vuruşlarından bağımsız snare, clap ve hi-hat darbelerini yakalayan hızlı zarf (Attack → Hold → Decay) ve düz beyaz gürültü (broadband steady noise) koruması.
