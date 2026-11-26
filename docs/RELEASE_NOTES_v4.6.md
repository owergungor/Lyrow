# Lyrow v4.6 — First-Run Welcome Experience (Yerel Hazırlık)

**Tarih:** 2026-11-26  
**Temel Sürüm:** v4.5 (`5286d20`)  
**Hedef Platform:** Windows 10 / 11 (x64, Self-Contained)  
**Test Sonucu:** 743 / 743 PASS (%100, deterministik/seri koşu)

---

## Öne Çıkan Yenilikler ve Değişiklikler

### 1. Windows 11 Fluent Design İlk Açılış Deneyimi (`WelcomeWindow`)
- Yeni kullanıcılar için 5 adımlı, Mica (`BaseAlt`) arka planlı, DPI duyarlı (`820×580`, ekran çalışma alanına güvenli ortalanmış) ve yumuşak geçiş animasyonlu karşılama deneyimi eklendi:
  1. **Welcome / Hoş Geldiniz:** Yeni Lyrow logosu, v4.5 hero görseli ve 3 temel yetenek kartı (`Real-time Lyrics`, `Dynamic Visuals`, `Audio-Reactive Lighting`) + **Get Started** / **Skip Setup**.
  2. **Appearance / Görünüm:** **System Default**, **Dark** ve **Light** görsel tema kartlarıyla anlık canlı önizleme.
  3. **Your Experience / Deneyimini Şekillendir:** **Enable Edge Light** (varsayılan kapalı), **Enable Dynamic Background** ve zorunlu olmayan **Compact Mode** tanıtım kartı.
  4. **Audio / Ses Deneyimi:** Mevcut WASAPI ses çıkış cihazlarının UI'ı kilitlemeden asenkron listelenmesi, isteğe bağlı cihaz seçimi ve Kenar Işığı yerel bellek analizi açıklaması.
  5. **Ready / Her Şey Hazır!:** Seçilen Tema, Kenar Işığı, Dinamik Arka Plan ve Ses Çıkışı özeti + **Start Listening**.

### 2. İlk Açılış Koruması ve Mevcut v4.5 Kullanıcı Migrasyonu
- `LyrowSettings.HasCompletedWelcome` kalıcı ayarı eklendi.
- **Yeni kurulumda** (`settings.json` henüz yoksa) Welcome ekranı otomatik açılır; **Start Listening**, **Skip Setup** veya pencere kapatma (`X`) sonrası `HasCompletedWelcome = true` kaydedilir ve sonraki açılışlarda tekrar gösterilmez.
- **v4.5 ve öncesi mevcut kullanıcılarda** diskte mevcut `settings.json` dosyası algılanarak `HasCompletedWelcome = true` olarak otomatik yükseltilir; Welcome ekranı otomatik açılmaz ve mevcut tüm kullanıcı tercihleri korunur.

### 3. Ayarlar'dan Yeniden Açma ve Tam Yerelleştirme
- **Ayarlar > Genel** ve **Ayarlar > Hakkında** bölümlerine **Show Welcome Screen / Karşılama Ekranını Göster** seçeneği eklendi; tıklandığında mevcut ayarlar önceden seçili olarak karşılama turu ana pencereyi kapatmadan açılır.
- Tüm karşılama metinleri `LocalizationService` üzerinden eksiksiz Türkçe ve İngilizce olarak desteklenir.

---

## Yerel Dağıtım Paketleri ve SHA-256 Doğrulama Özetleri

| Paket | Dosya Adı | Boyut | İmza Durumu | SHA-256 |
|---|---|---|---|---|
| **Windows Setup EXE** | `Lyrow-4.6-Setup-x64.exe` | 64.55 MB | İmzasız | `62674C1D29BDF2AA52EC0E5E56E7DF2FB7535FB367F0BDE82FCC32CEC585D135` |
| **Portable ZIP** | `Lyrow-v4.6-win-x64-portable.zip` | 92.75 MB | — | `415AFF5370562FF9F5F1A2E875E2E1E71304A9B4D3BB5895D4572969598B54B2` |
| **MSIX x64 (Store Kimlikli)** | `Lyrow-v4.6-x64.msix` | 94.81 MB | **İmzasız (`NotSigned`)** | `282E8557B3710BD1A784CA944C975E223BDCEEB0DA716671AF4B807A470179CE` |

> **Microsoft Partner Center Kimliği Doğrulandı:** `Identity Name="crius.Lyrow"`, `Publisher="CN=2F9A5694-FADC-405A-AF51-BB9269D3FFF1"`, `PublisherDisplayName="crius"`, `Version="4.6.0.0"`, `DisplayName="Lyrow"`, `runFullTrust`.
