#region Using declarations
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.MarketAnalyzerColumns;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
#endregion

namespace InvestiSoft.NinjaScript.VolumeProfile
{
    #region Data
    internal class FofVolumeProfileRow
    {
        public long buy = 0;
        public long sell = 0;
        public long other = 0;
        public long total { get { return buy + sell + other; } }

        public string toString()
        {
            return string.Format("<VolumeProfileRow buy={0} sell={1}>", buy, sell);
        }
    }

    internal class FofVolumeProfileData : ConcurrentDictionary<double, FofVolumeProfileRow>
    {
        public int StartBar { get; set; }
        public int EndBar { get; set; }
        public long MaxVolume { get; set; }
        public long TotalVolume { get; set; }
        public double VAH { get; set; }
        public double VAL { get; set; }
        public double POC { get; set; }

        public FofVolumeProfileRow UpdateRow(double price, long buyVolume, long sellVolume, long otherVolume)
        {
            var row = AddOrUpdate(
                price,
                (double key) => new FofVolumeProfileRow()
                {
                    buy = buyVolume,
                    sell = sellVolume,
                    other = otherVolume
                },
                (double key, FofVolumeProfileRow oldValue) => new FofVolumeProfileRow()
                {
                    buy = buyVolume + oldValue.buy,
                    sell = sellVolume + oldValue.sell,
                    other = otherVolume + oldValue.other
                }
            );
            // caculate POC
            if (row.total > MaxVolume)
            {
                MaxVolume = row.total;
                POC = price;
            }
            // calculate total volume for use in VAL and VAH
            TotalVolume += (buyVolume + sellVolume + otherVolume);
            return row;
        }

        public void CalculateValueArea(float valueAreaPerc)
        {
            if (Count == 0 || POC == 0) return;

            // Calculate the total trading volume
            List<double> priceList = Keys.OrderBy(p => p).ToList();
            int SmoothVA = 2;
            long upVol = 0;
            long downVol = 0;
            long valueVol = (long)(TotalVolume * valueAreaPerc);
            long areaVol = this[POC].total;
            int highIdx = priceList.IndexOf(POC);
            int lowIdx = highIdx;

            while (areaVol < valueVol)
            {
                if (upVol == 0)
                {
                    for (int n = 0; (n < SmoothVA && highIdx < priceList.Count - 1); n++)
                    {
                        highIdx++;
                        upVol += this[priceList[highIdx]].total;
                    }
                }

                if (downVol == 0)
                {
                    for (int n = 0; (n < SmoothVA && lowIdx > 0); n++)
                    {
                        lowIdx--;
                        downVol += this[priceList[lowIdx]].total;
                    }
                }

                if (upVol > downVol)
                {
                    areaVol += upVol;
                    upVol = 0;
                }
                else
                {
                    areaVol += downVol;
                    downVol = 0;
                }
            }
            VAH = priceList[highIdx];
            VAL = priceList[lowIdx];
        }

        public FofVolumeProfileRow GetValueOrDefault(double price)
        {
            FofVolumeProfileRow volume;
            if (!TryGetValue(price, out volume))
            {
                volume = new FofVolumeProfileRow();
            }
            return volume;
        }
    }
    #endregion

    #region ChartRenderer
    internal class FofVolumeProfileChartRenderer
    {
        private readonly ChartControl chartControl;
        private readonly ChartScale chartScale;
        private readonly ChartBars chartBars;
        private readonly RenderTarget renderTarget;

        public float Opacity { get; set; }
        public float ValueAreaOpacity { get; set; }
        public float WidthPercent;

        public FofVolumeProfileChartRenderer(
            ChartControl chartControl, ChartScale chartScale, ChartBars chartBars,
            RenderTarget renderTarget
        )
        {
            this.chartControl = chartControl;
            this.chartScale = chartScale;
            this.chartBars = chartBars;
            this.renderTarget = renderTarget;
            WidthPercent = 1;
        }

        internal SharpDX.RectangleF GetBarRect(
            FofVolumeProfileData profile, double price, long volume,
            bool fullwidth = false, bool inWindow = true
        )
        {
            // bar height and Y
            var tickSize = chartControl.Instrument.MasterInstrument.TickSize;
            float ypos = chartScale.GetYByValue(price + tickSize);
            float barHeight = chartScale.GetYByValue(price) - ypos;
            // center bar on price tick
            int halfBarDistance = (int)Math.Max(1, chartScale.GetPixelsForDistance(tickSize)) / 2; //pixels
            ypos += halfBarDistance;
            // bar width and X
            int chartBarWidth;
            int startX = (inWindow) ? (
                Math.Max(chartControl.GetXByBarIndex(chartBars, profile.StartBar), chartControl.CanvasLeft)
            ) : chartControl.GetXByBarIndex(chartBars, profile.StartBar);
            int endX = (inWindow) ? (
                Math.Min(chartControl.GetXByBarIndex(chartBars, profile.EndBar), chartControl.CanvasRight)
            ) : chartControl.GetXByBarIndex(chartBars, profile.EndBar);
            if (profile.StartBar > 0)
            {
                chartBarWidth = (
                    chartControl.GetXByBarIndex(chartBars, profile.StartBar) -
                    chartControl.GetXByBarIndex(chartBars, profile.StartBar - 1)
                ) / 2;
            }
            else
            {
                chartBarWidth = chartControl.GetBarPaintWidth(chartBars);
            }
            float xpos = startX;
            int maxWidth = Math.Max(endX - startX, chartBarWidth);
            float barWidth = (fullwidth) ? maxWidth : (
                maxWidth * (volume / (float)profile.MaxVolume) * WidthPercent
            );
            return new SharpDX.RectangleF(xpos, ypos, barWidth, barHeight);
        }

        internal void RenderProfile(FofVolumeProfileData profile, Brush volumeBrush)
        {
            foreach (KeyValuePair<double, FofVolumeProfileRow> row in profile)
            {
                var rect = GetBarRect(profile, row.Key, row.Value.total);
                if (row.Key >= profile.VAL && row.Key <= profile.VAH)
                {
                    volumeBrush.Opacity = ValueAreaOpacity;
                    renderTarget.FillRectangle(rect, volumeBrush);
                }
                else
                {
                    volumeBrush.Opacity = Opacity;
                    renderTarget.FillRectangle(rect, volumeBrush);
                }
            }
        }

        internal void RenderPoc(FofVolumeProfileData profile, Brush brush, float width, StrokeStyle strokeStyle, bool drawText = false)
        {
            var pocRect = GetBarRect(profile, profile.POC, profile.MaxVolume);
            renderTarget.FillRectangle(pocRect, brush);

            pocRect = GetBarRect(profile, profile.POC, profile.MaxVolume, true);
            pocRect.Y += pocRect.Height / 2;
            renderTarget.DrawLine(
                pocRect.TopLeft, pocRect.TopRight,
                brush, width, strokeStyle
            );
            if (drawText)
            {
                RnederText(
                    string.Format("{0}", profile.POC),
                    new SharpDX.Vector2(pocRect.Left, pocRect.Top),
                    brush,
                    pocRect.Width,
                    TextAlignment.Trailing
                );
            }
        }

        internal void RenderValueArea(FofVolumeProfileData profile, Brush brush, float width, StrokeStyle strokeStyle, bool drawText = false)
        {
            // draw VAH
            if (profile.ContainsKey(profile.VAH))
            {
                var vahRect = GetBarRect(profile, profile.VAH, profile[profile.VAH].total, true);
                vahRect.Y += vahRect.Height / 2;
                renderTarget.DrawLine(
                    vahRect.TopLeft, vahRect.TopRight,
                    brush, width, strokeStyle
                );
                if (drawText)
                {
                    RnederText(
                        string.Format("{0}", profile.VAH),
                        new SharpDX.Vector2(vahRect.Left, vahRect.Top),
                        brush,
                        vahRect.Width,
                        TextAlignment.Trailing
                    );
                }
            }
            // draw VAL
            if (profile.ContainsKey(profile.VAL))
            {
                var valRect = GetBarRect(profile, profile.VAL, profile[profile.VAL].total, true);
                valRect.Y += valRect.Height / 2;
                renderTarget.DrawLine(
                    valRect.TopLeft, valRect.TopRight,
                    brush, width, strokeStyle
                );
                if (drawText)
                {
                    RnederText(
                        string.Format("{0}", profile.VAL),
                        new SharpDX.Vector2(valRect.Left, valRect.Top),
                        brush,
                        valRect.Width,
                        TextAlignment.Trailing
                    );
                }
            }
        }

        internal void RenderBuySellProfile(FofVolumeProfileData profile, Brush buyBrush, Brush sellBrush)
        {
            foreach (KeyValuePair<double, FofVolumeProfileRow> row in profile)
            {
                var buyRect = GetBarRect(profile, row.Key, row.Value.buy);
                var sellRect = GetBarRect(profile, row.Key, row.Value.sell);
                buyRect.X = sellRect.Right;
                if (row.Key >= profile.VAL && row.Key <= profile.VAH)
                {
                    buyBrush.Opacity = ValueAreaOpacity;
                    sellBrush.Opacity = ValueAreaOpacity;
                }
                else
                {
                    buyBrush.Opacity = Opacity;
                    sellBrush.Opacity = Opacity;
                }
                renderTarget.FillRectangle(buyRect, buyBrush);
                renderTarget.FillRectangle(sellRect, sellBrush);
            }
        }

        internal void RenderDeltaProfile(FofVolumeProfileData profile, Brush buyBrush, Brush sellBrush)
        {
            foreach (KeyValuePair<double, FofVolumeProfileRow> row in profile)
            {
                var volumeDelta = Math.Abs(row.Value.buy - row.Value.sell);
                var rect = GetBarRect(profile, row.Key, volumeDelta);
                if (row.Key >= profile.VAL && row.Key <= profile.VAH)
                {
                    buyBrush.Opacity = ValueAreaOpacity;
                    sellBrush.Opacity = ValueAreaOpacity;
                }
                else
                {
                    buyBrush.Opacity = Opacity;
                    sellBrush.Opacity = Opacity;
                }
                renderTarget.FillRectangle(
                    rect, (row.Value.buy > row.Value.sell) ? buyBrush : sellBrush
                );
            }
        }

        internal void RnederText(string text, SharpDX.Vector2 position, Brush brush, float maxWidth, TextAlignment align = TextAlignment.Leading)
        {
            var textLayout = new TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                text,
                chartControl.Properties.LabelFont.ToDirectWriteTextFormat(),
                maxWidth,
                30
            );
            textLayout.TextAlignment = align;
            textLayout.WordWrapping = WordWrapping.NoWrap;
            var textWidth = textLayout.Metrics.Width;
            if (textWidth > maxWidth) return;
            renderTarget.DrawTextLayout(position, textLayout, brush);
        }

        internal void RenderTotalVolume(FofVolumeProfileData profile, Brush textBrush)
        {
            var maxPrice = profile.Keys.Max();
            var minPrice = profile.Keys.Min();
            var textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();
            textFormat.WordWrapping = WordWrapping.NoWrap;
            var textLayout = new TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                string.Format("∑ {0} / {1}", profile.TotalVolume, maxPrice - minPrice),
                textFormat,
                300,
                textFormat.FontSize + 4
            );
            var barRect = GetBarRect(profile, minPrice, 0, false);
            RnederText(
                string.Format("∑ {0} / {1}", profile.TotalVolume, maxPrice - minPrice),
                new SharpDX.Vector2(barRect.Left, barRect.Top),
                textBrush,
                barRect.Width,
                TextAlignment.Leading
            );
        }
    }
    #endregion

    public enum FofVolumeProfileMode { Standard, BuySell, Delta };
    public enum FofVolumeProfilePeriod { Sessions, Bars };
    public enum FofVolumeProfileResolution { Tick, Minute };
}
