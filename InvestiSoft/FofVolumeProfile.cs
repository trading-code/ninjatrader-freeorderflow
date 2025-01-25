#region Using declarations
using NinjaTrader.Gui.Chart;
using SharpDX.Direct2D1;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace InvestiSoft.NinjaScript.VolumeProfile
{
    #region Data
    internal class FofVolumeProfileRow
    {
        public long buy = 0;
        public long sell = 0;
        public long total { get { return buy + sell; } }
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
            int chartBarWidth = (int)(chartControl.BarWidth + chartControl.BarMarginLeft);
            int startX = (inWindow) ? (
                Math.Max(chartControl.GetXByBarIndex(chartBars, profile.StartBar), chartControl.CanvasLeft)
            ) : chartControl.GetXByBarIndex(chartBars, profile.StartBar);
            int endX = (inWindow) ? (
                Math.Min(chartControl.GetXByBarIndex(chartBars, profile.EndBar), chartControl.CanvasRight)
            ) : chartControl.GetXByBarIndex(chartBars, profile.EndBar);
            float xpos = (float)startX - chartBarWidth;
            int maxWidth = Math.Max(endX - startX, chartBarWidth);
            float barWidth = (fullwidth) ? maxWidth : (
                maxWidth * (volume / (float)profile.MaxVolume) * WidthPercent
            );
            return new SharpDX.RectangleF(startX, ypos, barWidth, barHeight);
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

        internal void RenderPoc(FofVolumeProfileData profile, Brush brush, float width, StrokeStyle strokeStyle)
        {
            var pocRect = GetBarRect(profile, profile.POC, profile.MaxVolume);
            renderTarget.FillRectangle(pocRect, brush);

            pocRect = GetBarRect(profile, profile.POC, profile.MaxVolume, true);
            pocRect.Y += pocRect.Height / 2;
            renderTarget.DrawLine(
                pocRect.TopLeft, pocRect.TopRight,
                brush, width, strokeStyle
            );
        }

        internal void RenderValueArea(FofVolumeProfileData profile, Brush brush, float width, StrokeStyle strokeStyle)
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

        internal void RenderTotalVolume(FofVolumeProfileData profile, Brush textBrush)
        {
            var textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();
            var textLayout = new SharpDX.DirectWrite.TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                string.Format("∑ {0}", profile.TotalVolume),
                textFormat,
                300, 30
            );
            var minPrice = profile.Keys.Min();
            var barRect = GetBarRect(profile, minPrice, 0, false);
            var textPos = new SharpDX.Vector2(barRect.Left, barRect.Top);
            renderTarget.DrawTextLayout(textPos, textLayout, textBrush);
        }
    }
    #endregion

    public enum FofVolumeProfileMode { Standard, BuySell, Delta };
    public enum FofVolumeProfilePeriod { Sessions, Bars };
    public enum FofVolumeProfileResolution { Tick, Minute };
}
