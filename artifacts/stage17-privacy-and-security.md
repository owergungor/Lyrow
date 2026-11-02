# Lyrow â€” Gizlilik ve GÃ¼venlik BeyanÄ± (Privacy & Security Declaration)

**ÃœrÃ¼n:** Lyrow (Windows MasaÃ¼stÃ¼ MÃ¼zik Deneyimi)
**Tarih:** 2026-11-02
**Mimari Ä°lke:** Private by Design (TasarÄ±m GereÄŸi Gizli)

---

## 1. Telemetri ve KullanÄ±cÄ± Takibi (Zero Telemetry)

- **Telemetri Yoktur:** Lyrow, kullanÄ±cÄ±larÄ±n dinleme alÄ±ÅŸkanlÄ±klarÄ±nÄ±, ÅŸarkÄ± geÃ§miÅŸini, IP adreslerini veya donanÄ±m kimliklerini kaydeden ya da uzaktaki bir sunucuya gÃ¶nderen herhangi bir analitik, telemetri veya izleme kÃ¼tÃ¼phanesi (Google Analytics, Sentry, Mixpanel vb.) Ä°Ã‡ERMEZ.
- **KullanÄ±cÄ± HesabÄ± Gerekmez:** Lyrow'u kullanmak iÃ§in e-posta, ÅŸifre veya oturum aÃ§ma gerekmez.

---

## 2. AÄŸ Ä°letiÅŸimi ve UÃ§ Noktalar (Network Endpoints)

Lyrow yalnÄ±zca uygulamanÄ±n temel iÅŸlevleri iÃ§in gereken iki harici HTTPS uÃ§ noktasÄ± ile iletiÅŸim kurar:

| UÃ§ Nokta | Protokol | KullanÄ±m AmacÄ± | GÃ¶nderilen Veri |
| :--- | :--- | :--- | :--- |
| `lrclib.net/api` | HTTPS (TLS 1.3) | ÅarkÄ± sÃ¶zlerini aramak ve senkronize zaman damgalarÄ±nÄ± Ã§ekmek | YalnÄ±zca aktif ÅŸarkÄ± adÄ±, sanatÃ§Ä± ve albÃ¼m adÄ± |
| `api.github.com` | HTTPS (TLS 1.3) | Yeni uygulama sÃ¼rÃ¼mlerini kontrol etmek (`/releases/latest`) | Standart User-Agent baÅŸlÄ±ÄŸÄ± (`Lyrow-Desktop`) |

Uygulama bu iki adres haricinde hiÃ§bir Ã¼Ã§Ã¼ncÃ¼ taraf sunucuya baÄŸlantÄ± kurmaz veya reklam aÄŸÄ±na veri aktarmaz.

---

## 3. Yerel Veri Saklama ve GÃ¼venlik (Local Data Storage)

TÃ¼m kullanÄ±cÄ± tercihleri ve Ã¶nbellekler yerel cihazda saklanÄ±r:

- **Ayar DosyasÄ±:** `%LOCALAPPDATA%\Lyrow\settings.json`
  (SeÃ§ilen tema, dil, akustik ayarlar, pencere boyutlarÄ±).
- **ÅarkÄ± SÃ¶zÃ¼ Ã–nbelleÄŸi:** `%LOCALAPPDATA%\Lyrow\cache\lyrics\`
  (Ä°ndirilen sÃ¶zler, gereksiz internet trafiÄŸini Ã¶nlemek iÃ§in yerel diskte saklanÄ±r).
- **TanÄ±lama KayÄ±tlarÄ±:** `%LOCALAPPDATA%\Lyrow\logs\app.log`
  (Uygulama Ã§Ã¶kmelerini ve donanÄ±m kesintilerini analiz etmek iÃ§in tutulan dÃ¶ngÃ¼sel kayÄ±tlar).

---

## 4. GÃ¼nlÃ¼k KaydÄ± Maskeleme (Log Sanitization)

Lyrow'un tanÄ±lama servisi (`LocalDiagnosticLogger`), gÃ¼venliÄŸi artÄ±rmak iÃ§in hassas dizeleri diske yazmadan Ã¶nce temizler:
- `Bearer`, `token`, `secret`, `password`, `key` anahtarlarÄ± ve deÄŸerleri `[REDACTED]` ifadesiyle maskelenir.
- KullanÄ±cÄ± dosya yollarÄ±ndaki kiÅŸisel profil isimleri (`C:\Users\<Username>\`) sanitize edilir.

---

## 5. GÃ¼ncelleme ve DaÄŸÄ±tÄ±m BÃ¼tÃ¼nlÃ¼ÄŸÃ¼

- Ä°ndirilen gÃ¼ncelleme paketleri ve resmi sÃ¼rÃ¼mler SHA-256 saÄŸlama toplamÄ± (`sha256sums.txt`) ile doÄŸrulanabilir.
- `installer/generate_checksums.ps1` ve `installer/verify_checksums.ps1` araÃ§larÄ± daÄŸÄ±tÄ±m paketlerinin bozulmadÄ±ÄŸÄ±nÄ± matematiksel olarak denetler.

---

## 6. Kod Ä°mzalama Durumu ve Microsoft SmartScreen ÅeffaflÄ±ÄŸÄ±

- **Authenticode Durumu:** `NOT CONFIGURED`
- Lyrow projesinde ticari bir kod imzalama sertifikasÄ± yapÄ±landÄ±rÄ±lmamÄ±ÅŸtÄ±r. Depoda hiÃ§bir Ã¶zel anahtar saklanmaz.
- Bu nedenle Windows Defender SmartScreen ilk indirmelerde "Bilinmeyen YayÄ±ncÄ±" uyarÄ±sÄ± verir.
- KullanÄ±cÄ±lar, dosyalarÄ±n resmi GitHub deposundan geldiÄŸini ve SHA-256 hash deÄŸerlerini kontrol ederek yazÄ±lÄ±mÄ±n orijinalliÄŸini baÄŸÄ±msÄ±z olarak doÄŸrulayabilirler.
