// Copyright QUANTOWER LLC. © 2024. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace AggregatedFootprint
{
    /// <summary>
    /// Volume data aggregation engine for multi-exchange FootPrint
    /// </summary>
    public class VolumeAggregator
    {
        private readonly Dictionary<Symbol, HistoricalData> symbolHistories;
        private readonly Dictionary<Symbol, BarData> symbolBarData;
        private readonly Dictionary<Symbol, IVolumeAnalysisCalculationProgress> volumeAnalysisProgress;
        
        public VolumeAggregator()
        {
            symbolHistories = new Dictionary<Symbol, HistoricalData>();
            symbolBarData = new Dictionary<Symbol, BarData>();
            volumeAnalysisProgress = new Dictionary<Symbol, IVolumeAnalysisCalculationProgress>();
        }

        public void AddSymbol(Symbol symbol, Period period, HistoryType historyType)
        {
            if (!symbolHistories.ContainsKey(symbol))
            {
                try
                {
                    // Grafiğin TF ve HistoryType'ını eşle
                    var from = DateTime.UtcNow.AddDays(-30);
                    var history = symbol.GetHistory(period, historyType, from);

                                    // Harici history için Volume Analysis'i özellikle başlat
                var progress = Core.Instance.VolumeAnalysis.CalculateProfile(history);
                    volumeAnalysisProgress[symbol] = progress;
                    
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
        }

        public void RemoveSymbol(Symbol symbol)
        {
            if (symbolHistories.ContainsKey(symbol))
            {
                // Abort volume analysis if still running
                if (volumeAnalysisProgress.ContainsKey(symbol))
                {
                    volumeAnalysisProgress[symbol]?.AbortLoading();
                    volumeAnalysisProgress.Remove(symbol);
                }
                
                symbolHistories[symbol]?.Dispose();
                symbolHistories.Remove(symbol);
                symbolBarData.Remove(symbol);
            }
        }

        public bool IsVolumeAnalysisReady(Symbol symbol)
        {
            if (!volumeAnalysisProgress.ContainsKey(symbol))
                return false;
                
            return volumeAnalysisProgress[symbol].State == VolumeAnalysisCalculationState.Finished;
        }

        public AggregatedBarData AggregateBar(int mainBarIndex, AggregationModel model, Symbol referenceSymbol = null, double tickSize = 0.1)
        {
            var aggregatedData = new Dictionary<double, VolumeAnalysisItem>();
            var barTimes = new List<DateTime>();
            var barRanges = new Dictionary<Symbol, PriceRange>();

            // Get main bar time for alignment
            var mainHistory = symbolHistories.Values.FirstOrDefault();
            if (mainHistory == null || mainBarIndex >= mainHistory.Count)
                return new AggregatedBarData { BarIndex = mainBarIndex, PriceLevels = aggregatedData };

            var mainBar = mainHistory[mainBarIndex] as HistoryItemBar;
            if (mainBar == null)
                return new AggregatedBarData { BarIndex = mainBarIndex, PriceLevels = aggregatedData };

            var mainTime = mainBar.TimeLeft;
            barTimes.Add(mainTime);

            // Collect data from all symbols with time alignment
            foreach (var kvp in symbolHistories)
            {
                var symbol = kvp.Key;
                var history = kvp.Value;
                
                // Check if volume analysis is ready
                if (!IsVolumeAnalysisReady(symbol))
                    continue;
                
                // Get time-aligned bar index
                var alignedIndex = (int)Math.Round(history.GetIndexByTime(mainTime.Ticks, SeekOriginHistory.End));
                if (alignedIndex < 0 || alignedIndex >= history.Count)
                    continue;
                
                var bar = history[alignedIndex] as HistoryItemBar;
                if (bar?.VolumeAnalysisData?.PriceLevels != null)
                {
                    // Store price range for normalization
                    if (model == AggregationModel.Normalized)
                    {
                        barRanges[symbol] = new PriceRange
                        {
                            High = bar.High,
                            Low = bar.Low,
                            Open = bar.Open,
                            Close = bar.Close
                        };
                    }

                    // Process price levels
                    foreach (var priceLevel in bar.VolumeAnalysisData.PriceLevels)
                    {
                        var originalPrice = priceLevel.Key;
                        var data = priceLevel.Value;
                        
                        // Apply aggregation model
                        var targetPrice = originalPrice;
                        if (model == AggregationModel.Normalized && referenceSymbol != null && 
                            barRanges.ContainsKey(referenceSymbol) && barRanges.ContainsKey(symbol))
                        {
                            targetPrice = NormalizePrice(originalPrice, barRanges[symbol], barRanges[referenceSymbol]);
                        }
                        
                        // Round to tick size
                        targetPrice = Math.Round(targetPrice / tickSize) * tickSize;
                        
                        // Aggregate data with proper object creation
                        if (!aggregatedData.TryGetValue(targetPrice, out var acc))
                        {
                            aggregatedData[targetPrice] = acc = new VolumeAnalysisItem();
                        }
                        
                        acc.BuyVolume += data.BuyVolume;
                        acc.SellVolume += data.SellVolume;
                        acc.Trades += data.Trades;
                        acc.Delta += data.Delta;
                    }
                }
            }

            return new AggregatedBarData
            {
                BarIndex = mainBarIndex,
                Time = barTimes.Count > 0 ? barTimes[0] : DateTime.MinValue,
                PriceLevels = aggregatedData,
                Model = model,
                ReferenceSymbol = referenceSymbol
            };
        }

        private double NormalizePrice(double price, PriceRange sourceRange, PriceRange referenceRange)
        {
            if (sourceRange.High == sourceRange.Low || referenceRange.High == referenceRange.Low)
                return price;

            // Calculate normalized position within source range
            var normalizedPosition = (price - sourceRange.Low) / (sourceRange.High - sourceRange.Low);
            
            // Clamp to [0, 1] range
            normalizedPosition = Math.Max(0, Math.Min(1, normalizedPosition));
            
            // Map to reference range
            return referenceRange.Low + normalizedPosition * (referenceRange.High - referenceRange.Low);
        }

        private void OnHistoryUpdated(Symbol symbol, HistoryEventArgs e)
        {
            // Update cached bar data if needed
            if (symbolBarData.ContainsKey(symbol))
            {
                symbolBarData.Remove(symbol);
            }
        }

        public void Dispose()
        {
            // Abort all volume analysis calculations
            foreach (var progress in volumeAnalysisProgress.Values)
            {
                progress?.AbortLoading();
            }
            volumeAnalysisProgress.Clear();
            
            foreach (var history in symbolHistories.Values)
            {
                history?.Dispose();
            }
            symbolHistories.Clear();
            symbolBarData.Clear();
        }
    }

    public class AggregatedBarData
    {
        public int BarIndex { get; set; }
        public DateTime Time { get; set; }
        public Dictionary<double, VolumeAnalysisItem> PriceLevels { get; set; }
        public AggregationModel Model { get; set; }
        public Symbol ReferenceSymbol { get; set; }
        
        public double TotalVolume => PriceLevels?.Values.Sum(v => v.BuyVolume + v.SellVolume) ?? 0;
        public double TotalBuyVolume => PriceLevels?.Values.Sum(v => v.BuyVolume) ?? 0;
        public double TotalSellVolume => PriceLevels?.Values.Sum(v => v.SellVolume) ?? 0;
        public double TotalDelta => PriceLevels?.Values.Sum(v => v.Delta) ?? 0;
        public int TotalTrades => PriceLevels?.Values.Sum(v => v.Trades) ?? 0;
    }

    public class BarData
    {
        public DateTime Time { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double Volume { get; set; }
        public Dictionary<double, VolumeAnalysisItem> PriceLevels { get; set; }
    }

    public class PriceRange
    {
        public double High { get; set; }
        public double Low { get; set; }
        public double Open { get; set; }
        public double Close { get; set; }
    }
}