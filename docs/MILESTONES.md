# Lyrow — Proje Kilometre Taşları (MILESTONES.md)

Bu belge, Lyrow projesinin aşama bazlı kilometre taşlarını, ilerleme durumunu ve kontrol noktalarını özetler.

---

## Kilometre Taşları Tablosu

| Kilometre Taşı | Aşama Adı | Hedef Kapsam | Durum |
| :--- | :--- | :--- | :--- |
| **M1** | **Aşama 1: Teknik Keşif ve Mimari Kararlar** | Ortam analizi, SDK uyumluluk doğrulaması, mimari tasarım, risk analizi, temel dokümantasyon (`ARCHITECTURE.md`, `DEVELOPMENT_PLAN.md`, `PERFORMANCE.md`, `README.md`). | **TAMAMLANDI** |
| **M2** | **Aşama 2: Çekirdek Altyapı ve Solution Yapılandırması** | .NET 10 LTS SDK kurulumu ve hizalaması, `Lyrow.Core`, modül yaşam döngüsü (`ILyrowModule`), 4 çalışma durumunu yöneten `ModuleManager`, `InMemoryEventBus`, `SettingsManager`, Avalonia Neumorphic ana pencere ve 17 adet xUnit testi. | **TAMAMLANDI** |
| **M3** | **Aşama 3: Glow Modülü (Ses Analizi & Görselleştirme)** | WASAPI Loopback sistem sesi yakalama, sıfır-tahsisli FFT analiz motoru, RMS/dBFS seviye algılama, logaritmik frekans bantları, Avalonia GPU hızlandırmalı dinamik spektrum penceresi (`GlowWindow`), 19 xUnit testi. | **TAMAMLANDI** |
| **M4** | **Aşama 4: Lyrics Modülü (Metadata & Senkronize Sözler)** | Windows GSMTC medya entegrasyonu, LRCLIB REST API istemcisi, çoklu timestamp destekli LRC ayrıştırıcı, Binary Search satır seçimi, yerel disk önbelleği, Neumorphic şarkı sözü penceresi (`LyricsWindow`), 28 xUnit testi. | **TAMAMLANDI** |
| **M5** | **Aşama 5: Entegrasyon, Gerçek Windows Ortamı Doğrulaması & Polish** | Glow + Lyrics eşzamanlı çalışma, gerçek WASAPI ve GSMTC donanım doğrulaması, gerçek LRCLIB çevrimiçi/çevrimdışı önbellek akışı, Açık/Koyu/Sistem temaları, timer ve adaptif GPU optimizasyonu, zarif kaynak temizliği, 71 xUnit testi. | **TAMAMLANDI** |
| **M6** | **Aşama 6: Ürünleştirme, UX, Windows Dağıtımı ve Son Kullanıcı Deneyimi** | Win32 native click-through (`WS_EX_LAYERED`/`WS_EX_TRANSPARENT`), çoklu monitör algılama ve 7 noktalı DPI ölçekli konumlandırma (`OverlayPositionManager`), tekilleştirilmiş Ayarlar ve parlatılmış Dashboard UI, self-contained win-x64 Inno Setup yükleyicisi (`Lyrow-1.0.0-Setup-x64.exe`), taşınabilir paket (`Lyrow-win-x64-portable.zip`), modern Neumorphic logo ve ikon (`lyrow.ico`), GitHub Releases iş akışı (`release.yml`), 86 xUnit testi. | **TAMAMLANDI** |
| **M7** | **Aşama 7: System Tray, Global Hotkeys, Yaşam Döngüsü ve v1.0.0 Release** | Native Windows System Tray menüsü ve 'X' ile tepsiye küçültme, global Win32 kısayolları (`Ctrl+Shift+L/V/C`), `WM_DISPLAYCHANGE` monitör/çözünürlük/DPI adaptasyonu, LRCLIB HTTP 429 rate-limit UX ve bekleme geri sayımı, güvenli HKCU Windows başlangıç yöneticisi, 95 xUnit testi (%100 başarı), Inno Setup kurulum paketi, taşınabilir paket, `v1.0.0` resmi sürümü. | **TAMAMLANDI** |
| **M8** | **Aşama 8: v1.1 Geliştirme, Kararlılık ve Modern UI Yenilemesi** | İki parçalı sürüm kuralı (`v1.1`), Glow & Lyrics terminoloji güncellemesi, Lyrics senkronizasyon motoru kök neden çözümü (5. satır playback -> 5. satır UI senkronizasyonu), Win32 1408 hotkey thread affinity çözümü, Tekil örnek (Single-instance) & anında açılış stabilitesi, yerel tanı günlüğü (`LocalDiagnosticLogger`), Vercel + Apple ilhamlı modern masaüstü UI, ToggleSwitch bileşenleri, eşit ağırlıklı Volume/Lyrics metrik kartları, 108 xUnit testi (%100 başarı, 0 warning/failed). | **TAMAMLANDI** |

---

## Aşama 7 Doğrulama Notları

- **System Tray (Sistem Tepsisi Entegrasyonu):** Lyrow logosu bildirim alanında (`TrayIcon`) görüntülenir. Ana pencere kapatıldığında (`Close`) varsayılan olarak arka planda çalışmayı sürdürür ve tepsiye küçülür (`MinimizeToTrayOnClose`). Sağ tık menüsü üzerinden `Lyrow'u Göster`, `Glow Aç/Kapat`, `Lyrics Aç/Kapat`, `Click-Through Aç/Kapat`, `Ayarlar` ve `Çıkış` komutları anlık olarak işletilir. Gerçek `Exit` çağrısı tüm modül döngülerini, pencere referanslarını ve arka plan mesaj dinleyicilerini zarifçe sonlandırır (`Shutdown`).
- **Global Hotkeys (Sistem Genelinde Kısayollar):** `WindowsGlobalHotkeyManager` adanmış Win32 mesaj döngüsü iş parçacığı ve gizli mesaj penceresi (`CreateWindowEx`, `RegisterHotKey`) üzerinden uygulama odakta değilken de tam yanıt verir:
  - `Ctrl + Shift + L` → Glow Görselleştiriciyi açar / kapatır
  - `Ctrl + Shift + V` → Lyrics Şarkı Sözlerini açar / kapatır
  - `Ctrl + Shift + C` → Click-through modunu açar / kapatır
  Uygulama sonlanırken veya kısayollar güncellenirken tüm kayıtlar güvenle `UnregisterHotKey` ile temizlenir; çakışma durumunda uygulama çökmeden kullanıcıya hata bildirimi sunulur.
- **Dinamik Ekran Değişikliği (`WM_DISPLAYCHANGE`):** Monitör eklenmesi, çıkarılması, çözünürlük veya sistem DPI ölçeklemesi değiştiğinde `WM_DISPLAYCHANGE` ve `WM_SETTINGCHANGE` mesajları polling yapılmaksızın event-driven yakalanır. Açık olan tüm overlay pencereleri `OverlayPositionManager.ApplyPosition` çağrısıyla yeni koordinatlarına ve geçerli monitör sınırlarına anında yeniden hizalanır.
- **LRCLIB Rate-Limit UX (HTTP 429 Koruması):** LRCLIB sunucusundan HTTP 429 yanıtı geldiğinde sunucunun `Retry-After` başlığı ayrıştırılır; UI üzerinde kullanıcı dostu bekleme süresi ve geri sayım gösterilir. API'ye gereksiz tekrar istek fırlatılması engellenir ve yerel disk önbelleğindeki şarkı sözleri kesintisiz olarak önceliklendirilir.
- **Windows Açılışında Başlatma Güvenliği:** `IWindowsStartupManager` ve `WindowsStartupManager` ile `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` kaydı güvenli şekilde yönetilir. Kullanıcı açıkça işaretlemedikçe devreye girmez; Inno Setup kaldırıcısı `uninsdeletevalue` bayrağı ile kaldırma sonrasında hiçbir artık kayıt bırakmaz.
- **Test ve Release Paketi:**
  - 95 xUnit testi (%100 başarı: 20 Core + 21 Audio + 33 Music + 21 App).
  - Self-contained `win-x64` Release yayını.
  - Inno Setup 6 kurulum paketi: `Lyrow-1.0.0-Setup-x64.exe` (57.0 MB).
  - Taşınabilir zip arşivi: `Lyrow-win-x64-portable.zip` (81.6 MB).
  - Git etiketi `v1.0.0` ve resmi GitHub Release.

- **Gerçek Windows Click-Through (`WS_EX_TRANSPARENT`):** Windows native Win32 window handle (`HWND`) üzerinden `GetWindowLongPtr` ve `SetWindowLongPtr` çağrılarıyla `WS_EX_LAYERED | WS_EX_TRANSPARENT` dinamik olarak uygulanır. P/Invoke mantığı `WindowsWindowPlatformAdapter` ile UI katmanından tamamen izole edildi; Windows dışı ve test ortamları için `SyntheticWindowPlatformAdapter` sağlandı. Click-through açıldığında overlay'e gelen fare tıklamaları arka plandaki masaüstü veya uygulamalara eksiksiz geçer; kapatıldığında pencereler normal etkileşime geri döner.
- **Çoklu Monitör ve DPI Duyarlı Konumlandırma:** `OverlayPositionManager` Avalonia ekran API'si (`Screens.All`) üzerinden tüm monitörleri algılar. Kullanıcı `Center`, `TopLeft`, `TopCenter`, `TopRight`, `BottomLeft`, `BottomCenter`, `BottomRight` konumlarından birini ve hedef monitörünü seçebilir. Ekran DPI ölçeği (`Screen.Scaling`) hesaba katılarak fiziksel piksel koordinatları otomatik ölçeklenir. İkincil monitörün sökülmesi veya geçersiz indeks durumunda sistem birincil monitöre zarifçe geri döner (`ResolveTargetScreen`).
- **Tekilleştirilmiş Ayarlar ve Dashboard:** `MainWindow` son kullanıcı seviyesine taşındı. Canlı çalan medya bilgisi, anlık ses seviyesi (dB) ölçeri, şarkı sözü durum rozetleri, modül pencerelerinin açık/kapalı durumları ve tüm ayarlar (Tema, Monitör, Konum, Click-through, Glow hassasiyeti, Lyrics otomatik söz ve yerel önbellek) tek ekranda toplandı. Ayarlar `%APPDATA%\Lyrow\settings.json` altında atomik olarak saklanır ve geriye dönük migration desteklenir.
- **Windows Dağıtımı & Paketleme:**
  - Self-contained `win-x64` Release publish (`dotnet publish -c Release -r win-x64 --self-contained true`).
  - Inno Setup 6 ile derlenen `Lyrow-1.0.0-Setup-x64.exe` (57.0 MB) kurulum paketi üretildi.
  - Bağımsız taşınabilir `Lyrow-win-x64-portable.zip` (81.6 MB) arşivi üretildi.
  - Kullanıcı ayarları (`%APPDATA%\Lyrow`) yükseltme ve kaldırma süreçlerinde korunur.
- **Uygulama İkonu & Marka:** Neumorphic koyu arkaplan, neon cam efektli frekans dalgası ve müzik notası hatlarını birleştiren modern `lyrow.ico` ve `lyrow.png` tasarlandı ve uygulamaya gömüldü.
- **GitHub Actions Release:** `.github/workflows/release.yml` oluşturuldu. `v*` tag'leri ile tetiklenen Release build, test, Inno Setup derleme ve GitHub Releases varlık yüklemesi otomasyonu hazırlandı.
- **Test Kapsamı:** 86 xUnit testi (%100 başarı: 19 Core + 21 Audio + 33 Music + 13 App).

---

## Aşama 5 Doğrulama Notları

- **Gerçek Windows Donanımı ve Servisleri:** Windows WASAPI loopback aygıt listeleme ve yakalama testi (`WasapiHardwareValidationTests`) ile Windows 10/11 WinRT GSMTC yaşam döngüsü testi (`GsmtcHardwareValidationTests`) gerçek işletim sistemi katmanında doğrulandı.
- **LRCLIB Çevrimiçi & Çevrimdışı Akış:** `LrclibOnlineOfflineIntegrationTests` ile gerçek LRCLIB REST API sorgusu (Coldplay - Yellow), LRC ayrıştırma, disk önbelleğine kaydetme (`LocalFileLyricsCache`), çevrimdışı modda önbellekten okuma ve seek senkronizasyonu doğrulandı.
- **Glow ve Lyrics Eşzamanlı Çalışması:** `LyricsAndGlowSimultaneousExecutionTests` ile iki modülün eşzamanlı çalışması, olay bus'ı üzerinden yayılan spektrum ve şarkı sözü olaylarının birbirini etkilemediği, mod geçişlerinin (`BothEnabled` -> `LyricsOnly` -> `GlowOnly` -> `BothDisabled`) kusursuz işlediği kanıtlandı.
- **Tema Tutarlılığı:** `App.axaml` içerisinde `ThemeDictionaries` üzerinden Koyu (`#181825`), Açık (`#F8FAFC`) ve Sistem temaları tanımlandı; `MainWindow`, `GlowWindow` ve `LyricsWindow` dinamik kaynaklarla tam uyumlu hale getirildi.
- **Performans & Kaynak Temizliği:**
  - `GlowVisualizerControl`: Sessizlik durumunda veya modül durduğunda render yenileme hızı 60 FPS'ten 5 FPS'e çekilerek CPU tüketimi %0'a indirildi.
  - Pencereler gizlendiğinde (`Hide`) veya simge durumuna küçültüldüğünde (`Minimized`) render ve UI timer'ları durduruldu; tekrar açıldığında otomatik devam ettirildi.
  - UI fırçaları (`SolidColorBrush`) statik/sabit havuzda tutularak heap tahsisleri engellendi.
  - 100,000 Binary Search satır araması yalnızca 9 milisaniye sürdü ve sıfır ek bellek tahsisi ölçüldü.
  - Uygulama kapanışında (`OnAppExit` ve `ShutdownWindow`) WASAPI, GSMTC, modül döngüleri ve DI konteyneri zarifçe serbest bırakıldı.
- **Test Kapsamı:** `Lyrow.Core.Tests` (17 test), `Lyrow.Audio.Tests` (21 test) ve `Lyrow.Music.Tests` (33 test) olmak üzere toplam 71 test Release modunda %100 başarıyla geçti.

---

## Aşama 4 Doğrulama Notları

- **Windows GSMTC Entegrasyonu:** `WindowsGsmtcMediaSessionMonitor` ile Windows 10/11 WinRT `GlobalSystemMediaTransportControlsSessionManager` API'si üzerinden çalınan şarkı başlığı, sanatçısı, albümü, kapağı ve oynatma durumu (`Playing`, `Paused`, `Stopped`) yakalanır.
- **LRCLIB REST API İstemcisi:** `LrclibClient` ile şarkı sözü sorguları yapılır. 404 (Not Found), 429 (Rate Limit) ve ağ kesintisi/zaman aşımı durumları exception fırlatılmaksızın güvenle ele alınır.
- **LRC Ayrıştırma & Binary Search:** `LrcParser` çoklu zaman etiketlerini (`[00:10.00][00:20.00]`), 2/3 basamaklı milisaniyeleri ve kronolojik sıralamayı destekler. $O(\log N)$ Binary Search ile aktif satır anında belirlenir; ileri/geri seek durumlarında doğru satıra atlar.
- **Yerel Disk Önbelleği:** `LocalFileLyricsCache` ile çekilen şarkı sözleri `%APPDATA%\Lyrow\LyricsCache` altında güvenli dosya adlarıyla (`SanitizeFileName`) atomik olarak önbelleğe alınır; çevrimdışı modda anında yüklenir.
- **Görselleştirici:** Avalonia 12 üzerinde modern Neumorphic şarkı sözü penceresi (`LyricsWindow`) hazırlandı. Aktif satır vurgusu, pürüzsüz otomatik kaydırma ve manuel ms zaman kaydırma butonları uygulandı.
- **Test Kapsamı:** `Lyrow.Music.Tests` altında 28 test, `Lyrow.Audio.Tests` altında 19 test ve `Lyrow.Core.Tests` altında 17 test olmak üzere toplam 64 xUnit testi Release modunda %100 başarıyla geçti.
- **Modüler Bağımsızlık:** Lyrics modülü Glow olmadan tek başına başlatılıp durdurulabilir; modüller yalnızca `IEventBus` sözleşmeleri üzerinden haberleşir.

---

## Aşama 3 Doğrulama Notları

- **WASAPI Loopback:** `WasapiAudioCaptureService` ile Windows varsayılan çıkış aygıtı doğrudan sürücü katmanından dinlenir. Aygıt kaybı (`AUDCLNT_E_DEVICE_INVALIDATED`) durumunda 500 ms içinde otomatik yeniden bağlanma döngüsü test edildi.
- **Sıfır Tahsisli FFT:** `FftAnalyzer` önceden hesaplanmış Hanning penceresi ve twiddle trigonometri tabloları ile sıfır heap tahsisiyle çalışır.
- **Müzikal Frekans Ayrıştırma:** Sub-bass (20-60 Hz), Bass (60-250 Hz), Mid (250-4000 Hz), Treble (4000-20000 Hz) ve 32 logaritmik çubuk `FrequencyBandProcessor` tarafından hesaplanır.
- **Görselleştirici:** Avalonia 12 üzerinde donanım hızlandırmalı GPU çizimi (`GlowVisualizerControl`) ve bağımsız spektrum penceresi (`GlowWindow`) 60 FPS hızında test edildi. Sessizlik anında CPU tüketimi %0 seviyesine inmektedir.
- **Test Kapsamı:** `Lyrow.Audio.Tests` altında 19 test ve `Lyrow.Core.Tests` altında 17 test olmak üzere toplam 36 xUnit testi Release konfigürasyonunda %100 başarıyla geçti.
- **Bağımsızlık:** Glow modülü Lyrics olmadan tek başına başlatılıp durdurulabilir; modüller arası izolasyon prensibi korundu.
