// Copyright QUANTOWER LLC. © 2024. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TradingPlatform.BusinessLayer;
using AggregatedFootprint.Services;

namespace AggregatedFootprint
{
    /// <summary>
    /// Birleştirilmiş FrontPrint Grafiği - BTC, ETH, XRP için çoklu borsa verilerini birleştirir
    /// </summary>
    public class AggregatedFootprint : Indicator, IVolumeAnalysisIndicator
    {
        #region Parameters

        [InputParameter("Aggregation Model", 0, variants: new object[] {
            "Model 1 - Direct Aggregation", AggregationModel.Direct,
            "Model 2 - Normalized Aggregation", AggregationModel.Normalized
        })]
        public AggregationModel AggregationModel { get; set; } = AggregationModel.Direct;

        [InputParameter("Reference Symbol (Model 2)", 1)]
        public Symbol ReferenceSymbol { get; set; }

        [InputParameter("Tick Size", 2, 0.0001, 1000, 0.0001, 4)]
        public double TickSize { get; set; } = 0.1;

        [InputParameter("Cell Height (pixels)", 3, 10, 100, 1, 0)]
        public int CellHeight { get; set; } = 20;

        [InputParameter("Show VWAP", 4)]
        public bool ShowVWAP { get; set; } = true;

        [InputParameter("Show Strike VWAP", 5)]
        public bool ShowStrikeVWAP { get; set; } = true;

        [InputParameter("Show FPBS", 6)]
        public bool ShowFPBS { get; set; } = true;

        [InputParameter("Show POC/VAL/VAH", 7)]
        public bool ShowPOC { get; set; } = true;

        [InputParameter("POC Period", 8, variants: new object[] {
            "Daily", POCPeriod.Daily,
            "Weekly", POCPeriod.Weekly
        })]
        public POCPeriod POCPeriod { get; set; } = POCPeriod.Daily;

        [InputParameter("CVD Reset Period", 9, variants: new object[] {
            "Daily", CVDResetPeriod.Daily,
            "Weekly", CVDResetPeriod.Weekly
        })]
        public CVDResetPeriod CVDResetPeriod { get; set; } = CVDResetPeriod.Daily;

        #endregion

        #region Private Fields

        private bool volumeAnalysisLoaded;
        private readonly List<Symbol> aggregatedSymbols = new List<Symbol>();
        private readonly Dictionary<Symbol, HistoricalData> symbolHistories = new Dictionary<Symbol, HistoricalData>();
        private readonly Dictionary<HistoricalData, IVolumeAnalysisCalculationProgress> vaProgress = new Dictionary<HistoricalData, IVolumeAnalysisCalculationProgress>();
        
        // Derivatives status service for OI/Funding data
        private readonly DerivativesStatusService derivativesService = new DerivativesStatusService();
        
        // Mouse event handler for proper disposal
        private EventHandler<TradingPlatform.BusinessLayer.Chart.ChartMouseNativeEventArgs> _mouseDownHandler;
        
        // Throttling for performance
        private long _nextDerivUpdateMs;
        private long _nextColorScaleUpdateMs;
        
        // VWAP calculations
        private double dailyVWAP;
        private double dailyVWAPVolume;
        private DateTime lastVWAPReset;
        private double strikeVWAP;
        private double strikeVWAPVolume;
        private int strikeVWAPStartIndex = -1;
        
        // FPBS calculations
        private double cumulativeDelta;
        private DateTime lastCVDReset;
        
        // POC calculations
        private double dailyPOC, dailyVAL, dailyVAH;
        private double weeklyPOC, weeklyVAL, weeklyVAH;
        private DateTime lastPOCReset;
        
        // Color scaling for heatmap
        private double maxVolume = 1;
        private double minVolume = 0;
        
        // UI state
        private Point lastMousePosition;
        private bool isMouseDown;
        
        // Cache for aggregated data
        private readonly Dictionary<string, Dictionary<double, VolumeAnalysisItem>> aggregatedDataCache = new Dictionary<string, Dictionary<double, VolumeAnalysisItem>>();
        private readonly Dictionary<string, DateTime> cacheTimestamps = new Dictionary<string, DateTime>();
        private readonly object _aggCacheLock = new object();
        private const int CACHE_EXPIRY_MINUTES = 1;

        #endregion

        #region Constructor

        public AggregatedFootprint()
            : base()
        {
            Name = "Aggregated FootPrint";
            Description = "Multi-exchange aggregated FootPrint for BTC, ETH, XRP with VWAP, FPBS, and POC indicators";
            
            // Add line series for VWAP and other indicators
            AddLineSeries("Daily VWAP", Color.Yellow, 2, LineStyle.Solid);
            AddLineSeries("Strike VWAP", Color.Orange, 2, LineStyle.Dashed);
            AddLineSeries("POC", Color.White, 1, LineStyle.Solid);
            AddLineSeries("VAL", Color.Red, 1, LineStyle.Solid);
            AddLineSeries("VAH", Color.Green, 1, LineStyle.Solid);
            
            SeparateWindow = false;
        }

        #endregion

        #region IVolumeAnalysisIndicator Implementation

        public bool IsRequirePriceLevelsCalculation => true;

        public void VolumeAnalysisData_Loaded()
        {
            volumeAnalysisLoaded = true;
            InitializeAggregatedSymbols();
        }

        #endregion

        #region Initialization

        protected override void OnInit()
        {
            base.OnInit();
            
            // Initialize reference symbol if not set
            if (ReferenceSymbol == null)
                ReferenceSymbol = Symbol;
                
            // Initialize reset times
            lastVWAPReset = GetUTCDayStart(DateTime.UtcNow);
            lastCVDReset = GetUTCDayStart(DateTime.UtcNow);
            lastPOCReset = GetUTCDayStart(DateTime.UtcNow);
            
            // Initialize aggregated symbols list
            InitializeAggregatedSymbols();
            
            // Calculate initial POC values
            CalculatePOC();
            
            // Mouse event handler for Strike VWAP (proper disposal pattern)
            if (CurrentChart != null && ShowStrikeVWAP)
            {
                _mouseDownHandler = (s, e) =>
                {
                    if (e.Button == TradingPlatform.BusinessLayer.Native.NativeMouseButtons.Left &&
                        CurrentChart.MainWindow.ClientRectangle.Contains(e.Location) &&
                        ShowStrikeVWAP)
                    {
                        var t = CurrentChart.MainWindow.CoordinatesConverter.GetTime(e.Location.X);
                        strikeVWAPStartIndex = (int)HistoricalData.GetIndexByTime(t.Ticks, SeekOriginHistory.End);
                        Refresh();
                        e.Handled = true;
                    }
                };
                CurrentChart.MouseDown += _mouseDownHandler;
            }
        }

        private void InitializeAggregatedSymbols()
        {
            if (!volumeAnalysisLoaded) return;
            
            // Add current symbol to aggregated list
            if (!aggregatedSymbols.Contains(Symbol))
            {
                aggregatedSymbols.Add(Symbol);
                LoadSymbolHistory(Symbol);
            }
        }

        private void LoadSymbolHistory(Symbol symbol)
        {
            try
            {
                // Grafiğin TF ve HistoryType'ını eşle
                var from = DateTime.UtcNow.AddDays(-30);
                var history = symbol.GetHistory(Period, HistoryType.Last, from); // crypto için genelde Last

                // Harici history için Volume Analysis'i özellikle başlat
                var progress = Core.Instance.VolumeAnalysis.CalculateProfile(history);
                vaProgress[history] = progress;
                
                symbolHistories[symbol] = history;
                
                // Subscribe to updates
                history.HistoryItemUpdated += (s, e) => OnHistoryUpdated(symbol, e);
                history.NewHistoryItem += (s, e) => OnHistoryUpdated(symbol, e);
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log($"Error loading history for {symbol.Name}: {ex.Message}", LoggingLevel.Error);
            }
        }

        private bool Ready(HistoricalData hd)
        {
            return vaProgress.TryGetValue(hd, out var progress) && 
                   progress.State == VolumeAnalysisCalculationState.Finished;
        }

        #endregion

        #region Update Methods

        protected override void OnUpdate(UpdateArgs args)
        {
            if (!volumeAnalysisLoaded) return;
            
            // Update VWAP calculations
            UpdateVWAP();
            
            // Update FPBS calculations
            UpdateFPBS();
            
            // Update POC calculations
            UpdatePOC();
            
            // Update color scaling (throttled)
            if (Environment.TickCount64 >= _nextColorScaleUpdateMs)
            {
                _nextColorScaleUpdateMs = Environment.TickCount64 + 500; // 500ms throttle
                UpdateColorScaling();
            }
            
            // Update derivatives status (throttled, fire-and-forget)
            _ = UpdateDerivativesStatusAsync();
        }

        private void UpdateVWAP()
        {
            // Check if volume analysis is ready
            if (HistoricalData.VolumeAnalysisCalculationProgress == null || 
                HistoricalData.VolumeAnalysisCalculationProgress.State != VolumeAnalysisCalculationState.Finished)
                return;
                
            var currentTime = GetUTCDayStart(Time(0));
            
            // Reset daily VWAP if new day
            if (currentTime > lastVWAPReset)
            {
                dailyVWAP = 0;
                dailyVWAPVolume = 0;
                lastVWAPReset = currentTime;
            }
            
            // Calculate daily VWAP - use End-based indexing for current bar
            if (ShowVWAP && Count > 0)
            {
                var bar = HistoricalData[0] as HistoryItemBar; // End=0 is current bar
                if (bar?.VolumeAnalysisData != null)
                {
                    var typicalPrice = (bar.High + bar.Low + bar.Close) / 3.0;
                    var volume = bar.VolumeAnalysisData.Total.Volume;
                    
                    dailyVWAP = (dailyVWAP * dailyVWAPVolume + typicalPrice * volume) / (dailyVWAPVolume + volume);
                    dailyVWAPVolume += volume;
                    
                    SetValue(dailyVWAP, 0); // Daily VWAP line
                }
            }
            
            // Calculate strike VWAP
            if (ShowStrikeVWAP && strikeVWAPStartIndex >= 0 && Count > strikeVWAPStartIndex)
            {
                strikeVWAP = 0;
                strikeVWAPVolume = 0;
                
                for (int i = strikeVWAPStartIndex; i < Count; i++)
                {
                    var bar = HistoricalData[i] as HistoryItemBar; // End-based indexing
                    if (bar?.VolumeAnalysisData != null)
                    {
                        var typicalPrice = (bar.High + bar.Low + bar.Close) / 3.0;
                        var volume = bar.VolumeAnalysisData.Total.Volume;
                        
                        strikeVWAP = (strikeVWAP * strikeVWAPVolume + typicalPrice * volume) / (strikeVWAPVolume + volume);
                        strikeVWAPVolume += volume;
                    }
                }
                
                SetValue(strikeVWAP, 1); // Strike VWAP line
            }
        }

        private void UpdateFPBS()
        {
            // Check if volume analysis is ready
            if (HistoricalData.VolumeAnalysisCalculationProgress == null || 
                HistoricalData.VolumeAnalysisCalculationProgress.State != VolumeAnalysisCalculationState.Finished)
                return;
                
            var currentTime = GetUTCDayStart(Time(0));
            
            // Reset CVD if new period
            if (ShouldResetCVD(currentTime))
            {
                cumulativeDelta = 0;
                lastCVDReset = currentTime;
            }
            
            // Calculate FPBS for current bar - use End-based indexing
            if (ShowFPBS && Count > 0)
            {
                var bar = HistoricalData[0] as HistoryItemBar; // End=0 is current bar
                if (bar?.VolumeAnalysisData != null)
                {
                    var total = bar.VolumeAnalysisData.Total;
                    var delta = total.BuyVolume - total.SellVolume;
                    cumulativeDelta += delta;
                    
                    // Store FPBS data for painting
                    // This will be used in OnPaintChart
                }
            }
        }

        private void UpdatePOC()
        {
            // Check if volume analysis is ready
            if (HistoricalData.VolumeAnalysisCalculationProgress == null || 
                HistoricalData.VolumeAnalysisCalculationProgress.State != VolumeAnalysisCalculationState.Finished)
                return;

            var currentTime = Time(0); // End-based 0 = current bar
            var currentDay = GetUTCDayStart(currentTime);
            var currentWeek = GetUTCWeekStart(currentTime);
            
            var needRecalc = (POCPeriod == POCPeriod.Daily && currentDay > lastPOCReset) ||
                           (POCPeriod == POCPeriod.Weekly && currentWeek > GetUTCWeekStart(lastPOCReset));

            if (needRecalc || dailyPOC == 0) // First load guard
            {
                CalculatePOC();
                lastPOCReset = POCPeriod == POCPeriod.Daily ? currentDay : currentWeek;
            }
            
            // Update POC lines
            if (ShowPOC)
            {
                SetValue(dailyPOC, 2); // POC line
                SetValue(dailyVAL, 3); // VAL line
                SetValue(dailyVAH, 4); // VAH line
            }
        }

        private void UpdateColorScaling()
        {
            // Update global min/max for consistent coloring with Ready guard
            double currentMax = 0;
            double currentMin = double.MaxValue;
            
            foreach (var history in symbolHistories.Values)
            {
                if (!Ready(history)) continue; // Volume Analysis hazır mı?
                
                for (int i = 0; i < Math.Min(history.Count, 100); i++) // Check last 100 bars
                {
                    var bar = history[i] as HistoryItemBar; // End-based indexing
                    if (bar?.VolumeAnalysisData?.PriceLevels != null)
                    {
                        foreach (var level in bar.VolumeAnalysisData.PriceLevels.Values)
                        {
                            var volume = level.BuyVolume + level.SellVolume;
                            if (volume > 0) // Only positive volumes
                            {
                                currentMax = Math.Max(currentMax, volume);
                                currentMin = Math.Min(currentMin, volume);
                            }
                        }
                    }
                }
            }
            
            if (currentMax > 0 && currentMin < double.MaxValue)
            {
                maxVolume = Math.Max(maxVolume, currentMax);
                minVolume = Math.Min(minVolume, currentMin);
            }
        }

        private async Task UpdateDerivativesStatusAsync()
        {
            // Throttle to prevent excessive calls
            if (Environment.TickCount64 < _nextDerivUpdateMs) return;
            _nextDerivUpdateMs = Environment.TickCount64 + 5000; // 5 second throttle

            try
            {
                // Get derivatives status for aggregated symbols
                var futuresSymbols = aggregatedSymbols.Where(IsDerivativesSymbol).ToList();
                if (!futuresSymbols.Any()) return;

                var aggregatedStatus = await derivativesService.GetAggregatedStatusAsync(futuresSymbols);
                if (aggregatedStatus != null)
                {
                    // Update UI or store for display
                    OnDerivativesStatusUpdated(aggregatedStatus);
                }
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log($"Error updating derivatives status: {ex.Message}", LoggingLevel.Error);
            }
        }

        private bool IsDerivativesSymbol(Symbol symbol)
        {
            if (symbol == null) return false;

            // Check symbol name for derivatives indicators
            var name = symbol.Name.ToUpper();
            var derivativesIndicators = new[] { "PERP", "FUTURES", "SWAP", "PERPETUAL", "USD-PERP", "USDT-PERP" };
            
            return derivativesIndicators.Any(indicator => name.Contains(indicator));
        }

        private void OnDerivativesStatusUpdated(DerivativesStatus status)
        {
            // This method can be used to update UI or store status for display
            // For now, we'll just log the status
            Core.Instance.Loggers.Log($"Derivatives Status Updated - OI: {status.OpenInterest}, Funding: {status.FundingRate:P4}", LoggingLevel.Info);
        }

        #endregion

        #region Aggregation Methods

        private Dictionary<double, VolumeAnalysisItem> AggregateVolumeData_ByTimeAligned(int mainEndIdx)
        {
            // Create deterministic cache key
            var symList = aggregatedSymbols.OrderBy(s => s.Name).Select(s => s.Name);
            var cacheKey = $"{mainEndIdx}_{AggregationModel}_{TickSize}_{string.Join(",", symList)}_{ReferenceSymbol?.Name}";
            
            // Check cache first (thread-safe)
            lock (_aggCacheLock)
            {
                if (aggregatedDataCache.TryGetValue(cacheKey, out var hit) &&
                    cacheTimestamps.TryGetValue(cacheKey, out var ts) &&
                    (DateTime.UtcNow - ts).TotalMinutes < CACHE_EXPIRY_MINUTES)
                {
                    return hit;
                }
            }
            
            var result = new Dictionary<double, VolumeAnalysisItem>();
            var mainBar = HistoricalData[mainEndIdx] as HistoryItemBar;
            if (mainBar == null) return result;
            var t = mainBar.TimeLeft.Ticks;

            foreach (var (symbol, hd) in symbolHistories)
            {
                if (!Ready(hd)) continue; // Volume Analysis hazır mı?

                int idx = (int)Math.Round(hd.GetIndexByTime(t, SeekOriginHistory.End));
                if (idx < 0 || idx >= hd.Count) continue;

                var bar = hd[idx] as HistoryItemBar;
                var pl = bar?.VolumeAnalysisData?.PriceLevels;
                if (pl == null) continue;

                foreach (var kv in pl)
                {
                    double price = kv.Key;
                    if (AggregationModel == AggregationModel.Normalized && ReferenceSymbol != null)
                    {
                        if (TryGetRange(hd, idx, out var sL, out var sH) &&
                            TryGetRange(symbolHistories[ReferenceSymbol],
                                (int)Math.Round(symbolHistories[ReferenceSymbol].GetIndexByTime(t, SeekOriginHistory.End)),
                                out var rL, out var rH))
                        {
                            price = NormalizePriceLinear(kv.Key, sL, sH, rL, rH);
                        }
                    }

                    price = Math.Round(price / TickSize) * TickSize;

                    if (!result.TryGetValue(price, out var acc))
                        result[price] = acc = new VolumeAnalysisItem();

                    acc.BuyVolume += kv.Value.BuyVolume;
                    acc.SellVolume += kv.Value.SellVolume;
                    acc.Trades += kv.Value.Trades;
                    acc.Delta += kv.Value.Delta;
                }
            }

            // Cache the result (thread-safe)
            lock (_aggCacheLock)
            {
                aggregatedDataCache[cacheKey] = result;
                cacheTimestamps[cacheKey] = DateTime.UtcNow;
                CleanCache();
            }

            return result;
        }

        private void CleanCache()
        {
            // This method is called within lock(_aggCacheLock) context
            var expiredKeys = cacheTimestamps
                .Where(kvp => DateTime.UtcNow.Subtract(kvp.Value).TotalMinutes > CACHE_EXPIRY_MINUTES)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                aggregatedDataCache.Remove(key);
                cacheTimestamps.Remove(key);
            }
        }

        private static bool TryGetRange(HistoricalData hd, int idx, out double low, out double high)
        {
            low = high = double.NaN;
            var b = hd?[idx] as HistoryItemBar;
            if (b == null) return false;
            low = b.Low; high = b.High; return high > low;
        }

        private static double NormalizePriceLinear(double p, double sL, double sH, double rL, double rH)
        {
            if (sH <= sL || rH <= rL) return p;
            var t = (p - sL) / (sH - sL);
            if (t < 0) t = 0; if (t > 1) t = 1;
            return rL + t * (rH - rL);
        }

        private double NormalizePrice(double price, Symbol sourceSymbol, Symbol referenceSymbol, int barIndex)
        {
            // Get price ranges for both symbols
            var sourceBar = symbolHistories[sourceSymbol][barIndex, SeekOriginHistory.Begin] as HistoryItemBar;
            var referenceBar = symbolHistories[referenceSymbol][barIndex, SeekOriginHistory.Begin] as HistoryItemBar;
            
            if (sourceBar == null || referenceBar == null) return price;
            
            var sourceRange = sourceBar.High - sourceBar.Low;
            var referenceRange = referenceBar.High - referenceBar.Low;
            
            if (sourceRange == 0 || referenceRange == 0) return price;
            
            // Calculate normalized position
            var normalizedPosition = (price - sourceBar.Low) / sourceRange;
            
            // Map to reference range
            return referenceBar.Low + normalizedPosition * referenceRange;
        }

        #endregion

        #region POC Calculations

        private void CalculatePOC()
        {
            var volumeProfile = new Dictionary<double, double>();
            
            // Collect volume data for the period
            var startTime = POCPeriod == POCPeriod.Daily ? 
                GetUTCDayStart(DateTime.UtcNow) : 
                GetUTCWeekStart(DateTime.UtcNow);
                
            foreach (var history in symbolHistories.Values)
            {
                for (int i = 0; i < history.Count; i++)
                {
                    var bar = history[i, SeekOriginHistory.Begin] as HistoryItemBar;
                    if (bar?.TimeLeft >= startTime && bar?.VolumeAnalysisData?.PriceLevels != null)
                    {
                        foreach (var level in bar.VolumeAnalysisData.PriceLevels)
                        {
                            var price = Math.Round(level.Key / TickSize) * TickSize;
                            var volume = level.Value.BuyVolume + level.Value.SellVolume;
                            
                            if (volumeProfile.ContainsKey(price))
                                volumeProfile[price] += volume;
                            else
                                volumeProfile[price] = volume;
                        }
                    }
                }
            }
            
            if (volumeProfile.Count > 0)
            {
                // Find POC (Point of Control)
                var poc = volumeProfile.OrderByDescending(x => x.Value).First();
                dailyPOC = poc.Key;
                
                // Calculate VAL and VAH (Value Area Low/High)
                CalculateValueArea(volumeProfile, out dailyVAL, out dailyVAH);
            }
        }

        private void CalculateValueArea(Dictionary<double, double> volumeProfile, out double val, out double vah)
        {
            var sortedLevels = volumeProfile.OrderBy(x => x.Key).ToList();
            var totalVolume = volumeProfile.Values.Sum();
            var valueAreaVolume = totalVolume * 0.7; // 70% value area
            
            var pocIndex = sortedLevels.FindIndex(x => x.Key == dailyPOC);
            if (pocIndex == -1) pocIndex = sortedLevels.Count / 2;
            
            var currentVolume = sortedLevels[pocIndex].Value;
            var upIndex = pocIndex;
            var downIndex = pocIndex;
            
            while (currentVolume < valueAreaVolume && (upIndex > 0 || downIndex < sortedLevels.Count - 1))
            {
                var upVolume = upIndex > 0 ? sortedLevels[upIndex - 1].Value : 0;
                var downVolume = downIndex < sortedLevels.Count - 1 ? sortedLevels[downIndex + 1].Value : 0;
                
                if (upVolume >= downVolume && upVolume > 0)
                {
                    upIndex--;
                    currentVolume += upVolume;
                }
                else if (downVolume > 0)
                {
                    downIndex++;
                    currentVolume += downVolume;
                }
                else
                {
                    break;
                }
            }
            
            val = sortedLevels[upIndex].Key;
            vah = sortedLevels[downIndex].Key;
        }

        #endregion

        #region Painting

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);
            
            if (!volumeAnalysisLoaded || CurrentChart == null) return;
            
            var graphics = args.Graphics;
            var mainWindow = CurrentChart.MainWindow;
            
            // Set clipping to main window
            var prevClip = graphics.ClipBounds;
            graphics.SetClip(mainWindow.ClientRectangle);
            
            try
            {
                // Paint FootPrint cells
                PaintFootPrintCells(graphics, mainWindow);
                
                // Paint FPBS
                if (ShowFPBS)
                    PaintFPBS(graphics, mainWindow);
                
                // Paint POC/VAL/VAH
                if (ShowPOC)
                    PaintPOCLines(graphics, mainWindow);
                
                // Paint VWAP deviation bands
                if (ShowVWAP)
                    PaintVWAPBands(graphics, mainWindow);
            }
            finally
            {
                graphics.SetClip(prevClip);
            }
        }

        private void PaintFootPrintCells(Graphics graphics, IChartWindow mainWindow)
        {
            // DrawOnBars & DrawValueAreaForEachBarIndicator kalıbı
            var wnd = mainWindow;
            var leftTime = wnd.CoordinatesConverter.GetTime(wnd.ClientRectangle.Left);
            var rightTime = wnd.CoordinatesConverter.GetTime(wnd.ClientRectangle.Right);

            int leftIdx = (int)wnd.CoordinatesConverter.GetBarIndex(leftTime);
            int rightIdx = (int)Math.Ceiling(wnd.CoordinatesConverter.GetBarIndex(rightTime));
            
            var font = new Font("Arial", 8, FontStyle.Regular);

            for (int chartIdx = leftIdx; chartIdx <= rightIdx; chartIdx++)
            {
                if (chartIdx <= 0 || chartIdx >= HistoricalData.Count) continue;

                // chart index → zaman → main HD end-bazlı index
                var t = (HistoricalData[chartIdx, SeekOriginHistory.Begin] as HistoryItemBar)?.TimeLeft
                        ?? wnd.CoordinatesConverter.GetTime(chartIdx);
                int mainEndIdx = (int)Math.Round(HistoricalData.GetIndexByTime(t.Ticks, SeekOriginHistory.End));

                var agg = AggregateVolumeData_ByTimeAligned(mainEndIdx);
                PaintBarFootPrint(graphics, wnd, mainEndIdx, agg, font);
            }
        }

        private void PaintBarFootPrint(Graphics graphics, IChartWindow mainWindow, int barIndex, 
            Dictionary<double, VolumeAnalysisItem> volumeData, Font font)
        {
            var bar = HistoricalData[barIndex] as HistoryItemBar; // End-based indexing
            if (bar == null) return;
            
            var barX = (int)Math.Round(mainWindow.CoordinatesConverter.GetChartX(bar.TimeLeft));
            var barWidth = CurrentChart.BarsWidth;
            
            // Sort price levels
            var sortedLevels = volumeData.OrderByDescending(x => x.Key).ToList();
            
            foreach (var level in sortedLevels)
            {
                var price = level.Key;
                var data = level.Value;
                
                var cellY = (int)mainWindow.CoordinatesConverter.GetChartY(price);
                var cellRect = new Rectangle(barX, cellY - CellHeight / 2, barWidth, CellHeight);
                
                // Calculate colors based on buy/sell volume
                var totalVolume = data.BuyVolume + data.SellVolume;
                var buyRatio = data.BuyVolume / totalVolume;
                var sellRatio = data.SellVolume / totalVolume;
                
                Color cellColor;
                if (buyRatio > sellRatio)
                {
                    // Green shades for buy dominance
                    var intensity = (float)(buyRatio * GetVolumeIntensity(totalVolume));
                    cellColor = Color.FromArgb(128, Color.Green.R, (int)(Color.Green.G * intensity), Color.Green.B);
                }
                else
                {
                    // Red shades for sell dominance
                    var intensity = (float)(sellRatio * GetVolumeIntensity(totalVolume));
                    cellColor = Color.FromArgb(128, (int)(Color.Red.R * intensity), Color.Red.G, Color.Red.B);
                }
                
                // Fill cell
                using (var brush = new SolidBrush(cellColor))
                {
                    graphics.FillRectangle(brush, cellRect);
                }
                
                // Draw text
                var buyText = data.BuyVolume.ToString("F1");
                var sellText = data.SellVolume.ToString("F1");
                
                var buyRect = new Rectangle(cellRect.X, cellRect.Y, cellRect.Width / 2, cellRect.Height);
                var sellRect = new Rectangle(cellRect.X + cellRect.Width / 2, cellRect.Y, cellRect.Width / 2, cellRect.Height);
                
                var buyFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var sellFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                
                graphics.DrawString(buyText, font, Brushes.White, buyRect, buyFormat);
                graphics.DrawString(sellText, font, Brushes.White, sellRect, sellFormat);
            }
        }

        private void PaintFPBS(Graphics graphics, IChartWindow mainWindow)
        {
            // Paint FPBS bar at the bottom
            var fpbsHeight = 100;
            var fpbsY = mainWindow.ClientRectangle.Bottom - fpbsHeight;
            var fpbsRect = new Rectangle(mainWindow.ClientRectangle.Left, fpbsY, 
                mainWindow.ClientRectangle.Width, fpbsHeight);
            
            using (var brush = new SolidBrush(Color.FromArgb(50, Color.Black)))
            {
                graphics.FillRectangle(brush, fpbsRect);
            }
            
            // Draw FPBS labels
            var font = new Font("Arial", 8, FontStyle.Regular);
            var y = fpbsY + 10;
            
            graphics.DrawString("VOLUME | DELTA | CVD | BUY | SELL", font, Brushes.White, 
                fpbsRect.Left + 10, y);
        }

        private void PaintPOCLines(Graphics graphics, IChartWindow mainWindow)
        {
            // POC lines are already drawn by the line series
            // This method can be used for additional POC visualization
        }

        private void PaintVWAPBands(Graphics graphics, IChartWindow mainWindow)
        {
            // Paint VWAP deviation bands
            if (ShowVWAP && dailyVWAP > 0)
            {
                var vwapY = (int)mainWindow.CoordinatesConverter.GetChartY(dailyVWAP);
                var leftX = mainWindow.ClientRectangle.Left;
                var rightX = mainWindow.ClientRectangle.Right;
                
                // Draw VWAP line (already drawn by line series)
                // Add deviation bands here if needed
            }
        }

        #endregion

        #region Helper Methods

        private float GetVolumeIntensity(double volume)
        {
            if (maxVolume == minVolume) return 1.0f;
            return (float)((volume - minVolume) / (maxVolume - minVolume));
        }

        private DateTime GetUTCDayStart(DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0, DateTimeKind.Utc);
        }

        private DateTime GetUTCWeekStart(DateTime dateTime)
        {
            var dayOfWeek = (int)dateTime.DayOfWeek;
            if (dayOfWeek == 0) dayOfWeek = 7; // Sunday = 7
            return GetUTCDayStart(dateTime.AddDays(-dayOfWeek + 1));
        }

        private bool ShouldResetCVD(DateTime currentTime)
        {
            if (CVDResetPeriod == CVDResetPeriod.Daily)
                return currentTime > lastCVDReset;
            else
                return GetUTCWeekStart(currentTime) > GetUTCWeekStart(lastCVDReset);
        }

        private void OnHistoryUpdated(Symbol symbol, HistoryEventArgs e)
        {
            // Handle history updates for aggregated symbols
            Refresh();
        }

        #endregion

        #region Mouse Events

        public override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            
            if (e.Button == MouseButtons.Left && ShowStrikeVWAP)
            {
                isMouseDown = true;
                lastMousePosition = e.Location;
                
                // Set strike VWAP start point - convert chart index to End-based index
                var mainWindow = CurrentChart.MainWindow;
                var time = mainWindow.CoordinatesConverter.GetTime(e.X);
                var chartIndex = mainWindow.CoordinatesConverter.GetBarIndex(time);
                strikeVWAPStartIndex = (int)Math.Round(HistoricalData.GetIndexByTime(time.Ticks, SeekOriginHistory.End));
                
                Refresh();
            }
        }

        public override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            isMouseDown = false;
        }

        public override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            lastMousePosition = e.Location;
        }

        #endregion

        #region Public Methods

        public void ApplySelectedSymbols(List<Symbol> selectedSymbols)
        {
            // Clear existing symbols
            foreach (var symbol in aggregatedSymbols.ToList())
            {
                if (symbol != Symbol) // Keep main symbol
                {
                    RemoveSymbol(symbol);
                }
            }
            
            // Add new symbols
            foreach (var symbol in selectedSymbols)
            {
                if (symbol != Symbol && !aggregatedSymbols.Contains(symbol))
                {
                    AddSymbol(symbol);
                }
            }
        }

        private void AddSymbol(Symbol symbol)
        {
            if (!aggregatedSymbols.Contains(symbol))
            {
                aggregatedSymbols.Add(symbol);
                LoadSymbolHistory(symbol);
            }
        }

        private void RemoveSymbol(Symbol symbol)
        {
            if (aggregatedSymbols.Contains(symbol))
            {
                aggregatedSymbols.Remove(symbol);
                if (symbolHistories.ContainsKey(symbol))
                {
                    symbolHistories[symbol]?.Dispose();
                    symbolHistories.Remove(symbol);
                }
            }
        }

        #endregion

        #region Cleanup

        public override void Dispose()
        {
            // Unsubscribe mouse event handler
            if (CurrentChart != null && _mouseDownHandler != null)
            {
                CurrentChart.MouseDown -= _mouseDownHandler;
            }
            
            // Abort all volume analysis calculations
            foreach (var kv in vaProgress)
            {
                if (kv.Value != null && kv.Value.State != VolumeAnalysisCalculationState.Finished)
                    kv.Value.AbortLoading();
            }
            vaProgress.Clear();
            
            // Clear cache (thread-safe)
            lock (_aggCacheLock)
            {
                aggregatedDataCache.Clear();
                cacheTimestamps.Clear();
            }
            
            // Clear derivatives service cache
            derivativesService.ClearAllCache();
            
            // Unsubscribe from history events and dispose
            foreach (var (symbol, history) in symbolHistories)
            {
                if (history != null)
                {
                    history.HistoryItemUpdated -= (s, e) => OnHistoryUpdated(symbol, e);
                    history.NewHistoryItem -= (s, e) => OnHistoryUpdated(symbol, e);
                    history.Dispose();
                }
            }
            symbolHistories.Clear();
            
            base.Dispose();
        }

        #endregion
    }

    #region Enums

    public enum AggregationModel
    {
        Direct,
        Normalized
    }

    public enum POCPeriod
    {
        Daily,
        Weekly
    }

    public enum CVDResetPeriod
    {
        Daily,
        Weekly
    }

    #endregion
}
