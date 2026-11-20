# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [4.4.0] - 2026-11-20

### Added
- **Ortak Güncelleme Koordinatörü (`UpdateCoordinator`):** Sağ üst güncelleme toast bildirimi ve **Ayarlar > Güncellemeler** sekmesi aynı tekil, iş parçacığı güvenli güncelleme akışına bağlandı; eşzamanlı çift indirme/kurulum engellenir, kurulum öncesi kullanıcı ayarları güvenle diske yazılır, PE + SHA-256 doğrulaması başarısız olursa dosya silinip yeniden denemeye izin verilir.
- **Sağ Üst Windows 11 Fluent Güncelleme Toast Bildirimi:** Yeni sürüm bulunduğunda Normal, Tam Ekran ve Dar modların tamamında sağ üst köşede `ThemeShadow` gölgeli, Açık/Koyu tema duyarlı, klavye (`Esc`) ve ekran okuyucu erişilebilir toast bildirimi gösterilir; **Şimdi Kur** indirme/kurulumu başlatır, **Sonra** aynı oturumda bildirimi kapatır ancak güncelleme bilgisini Ayarlar'da korur.
- **Zenginleştirilmiş Ayarlar > Güncellemeler Sekmesi:** Denetim sırasında `ProgressRing` yükleniyor göstergesi; yeni sürüm bulunduğunda sürüm numarası, yayın tarihi (`published_at`), kısa sürüm özeti, belirgin **Yeni Sürümü Kur** butonu, canlı indirme/kurulum ilerleme çubuğu, hata durumunda anlaşılır mesaj + **Tekrar Dene** butonu ve MSIX/Portable dağıtımları için açıklayıcı yönlendirme eklendi.
- **v4.4 Otomatik Güncelleme Test Paketi:** `UpdateCoordinatorTests.cs` (11 yeni davranışsal test) ve `LyrowV44UpdateExperienceTests.cs` (4 yeni arayüz/konum/tema/bağlantı doğrulama testi) eklendi.

### Changed
- **GitHub Releases Meta Veri & SHA-256 Çözümlemesi (`GitHubUpdateService`):** GitHub API'den `published_at` yayın tarihi ve asset `digest` (`sha256:...`) değeri doğrudan okunur (release notes regex geri dönüşü korunarak); `App.CurrentReleaseVersion` (`"4.4"`) çalışma zamanı sürüm karşılaştırmasına bağlandı.
- **Yerelleştirme (`LocalizationService`):** `Updates_InstallNow` (`Şimdi Kur` / `Install Now`), `Updates_InstallNewVersion` (`Yeni Sürümü Kur` / `Install New Version`), `Updates_Later` (`Sonra` / `Later`), `Updates_PublishedOn`, `Updates_StartingInstall`, `Updates_VerifyFailed`, `Updates_UnsupportedMsix`, `Updates_UnsupportedPortable` ve `Updates_NoInstallerAsset` anahtarları tüm dillerde eklendi/güncellendi.

### Validation
- **Birim ve Entegrasyon Testleri (Deterministik/Seri Koşu):** 728 / 728 PASS (%100 başarı).
- **Gerçek Çalıştırma Doğrulaması:** Hem `publish\win-x64\Lyrow.App.exe` hem `AppData\Local\Programs\Lyrow\Lyrow.App.exe` `--smoke-test` (`ExitCode: 0`) ve canlı pencere açılışıyla doğrulandı.
- **Dağıtım Paketleri:** `Lyrow-4.4-Setup-x64.exe`, `Lyrow-v4.4-win-x64-portable.zip` ve `Lyrow-v4.4-x64.msix` (imzasız) üretildi ve SHA-256 doğrulamasından geçti.

---

## [4.3.0] - 2026-11-20

### Added
- **Normal Mod & Tam Ekran Mod — İkon-Odaklı Audio Output Butonu (`BtnDeviceSelectorToolbar`):** Kenar Işığı butonunun hemen sağına `28×28` Ghost Button olarak eklendi; mevcut ses çıkış menüsünü paylaşır, aktif cihazı Tooltip ile gösterir ve Normal/Tam Ekran/Dar Modlar arasında anında senkronize çalışır.
- **Ortak Kenar Işığı Paneli — Parlaklık (`10%–100%`) ve Kalınlık (`0–64 px`):** Hem Normal/Tam Ekran Modda hem Dar Modda Kenar Işığı butonuna tıklandığında birebir aynı ortak panel (`FlyoutEdgeGlowThickness`) açılır; üstte **Kenar Işığı Parlaklığı (`SliderEdgeGlowBrightness`)**, altta **Kenar Işığı Kalınlığı (`SliderEdgeGlowThickness`)** sürgüleri min-max uç etiketleri olmadan sade biçimde yer alır; mevcut `GlowIntensity` ve `GlowThickness` ayarları üzerinden anlık uygulanır, kalıcı kaydedilir ve Ayarlar sayfasıyla senkronize çalışır.
- **Dar Mod — Kenar Işığı Butonu (`BtnToggleEdgeGlowCompact`):** Compact Lyrics Show/Hide butonunun hemen soluna `26×26` ikon-odaklı Kenar Işığı butonu eklendi; tıklandığında Normal Modla birebir aynı Parlaklık + Kalınlık panelini dar pencereden taşmadan açar, `0 px`'te Kenar Işığını tamamen kapatır ve mod geçişlerinde ayarları senkronize korur.
- **v4.3 Doğrulama Test Paketi:** Dar Mod 6 Ghost Button, Normal/Tam Ekran/Dar Mod ikon-odaklı Audio Output senkronizasyonu (Play/Pause üzerindeki eski dropdown'un kaldırılması dahil), ortak Kenar Işığı Parlaklık + `0–64 px` Kalınlık paneli (min-max etiketsiz) ve 7 sekmeli sola hizalı sabit boyutlu Windows 11 Fluent ayarlar tasarımını doğrulayan testler (`LyrowV43FeatureVerificationTests.cs`) eklendi.

### Changed
- **Normal Mod — Play/Pause Üzerindeki Eski Ses Çıkışı Dropdown'u Kaldırıldı:** Play/Pause butonunun hemen üstünde bulunan eski metinli `BtnDeviceSelector` dropdown'u kaldırılarak ses çıkışı kontrolü araç çubuğundaki ikon-odaklı `BtnDeviceSelectorToolbar` butonuna taşındı; Dar Moddaki `BtnDeviceSelectorCompact` korundu.
- **Dar Mod — Ghost Button Tasarımı:** Volume & Mute, Play/Pause, Previous, Next, Normal Mode ve Compact Lyrics Show/Hide butonlarının tamamı Açık/Koyu tema uyumlu `LyrowGhostButtonStyle` tasarımına geçirildi.
- **Dar Mod — İkon-Odaklı Audio Output Butonu (`BtnDeviceSelectorCompact`):** Metin etiketi tamamen kaldırılarak Volume & Mute ile birebir aynı **`26×26` (`CornerRadius="6"`)** Ghost Button boyutuna getirildi; aktif ses çıkış aygıtı Tooltip üzerinden gösterilir ve cihaz seçme işlevi korunur.
- **Kenar Işığı Etkileşimi (`0–64 px`):** Hover ile açılma kaldırıldı; tıklamayla açılıp kapanan ortak panelde `0 px` Kenar Işığını tamamen kapatır, `1–64 px` anlık kalınlık uygular ve mod geçişlerinde otomatik kapanır.
- **Ayarlar Sayfası — Sola Hizalı Sidebar, Sabit Boyut ve 7 Sekmeli Windows 11 Fluent Design:** `LyrowGhostButtonStyle` içerik hizalaması düzeltilerek sidebar öğeleri tutarlı şekilde sola hizalandı; modal genişlik/yüksekliği (`740×520`) sabitlenerek sekme geçişlerinde boyut sıçraması önlendi; ayarlar **Genel**, **Görünüm**, **Işık**, **Şarkı Sözleri**, **Sistem**, **Güncellemeler** ve **Hakkında** (Kısayollar dahil) olmak üzere tam 7 ayrı sekmede düzenlendi (tüm mevcut ayarlar, Açık/Koyu/Sistem teması, Türkçe/İngilizce yerelleştirme ve ESC davranışı korunarak).

### Validation
- **Birim ve Entegrasyon Testleri (Deterministik/Seri Koşu):** 713 / 713 PASS (%100 başarı).
- **Gerçek Çalıştırma Doğrulaması:** Hem `publish\win-x64\Lyrow.App.exe` hem `AppData\Local\Programs\Lyrow\Lyrow.App.exe` `--smoke-test` (`ExitCode: 0`) ve canlı pencere açılışıyla doğrulandı.
- **Dağıtım Paketleri:** `Lyrow-4.3-Setup-x64.exe`, `Lyrow-v4.3-win-x64-portable.zip` ve `Lyrow-v4.3-x64.msix` (imzasız) üretildi ve SHA-256 doğrulamasından geçti.

---

## [4.2.0] - 2026-11-19

### Added
- **Albüm Kapağına Göre Otomatik Kenar Işığı Rengi:** Çalan parçanın albüm kapağı analiz edilerek baskın ve temsil edici renkler çıkarılır (`ArtworkColorExtractor` + içerik özeti önbellekli `ArtworkBitmapManager`), Kenar Işığı (`GlowOverlayManager`) bu renklere yumuşak cross-fade animasyonuyla otomatik geçiş yapar; kapak bulunamazsa varsayılan palete güvenli dönüş yapılır.
- **Kenar Işığı Kalınlığı Hover Sürgüsü:** Kenar Işığı aç/kapa butonunun (`BtnToggleEdgeGlow`) üzerine gelindiğinde Dar Moddaki ses açılır kontrolüne benzer akıcı kalınlık ayar sürgüsü (`FlyoutEdgeGlowThickness` / `SliderEdgeGlowThickness`) açılır; kalınlık anlık güncellenir, fare ayrıldığında gecikmeli kapanır, klavye (Yukarı/Aşağı ok) ile erişilebilir ve kalıcı kaydedilir.
- **Şarkı Sözlerini Göster/Gizle — 2× / 0.5× Akıcı Pencere Genişliği Geçişi:** `LyricsWindowWidthAnimatorCalculator` ile Normal Modda sözler gizlendiğinde pencere X genişliği tam yarıya iner, tekrar açıldığında tam iki katına çıkar; Y konumu ve pencere yüksekliği korunur, ekran dışı taşma engellenir, farklı DPI ve çözünürlükler ile hızlı çift tıklama iptali desteklenir.
- **v4.2 Doğrulama Test Paketi:** Kapak renk analizi ve önbellekleme, Kenar Işığı renk eşitleme, kalınlık hover sürgüsü, Dar Modda üç nokta gizleme, 7 Ghost Button stili, Windows 11 iki panelli ayarlar modalı ve DPI/çözünürlük/ekran kenarı 2×/0.5× pencere genişliği hesaplamalarını doğrulayan 12 yeni test (`LyrowV42FeatureVerificationTests.cs`) eklendi.

### Changed
- **Modern Windows 11 Ayarlar Sayfası:** Ayarlar modalı sıfırdan iki panelli Windows 11 Fluent düzeniyle tasarlandı — solda vurgu çizgili ve ikonlu kategori navigasyonu (`SettingsModalSidebar`), sağda bağımsız kaydırılabilir Settings Cards içeriği, dar pencerede otomatik kompakt ikon rayı, Açık/Koyu tema ve eksiksiz Türkçe/İngilizce yerelleştirme ("Tümü" sekmesi olmadan, tüm mevcut ayarlar korunarak).
- **Ghost Button Tasarımı:** Dar Mod (`BtnDarMod`), Tam Ekran (`BtnFullscreen`), Önceki Parça (`BtnPrev`), Sonraki Parça (`BtnNext`), Oynat/Duraklat (`BtnPlayPause` — merkez konumu ve 46×46 boyut hiyerarşisi korunarak), MS azaltma (`BtnOffsetMinus`) ve MS artırma (`BtnOffsetPlus`) butonları `LyrowGhostButtonStyle` tasarımına geçirildi.
- **Dar Modda Üç Nokta Butonu:** Dar Modda sol üst köşedeki üç nokta ayarlar butonu (`BtnOpenSettingsModal`) tamamen gizlenir, Normal Moda dönüldüğünde tekrar görünür.

### Validation
- **Birim ve Entegrasyon Testleri:** 705 / 705 PASS (%100 başarı).
- **Dağıtım Paketleri (Yerel):** `Lyrow-4.2-Setup-x64.exe`, `Lyrow-v4.2-win-x64-portable.zip` ve `Lyrow-v4.2-x64.msix` (imzasız) yerel olarak üretildi ve SHA-256 doğrulamasından geçti.

---

## [4.1.0] - 2026-11-19

### Added
- **MSIX x64 Paket Desteği:** Dağıtım pipeline'ına (`installer/build_packages.ps1` + `installer/AppxManifest.xml.template`) standart Windows x64 MSIX paket üretimi (`Lyrow-v4.1-x64.msix`) eklendi. Her sürümde artık Windows Setup EXE, Portable ZIP ve MSIX x64 olmak üzere 3 paket otomatik üretilir ve SHA-256 doğrulamasından geçer.
- **Kenar Işığı (Edge Light) Hızlı Kontrol Butonu:** Lyrics penceresinin alt kontrol satırında `- ms` butonunun soluna Kenar Işığı açma/kapama butonu (`BtnToggleEdgeGlow`) eklendi ve mevcut `GlowModule` + `GlowOverlayManager` altyapısına bağlandı.
- **v4.1 Doğrulama Test Paketi:** Glow varsayılan kapalı durumu, aktif buton vurgu renkleri, Dar Modda Glow izolasyonu, sözler gizlendiğinde Glow butonunun gizlenmesi, alt kontrol satırı 28×28 boyut eşitliği ve geometrik ortalama, `Tümü/All` sekmesinin kaldırılması, `MainWindow`'un tamamen kaldırılması ve MSIX manifest/pipeline doğrulaması için 8 yeni otomatik test (`LyrowV41FeatureVerificationTests.cs`) eklendi.

### Changed
- **Kontrol Paneli Penceresinin Tamamen Kaldırılması:** Eski `MainWindow.xaml` ve `MainWindow.xaml.cs` tamamen kaldırıldı; `LyricsWindow` uygulamanın tek birincil penceresi (`App.PrimaryWindowInstance`) haline getirildi. Sistem tepsisi, tek-örnek uyandırma, global kısayollar ve güncelleme denetimi doğrudan `LyricsWindow` yaşam döngüsüne bağlandı.
- **Alt Kontrol Satırı Düzeni ve Hizalaması:** Kenar Işığı (`BtnToggleEdgeGlow`), `- ms`, `+ ms`, Şarkı Sözü Parlaması (`BtnToggleLyricsGlow`) ve Şarkı Sözlerini Göster/Gizle (`BtnToggleLyrics`) butonlarının tamamı eşit `28×28` (`CornerRadius="6"`) boyutuna getirildi; `- ms / 0 ms / + ms` grubu simetrik `*,Auto,*` ızgarasıyla Play/Pause butonunun yatay merkezine geometrik olarak hizalandı.
- **Glow (Şarkı Sözü Parlaması) Davranışı:** Temiz kurulumda varsayılan olarak kapalı (`false`) başlar, kullanıcı tercihi kaydedilir, aktifken Play/Pause ile birebir aynı vurgu renklerini (`AccentButtonStyle`) kullanır, şarkı sözleri gizlendiğinde tercih değiştirilmeden buton da gizlenir ve sözler tekrar açıldığında geri döner.
- **Ayarlar Modalı Sadeleştirme ve İsimlendirme Birliği:** **"Tümü" / "All"** sekmesi ve `Tab_All` yerelleştirme anahtarları tüm dillerden kaldırıldı. Türkçe ve İngilizce arayüz metinleri denetlenerek **"Kenar Işığı" / "Edge Light"**, **"Şarkı Sözü Parlaması" / "Lyrics Glow"** ve **"Dar Mod" / "Compact Mode"** terimleri tüm arayüzde tutarlı hale getirildi.

### Removed
- **Dar Modda (Kompakt Mod) Glow Kaldırıldı:** Dar Moddaki Glow butonu (`BtnToggleLyricsGlowCompact`) ve parlama efektleri tamamen kaldırılarak sıfır GPU/CPU yükü sağlandı; Normal Moda dönüldüğünde kullanıcının Glow tercihi korunur.
- **Eski Kontrol Paneli (`MainWindow`):** `src/Lyrow.App/MainWindow.xaml` ve `src/Lyrow.App/MainWindow.xaml.cs` projeden tamamen silindi.

### Validation
- **Birim ve Entegrasyon Testleri:** 692 / 692 PASS (%100 başarı).
- **Dağıtım Paketleri (Yerel):** `Lyrow-4.1-Setup-x64.exe`, `Lyrow-v4.1-win-x64-portable.zip` ve `Lyrow-v4.1-x64.msix` (imzasız) yerel olarak üretildi ve SHA-256 doğrulamasından geçti.

---

## [4.0.0] - 2026-11-19

### Added
- **Açılışta Otomatik Lyrics Penceresi:** Lyrow başlatıldığında Lyrics penceresi otomatik olarak açılır ve kullanıcı doğrudan şarkı sözleri arayüzünü görür; tekil pencere koruması (`_isOpeningLyricsWindow` & `_lyricsWindow` guard) ile aynı pencerenin birden fazla kez oluşturulması engellenir.
- **Şarkı Sözü Parlama (Lyrics Glow) Açma/Kapama Butonu:** Hem Normal Modda ("Şarkı Sözlerini Göster/Gizle" butonunun solunda) hem de Kompakt Modda yer alan, neumorphism tasarım diline uygun, açık (`\uE706`) ve kapalı (`\uE708`) durumları görsel olarak ayrışan ve sıfır bellek tahsisli GPU hızlandırmalı parlama efektini kontrol eden buton eklendi.
- **Sol Üst Üç Nokta (`...`) Ghost Button & Merkezlenmiş Modal Ayarlar Penceresi:** Lyrics penceresinin en sol üst köşesine Ghost Button (`\uE712`) eklendi; tıklandığında pencere boyut ve konumunu değiştirmeden arka planı yarı saydam koyu katmanla karartan, arka plan etkileşimini engelleyen, bağımsız kaydırılabilen, ESC ve kapatma butonuyla kapanan, yumuşak açılış/kapanış animasyonlu ve tüm Lyrow ayarlarını içeren merkezlenmiş modal ayarlar penceresi eklendi.
- **v4.0 Doğrulama Test Paketi:** Açılış davranışı, Glow butonu konum/durum/geriye dönük uyumluluk, Ghost Button & Modal Ayarlar, yeni Dar Mod ikonu, Açık/Koyu tema ve Türkçe/İngilizce yerelleştirme kapsamını doğrulayan 8 yeni otomatik test (`LyrowV40FeatureVerificationTests.cs`) eklendi.

### Changed
- **Dar Mod (Kompakt Görünüm) Buton İkonu:** Lyrics penceresindeki Dar Mod butonu ikonu (`BtnDarMod`), kompakt görünüme geçişi daha anlaşılır temsil eden Windows 11 Segoe Fluent Icons `BackToWindow` (`\uE73F`) ikonu ile güncellendi ve Kompakt Moddan Normal Moda dönüş ikonu (`\uE740`) ile görsel olarak eşleştirildi.

### Validation
- **Birim ve Entegrasyon Testleri:** 684 / 684 PASS (%100 başarı).
- **Dağıtım Paketleri:** Portable ZIP (`Lyrow-v4.0-win-x64-portable.zip`) ve Inno Setup Installer (`Lyrow-4.0-Setup-x64.exe`) başarıyla üretildi ve SHA-256 doğrulamasından geçti.

---

## [3.8.0] - 2026-11-11

### Added
- **Karaoke Kelime Vurgulama Motoru:** Kelime zaman damgası (word-level timing) içeren şarkı sözlerinde aktif kelimelerin playback akışına paralel olarak soldan sağa beyaz renkte pürüzsüz biçimde vurgulanması sağlandı.
- **Satır Sarmalı (Wrapped) Karaoke Desteği:** İki veya daha fazla alt satıra bölünen uzun sözlerde, 1. alt satır tamamlanmadan ve 2. satırın zaman damgası gelmeden 2. satırdaki kelimelerin erken boyanması kesinlikle engellendi.
- **Stage 41 Regresyon ve Doğrulama Test Paketi:** Karaoke kelime vurgulama zamanlaması, alt satır izolasyonu, düz senkronize satır uyumu ve düzen koruma testleri eklendi.

### Changed
- **Normal Mod Pencere Düzeni ve Boyutlandırma:** Normal modda "Hide Lyrics" butonu, albüm kapağı, cihaz seçimi ve tüm alt oynatma kontrolleri ilk açılışta ve farklı DPI ölçeklerinde pencere içinde eksiksiz görünecek şekilde optimize edildi.
- **Kompakt Mod Geçiş Simgesi:** Dar moddan normal moda geçiş butonu simgesi standart köşegen genişletme simgesi (`\uE740`) ile güncellendi.
- **Ayarlar Sekme Yapısı:** Kullanıcı arayüzünde işlevsiz kalan Sistem sekmesi tamamen kaldırılarak Güncellemeler ve Kısayollar sekmeleri öne çıkarıldı.

### Fixed
- **Karaoke Vurgulama Regresyonu:** WinUI 3'te `Visibility.Collapsed` olarak başlatılan TextBlock elemanlarının ölçümünün sıfırlanması sonucu kelimelerin boyanmaması sorunu giderildi; layout ölçümü korunurken `RectangleGeometry` kırpmasıyla akıcı 60 FPS Apple Music stili boyama sağlandı.
- **Şarkı Geçişinde Kalan Süre Eşitlemesi:** Parça geçişlerinde yeni şarkının toplam süresi GSMTC'den okunana kadar kalan sürenin eski parçaya ait kalması engellendi; süreler anında sıfırlanıp yeni parça süresiyle eşzamanlanır.
- **Ayarlar Yerelleştirmesi:** Türkçe ve İngilizce dil dosyalarındaki eksik çeviri anahtarları tamamlandı ve tutarlı hale getirildi.

### Validation
- **Birim ve Entegrasyon Testleri:** 676 / 676 PASS (%100 başarı).
- **GitHub Actions CI:** Run `43065963040` — SUCCESS.
- **Dağıtım Paketleri:** Portable ZIP (`Lyrow-v3.8-win-x64-portable.zip`) ve Inno Setup Installer (`Lyrow-3.8-Setup-x64.exe`) başarıyla üretildi.

---

## [3.7.0] - 2026-11-10

### Added
- **Merkezi Playback Timeline Motoru (`IPlaybackTimelineEngine`):** Tüm pencereler (`MainWindow`, `LyricsWindow`), arka plan servisleri (`LyricsModule`) ve donanım oturum yöneticisi (`WindowsGsmtcMediaSessionMonitor`) için tek ve yetkili zaman çizelgesi motoru mimarisi eklendi.
- **Deterministik Zaman Formatlayıcıları:** Geçen süre için saniye tabanlı tabanlama (`FormatElapsed`), kalan süre için enterpolasyon kaynaklı salınımları engelleyen yuvarlama (`FormatRemaining`) eklendi.
- **Stage 33 Regresyon Test Paketi:** 13 yeni otomatik test ile parça geçiş izolasyonu, geçen ve kalan süre hesaplamaları, seek ve duraklatma senaryoları doğrulandı.

### Changed
- **Lyrics Penceresi Başlık Sadeleştirmesi:** Lyrics penceresinin sol üst köşesinde yer alan `Lyrow — Synchronized Lyrics` metni kaldırılarak başlık çubuğu alanı temiz ve minimalist bir görünüme kavuşturuldu.
- **Zaman Çizelgesi İzolasyonu:** Otomatik parça geçişlerinde (`A → B`) önceki şarkının süresi ve son pozisyonu tamamen temizlenerek yeni parçanın zaman çizelgesine taşınması engellendi.
- **Normal ve Dar Mod Eşzamanlılığı:** Hem Normal Mod hem de Dar Mod aynı merkezi zaman motorunu dinleyecek şekilde eşitlendi.

### Fixed
- **Kümülatif Playback Time Hatası:** Dinlenen sürenin önceki parçaların sürelerini toplayarak artması (örn. 3:30 + 1:01 = 4:31) kök neden analiziyle giderildi; geçen süre artık yalnızca o an çalan parçanın süresini (örn. `1:01`) gösterir.
- **Kalan Süre Hesaplama Hatası:** Kalan sürenin kümülatif süreler üzerinden hesaplanması düzeltildi; kalan süre bağımsız olarak `Parça Süresi - Geçen Süre` formülüyle (örn. `-2:10`) hesaplanır.
- **Stale GSMTC Callback Koruması:** Otomatik parça geçişi sonrasında eski parçanın son saniyelerini içeren gecikmeli GSMTC callback'lerinin yeni parçanın zaman çizelgesini bozması engellendi.

### Validation
- **Birim ve Entegrasyon Testleri:** 635 / 635 PASS (%100 başarı).
- **GitHub Actions CI:** Run `42788361887` — SUCCESS.
- **Smoke Test:** Windows x64 self-contained yürütülebilir dosyası üzerinde başarıyla doğrulandı.

---

## [3.6.0] - 2026-11-08

### Added
- **İlerleme Çubuğu ve Söz Satırından Konum Sarma (Seek):** İlerleme çubuğu ve senkronize şarkı sözü satırları üzerinden doğrudan şarkı konumu atlama (`TryChangePlaybackPositionAsync`).
- **Gecikme Giderici Zaman Enterpolasyonu:** GSMTC oturumlarının `Playing` durumundaki zaman çizelgesi `pos + (UtcNow - LastUpdatedTime)` formülüyle enterpole edilerek şarkı sözlerindeki 3 saniyelik gecikme giderildi.
- **Compact Player Genişletilebilir Şarkı Sözü Paneli:** Mini oynatıcı modunda ekran sınırlarını koruyarak aşağı doğru açılan kompakt şarkı sözü paneli.
- **WASAPI Ses Aygıtı Seçimi:** Lyrics penceresinden varsayılan ses çıkış/yakalama aygıtının anında değiştirilmesi ve loopback oturumunun yeniden bağlanması.

### Changed
- **Ayarlar Sekmesi:** Tekrarlayan ayarlar sadeleştirildi.
- **Animasyonlu Albüm Kapağı & Erişilebilirlik:** Windows Reduced Motion tercihlerine tam uyum sağlandı.

### Validation
- 566 / 566 test PASS.
- GitHub Actions CI SUCCESS.

---

## [3.5.0] - 2026-10-31

### Added
- **Yatay Taşmayan Şarkı Sözü Sarma (`TextWrapping`):** Uzun sözlerin pencere kenarlarından taşması engellendi.
- **Yüksek Çözünürlüklü & Animasyonlu Kapak Desteği:** Donanım hızlandırmalı animasyonlu GIF ve WebP kapak oynatma.
- **24px Varsayılan Edge Bloom:** Glow parlama kalınlığı dengeli 24px değerine ayarlandı.
- **Gelişmiş Beat ve Tempo Algılama:** 5 bantlı enerji ayrımı ve Spectral Flux vuruş takibi.

### Validation
- 362 / 362 test PASS.

---

## [3.4.0] - 2026-10-31
- Duyarlı (responsive) oynatıcı düzeni ve pencere yeniden boyutlandırma optimizasyonları.
- LRCLIB çok aşamalı arama stratejisi ve yerel şarkı sözü önbellekleme.

---

## [3.3.0] - 2026-10-31
- Apple Music benzeri 60 FPS dikey merkezlenen karaoke şarkı sözü akışı.
- Sol-sağ karakter/kelime dolum animasyonları.

---

## [3.2.0] - 2026-10-25
- Windows GSMTC oturum dinleme ve metadata normalizasyonu geliştirmeleri.
- Otomatik parça geçişlerinde temiz yükleme durumları.

---

## [3.1.0] - 2026-10-24
- 4 kenar ortam aydınlatması (Top, Bottom, Left, Right) ve WASAPI Loopback ses yakalama motoru.
- Sıfır tahsisli (zero-allocation) FFT analiz algoritması.

---

## [3.0.0] - 2026-10-24
- Windows 11 Native WinUI 3 ve Windows App SDK mimarisine geçiş.
- Neumorphic kullanıcı arayüzü ve Fluent Mica arka plan entegrasyonu.

---

## [2.0.0] - 2026-10-20
- İlk Windows 11 yerel arayüz prototipleri ve modüler mimari tasarımı.

---

## [1.0.0] - 2026-10-16
- İlk sürüm: Temel ortam aydınlatması ve şarkı sözü yakalama altyapısı.
