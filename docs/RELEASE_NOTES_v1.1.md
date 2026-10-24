# Lyrow v1.1 — Modern UI, Reliable Lyrics Sync & Stability

Lyrow v1.1, önceki sürümdeki senkronizasyon, kısayol ve açılış problemlerini kökten çözen, arayüzü modern Vercel ve Apple tasarım standartlarına yükselten ve yeni marka terminolojisini (Glow & Lyrics) benimseyen kararlı resmi sürümdür.

---

## 🚀 Öne Çıkan Yenilikler & İyileştirmeler

### 1. Yeni Ürün Terminolojisi: Glow & Lyrics
- **Glow → Glow**: Kullanıcı arayüzünde ses analizi ve spektrum görselleştirme modülü artık **Glow** olarak adlandırılmaktadır.
- **Lyrics → Lyrics**: Kullanıcı arayüzünde senkronize şarkı sözü ve medya bilgisi modülü artık **Lyrics** olarak adlandırılmaktadır.

### 2. Vercel & Apple İlhamlı Modern Masaüstü Tasarımı
- **Vercel / shadcn Teması**: Saf siyah arka planlar, 1px zarif sınırlar (`#27272A`), nötr tipografi ve ferah kart düzeni.
- **Apple Tarzı Switch / Toggle**: Akıcı animasyonlu, yuvarlatılmış hap formunda modern geçiş anahtarları.
- **Metrik Kartları Düzeni**: Ana gösterge panelinde **Ses Seviyesi (Volume)** solda, **Söz Durumu (Lyrics Status)** sağda eşit görsel ağırlıkla yeniden konumlandırıldı.
- **Minimalist Dashboard**: Karmaşık "Hızlı Çalışma Durumları" ve sağ üstteki teknik "Mod" göstergesi kaldırılarak sade, doğrudan ve sezgisel bir kontrol hiyerarşisi oluşturuldu.
- **Light, Dark & System Desteği**: Merkezi renk token sistemi ile her üç tema da tutarlı görsel standartta çalışır.

### 3. Kritik Hata Düzeltmeleri & Kök Nedenleri

#### A. Şarkı Sözü Senkronizasyonu (Lyrics Synchronization)
- **Problem**: Şarkı sözleri doğru çekilmesine rağmen müzik 5. satıra geldiğinde pencere eski satırda (örneğin 12. satırda) takılı kalıyordu.
- **Kök Neden**: Windows GSMTC API'si çalma sırasında düzenli timeline tick üretmez; sadece duraklatma/başlatma veya parça geçişlerinde event ateşler. Bu nedenle ara interpolasyon çalışmıyordu ve UI katmanı şarkı sözleri internetten sonradan yüklendiğinde liste referansını güncellemiyordu.
- **Çözüm**: `LyricsTimelineSynchronizer` motoru `LyricsModule`'e tam entegre edildi. 30 FPS allocation-free `PeriodicTimer` ile canlı interpolasyon ve O(log N) ikili arama sağlandı. Aktif satır değiştiğinde yumuşak `BringIntoView` scroll tetiklenerek 5. satır playback anında UI 5. satıra getirildi. Pozitif/negatif manuel offset motorla senkronize edildi.

#### B. Ctrl+Shift+L Global Kısayol & Win32 1408 Hatası
- **Problem**: `Ctrl + Shift + L` kısayolu kaydedilirken `Win32 Kayıt hatası 1408` alınıyordu.
- **Kök Neden**: Arka plan mesaj pompası (`LyrowNativeHookSinkThread`) pencere handle'ını (`_hwnd`) oluştururken, UI Thread'in doğrudan `RegisterHotKey(_hwnd, ...)` çağrısı yapması sonucu Windows API `ERROR_WINDOW_OF_OTHER_THREAD` (1408) hatası veriyordu.
- **Çözüm**: Kısayol kayıt ve kaldırma işlemleri iş kuyruğu ile doğrudan mesaj thread'inin içine (`WM_APP_DISPATCH`) taşındı. Win32 1408 hatası tamamen ortadan kaldırıldı. Çakışma durumunda kullanıcıya anlaşılır Türkçe mesaj gösterilmesi sağlandı.

#### C. Uygulama Başlatma & Tekil Örnek (Single-Instance) Stabilitesi
- **Problem**: Uygulama bazen ilk açılışta açılmıyor, kullanıcı ancak 4 kez tıkladığında açılabiliyordu.
- **Kök Neden**: Avalonia `OnFrameworkInitializationCompleted` metodunda `MainWindow`'un atanması arka plan asenkron modül yüklemelerine bağımlıydı. Ayrıca tekil örnek (single-instance) kontrolü bulunmadığından arkada çalışan instance varken yeni process'ler çakışıyordu.
- **Çözüm**: `MainWindow` ilk anda senkron oluşturulup ekranda gösterilir; ses ve medya modülleri UI'ı bloke etmeden arka planda başlatılır. Sistem genelinde `Mutex` ve `EventWaitHandle` ile tekil örnek mekanizması kuruldu; ikinci kez açıldığında arkadaki veya tepsideki mevcut pencere anında öne getirilir. Sessiz çökmeleri önlemek için sıfır-telemetrili yerel tanı günlüğü (`%APPDATA%\Lyrow\logs\app.log`) eklendi.

---

## 📦 Dağıtım Paketleri

İki parçalı sürüm kuralı gereğince:
- **Kurulum Programı**: `Lyrow-1.1-Setup-x64.exe`
- **Taşınabilir Arşiv**: `Lyrow-win-x64-portable.zip`
- **Git Etiketi**: `v1.1`
- **Hedef Framework**: `.NET 10.0 (Windows 10 Build 19041+)`
