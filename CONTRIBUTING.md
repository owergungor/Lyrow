# Lyrow Projesine Katkıda Bulunma Kılavuzu (CONTRIBUTING)

Lyrow açık kaynaklı bir topluluk projesidir. Katkılarınız, hata bildirimleriniz ve özellik önerileriniz projenin gelişiminde büyük önem taşır.

---

## 🛠️ Geliştirme Ortamı Kurulumu

### Ön Gereksinimler
- **İşletim Sistemi:** Windows 10 (Derleme 19041+) veya Windows 11 (22H2 / 24H2 önerilir)
- **.NET SDK:** [.NET 10 LTS SDK](https://dotnet.microsoft.com/)
- **Geliştirme Ortamı:** Visual Studio 2022 (v17.10+) veya VS Code (C# Dev Kit eklentisiyle)
- **Windows İş Yükü:** Visual Studio Yükleyicisi üzerinden *"Windows Uygulaması Geliştirme"* (Windows App SDK / WinUI 3 araçları)

### Depoyu Klonlama ve Derleme
```bash
# Depoyu klonlayın
git clone https://github.com/owergungor/Lyrow.git
cd Lyrow

# Bağımlılıkları geri yükleyin
dotnet restore Lyrow.sln

# Çözümü derleyin (Debug veya Release)
dotnet build Lyrow.sln -c Release
```

---

## 🧪 Testleri Çalıştırma

Kod tabanındaki tüm değişikliklerin mevcut davranışı bozmadığından emin olmak için test paketini çalıştırmalısınız:

```bash
# Tüm birim ve entegrasyon testlerini çalıştırın (635 test)
dotnet test Lyrow.sln -c Release

# Belirli bir test projesini çalıştırmak için:
dotnet test tests/Lyrow.Core.Tests/Lyrow.Core.Tests.csproj
dotnet test tests/Lyrow.Audio.Tests/Lyrow.Audio.Tests.csproj
dotnet test tests/Lyrow.Music.Tests/Lyrow.Music.Tests.csproj
dotnet test tests/Lyrow.App.Tests/Lyrow.App.Tests.csproj
```

---

## 📦 Bağımsız Paket (win-x64) Üretme ve Smoke Test

```bash
# Bağımsız win-x64 dağıtımı oluşturun
dotnet publish src/Lyrow.App/Lyrow.App.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64

# Smoke testi çalıştırın
.\publish\win-x64\Lyrow.App.exe --smoke-test
```

---

## 📝 Kodlama Standartları ve İlkeleri

1. **Gevşek Bağlılık (Loose Coupling):** `Lyrow.Audio` ve `Lyrow.Music` modülleri birbirine doğrudan referans vermez; iletişim `Lyrow.Core` arayüzleri ve `IEventBus` üzerinden sağlanır.
2. **Merkezi Zamanlama:** Süre, konum ve ilerleme çubuğu mantıkları doğrudan `IPlaybackTimelineEngine` üzerinden tüketilir; pencerelerde bağımsız kümülatif süre sayaçları oluşturulmamalıdır.
3. **Sıfır Tahsisli (Zero-Alloc) Ses İşleme:** Gerçek zamanlı ses analizi döngülerinde (FFT, vuruş algılama) heap tahsislerinden kaçının; `Span<T>`, `Memory<T>` veya `ArrayPool<T>` kullanın.
4. **Platform Uyumu:** Windows GSMTC ve CoreAudio çağrıları Windows sürüm denetimi (`OperatingSystem.IsWindowsVersionAtLeast`) arkasında güvenle yürütülmelidir.

---

## 🚀 Katkı Süreci (Pull Request Adımları)

1. Projeyi kendi GitHub hesabınıza çatallayın (Fork).
2. Yeni bir özellik veya hata düzeltmesi için dal oluşturun:
   ```bash
   git checkout -b feature/harika-ozellik
   ```
3. Değişikliklerinizi yapın ve ilgili testleri ekleyin / güncelleyin.
4. Çözümün derlendiğini ve tüm testlerin geçtiğini doğrulayın (`dotnet test Lyrow.sln`).
5. Anlaşılır bir commit mesajı ile kaydedin (Conventional Commits önerilir):
   ```bash
   git commit -m "feat(lyrics): add support for new provider"
   ```
6. Dalınızı GitHub'a gönderin ve ana depoya yönelik bir Pull Request (PR) açın.

---

## 🐛 Hata Bildirimi

Bir hata ile karşılaştıysanız lütfen GitHub Issues üzerinden aşağıdaki bilgileri içeren bir kayıt oluşturun:
- Windows sürümünüz ve derleme numarası (örn. Windows 11 24H2)
- Kullandığınız müzik çalar (örn. Spotify Desktop, Apple Music Web, YouTube Music)
- Hatayı yeniden üretme adımları
- Beklenen davranış ve fiili sonuç
- Varsa `%APPDATA%\Lyrow` altındaki hata günlükleri
