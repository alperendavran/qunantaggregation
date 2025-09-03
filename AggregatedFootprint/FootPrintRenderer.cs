// Copyright QUANTOWER LLC. © 2024. All rights reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace AggregatedFootprint
{
    /// <summary>
    /// Advanced FootPrint rendering engine with heatmap coloring and cell management
    /// </summary>
    public class FootPrintRenderer
    {
        private readonly ColorScaleManager colorScaleManager;
        private readonly Font cellFont;
        private readonly StringFormat cellFormat;
        
        // Color schemes
        private readonly Color[] buyColors = new Color[]
        {
            Color.FromArgb(50, 0, 100, 0),    // Very light green
            Color.FromArgb(100, 0, 150, 0),   // Light green
            Color.FromArgb(150, 0, 200, 0),   // Medium green
            Color.FromArgb(200, 0, 255, 0),   // Bright green
            Color.FromArgb(255, 0, 255, 0)    // Full green
        };
        
        private readonly Color[] sellColors = new Color[]
        {
            Color.FromArgb(50, 100, 0, 0),    // Very light red
            Color.FromArgb(100, 150, 0, 0),   // Light red
            Color.FromArgb(150, 200, 0, 0),   // Medium red
            Color.FromArgb(200, 255, 0, 0),   // Bright red
            Color.FromArgb(255, 255, 0, 0)    // Full red
        };

        public FootPrintRenderer()
        {
            colorScaleManager = new ColorScaleManager();
            cellFont = new Font("Arial", 8, FontStyle.Regular);
            cellFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
        }

        public void RenderFootPrint(Graphics graphics, IChartWindow mainWindow, 
            AggregatedBarData barData, int barWidth, int cellHeight, Symbol symbol)
        {
            if (barData?.PriceLevels == null || barData.PriceLevels.Count == 0)
                return;

            var bar = GetBarFromIndex(barData.BarIndex);
            if (bar == null) return;

            var barX = (int)Math.Round(mainWindow.CoordinatesConverter.GetChartX(bar.TimeLeft));
            
            // Sort price levels by price (descending for top-to-bottom rendering)
            var sortedLevels = barData.PriceLevels.OrderByDescending(x => x.Key).ToList();
            
            // Update color scale with current bar data
            colorScaleManager.UpdateScale(barData.PriceLevels.Values);
            
            foreach (var level in sortedLevels)
            {
                var price = level.Key;
                var data = level.Value;
                
                var cellY = (int)mainWindow.CoordinatesConverter.GetChartY(price);
                var cellRect = new Rectangle(barX, cellY - cellHeight / 2, barWidth, cellHeight);
                
                // Skip if cell is outside visible area
                if (cellRect.Bottom < mainWindow.ClientRectangle.Top || 
                    cellRect.Top > mainWindow.ClientRectangle.Bottom)
                    continue;
                
                RenderFootPrintCell(graphics, cellRect, data, symbol);
            }
        }

        private void RenderFootPrintCell(Graphics graphics, Rectangle cellRect, VolumeAnalysisItem data, Symbol symbol)
        {
            var totalVolume = data.BuyVolume + data.SellVolume;
            if (totalVolume <= 0) return;

            var buyRatio = data.BuyVolume / totalVolume;
            var sellRatio = data.SellVolume / totalVolume;
            
            // Determine dominant side and color intensity
            bool isBuyDominant = buyRatio > sellRatio;
            var dominantRatio = isBuyDominant ? buyRatio : sellRatio;
            var dominantVolume = isBuyDominant ? data.BuyVolume : data.SellVolume;
            
            // Get color based on volume intensity
            var color = GetCellColor(isBuyDominant, dominantVolume);
            
            // Fill cell background
            using (var brush = new SolidBrush(color))
            {
                graphics.FillRectangle(brush, cellRect);
            }
            
            // Draw cell border
            using (var pen = new Pen(Color.FromArgb(100, Color.White), 1))
            {
                graphics.DrawRectangle(pen, cellRect);
            }
            
            // Draw text
            RenderCellText(graphics, cellRect, data, symbol);
        }

        private Color GetCellColor(bool isBuyDominant, double volume)
        {
            var intensity = colorScaleManager.GetIntensity(volume);
            var colorArray = isBuyDominant ? buyColors : sellColors;
            
            // Interpolate between color levels
            var scaledIntensity = intensity * (colorArray.Length - 1);
            var lowerIndex = (int)Math.Floor(scaledIntensity);
            var upperIndex = Math.Min(lowerIndex + 1, colorArray.Length - 1);
            var fraction = scaledIntensity - lowerIndex;
            
            if (lowerIndex == upperIndex)
                return colorArray[lowerIndex];
            
            return InterpolateColor(colorArray[lowerIndex], colorArray[upperIndex], fraction);
        }

        private Color InterpolateColor(Color color1, Color color2, float fraction)
        {
            var r = (int)(color1.R + (color2.R - color1.R) * fraction);
            var g = (int)(color1.G + (color2.G - color1.G) * fraction);
            var b = (int)(color1.B + (color2.B - color1.B) * fraction);
            var a = (int)(color1.A + (color2.A - color1.A) * fraction);
            
            return Color.FromArgb(a, r, g, b);
        }

        private void RenderCellText(Graphics graphics, Rectangle cellRect, VolumeAnalysisItem data, Symbol symbol)
        {
            // Format volume values
            var buyText = FormatVolume(data.BuyVolume, symbol);
            var sellText = FormatVolume(data.SellVolume, symbol);
            
            // Split cell into buy (right) and sell (left) sections
            var buyRect = new Rectangle(cellRect.X + cellRect.Width / 2, cellRect.Y, 
                cellRect.Width / 2, cellRect.Height);
            var sellRect = new Rectangle(cellRect.X, cellRect.Y, 
                cellRect.Width / 2, cellRect.Height);
            
            // Choose text color based on background
            var textColor = GetContrastingTextColor(cellRect);
            using (var brush = new SolidBrush(textColor))
            {
                // Draw buy volume (right side)
                if (data.BuyVolume > 0)
                {
                    graphics.DrawString(buyText, cellFont, brush, buyRect, cellFormat);
                }
                
                // Draw sell volume (left side)
                if (data.SellVolume > 0)
                {
                    graphics.DrawString(sellText, cellFont, brush, sellRect, cellFormat);
                }
            }
        }

        private string FormatVolume(double volume, Symbol symbol)
        {
            if (volume >= 1000000)
                return $"{volume / 1000000:F1}M";
            else if (volume >= 1000)
                return $"{volume / 1000:F1}K";
            else if (volume >= 1)
                return $"{volume:F1}";
            else
                return $"{volume:F3}";
        }

        private Color GetContrastingTextColor(Rectangle cellRect)
        {
            // Simple contrast calculation - in a real implementation,
            // you might want to sample the actual background color
            return Color.White;
        }

        private HistoryItemBar GetBarFromIndex(int barIndex)
        {
            // This would need to be passed from the indicator
            // For now, return null - this should be handled by the calling indicator
            return null;
        }

        public void RenderFPBS(Graphics graphics, IChartWindow mainWindow, 
            List<FPBSData> fpbsData, int barWidth)
        {
            if (fpbsData == null || fpbsData.Count == 0) return;

            var fpbsHeight = 80;
            var fpbsY = mainWindow.ClientRectangle.Bottom - fpbsHeight;
            var fpbsRect = new Rectangle(mainWindow.ClientRectangle.Left, fpbsY, 
                mainWindow.ClientRectangle.Width, fpbsHeight);
            
            // Background
            using (var brush = new SolidBrush(Color.FromArgb(80, Color.Black)))
            {
                graphics.FillRectangle(brush, fpbsRect);
            }
            
            // Border
            using (var pen = new Pen(Color.Gray, 1))
            {
                graphics.DrawRectangle(pen, fpbsRect);
            }
            
            // Render FPBS bars
            var font = new Font("Arial", 7, FontStyle.Regular);
            var y = fpbsY + 5;
            
            // Header
            graphics.DrawString("VOLUME | DELTA | CVD | BUY | SELL", font, Brushes.White, 
                fpbsRect.Left + 10, y);
            y += 15;
            
            // Data rows
            foreach (var data in fpbsData.Take(3)) // Show last 3 bars
            {
                var text = $"{data.Volume:F1} | {data.Delta:F1} | {data.CVD:F1} | {data.Buy:F1} | {data.Sell:F1}";
                graphics.DrawString(text, font, Brushes.LightGray, fpbsRect.Left + 10, y);
                y += 12;
            }
        }

        public void RenderPOCLines(Graphics graphics, IChartWindow mainWindow, 
            POCData pocData, int barWidth)
        {
            if (pocData == null) return;

            var leftX = mainWindow.ClientRectangle.Left;
            var rightX = mainWindow.ClientRectangle.Right;
            
            // POC line
            if (pocData.POC > 0)
            {
                var pocY = (int)mainWindow.CoordinatesConverter.GetChartY(pocData.POC);
                using (var pen = new Pen(Color.White, 2))
                {
                    graphics.DrawLine(pen, leftX, pocY, rightX, pocY);
                }
                
                // POC label
                var font = new Font("Arial", 8, FontStyle.Bold);
                graphics.DrawString("POC", font, Brushes.White, rightX - 30, pocY - 10);
            }
            
            // VAL line
            if (pocData.VAL > 0)
            {
                var valY = (int)mainWindow.CoordinatesConverter.GetChartY(pocData.VAL);
                using (var pen = new Pen(Color.Red, 1))
                {
                    graphics.DrawLine(pen, leftX, valY, rightX, valY);
                }
                
                // VAL label
                var font = new Font("Arial", 8, FontStyle.Bold);
                graphics.DrawString("VAL", font, Brushes.Red, rightX - 30, valY - 10);
            }
            
            // VAH line
            if (pocData.VAH > 0)
            {
                var vahY = (int)mainWindow.CoordinatesConverter.GetChartY(pocData.VAH);
                using (var pen = new Pen(Color.Green, 1))
                {
                    graphics.DrawLine(pen, leftX, vahY, rightX, vahY);
                }
                
                // VAH label
                var font = new Font("Arial", 8, FontStyle.Bold);
                graphics.DrawString("VAH", font, Brushes.Green, rightX - 30, vahY - 10);
            }
        }

        public void RenderVWAPBands(Graphics graphics, IChartWindow mainWindow, 
            VWAPData vwapData, int barWidth)
        {
            if (vwapData == null || vwapData.VWAP <= 0) return;

            var leftX = mainWindow.ClientRectangle.Left;
            var rightX = mainWindow.ClientRectangle.Right;
            var vwapY = (int)mainWindow.CoordinatesConverter.GetChartY(vwapData.VWAP);
            
            // VWAP line
            using (var pen = new Pen(Color.Yellow, 2))
            {
                graphics.DrawLine(pen, leftX, vwapY, rightX, vwapY);
            }
            
            // Deviation bands
            if (vwapData.Deviation1 > 0)
            {
                var upperBand1 = vwapData.VWAP + vwapData.Deviation1;
                var lowerBand1 = vwapData.VWAP - vwapData.Deviation1;
                
                var upperY1 = (int)mainWindow.CoordinatesConverter.GetChartY(upperBand1);
                var lowerY1 = (int)mainWindow.CoordinatesConverter.GetChartY(lowerBand1);
                
                using (var pen = new Pen(Color.FromArgb(100, Color.Yellow), 1))
                {
                    graphics.DrawLine(pen, leftX, upperY1, rightX, upperY1);
                    graphics.DrawLine(pen, leftX, lowerY1, rightX, lowerY1);
                }
            }
            
            if (vwapData.Deviation2 > 0)
            {
                var upperBand2 = vwapData.VWAP + vwapData.Deviation2;
                var lowerBand2 = vwapData.VWAP - vwapData.Deviation2;
                
                var upperY2 = (int)mainWindow.CoordinatesConverter.GetChartY(upperBand2);
                var lowerY2 = (int)mainWindow.CoordinatesConverter.GetChartY(lowerBand2);
                
                using (var pen = new Pen(Color.FromArgb(50, Color.Yellow), 1))
                {
                    graphics.DrawLine(pen, leftX, upperY2, rightX, upperY2);
                    graphics.DrawLine(pen, leftX, lowerY2, rightX, lowerY2);
                }
            }
        }

        public void Dispose()
        {
            cellFont?.Dispose();
            cellFormat?.Dispose();
        }
    }

    public class ColorScaleManager
    {
        private double minVolume = double.MaxValue;
        private double maxVolume = double.MinValue;
        private readonly List<double> recentVolumes = new List<double>();
        private const int MAX_RECENT_VOLUMES = 1000;
        private const double LOWER_QUANTILE = 0.05; // 5th percentile
        private const double UPPER_QUANTILE = 0.95; // 95th percentile

        public void UpdateScale(IEnumerable<VolumeAnalysisItem> volumeData)
        {
            foreach (var item in volumeData)
            {
                var volume = item.BuyVolume + item.SellVolume;
                if (volume > 0) // Only add positive volumes
                {
                    recentVolumes.Add(volume);
                    
                    if (recentVolumes.Count > MAX_RECENT_VOLUMES)
                    {
                        recentVolumes.RemoveAt(0);
                    }
                }
            }
            
            if (recentVolumes.Count > 10) // Need enough data for quantiles
            {
                var sortedVolumes = recentVolumes.OrderBy(v => v).ToList();
                var lowerIndex = (int)(sortedVolumes.Count * LOWER_QUANTILE);
                var upperIndex = (int)(sortedVolumes.Count * UPPER_QUANTILE);
                
                minVolume = sortedVolumes[lowerIndex];
                maxVolume = sortedVolumes[upperIndex];
            }
        }

        public float GetIntensity(double volume)
        {
            if (maxVolume <= minVolume || volume <= 0) return 0.1f; // Minimum intensity for visibility
            
            var intensity = (volume - minVolume) / (maxVolume - minVolume);
            return (float)Math.Max(0.1, Math.Min(1.0, intensity)); // Clamp between 0.1 and 1.0
        }
    }

    public class FPBSData
    {
        public double Volume { get; set; }
        public double Delta { get; set; }
        public double CVD { get; set; }
        public double Buy { get; set; }
        public double Sell { get; set; }
        public DateTime Time { get; set; }
    }

    public class POCData
    {
        public double POC { get; set; }
        public double VAL { get; set; }
        public double VAH { get; set; }
        public DateTime Time { get; set; }
    }

    public class VWAPData
    {
        public double VWAP { get; set; }
        public double Deviation1 { get; set; }
        public double Deviation2 { get; set; }
        public DateTime Time { get; set; }
    }
}
