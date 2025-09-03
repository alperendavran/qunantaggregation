using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Modules;

namespace AggregatedFootprint.Services
{
    /// <summary>
    /// Exchange-agnostic derivatives status service
    /// Uses Quantower's vendor system instead of direct exchange APIs
    /// </summary>
    public class DerivativesStatusService
    {
        #region Private Fields

        private readonly Dictionary<string, DerivativesStatus> statusCache = new Dictionary<string, DerivativesStatus>();
        private readonly Dictionary<string, DateTime> lastUpdateTimes = new Dictionary<string, DateTime>();
        private readonly object cacheLock = new object();
        
        private const int CACHE_EXPIRY_SECONDS = 30; // 30 saniye cache
        private const int MAX_CACHE_SIZE = 1000;

        #endregion

        #region Public Methods

        /// <summary>
        /// Get derivatives status for a symbol using Quantower's vendor system
        /// </summary>
        public async Task<DerivativesStatus> GetDerivativesStatusAsync(Symbol symbol)
        {
            if (symbol == null) return null;

            var cacheKey = GetCacheKey(symbol);
            
            // Check cache first
            lock (cacheLock)
            {
                if (statusCache.ContainsKey(cacheKey) && 
                    lastUpdateTimes.ContainsKey(cacheKey) &&
                    DateTime.UtcNow.Subtract(lastUpdateTimes[cacheKey]).TotalSeconds < CACHE_EXPIRY_SECONDS)
                {
                    return statusCache[cacheKey];
                }
            }

            // Get from Quantower's vendor system
            var status = await GetStatusFromVendorAsync(symbol);
            
            if (status != null)
            {
                lock (cacheLock)
                {
                    // Clean old cache entries if needed
                    if (statusCache.Count > MAX_CACHE_SIZE)
                    {
                        CleanOldCacheEntries();
                    }
                    
                    statusCache[cacheKey] = status;
                    lastUpdateTimes[cacheKey] = DateTime.UtcNow;
                }
            }

            return status;
        }

        /// <summary>
        /// Get aggregated derivatives status for multiple symbols
        /// </summary>
        public async Task<DerivativesStatus> GetAggregatedStatusAsync(IEnumerable<Symbol> symbols)
        {
            if (symbols == null || !symbols.Any()) return null;

            var statuses = new List<DerivativesStatus>();
            
            foreach (var symbol in symbols)
            {
                var status = await GetDerivativesStatusAsync(symbol);
                if (status != null)
                {
                    statuses.Add(status);
                }
            }

            if (!statuses.Any()) return null;

            // Aggregate the statuses
            return AggregateStatuses(statuses);
        }

        /// <summary>
        /// Clear cache for a specific symbol
        /// </summary>
        public void ClearCache(Symbol symbol)
        {
            if (symbol == null) return;

            var cacheKey = GetCacheKey(symbol);
            lock (cacheLock)
            {
                statusCache.Remove(cacheKey);
                lastUpdateTimes.Remove(cacheKey);
            }
        }

        /// <summary>
        /// Clear all cache
        /// </summary>
        public void ClearAllCache()
        {
            lock (cacheLock)
            {
                statusCache.Clear();
                lastUpdateTimes.Clear();
            }
        }

        #endregion

        #region Private Methods

        private async Task<DerivativesStatus> GetStatusFromVendorAsync(Symbol symbol)
        {
            try
            {
                // Use Quantower's vendor system instead of direct exchange APIs
                var connection = symbol.Connection;
                if (connection == null) return null;

                // Check if this is a futures/derivatives symbol
                if (!IsDerivativesSymbol(symbol)) return null;

                // Get current market data from the vendor
                var currentPrice = await GetCurrentPriceAsync(symbol);
                if (currentPrice == null) return null;

                // Try to get Open Interest from the symbol's properties
                var openInterest = GetOpenInterestFromSymbol(symbol);
                
                // Try to get funding rate from the symbol's properties
                var fundingRate = GetFundingRateFromSymbol(symbol);

                // Try to get mark price from the symbol's properties
                var markPrice = GetMarkPriceFromSymbol(symbol);

                return new DerivativesStatus
                {
                    Symbol = symbol,
                    OpenInterest = openInterest,
                    FundingRate = fundingRate,
                    MarkPrice = markPrice,
                    IndexPrice = currentPrice.IndexPrice,
                    LastPrice = currentPrice.LastPrice,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log($"Error getting derivatives status for {symbol.Name}: {ex.Message}", LoggingLevel.Error);
                return null;
            }
        }

        private async Task<CurrentPrice> GetCurrentPriceAsync(Symbol symbol)
        {
            try
            {
                // Use Quantower's current price system
                var currentPrice = Core.Instance.CurrentPrices.GetCurrentPrice(symbol);
                if (currentPrice != null)
                {
                    return new CurrentPrice
                    {
                        LastPrice = currentPrice.LastPrice,
                        IndexPrice = currentPrice.IndexPrice,
                        MarkPrice = currentPrice.MarkPrice
                    };
                }

                // Fallback: get from historical data
                var history = symbol.GetHistory(Period.MIN1, HistoryType.Last, DateTime.UtcNow.AddMinutes(-1));
                if (history != null && history.Count > 0)
                {
                    var lastBar = history[0] as HistoryItemBar;
                    if (lastBar != null)
                    {
                        return new CurrentPrice
                        {
                            LastPrice = lastBar.Close,
                            IndexPrice = lastBar.Close, // Fallback
                            MarkPrice = lastBar.Close   // Fallback
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log($"Error getting current price for {symbol.Name}: {ex.Message}", LoggingLevel.Error);
                return null;
            }
        }

        private double? GetOpenInterestFromSymbol(Symbol symbol)
        {
            try
            {
                // Try to get Open Interest from symbol properties
                if (symbol.Properties != null)
                {
                    // Common property names for Open Interest
                    var oiPropertyNames = new[] { "OpenInterest", "OI", "Open_Interest", "open_interest" };
                    
                    foreach (var propName in oiPropertyNames)
                    {
                        if (symbol.Properties.ContainsKey(propName))
                        {
                            if (double.TryParse(symbol.Properties[propName].ToString(), out var oi))
                            {
                                return oi;
                            }
                        }
                    }
                }

                // Try to get from connection properties
                if (symbol.Connection?.Properties != null)
                {
                    var oiPropertyNames = new[] { "OpenInterest", "OI", "Open_Interest", "open_interest" };
                    
                    foreach (var propName in oiPropertyNames)
                    {
                        if (symbol.Connection.Properties.ContainsKey(propName))
                        {
                            if (double.TryParse(symbol.Connection.Properties[propName].ToString(), out var oi))
                            {
                                return oi;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log($"Error getting Open Interest for {symbol.Name}: {ex.Message}", LoggingLevel.Error);
                return null;
            }
        }

        private double? GetFundingRateFromSymbol(Symbol symbol)
        {
            try
            {
                // Try to get Funding Rate from symbol properties
                if (symbol.Properties != null)
                {
                    var fundingPropertyNames = new[] { "FundingRate", "Funding", "funding_rate", "funding" };
                    
                    foreach (var propName in fundingPropertyNames)
                    {
                        if (symbol.Properties.ContainsKey(propName))
                        {
                            if (double.TryParse(symbol.Properties[propName].ToString(), out var funding))
                            {
                                return funding;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log($"Error getting Funding Rate for {symbol.Name}: {ex.Message}", LoggingLevel.Error);
                return null;
            }
        }

        private double? GetMarkPriceFromSymbol(Symbol symbol)
        {
            try
            {
                // Try to get Mark Price from symbol properties
                if (symbol.Properties != null)
                {
                    var markPricePropertyNames = new[] { "MarkPrice", "Mark", "mark_price", "mark" };
                    
                    foreach (var propName in markPricePropertyNames)
                    {
                        if (symbol.Properties.ContainsKey(propName))
                        {
                            if (double.TryParse(symbol.Properties[propName].ToString(), out var markPrice))
                            {
                                return markPrice;
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Core.Instance.Loggers.Log($"Error getting Mark Price for {symbol.Name}: {ex.Message}", LoggingLevel.Error);
                return null;
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

        private string GetCacheKey(Symbol symbol)
        {
            return $"{symbol.Name}_{symbol.Connection?.Name}";
        }

        private void CleanOldCacheEntries()
        {
            var expiredKeys = lastUpdateTimes
                .Where(kvp => DateTime.UtcNow.Subtract(kvp.Value).TotalSeconds > CACHE_EXPIRY_SECONDS)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                statusCache.Remove(key);
                lastUpdateTimes.Remove(key);
            }
        }

        private DerivativesStatus AggregateStatuses(List<DerivativesStatus> statuses)
        {
            if (!statuses.Any()) return null;

            return new DerivativesStatus
            {
                Symbol = statuses.First().Symbol, // Use first symbol as reference
                OpenInterest = statuses.Sum(s => s.OpenInterest ?? 0),
                FundingRate = statuses.Average(s => s.FundingRate ?? 0),
                MarkPrice = statuses.Average(s => s.MarkPrice ?? 0),
                IndexPrice = statuses.Average(s => s.IndexPrice ?? 0),
                LastPrice = statuses.Average(s => s.LastPrice ?? 0),
                Timestamp = DateTime.UtcNow
            };
        }

        #endregion
    }

    #region Data Models

    public class DerivativesStatus
    {
        public Symbol Symbol { get; set; }
        public double? OpenInterest { get; set; }
        public double? FundingRate { get; set; }
        public double? MarkPrice { get; set; }
        public double? IndexPrice { get; set; }
        public double? LastPrice { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CurrentPrice
    {
        public double? LastPrice { get; set; }
        public double? IndexPrice { get; set; }
        public double? MarkPrice { get; set; }
    }

    #endregion
}
