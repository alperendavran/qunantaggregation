// Copyright QUANTOWER LLC. © 2024. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace AggregatedFootprint
{
    /// <summary>
    /// Symbol selector for adding/removing symbols to aggregation
    /// </summary>
    public class SymbolSelector
    {
        private readonly List<Symbol> availableSymbols = new List<Symbol>();
        private readonly List<Symbol> selectedSymbols = new List<Symbol>();

        public event EventHandler<SymbolSelectionChangedEventArgs> SelectionChanged;

        public SymbolSelector()
        {
            LoadAvailableSymbols();
        }

        private void LoadAvailableSymbols()
        {
            // Load BTC, ETH, XRP symbols from all available exchanges
            var targetCoins = new[] { "BTC", "ETH", "XRP" };
            var targetCurrencies = new[] { "USDT", "USD", "USDC" };
            var targetExchanges = new[] { "Binance", "Coinbase", "Kraken", "Bybit", "OKX", "Bitfinex", "Bitget", "CoinW", "Gate.io", "Huobi", "KuCoin" };

            foreach (var coin in targetCoins)
            {
                foreach (var currency in targetCurrencies)
                {
                    // Use regex pattern for more robust matching
                    var pattern = $@"^{coin}.*{currency}$|^{coin}.*{currency}.*$";
                    var symbols = Core.Instance.Symbols
                        .Where(s => System.Text.RegularExpressions.Regex.IsMatch(s.Name, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        .Where(s => targetExchanges.Any(ex => s.ConnectionId.Contains(ex, StringComparison.OrdinalIgnoreCase)))
                        .ToArray();

                    availableSymbols.AddRange(symbols);
                }
            }

            // Remove duplicates
            availableSymbols = availableSymbols.Distinct().ToList();
        }

        public List<Symbol> GetAvailableSymbols()
        {
            return new List<Symbol>(availableSymbols);
        }

        public List<Symbol> GetSelectedSymbols()
        {
            return new List<Symbol>(selectedSymbols);
        }

        public void AddSymbol(Symbol symbol)
        {
            if (!selectedSymbols.Contains(symbol))
            {
                selectedSymbols.Add(symbol);
                OnSelectionChanged(new SymbolSelectionChangedEventArgs(symbol, true));
            }
        }

        public void RemoveSymbol(Symbol symbol)
        {
            if (selectedSymbols.Contains(symbol))
            {
                selectedSymbols.Remove(symbol);
                OnSelectionChanged(new SymbolSelectionChangedEventArgs(symbol, false));
            }
        }

        public void ClearSelection()
        {
            var removedSymbols = new List<Symbol>(selectedSymbols);
            selectedSymbols.Clear();
            
            foreach (var symbol in removedSymbols)
            {
                OnSelectionChanged(new SymbolSelectionChangedEventArgs(symbol, false));
            }
        }

        public List<Symbol> GetSymbolsByCoin(string coin)
        {
            return availableSymbols
                .Where(s => s.Name.StartsWith(coin + "/"))
                .ToList();
        }

        public List<Symbol> GetSymbolsByExchange(string exchange)
        {
            return availableSymbols
                .Where(s => s.ConnectionId.Contains(exchange))
                .ToList();
        }

        public List<Symbol> GetSymbolsByMarketType(MarketType marketType)
        {
            return availableSymbols
                .Where(s => s.MarketType == marketType)
                .ToList();
        }

        protected virtual void OnSelectionChanged(SymbolSelectionChangedEventArgs e)
        {
            SelectionChanged?.Invoke(this, e);
        }
    }

    public class SymbolSelectionChangedEventArgs : EventArgs
    {
        public Symbol Symbol { get; }
        public bool IsAdded { get; }

        public SymbolSelectionChangedEventArgs(Symbol symbol, bool isAdded)
        {
            Symbol = symbol;
            IsAdded = isAdded;
        }
    }
}
