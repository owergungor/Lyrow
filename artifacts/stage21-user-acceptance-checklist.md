# Lyrow — Stage 21: Son Kullanıcı Kabul Testi Kontrol Listesi (User Acceptance Checklist)

> **Hedef Sürüm:** Lyrow v3.5
> **Kullanım Amacı:** Fiziksel Windows 10/11 ortamında son kullanıcı tarafından 5–10 dakika içinde uygulanabilecek kabul testleri.
> **Tanı Günlüğü Konumu:** `%APPDATA%\Lyrow\logs\app.log`

---

## 1. Hızlı Doğrulama Matrisi

| # | Test Senaryosu | Nasıl Uygulanır? | Beklenen Sonuç | Sonuç | Sorun Halinde İncelenecek Log / Hata |
|---|---|---|---|:---:|---|
| 1 | **SmartScreen Gözlemi** | İndirilen `Lyrow-3.5-Setup-x64.exe` dosyasını çalıştırın. | "Windows kişisel bilgisayarınızı korudu" ekranı çıkar. "Ek bilgi" -> "Yine de çalıştır" tıklandığında kurulum sihirbazı açılır. | `[ ] PASS`<br>`[ ] FAIL` | Windows Defender SmartScreen olay günlükleri veya indirme Mark-of-the-Web zone identifier. |
| 2 | **Apple Music / GSMTC Canlı Oturumu** | Apple Music masaüstü uygulamasında (veya Spotify'da) bir parça başlatın ve Lyrow'u açın. | Lyrow penceresinde çalan şarkının adı, sanatçısı ve albüm kapağı anında görüntülenir. | `[ ] PASS`<br>`[ ] FAIL` | `%APPDATA%\Lyrow\logs\app.log` içinde `[GsmtcMediaSessionService]` logları. |
| 3 | **Hızlı Şarkı Değişimi & Metadata** | Oynatıcıda arka arkaya 3-4 şarkı ileri sarın / değiştirin. | Arayüz donmaz; şarkı adı, albüm kapağı ve sözler yeni parçaya gecikmesiz senkronize olur. | `[ ] PASS`<br>`[ ] FAIL` | `%APPDATA%\Lyrow\logs\app.log` içinde `OnTimelinePropertiesChanged` ve LRCLIB istek kayıtları. |
| 4 | **Ambient Glow Gerçek Ses Tepkisi** | Bir müzik çalarken **Ambient Glow** özelliğini açın. Müziği duraklatın ve tekrar başlatın. | Müzik durduğunda aydınlatma sakinleşir/söner; müzik başladığında ritim ve bas frekanslarına göre akıcı renk dalgalanması başlar. | `[ ] PASS`<br>`[ ] FAIL` | `%APPDATA%\Lyrow\logs\app.log` içinde `[AudioCaptureService]` ve `[GlowModule]` kayıtları. |
| 5 | **3.5 mm Ses Jakı Hotplug** | Ambient Glow açıkken ve kulaklıktan müzik dinlerken 3.5 mm ses jakını aniden çekin; 5 sn sonra geri takın. | Sistem hoparlörüne geçişte ve jak tekrar takıldığında Lyrow çökmez; ses yakalama yeni cihaza bağlanarak devam eder. | `[ ] PASS`<br>`[ ] FAIL` | `%APPDATA%\Lyrow\logs\app.log` içinde WASAPI default audio endpoint change logları. |
| 6 | **USB Ses Aygıtı Hotplug** | USB kulaklık veya harici USB DAC üzerinden müzik çalarken USB kablosunu çıkarıp 3 sn sonra geri takın. | Uygulama donmaz veya çökmez; USB aygıtı geri bağlandığında Ambient Glow çalışmaya devam eder. | `[ ] PASS`<br>`[ ] FAIL` | `%APPDATA%\Lyrow\logs\app.log` içinde `AudioCaptureService: Endpoint reconnect` kayıtları. |
| 7 | **Windows Narrator İşitsel Kontrol** | `Win + Ctrl + Enter` ile Narrator'ı açın; `Tab` ve ok tuşlarıyla Lyrow butonları ve ayarları üzerinde gezinin. | Her buton, kaydırıcı ve sekme için Narrator anlamlı Türkçe/İngilizce isim ve durum bilgisi seslendirir. | `[ ] PASS`<br>`[ ] FAIL` | XAML `AutomationProperties.Name` ve erişilebilirlik ağacı. |
| 8 | **Sistem Tepsisi & Global Kısayollar** | Kapat veya küçült butonuna basın; sistem tepsisi (System Tray) simgesine çift tıklayın veya Compact Player kısayolunu kullanın. | Pencere tepsiye küçülür; tepsi simgesinden kolayca geri açılır; kısayollar arka plandayken yanıt verir. | `[ ] PASS`<br>`[ ] FAIL` | `%APPDATA%\Lyrow\logs\app.log` içinde `[TrayIconManager]` ve `[GlobalHotkeyHook]` logları. |
| 9 | **Yeniden Başlatma Kalıcılığı (Persistence)** | Pencereyi ekranın istediğiniz bir yerine taşıyın, temasını değiştirin ve uygulamayı kapatıp açın. | Son konum, pencere boyutları ve tema ayarları bozulmadan korunmuş olarak açılır. | `[ ] PASS`<br>`[ ] FAIL` | `%APPDATA%\Lyrow\settings.json` dosyasındaki koordinat ve ayar değerleri. |

---

## 2. Sorun Bildirimi İçin İstenen Veriler

Kullanıcı testi sırasında beklenmedik bir durum veya çökme yaşanırsa, GitHub Issues üzerinden bildirirken şu bilgileri ekleyiniz:

1. Windows Sürümü ve Derleme Numarası (`winver` komutu çıktısı).
2. Ses Çıkış Donanımı Modeli (Örn: Realtek ALC287, harici USB DAC).
3. Kullanılan Medya Oynatıcı ve Sürümü (Örn: Apple Music v1.4, Spotify Desktop).
4. Tanı Günlüğü Dosyası: `%APPDATA%\Lyrow\logs\app.log`.
