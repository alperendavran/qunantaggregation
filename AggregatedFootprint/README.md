# Aggregated FootPrint Indicator

Bu proje, Quantower API kullanarak BTC, ETH ve XRP coinlerinin farklı borsalardaki paritelerini birleştiren gelişmiş bir FootPrint grafiği oluşturur.

## Özellikler

### 🎯 Ana Özellikler
- **Çoklu Borsa Birleştirme**: Binance, Coinbase, Kraken, Bybit, OKX, Bitfinex, Bitget, CoinW, Gate.io, Huobi, KuCoin
- **İki Birleştirme Modeli**:
  - **Model 1**: Doğrudan toplama (farklı fiyat seviyeleri korunur)
  - **Model 2**: Normalize edilmiş birleştirme (referans mum uzunluğuna göre eşleme)
- **Gerçek Zamanlı Güncelleme**: Maksimum 10 saniye gecikme
- **UTC+0 Zaman Dilimi**: Tüm hesaplamalar UTC bazlı

### 📊 FootPrint Özellikleri
- **Hücre Bazlı Görselleştirme**: Her fiyat seviyesi için ayrı hücre
- **Heatmap Renklendirme**: Hacim yoğunluğuna göre renk tonları
- **BID/ASK Ayrımı**: Sol taraf BID, sağ taraf ASK
- **Ayarlanabilir Tick Size**: BTC (0.1, 0.05), ETH (1, 0.5), XRP (0.001, 0.0001, 0.0025, 0.005)

### 📈 Teknik Göstergeler

#### VWAP (Volume Weighted Average Price)
- **Günlük VWAP**: UTC 00:00'da otomatik sıfırlanır
- **Strike VWAP**: Fare ile tıklanan noktadan başlar
- **VWAP Sapmaları**: 1. ve 2. seviye sapma bantları

#### FPBS (FootPrint Bar Statistics)
- **VOLUME**: Toplam hacim (BID + ASK)
- **DELTA**: BID - ASK farkı
- **CVD**: Kümülatif Delta (günlük/haftalık sıfırlanabilir)
- **BUY**: Toplam ASK hacmi
- **SELL**: Toplam BID hacmi

#### POC/VAL/VAH
- **POC**: Point of Control (en yoğun hacim seviyesi)
- **VAL**: Value Area Low (değer alanı alt sınırı)
- **VAH**: Value Area High (değer alanı üst sınırı)
- **Günlük/Haftalık**: Otomatik hesaplama ve görselleştirme

### 🎨 Görsel Özellikler
- **Renk Tutarlılığı**: Aynı hacim = aynı renk (tüm mumlarda)
- **Çizim Araçları**: Yatay çizgi, trend çizgisi, dikdörtgen
- **Volume Profile**: Seçili alanın yatay hacim profili
- **Open Interest**: Futures pariteler için alt panel

## Kurulum

### Gereksinimler
- Quantower Platform
- .NET 8.0
- Visual Studio 2022 (önerilen)

### Adımlar
1. Projeyi klonlayın
2. Visual Studio'da açın
3. NuGet paketlerini geri yükleyin
4. Projeyi derleyin
5. Quantower'a yükleyin

## Kullanım

### Temel Kullanım
1. **Sembol Seçimi**: Ana grafikte BTC, ETH veya XRP paritesi seçin
2. **Borsa Ekleme**: UI panelinden ek borsaları seçin
3. **Model Seçimi**: Model 1 veya Model 2'yi seçin
4. **Ayarlar**: Tick size, hücre yüksekliği vb. ayarlayın

### Gelişmiş Özellikler
- **Strike VWAP**: Grafikte istediğiniz noktaya tıklayın
- **POC Toggle**: Günlük/haftalık POC seviyelerini açın/kapatın
- **CVD Reset**: CVD sıfırlama periyodunu değiştirin
- **Volume Profile**: Alan seçimi ile hacim profili görüntüleyin

## API Kullanımı

### VolumeAnalysisData
```csharp
// Bar verilerine erişim
var bar = HistoricalData[0, SeekOriginHistory.Begin] as HistoryItemBar;
var volumeData = bar.VolumeAnalysisData;

// Fiyat seviyeleri
foreach (var level in volumeData.PriceLevels)
{
    var price = level.Key;
    var data = level.Value;
    
    var buyVolume = data.BuyVolume;
    var sellVolume = data.SellVolume;
    var delta = data.Delta;
    var trades = data.Trades;
}
```

### Aggregation
```csharp
// Model 1: Doğrudan toplama
var aggregatedData = volumeAggregator.AggregateBar(barIndex, AggregationModel.Direct);

// Model 2: Normalize edilmiş
var normalizedData = volumeAggregator.AggregateBar(barIndex, AggregationModel.Normalized, referenceSymbol);
```

### Custom Rendering
```csharp
public override void OnPaintChart(PaintChartEventArgs args)
{
    base.OnPaintChart(args);
    
    // FootPrint hücrelerini çiz
    footPrintRenderer.RenderFootPrint(graphics, mainWindow, aggregatedData, barWidth, cellHeight, symbol);
    
    // FPBS çiz
    footPrintRenderer.RenderFPBS(graphics, mainWindow, fpbsData, barWidth);
    
    // POC çizgilerini çiz
    footPrintRenderer.RenderPOCLines(graphics, mainWindow, pocData, barWidth);
}
```

## Desteklenen Borsalar

### Spot Pariteler
- **BTC**: BTC/USDT, BTC/USD, BTC/USDC
- **ETH**: ETH/USDT, ETH/USD, ETH/USDC  
- **XRP**: XRP/USDT, XRP/USD, XRP/USDC

### Futures Pariteler
- **BTC**: BTCUSDT, BTCUSD
- **ETH**: ETHUSDT, ETHUSD
- **XRP**: XRPUSDT, XRPUSD

## Performans Optimizasyonu

### Veri Yönetimi
- **Lazy Loading**: Sadece görünür alan yüklenir
- **Caching**: Hesaplanmış veriler önbelleğe alınır
- **Memory Management**: Kullanılmayan veriler temizlenir

### Rendering Optimizasyonu
- **Clipping**: Sadece görünür hücreler çizilir
- **Color Caching**: Renk hesaplamaları önbelleğe alınır
- **Batch Updates**: Toplu güncellemeler

## Sorun Giderme

### Yaygın Sorunlar
1. **Veri Yüklenmiyor**: Borsa bağlantısını kontrol edin
2. **Renkler Tutarsız**: ColorScaleManager'ı sıfırlayın
3. **Performans Düşük**: Görünür alanı daraltın

### Log Mesajları
```csharp
Core.Instance.Loggers.Log("FootPrint data loaded", LoggingLevel.Info);
Core.Instance.Loggers.Log($"Aggregated {symbols.Count} symbols", LoggingLevel.Debug);
```

## Katkıda Bulunma

1. Fork yapın
2. Feature branch oluşturun (`git checkout -b feature/amazing-feature`)
3. Commit yapın (`git commit -m 'Add amazing feature'`)
4. Push yapın (`git push origin feature/amazing-feature`)
5. Pull Request oluşturun

## Lisans

Bu proje Quantower API örnekleri kapsamında geliştirilmiştir.

## İletişim

Sorularınız için GitHub Issues kullanın veya Quantower topluluğuna katılın.

---

**Not**: Bu indicator eğitim amaçlıdır. Gerçek trading için ek testler ve optimizasyonlar gerekebilir.
