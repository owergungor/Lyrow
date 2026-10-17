# Lyrow v1.2 Sürüm Notları (Release Notes)

## Genel Bakış
Lyrow v1.2, Windows-first masaüstü ses ve şarkı sözü deneyimini yeni nesil ortam aydınlatması, sıfır varsayılanlı dinamik kısayol mimarisi, merkezlenmiş söz akışı, albüm kapağı (artwork) hattı ve rafine edilmiş modern Vercel/Apple arayüzü ile taçlandırıyor.

Sürüm Numarası Kuralı: **v1.2** (İki noktalı katı format korunmuştur).

---

## Yeni Özellikler ve İyileştirmeler

### 1. Sıfır Varsayılanlı & Özelleştirilebilir Global Kısayollar
- **İlk Kurulum Sıfır Kısayol**: Uygulama ilk kurulduğunda hiçbir tuş kombinasyonu sistem genelinde rezerve edilmez.
- **Kullanıcı Tanımlı Kısayollar**: Ayarlar kartı üzerinden Glow, Lyrics ve Click-Through için istenen kombinasyonlar girilebilir veya temizlenebilir ("" boş bırakılabilir).
- **Çakışma Koruması**:
  - Dahili çakışma kontrolü: Aynı Lyrow oturumunda iki farklı eyleme aynı tuşun atanması engellenir.
  - Harici Win32 kontrolü (1409): Windows veya başka bir program tarafından kullanılan kısayollar tespit edilip net bir hata bildirimi verilir.
- **Win32 1408 İzolasyonu**: Dedicated STA HWND mesaj döngüsü korunarak thread ownership güvence altına alınmıştır.

### 2. Viewport Merkezli Şarkı Sözleri (Lyrics Centering)
- **Görsel Merkezleme**: Çalan şarkı sözü satırı viewport yüksekliğinin tam dikey merkezinde tutulur.
- **Dinamik Viewport Marjı**: Listenin üst ve alt sınırlarına dinamik marj atanarak ilk ve son satırların dahi merkeze gelebilmesi sağlandı.
- **Seek Senkronu**: Şarkıda ileri/geri atlama yapıldığında yeni aktif satır anında merkeze alınır.
- **Pause/Resume Korunumu**: Oynatma duraklatılıp devam ettirildiğinde merkezleme bozulmaz, gereksiz scroll sıçraması yapılmaz.
- **Şarkı Değişimi Temizliği**: Parça değiştiğinde eski söz durumu sıfırlanır, yeni sözler gelene kadar temiz durum korunur.

### 3. Asenkron Albüm Kapağı (Artwork Pipeline)
- **Windows GSMTC Entegrasyonu**: Windows Media Control API'si üzerinden `IRandomAccessStream` doğrudan okunur.
- **Asenkron ve Bloklamayan Çözümleme**: UI thread'i bloklanmadan görsel decode edilir.
- **Bellek İçi Önbellek (Cache)**: Aynı parçanın kapağı defalarca okunmaz, cache'ten sunulur.
- **Zarif Fallback & Placeholder**: Kapağı olmayan veya okunamayan medya için Apple/Vercel estetiğinde placeholder gösterilir.
- **Konumlar**: Hem Dashboard Canlı Medya kartında hem de Lyrics penceresinde albüm kapağı rounded corners ile görüntülenir.

### 4. Ambient Glow — 4 Kenarlı Ekran Kenar Aydınlatması
- **Müzik Enerjisine Duyarlı**: Basit bir seviye göstergesi yerine ekranın dört kenarında ambient aydınlatma.
- **GlowBeatEngine**: Bas, orta ve tiz bantlarını izole eden, dinamik eşik ve transient onset türevi ile kick/snare ritimlerini yakalayan sıfır tahsisatlı (zero-allocation) motor.
- **Smooth Attack & Decay**: Hızlı tepki (attack), ~150 ms doğal sönümleme (decay), titreşimsiz (flicker-free) yumuşak geçiş.
- **Düşük Kaynak Tüketimi**: Sessizlik anında otomatik render throttling ile %0 CPU ve ~47 MB RAM.
- **Çoklu Monitör & DPI**: Seçilen monitörün çalışma alanına tam oturan, click-through şeffaf overlay penceresi.

### 5. Sabit Dashboard Kart Geometrisi
- İçerik değişimlerinde (uzun şarkı adı, uzun sanatçı adı, durum bildirimleri) kartların boyutlarının değişmesi engellendi.
- Canlı Medya kartı `76px`, Metrikler `104px`, Modüller `200px` yüksekliklerine sabitlendi.
- Uzun metinler için `TextTrimming="CharacterEllipsis"` ve `ToolTip` eklendi.

### 6. Sağ Üst Toast Bildirim Sistemi
- Sayfa altındaki kalıcı tema durumu yazısı tamamen kaldırıldı.
- Sağ üstte kayan, Vercel/HeroUI tarzı minimal, yarı saydam ve hafif gölgeli toast bildirimleri entegre edildi.
- Anti-spam debounce mekanizması ile 1.5 saniye içinde aynı mesajın tekrar tekrar belirmesi önlendi.

---

## Test ve Kalite Güvencesi
- **140 Test**: 108 mevcut v1.1 testi korunmuş, 32 yeni v1.2 birim ve entegrasyon testi eklenmiştir. %100 Başarı (0 Hata).
- **Derleme Durumu**: .NET 10 Release win-x64 derlemesinde 0 Uyarı, 0 Hata.
- **Dağıtım**: Tek dosyalı self-contained yürütülebilir, Inno Setup kurulum sihirbazı (`Lyrow-1.2-Setup-x64.exe`) ve taşınabilir zip paketi (`Lyrow-win-x64-portable.zip`).
