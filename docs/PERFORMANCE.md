# Lyrow — Performans Hedefleri ve Ölçüm Metodolojisi (PERFORMANCE.md)

Bu belge, Lyrow uygulamasının sistem kaynağı (RAM, CPU, GPU) hedeflerini, test senaryolarını, ölçüm metodolojisini ve optimizasyon ilkelerini belirler.

---

## 1. Başlangıç Performans Hedefleri

> **Önemli Not:** Aşağıdaki değerler hedeflenen üst limitlerdir (SLO - Service Level Objectives). Proje şu anda Aşama 1 (Teknik Keşif) safhasında olduğundan bu değerler **henüz ölçülmemiştir**; Aşama 6'da deneysel olarak doğrulanıp kayıt altına alınacaktır.

### 1.1. RAM (Working Set) Hedefleri

| Çalışma Durumu | Hedeflenen Bellek (RAM) | Not / Kısıt |
| :--- | :--- | :--- |
| **Boşta / Arka Plan (Modüller Kapalı, Sadece Tray)** | **≤ 100 – 150 MB** | Avalonia çekirdek çalışma zamanı ve DI servisleri aktif. Render devre dışı. |
| **Yalnızca Lumn Aktif** | **150 – 250 MB** | WASAPI ses yakalama buffer'ları ve FFT analiz dizileri havuzlanmış olmalı. |
| **Yalnızca Verci Aktif** | **150 – 250 MB** | Albüm kapağı önbelleği sınırlandırılmalı (maks. 10 kapak hafızada). |
| **İki Modül Birlikte Aktif** | **200 – 350 MB** | Eşzamanlı ses analizi, söz kaydırma ve çift Avalonia render katmanı. |

### 1.2. CPU Kullanım Hedefleri (Modern 6+ Çekirdekli İşlemci Baz Alınarak)

| Çalışma Durumu | Hedeflenen Ortalama CPU | Zirve (Spike) Toleransı |
| :--- | :--- | :--- |
| **Boşta (Müzik Çalmıyorken / Duraklatılmış)** | **< %1** | Kısa süreli <%2 |
| **Lumn Aktif (60 FPS Efekt Render)** | **%4 – %8** | <%10 |
| **Verci Aktif (Satır Kaydırma / Zaman Takibi)** | **%1 – %3** | <%5 |
| **İki Modül Birlikte Aktif** | **%5 – %10** | Maks. <%12 |

---

## 2. Ölçüm Metodolojisi ve Araçlar

Performans ölçümleri kullanıcı tahminine bırakılmayacak, aşağıdaki standart araçlar ile otomatikleştirilecektir:

1. **`dotnet-counters`:**
   ```bash
   dotnet-counters monitor --process-id <PID> --counters System.Runtime,Avalonia
   ```
   - `cpu-usage`: Gerçek zamanlı işlemci yüzdesi.
   - `working-set`: MB cinsinden fiziksel bellek kullanımı.
   - `gc-heap-size`: Yönetilen (managed) bellek miktarı.
   - `gen-0-gc-count / gen-2-gc-count`: Çöp toplayıcı baskısı.
2. **Windows Performans İzleyicisi (Perfmon):**
   - `\Process(Lyrow)\% Processor Time`
   - `\Process(Lyrow)\Working Set - Private`
3. **Dahili Tanılama Sayacı (Debug Build):**
   - Lyrow içinde geliştirilecek hafif bir `PerformanceTracker` servisi ile saniyede 1 kez process verisi loglanabilir.

---

## 3. Test Senaryoları (Test Scenarios)

1. **Senaryo A: Temiz Başlatma ve Boşta Bekleme (Idle)**
   - Uygulama başlatılır, modüller kapalıdır veya ses çalmıyordur. 15 dakika beklenir. Bellek sızıntısı ve arka plan CPU tüketimi ölçülür.
2. **Senaryo B: Sürekli Müzik Çalma (Lumn Tek Başına)**
   - 120 BPM yüksek tempolu elektronik müzik kesintisiz 60 dakika çalınır. FFT motorunun bellek tahsisatı (allocation) ve kare hızı (FPS kararlılığı) ölçülür.
3. **Senaryo C: Şarkı Değiştirme Döngüsü (Verci Tek Başına)**
   - 20 farklı şarkı sırayla oynatılır. Her şarkıda LRCLIB'den sözler indirilir ve albüm kapağı yüklenir. Belleğin serbest bırakılıp bırakılmadığı kontrol edilir.
4. **Senaryo D: Stres ve Aşırı Yük (Lumn + Verci Entegre)**
   - İki modül aynı anda çalışırken kullanıcı pencere boyutlarını hızla değiştirir ve şeffaf mod / click-through arasında geçiş yapar. Donma veya gecikme (jank) gözlemlenir.

---

## 4. Test Ortamı Donanım ve Yazılım Kayıt Formatı

Her ölçüm raporu aşağıdaki ortam bilgileriyle eşleştirilecektir:

- **İşlemci (CPU):** Model, çekirdek ve iş parçacığı sayısı, taban frekans.
- **Bellek (RAM):** Toplam miktar (GB) ve frekans (DDR4/DDR5).
- **Grafik Kartı (GPU):** Model ve sürücü sürümü.
- **İşletim Sistemi:** Windows sürümü, Build numarası ve ekran ölçekleme oranı (DPI).
- **Ses Donanımı:** Örnekleme hızı (ör. 24-bit 48000 Hz) ve kanal sayısı.

---

## 5. Optimizasyon İlkeleri

1. **Sıfır Tahsisli Ses Analizi (Zero-Allocation FFT):**
   - Her ses tamponunda (buffer) yeni `float[]` dizileri üretilmeyecek; `ArrayPool<float>.Shared` veya modüle ait sabit uzunluklu önceden tahsis edilmiş buffer'lar kullanılacaktır.
2. **Kare Hızı Sınırlama (Frame Pacing):**
   - Lumn görselleştirmeleri ekran tazeleme oranına (V-Sync / 60 Hz) kilitlenecek, boş yere 144+ FPS render alarak CPU/GPU israf edilmeyecektir.
3. **Müzik Durduğunda Uyuma (Sleep-on-Silence):**
   - Ses seviyesi 5 saniye boyunca sessizlik eşiğinin altındaysa render döngüsü duraklatılacak ve CPU kullanımı %0'a çekilecektir.
4. **Görsel Efektlerde Basitlik (Shader & Neumorphism Dikkat):**
   - Çok katmanlı dinamik bulanıklık (blur) ve aşırı yumuşak gölgeler yerine GPU dostu önceden hesaplanmış (pre-rendered) fırçalar ve SkiaSharp optimize canvas primitifleri tercih edilecektir.
5. **Görsel Olmayan Zamanlarda Kaynak Salınımı:**
   - Modül pencereleri simge durumuna küçültüldüğünde veya gizlendiğinde çizim çağrıları (`InvalidateVisual`) durdurulacaktır.

---

## 6. Mevcut Durum Beyanı

- [x] Başlangıç hedefleri ve metodoloji belirlendi.
- [ ] Uygulama kodu henüz yazılmadığı için **gerçekleştirilmiş bir performans ölçümü bulunmamaktadır**.
- [ ] Ölçümler Aşama 6'da gerçekleştirilecek ve bu dokümana sonuç tabloları eklenecektir.
