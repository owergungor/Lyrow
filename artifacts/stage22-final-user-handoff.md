# Lyrow v3.5 — Kullanıcı Teslim ve Başlangıç Rehberi (Final User Handoff Guide)

> **Sürüm:** Lyrow v3.5
> **Hedef Platform:** Windows 10 (19041+) / Windows 11 (x64)
> **Durum:** Sürüm Teslim Dokümantasyonu
> **Tanı Günlükleri:** `%APPDATA%\Lyrow\logs\app.log`

---

## 1. Lyrow v3.5'e Giriş

Lyrow, Windows masaüstü için geliştirilmiş modern, akıcı ve gizlilik odaklı bir müzik yardımcı uygulamasıdır. Spotify, Apple Music, Tidal veya web tarayıcınızda müzik dinlerken arka planda çalışarak:
- Şarkı sözlerini gerçek zamanlı ve senkronize olarak ekrana getirir,
- Müziğin ritmine ve bas frekanslarına göre dinamik **Ambient Glow** ortam aydınlatması oluşturur,
- Minimalist **Compact Player (Mini Oynatıcı)** moduyla masaüstünüzde çalışırken dikkatinizi dağıtmadan medya kontrolü sunar.

---

## 2. Sistem Gereksinimleri

| Bileşen | Asgari Gereksinim | Önerilen |
|---|---|---|
| **İşletim Sistemi** | Windows 10 64-bit (Sürüm 19041 veya üstü) | Windows 11 (22H2 / 23H2 / 24H2 x64) |
| **İşlemci (CPU)** | x64 uyumlu çift çekirdekli işlemci | Modern 4+ çekirdekli x64 işlemci |
| **Bellek (RAM)** | 2 GB RAM | 4 GB veya daha fazla RAM |
| **Disk Alanı** | 200 MB boş alan | 500 MB (şarkı sözü önbelleği için) |
| **Ses Donanımı** | Windows uyumlu ses çıkış aygıtı (WASAPI) | Dahili ses kartı, kulaklık veya USB DAC |
| **Ağ Bağlantısı** | Şarkı sözlerini indirmek için internet erişimi | Çevrimdışı önbellek için ilk indirmeden sonra internet şart değildir |

---

## 3. Dağıtım Paketleri: Installer mı, Portable mı?

Lyrow iki farklı formatta sunulmaktadır:

### 3.1. Kurulum Paketi (Installer) — `Lyrow-3.5-Setup-x64.exe`
- **Boyut:** ~64.7 MB (67,790,366 bayt)
- **Kimin İçin Uygun:** Lyrow'u birincil bilgisayarına kalıcı olarak kurmak, Başlat menüsü kısayolu oluşturmak ve Windows açılışında otomatik başlatmak isteyen genel kullanıcılar.
- **İzin Gereksinimi:** Standart kullanıcı hesabı yeterlidir; UAC yönetici yetkisi gerektirmez. Dosyalar `%LOCALAPPDATA%\Programs\Lyrow` içine kurulur.

### 3.2. Taşınabilir Sürüm (Portable ZIP) — `Lyrow-v3.5-win-x64-portable.zip`
- **Boyut:** ~93.3 MB (97,809,524 bayt)
- **Kimin İçin Uygun:** Bilgisayarına yazılım kurmak istemeyen, uygulamayı USB bellekte taşımak isteyen veya kısıtlı iş ortamlarında kullanan ileri seviye kullanıcılar.
- **Kullanım:** İndirilen ZIP arşivi istenen klasöre açılır ve doğrudan `Lyrow.App.exe` çalıştırılır. Kayıt defterine sistem çapında anahtar yazmaz.

---

## 4. Kurulum ve İlk Çalıştırma

### 4.1. Kurulum Adımları (Installer)
1. `Lyrow-3.5-Setup-x64.exe` dosyasını çalıştırın.
2. Windows SmartScreen ekranı çıkarsa **“Ek bilgi”** (More info) linkine tıklayın ve **“Yine de çalıştır”** (Run anyway) seçeneğini seçin.
3. Kurulum dilini seçin (Türkçe, English, Français, Español, Deutsch).
4. İsteğe bağlı masaüstü simgesi kutusunu işaretleyip kurulumu tamamlayın.

### 4.2. Karşılama Sihirbazı (Welcome Wizard)
Uygulama ilk kez açıldığında kullanıcıyı karşılama sihirbazı karşılar:
- **Dil Seçimi:** Arayüz dilinizi belirleyin.
- **Görsel Tema:** Açık, Koyu veya Windows Sistem Teması arasında seçim yapın.
- **Akustik Profil:** Ambient Glow için frekans dengesini (Dengeli, Bas Güçlendirme, Vokal Odaklı) ayarlayın.

---

## 5. Uygulama Özellikleri ve Kullanım Kılavuzu

### 5.1. Canlı Şarkı Sözleri (LRCLIB Entegrasyonu)
- Lyrow, çalan parçayı Windows medya oturumundan (GSMTC) otomatik algılar ve açık kaynaklı LRCLIB veritabanından senkronize şarkı sözlerini çeker.
- Şarkı ilerledikçe aktif dize parlak şekilde vurgulanır ve ekran otomatik kaydırılır.
- **Çevrimdışı Önbellek:** İndirilen şarkı sözleri yerel diskinizde saklanır; internetiniz olmasa bile daha önce dinlediğiniz parçaların sözleri anında gösterilir.

### 5.2. Ambient Glow (Ortam Aydınlatması)
- Windows varsayılan ses çıkışını (WASAPI loopback) dinleyerek müziğin ritmine, vuruşlarına ve bas dinamiklerine göre ekranın kenarlarında akıcı bir renk efekti oluşturur.
- Çalan albümün kapağındaki baskın renkleri otomatik yakalayarak aydınlatma paletini parçaya göre özelleştirir.
- Müzik durduğunda veya sessizlik algılandığında gereksiz kaynak tüketmemek için kendini otomatik olarak sakinleştirir / kapatır.

### 5.3. Sistem Tepsisi ve Klavye Kısayolları
- **Tepsiye Küçültme:** Pencere kapatıldığında veya küçültüldüğünde arka planda sessizce sistem tepsisinde (sağ altta saat yanı) çalışmaya devam eder.
- **Tepsi Menüsü:** Tepsi simgesine sağ tıklayarak parçayı duraklatabilir, değiştirebilir veya Compact Player'a geçebilirsiniz.
- **Klavye Kısayolları:** Ayarlar üzerinden özelleştirilebilen global kısayollarla uygulamayı öne getirebilir veya mini oynatıcı moduna geçebilirsiniz.

### 5.4. Ayarlar ve Kullanıcı Verilerinin Konumu
Kişisel ayarlarınız, pencere koordinatlarınız ve şarkı sözü önbelleğiniz tamamen sizin kullanıcı profilinizde saklanır:
- **Ayarlar Dosyası:** `%APPDATA%\Lyrow\settings.json`
- **Şarkı Sözü Önbelleği:** `%APPDATA%\Lyrow\LyricsCache\`
- **Tanı Günlükleri:** `%APPDATA%\Lyrow\logs\app.log`

---

## 6. Güncelleme ve Kaldırma

### 6.1. Yeni Sürüme Yükseltme (Upgrade)
Yeni bir Lyrow yükleyicisi çalıştırıldığında eski kurulum algılanır; ayarlarınız ve önbelleğiniz silinmeden yalnızca uygulama dosyaları güncellenir.

### 6.2. Uygulamayı Kaldırma (Uninstall)
Windows **Ayarlar > Yüklü Uygulamalar > Lyrow > Kaldır** adımı izlendiğinde kaldırıcı size şu soruyu soracaktır:
> *“Kişiselleştirilmiş Lyrow ayarlarınızı ve şarkı sözü önbelleğinizi saklamak ister misiniz?”*
- **Evet (Önerilen):** Uygulama dosyaları silinir; ayarlarınız `%APPDATA%\Lyrow` içinde korunur. Gelecekte tekrar kurduğunuzda kaldığınız yerden devam edebilirsiniz.
- **Hayır:** Tüm ayarlar, loglar ve önbellek tamamen temizlenir.

---

## 7. SmartScreen Uyarısı ve Kod İmzalama Durumu

Lyrow açık kaynaklı bir topluluk projesidir. Şu anda pahalı bir kurumsal EV/OV Authenticode sertifikası yapılandırılmamıştır (**NOT CONFIGURED**).

Bu nedenle, Windows Defender SmartScreen ilk indirmede "Bilinmeyen Yayıncı" uyarısı verir. Bu bir güvenlik açığı veya virüs uyarısı değildir; yalnızca dosyanın ticari bir sertifika ile imzalanmadığını gösterir.

### Güvenli Çalıştırma:
1. İndirdiğiniz dosyanın SHA-256 özetini bu rehberdeki değerle karşılaştırın (`Get-FileHash`).
2. SmartScreen ekranında **“Ek bilgi”** butonuna, ardından **“Yine de çalıştır”** butonuna tıklayarak kurulumu tamamlayın.

---

## 8. Bilinen Sınırlamalar

1. **Özel / DRM Tarayıcı Eklentileri:** Tarayıcılarda GSMTC medya anahtarları devre dışı bırakılmışsa parça başlığı yakalanamayabilir (Tarayıcı ayarlarından `Hardware Media Key Handling` açık olmalıdır).
2. **Ses Jakı / DAC Geçişleri:** Fiziksel kulaklık çıkarıldığında Windows varsayılan ses aygıtı değişir. Lyrow yeni cihaza otomatik bağlanır; ancak eski ses kartı sürücülerinde 1-2 saniyelik geçiş gecikmesi yaşanabilir.
3. **Şarkı Sözü Eşleşmesi:** Parça başlığında aşırı derecede ek açıklama olan şarkılarda (Örn: `Live at Wembley 1986 [Remastered]`) LRCLIB tam eşleşme bulamayabilir.

---

## 9. Sorun Giderme Özeti

| Sorun | Hızlı Çözüm Adımı |
|---|---|
| Şarkı adı algılanmıyor | Oynatıcıda (Spotify/Apple Music) şarkıyı duraklatıp tekrar başlatın. |
| Söz bulunamadı | İnternet bağlantınızı kontrol edin veya `%APPDATA%\Lyrow\LyricsCache` önbelleğini temizleyin. |
| Ambient Glow ışığı yanmıyor | Müziğin ses seviyesini artırın ve Ayarlar > Ambient Glow sekmesinden hassasiyeti yükseltin. |
| Uygulama açılmıyor | `%APPDATA%\Lyrow\logs\app.log` dosyasını açıp hata mesajını inceleyin. |

---

## 10. Kullanıcı Kabul Testlerine Yönlendirme

Lyrow'u sisteminizde eksiksiz test etmek ve deneyiminizi doğrulamak için lütfen 9 adımlık kabul kontrol listesini inceleyiniz:
👉 [stage21-user-acceptance-checklist.md](file:///C:/Users/omer/Projects/Lyrow/artifacts/stage21-user-acceptance-checklist.md)
