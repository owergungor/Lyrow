# Lyrow — Kullanıcı Kurulum ve Başlangıç Kılavuzu (User Installation Guide)

**Ürün:** Lyrow (Windows için Premium Müzik Deneyimi)
**Hedef Sürüm:** v3.5
**Desteklenen Platform:** Windows 10 (Sürüm 19041+) ve Windows 11 (x64)

---

## 1. Kurulum Seçenekleri

Lyrow iki farklı dağıtım biçiminde sunulmaktadır:

### Seçenek A: Kurulum Paketi (Installer — Önerilen)
- **Dosya Adı:** `Lyrow-3.5-Setup-x64.exe`
- **Gereken İzin:** Standart kullanıcı izni (Yönetici / UAC yetkisi gerektirmez).
- **Varsayılan Kurulum Konumu:** `%LOCALAPPDATA%\Programs\Lyrow`
- **Özellikler:**
  - Başlat Menüsü ve isteğe bağlı Masaüstü kısayolu.
  - Windows ile birlikte otomatik başlama seçeneği.
  - Temiz kaldırma ve sürüm yükseltme desteği.

#### Kurulum Adımları:
1. `Lyrow-3.5-Setup-x64.exe` dosyasını indirin.
2. Dosyaya çift tıklayın.
3. Windows Defender SmartScreen "Windows kişisel bilgisayarınızı korudu" ekranı çıkarsa:
   - **“Ek bilgi”** (More info) bağlantısına tıklayın.
   - **“Yine de çalıştır”** (Run anyway) butonuna basın.
4. Tercih ettiğiniz kurulum dilini (Türkçe, English, Français, Español, Deutsch) seçin.
5. İsteğe bağlı olarak masaüstü simgesi oluşturma kutusunu işaretleyin ve **İleri**'ye tıklayarak kurulumu tamamlayın.

---

### Seçenek B: Taşınabilir Sürüm (Portable ZIP)
- **Dosya Adı:** `Lyrow-v3.5-win-x64-portable.zip`
- **Kullanım:** İndirilen ZIP arşivini istediğiniz herhangi bir klasöre (Örn: `D:\Apps\Lyrow` veya USB bellek) çıkartın.
- **Başlatma:** Klasör içerisindeki `Lyrow.App.exe` dosyasını çalıştırın.
- Kurulum gerektirmez; kayıt defterine sistem çapında anahtar yazmaz.

---

## 2. Paket Bütünlüğü ve SHA-256 Doğrulaması

İndirdiğiniz dosyaların bütünlüğünü ve orijinalliğini doğrulamak için PowerShell üzerinden SHA-256 özetini kontrol edebilirsiniz:

```powershell
# Kurulum dosyasının özetini alma:
Get-FileHash -Algorithm SHA256 "Lyrow-3.5-Setup-x64.exe"

# Taşınabilir paketin özetini alma:
Get-FileHash -Algorithm SHA256 "Lyrow-v3.5-win-x64-portable.zip"
```

Hesaplanan değerleri sürümle birlikte yayınlanan `sha256sums.txt` dosyasıyla karşılaştırınız.

---

## 3. İlk Çalıştırma Sihirbazı (Welcome Wizard)

Lyrow ilk kez açıldığında kullanıcıyı karşılama sihirbazı karşılar:
1. **Dil Seçimi:** 5 resmi dil arasından tercih yapabilirsiniz (Türkçe varsayılan olarak sunulur).
2. **Görsel Tema:** Koyu (Dark), Açık (Light) veya Sistem (Windows Teması) seçeneklerinden birini belirleyin.
3. **Akustik Tercih:** Ambient Glow için frekans profilini (Dengeli, Bas Güçlendirme, Vokal Odaklı) seçin.
4. **Tamamlama:** Tercihleriniz anında kaydedilir ve ana ekrana yönlendirilirsiniz.

---

## 4. Windows Başlangıcında Çalışma Ayarı

Lyrow'un bilgisayarınız açıldığında otomatik olarak arka planda sistem tepsisinde hazır beklemesini istiyorsanız:
1. Lyrow penceresinde sağ üstteki **Ayarlar (Dişli çark)** simgesine tıklayın.
2. **Genel** sekmesine gelin.
3. **“Windows ile birlikte başlat”** anahtarını açık konuma getirin.
4. İsteğe bağlı olarak **“Sistem tepsisine simge durumuna küçült”** seçeneğini aktif edebilirsiniz.

---

## 5. Tema ve Dil Ayarlarını Değiştirme

Kurulum sonrasında istediğiniz zaman:
- **Ayarlar > Arayüz** sekmesinden dilinizi değiştirebilirsiniz (Arayüz anında seçilen dile çevrilir, yeniden başlatma gerekmez).
- **Açık / Koyu** tema geçişiyle Windows 11 Fluent tasarımının ve Akrilik arka plan efektlerinin keyfini çıkarabilirsiniz.

---

## 6. Uygulamayı Kaldırma ve Veri Koruma

Lyrow'u kaldırmak istediğinizde:
1. Windows **Ayarlar > Uygulamalar > Yüklü Uygulamalar** menüsüne gidin.
2. Listeden **Lyrow** uygulamasını bulun ve **Kaldır** butonuna tıklayın.
3. Kaldırma sihirbazı size şu soruyu soracaktır:
   > *“Kişiselleştirilmiş Lyrow ayarlarınızı ve şarkı sözü önbelleğinizi saklamak ister misiniz?”*
   - **Evet:** Tercihleriniz ve indirilmiş sözler `%APPDATA%\Lyrow` klasöründe saklanır. Uygulamayı gelecekte tekrar kurduğunuzda kaldığınız yerden devam edebilirsiniz.
   - **Hayır:** Tüm ayar dosyaları, loglar ve şarkı sözü önbelleği tamamen silinir.
