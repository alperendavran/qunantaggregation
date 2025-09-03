// Copyright QUANTOWER LLC. © 2024. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TradingPlatform.BusinessLayer;

namespace AggregatedFootprint
{
    /// <summary>
    /// UI control for FootPrint settings and symbol selection
    /// </summary>
    public class FootPrintUI : UserControl
    {
        private AggregatedFootprint indicator;
        private SymbolSelector symbolSelector;
        
        // UI Controls
        private ComboBox aggregationModelCombo;
        private ComboBox referenceSymbolCombo;
        private NumericUpDown tickSizeNumeric;
        private NumericUpDown cellHeightNumeric;
        private CheckBox showVWAPCheck;
        private CheckBox showStrikeVWAPCheck;
        private CheckBox showFBPSCheck;
        private CheckBox showPOCCheck;
        private ComboBox pocPeriodCombo;
        private ComboBox cvdResetCombo;
        
        // Symbol selection controls
        private ListBox availableSymbolsList;
        private ListBox selectedSymbolsList;
        private Button addSymbolButton;
        private Button removeSymbolButton;
        private Button clearSelectionButton;
        private ComboBox coinFilterCombo;
        private ComboBox exchangeFilterCombo;
        private ComboBox marketTypeFilterCombo;

        public FootPrintUI(AggregatedFootprint indicator)
        {
            this.indicator = indicator;
            this.symbolSelector = new SymbolSelector();
            
            InitializeComponent();
            SetupEventHandlers();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.Size = new Size(400, 600);
            this.Text = "Aggregated FootPrint Settings";
            
            var y = 10;
            var labelHeight = 20;
            var controlHeight = 25;
            var spacing = 30;
            
            // Aggregation Model
            var aggregationLabel = new Label
            {
                Text = "Aggregation Model:",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight)
            };
            this.Controls.Add(aggregationLabel);
            
            aggregationModelCombo = new ComboBox
            {
                Location = new Point(170, y),
                Size = new Size(200, controlHeight),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            aggregationModelCombo.Items.AddRange(new object[] { "Model 1 - Direct", "Model 2 - Normalized" });
            this.Controls.Add(aggregationModelCombo);
            y += spacing;
            
            // Reference Symbol (for Model 2)
            var referenceLabel = new Label
            {
                Text = "Reference Symbol:",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight)
            };
            this.Controls.Add(referenceLabel);
            
            referenceSymbolCombo = new ComboBox
            {
                Location = new Point(170, y),
                Size = new Size(200, controlHeight),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            this.Controls.Add(referenceSymbolCombo);
            y += spacing;
            
            // Tick Size
            var tickSizeLabel = new Label
            {
                Text = "Tick Size:",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight)
            };
            this.Controls.Add(tickSizeLabel);
            
            tickSizeNumeric = new NumericUpDown
            {
                Location = new Point(170, y),
                Size = new Size(100, controlHeight),
                DecimalPlaces = 4,
                Minimum = 0.0001m,
                Maximum = 1000m,
                Increment = 0.0001m
            };
            this.Controls.Add(tickSizeNumeric);
            y += spacing;
            
            // Cell Height
            var cellHeightLabel = new Label
            {
                Text = "Cell Height (px):",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight)
            };
            this.Controls.Add(cellHeightLabel);
            
            cellHeightNumeric = new NumericUpDown
            {
                Location = new Point(170, y),
                Size = new Size(100, controlHeight),
                Minimum = 10,
                Maximum = 100,
                Value = 20
            };
            this.Controls.Add(cellHeightNumeric);
            y += spacing;
            
            // Checkboxes
            showVWAPCheck = new CheckBox
            {
                Text = "Show VWAP",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight),
                Checked = true
            };
            this.Controls.Add(showVWAPCheck);
            y += 25;
            
            showStrikeVWAPCheck = new CheckBox
            {
                Text = "Show Strike VWAP",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight),
                Checked = true
            };
            this.Controls.Add(showStrikeVWAPCheck);
            y += 25;
            
            showFBPSCheck = new CheckBox
            {
                Text = "Show FPBS",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight),
                Checked = true
            };
            this.Controls.Add(showFBPSCheck);
            y += 25;
            
            showPOCCheck = new CheckBox
            {
                Text = "Show POC/VAL/VAH",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight),
                Checked = true
            };
            this.Controls.Add(showPOCCheck);
            y += spacing;
            
            // POC Period
            var pocPeriodLabel = new Label
            {
                Text = "POC Period:",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight)
            };
            this.Controls.Add(pocPeriodLabel);
            
            pocPeriodCombo = new ComboBox
            {
                Location = new Point(170, y),
                Size = new Size(100, controlHeight),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            pocPeriodCombo.Items.AddRange(new object[] { "Daily", "Weekly" });
            this.Controls.Add(pocPeriodCombo);
            y += spacing;
            
            // CVD Reset Period
            var cvdResetLabel = new Label
            {
                Text = "CVD Reset:",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight)
            };
            this.Controls.Add(cvdResetLabel);
            
            cvdResetCombo = new ComboBox
            {
                Location = new Point(170, y),
                Size = new Size(100, controlHeight),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cvdResetCombo.Items.AddRange(new object[] { "Daily", "Weekly" });
            this.Controls.Add(cvdResetCombo);
            y += spacing + 10;
            
            // Separator
            var separator = new Label
            {
                Text = "Symbol Selection",
                Location = new Point(10, y),
                Size = new Size(200, labelHeight),
                Font = new Font("Arial", 10, FontStyle.Bold)
            };
            this.Controls.Add(separator);
            y += 30;
            
            // Filters
            var filterLabel = new Label
            {
                Text = "Filters:",
                Location = new Point(10, y),
                Size = new Size(50, labelHeight)
            };
            this.Controls.Add(filterLabel);
            
            coinFilterCombo = new ComboBox
            {
                Location = new Point(70, y),
                Size = new Size(80, controlHeight),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            coinFilterCombo.Items.AddRange(new object[] { "All", "BTC", "ETH", "XRP" });
            coinFilterCombo.SelectedIndex = 0;
            this.Controls.Add(coinFilterCombo);
            
            exchangeFilterCombo = new ComboBox
            {
                Location = new Point(160, y),
                Size = new Size(100, controlHeight),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            exchangeFilterCombo.Items.AddRange(new object[] { "All", "Binance", "Coinbase", "Kraken", "Bybit", "OKX", "Bitfinex" });
            exchangeFilterCombo.SelectedIndex = 0;
            this.Controls.Add(exchangeFilterCombo);
            
            marketTypeFilterCombo = new ComboBox
            {
                Location = new Point(270, y),
                Size = new Size(80, controlHeight),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            marketTypeFilterCombo.Items.AddRange(new object[] { "All", "Spot", "Futures" });
            marketTypeFilterCombo.SelectedIndex = 0;
            this.Controls.Add(marketTypeFilterCombo);
            y += spacing;
            
            // Available Symbols
            var availableLabel = new Label
            {
                Text = "Available Symbols:",
                Location = new Point(10, y),
                Size = new Size(150, labelHeight)
            };
            this.Controls.Add(availableLabel);
            y += 25;
            
            availableSymbolsList = new ListBox
            {
                Location = new Point(10, y),
                Size = new Size(180, 120),
                SelectionMode = SelectionMode.MultiExtended
            };
            this.Controls.Add(availableSymbolsList);
            
            // Selected Symbols
            var selectedLabel = new Label
            {
                Text = "Selected Symbols:",
                Location = new Point(210, y - 25),
                Size = new Size(150, labelHeight)
            };
            this.Controls.Add(selectedLabel);
            
            selectedSymbolsList = new ListBox
            {
                Location = new Point(210, y),
                Size = new Size(180, 120),
                SelectionMode = SelectionMode.MultiExtended
            };
            this.Controls.Add(selectedSymbolsList);
            
            // Buttons
            addSymbolButton = new Button
            {
                Text = "Add >>",
                Location = new Point(200, y + 20),
                Size = new Size(60, 25)
            };
            this.Controls.Add(addSymbolButton);
            
            removeSymbolButton = new Button
            {
                Text = "<< Remove",
                Location = new Point(200, y + 50),
                Size = new Size(60, 25)
            };
            this.Controls.Add(removeSymbolButton);
            
            clearSelectionButton = new Button
            {
                Text = "Clear All",
                Location = new Point(200, y + 80),
                Size = new Size(60, 25)
            };
            this.Controls.Add(clearSelectionButton);
            
            // Apply Button
            var applyButton = new Button
            {
                Text = "Apply Settings",
                Location = new Point(10, y + 150),
                Size = new Size(100, 30),
                BackColor = Color.LightBlue
            };
            this.Controls.Add(applyButton);
            applyButton.Click += ApplyButton_Click;
        }

        private void SetupEventHandlers()
        {
            // Symbol selector events
            symbolSelector.SelectionChanged += SymbolSelector_SelectionChanged;
            
            // Filter events
            coinFilterCombo.SelectedIndexChanged += Filter_Changed;
            exchangeFilterCombo.SelectedIndexChanged += Filter_Changed;
            marketTypeFilterCombo.SelectedIndexChanged += Filter_Changed;
            
            // Symbol selection events
            addSymbolButton.Click += AddSymbolButton_Click;
            removeSymbolButton.Click += RemoveSymbolButton_Click;
            clearSelectionButton.Click += ClearSelectionButton_Click;
            
            // Load available symbols
            LoadAvailableSymbols();
        }

        private void LoadSettings()
        {
            // Load current indicator settings
            aggregationModelCombo.SelectedIndex = (int)indicator.AggregationModel;
            tickSizeNumeric.Value = (decimal)indicator.TickSize;
            cellHeightNumeric.Value = indicator.CellHeight;
            showVWAPCheck.Checked = indicator.ShowVWAP;
            showStrikeVWAPCheck.Checked = indicator.ShowStrikeVWAP;
            showFBPSCheck.Checked = indicator.ShowFPBS;
            showPOCCheck.Checked = indicator.ShowPOC;
            pocPeriodCombo.SelectedIndex = (int)indicator.POCPeriod;
            cvdResetCombo.SelectedIndex = (int)indicator.CVDResetPeriod;
            
            // Load reference symbols
            LoadReferenceSymbols();
        }

        private void LoadReferenceSymbols()
        {
            referenceSymbolCombo.Items.Clear();
            var symbols = symbolSelector.GetAvailableSymbols();
            foreach (var symbol in symbols)
            {
                referenceSymbolCombo.Items.Add($"{symbol.Name} ({symbol.ConnectionId})");
            }
            
            if (indicator.ReferenceSymbol != null)
            {
                var currentRef = $"{indicator.ReferenceSymbol.Name} ({indicator.ReferenceSymbol.ConnectionId})";
                var index = referenceSymbolCombo.Items.IndexOf(currentRef);
                if (index >= 0)
                    referenceSymbolCombo.SelectedIndex = index;
            }
        }

        private void LoadAvailableSymbols()
        {
            availableSymbolsList.Items.Clear();
            
            var symbols = symbolSelector.GetAvailableSymbols();
            
            // Apply filters
            if (coinFilterCombo.SelectedItem?.ToString() != "All")
            {
                var coin = coinFilterCombo.SelectedItem.ToString();
                symbols = symbols.Where(s => s.Name.StartsWith(coin + "/")).ToList();
            }
            
            if (exchangeFilterCombo.SelectedItem?.ToString() != "All")
            {
                var exchange = exchangeFilterCombo.SelectedItem.ToString();
                symbols = symbols.Where(s => s.ConnectionId.Contains(exchange)).ToList();
            }
            
            if (marketTypeFilterCombo.SelectedItem?.ToString() != "All")
            {
                var marketType = marketTypeFilterCombo.SelectedItem.ToString() == "Spot" ? 
                    MarketType.Spot : MarketType.Futures;
                symbols = symbols.Where(s => s.MarketType == marketType).ToList();
            }
            
            foreach (var symbol in symbols)
            {
                availableSymbolsList.Items.Add($"{symbol.Name} ({symbol.ConnectionId})");
            }
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            LoadAvailableSymbols();
        }

        private void AddSymbolButton_Click(object sender, EventArgs e)
        {
            var selectedIndices = availableSymbolsList.SelectedIndices;
            var symbols = symbolSelector.GetAvailableSymbols();
            
            foreach (int index in selectedIndices)
            {
                if (index < symbols.Count)
                {
                    symbolSelector.AddSymbol(symbols[index]);
                }
            }
        }

        private void RemoveSymbolButton_Click(object sender, EventArgs e)
        {
            var selectedIndices = selectedSymbolsList.SelectedIndices;
            var selectedSymbols = symbolSelector.GetSelectedSymbols();
            
            foreach (int index in selectedIndices)
            {
                if (index < selectedSymbols.Count)
                {
                    symbolSelector.RemoveSymbol(selectedSymbols[index]);
                }
            }
        }

        private void ClearSelectionButton_Click(object sender, EventArgs e)
        {
            symbolSelector.ClearSelection();
        }

        private void SymbolSelector_SelectionChanged(object sender, SymbolSelectionChangedEventArgs e)
        {
            UpdateSelectedSymbolsList();
        }

        private void UpdateSelectedSymbolsList()
        {
            selectedSymbolsList.Items.Clear();
            var selectedSymbols = symbolSelector.GetSelectedSymbols();
            
            foreach (var symbol in selectedSymbols)
            {
                selectedSymbolsList.Items.Add($"{symbol.Name} ({symbol.ConnectionId})");
            }
        }

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            // Apply settings to indicator
            indicator.AggregationModel = (AggregationModel)aggregationModelCombo.SelectedIndex;
            indicator.TickSize = (double)tickSizeNumeric.Value;
            indicator.CellHeight = (int)cellHeightNumeric.Value;
            indicator.ShowVWAP = showVWAPCheck.Checked;
            indicator.ShowStrikeVWAP = showStrikeVWAPCheck.Checked;
            indicator.ShowFPBS = showFBPSCheck.Checked;
            indicator.ShowPOC = showPOCCheck.Checked;
            indicator.POCPeriod = (POCPeriod)pocPeriodCombo.SelectedIndex;
            indicator.CVDResetPeriod = (CVDResetPeriod)cvdResetCombo.SelectedIndex;
            
            // Apply reference symbol
            if (referenceSymbolCombo.SelectedIndex >= 0)
            {
                var symbols = symbolSelector.GetAvailableSymbols();
                if (referenceSymbolCombo.SelectedIndex < symbols.Count)
                {
                    indicator.ReferenceSymbol = symbols[referenceSymbolCombo.SelectedIndex];
                }
            }
            
            // Apply selected symbols to indicator
            var selectedSymbols = symbolSelector.GetSelectedSymbols();
            indicator.ApplySelectedSymbols(selectedSymbols);
            
            indicator.Refresh();
        }
    }
}
