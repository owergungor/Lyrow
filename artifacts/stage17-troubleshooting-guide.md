# Lyrow — Sorun Giderme Kılavuzu (Troubleshooting Guide)

**Hedef:** Lyrow masaüstü uygulamasında karşılaşılabilecek olası durumlar, nedenleri ve çözüm adımları.
**Hedef Sürüm:** v3.5

---

## 1. Medya Oturumu Algılanmıyor

### Belirti
Lyrow penceresinde şarkı adı görünmüyor veya "Medya oturumu bekleniyor" yazısı kalıyor.

### Olası Nedenler ve Çözümler
1. **Oynatıcı GSMTC Desteklemiyor:**
   - Lyrow, Windows Global System Media Transport Controls (GSMTC) arabirimini kullanır. Spotify, Apple Music, Tidal, Deezer, Chrome, Edge ve Firefox GSMTC ile tam uyumludur.
   - Tarayıcıda müzik dinliyorsanız, tarayıcınızın arka plan medya entegrasyonunun devrede olduğundan emin olun (Edge/Chrome: `Hardware Media Key Handling` bayrağı).
2. **Uygulama İzinleri:**
   - Windows Ayarları > Gizlilik ve Güvenlik altından masaüstü uygulamalarının medya kontrollerine erişiminin açık olduğunu doğrulayın.
3. **Oturumun Yenilenmesi:**
   - Oynatıcı uygulamanızda şarkıyı duraklatıp tekrar oynatın veya bir sonraki şarkıya geçin.

---

## 2. Şarkı Sözleri Yüklenmiyor veya Yanlış Eşleşiyor

### Belirti
"Şarkı sözü bulunamadı" uyarısı veya parçayla uyuşmayan sözler gösteriliyor.

### Olası Nedenler ve Çözümler
1. **İnternet Bağlantısı:**
   - Şarkı sözleri LRCLIB veritabanından dinamik olarak çekilir. İnternet erişiminizin aktif olduğunu doğrulayın.
2. **Önbellek Temizleme:**
   - Yanlış bir şarkı sözü eşleşmesi önbelleğe alınmışsa, `%APPDATA%\Lyrow\LyricsCache` klasörüne giderek ilgili JSON dosyasını silebilirsiniz. Lyrow şarkı sözlerini yeniden arayacaktır.
3. **Metadata Uyumu:**
   - Oynatılan parçanın başlığında gereksiz metinler (Örn: "(Official Video)", "[Remastered 2021]") varsa, LRCLIB araması tam eşleşmeyebilir.

---

## 3. Ambient Glow Tepki Vermiyor veya Renk Değişmiyor

### Belirti
Glow aydınlatması sönük kalıyor veya ses seviyesine/ritmine göre dalgalanmıyor.

### Olası Nedenler ve Çözümler
1. **Ses Yakalama Cihazı:**
   - Ambient Glow, varsayılan Windows ses çıkışını (WASAPI loopback) dinler.
   - Farklı bir ses çıkış cihazına geçtiyseniz (kulaklık/hoparlör değişimi), Lyrow otomatik olarak yeni varsayılan aygıta bağlanacaktır.
2. **Sistem Sesi / Uygulama Sesi:**
   - Müzik çalar uygulamanızın sesi çok düşükse (örneğin %5 altı), FFT eşiği tetiklenmeyebilir. Uygulama sesini açıp genel sistem sesini ayarlayınız.
3. **Glow Hassasiyet Ayarı:**
   - **Ayarlar > Ambient Glow** sekmesinden hassasiyet (Sensitivity) ve yoğunluk (Intensity) değerlerini artırın.

---

## 4. Windows SmartScreen Uyarısı

### Belirti
Kurulum dosyası (`Lyrow-3.5-Setup-x64.exe`) veya portable sürüm çalıştırıldığında mavi "Windows kişisel bilgisayarınızı korudu" ekranı çıkıyor.

### Çözüm
- Lyrow açık kaynaklı bir topluluk projesidir ve şu anda ticari bir Authenticode sertifikasıyla imzalanmamıştır.
- Ekrandaki **“Ek bilgi”** (More info) linkine tıklayın ve ardından **“Yine de çalıştır”** (Run anyway) butonuna basarak devam edin.
- İndirdiğiniz dosyanın bütünlüğünden emin olmak için SHA-256 özetini `sha256sums.txt` dosyasındaki değerle karşılaştırınız.

---

## 5. Günlük (Log) Dosyalarına Erişim

Bir hata durumunda teknik destek almak veya sorunları incelemek için Lyrow tanı günlüklerine şu yoldan ulaşabilirsiniz:

- **Konum:** `%APPDATA%\Lyrow\logs\app.log`
- Günlük dosyaları yerel olarak saklanır, dışa aktarılmaz ve en fazla 5 MB boyutunda 3 yedek dosyayla sınırlandırılmıştır.
