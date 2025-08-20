/*
 * FofVWAP.cs
 * Copyright (c) 2025 Roldão Rego Jr.
 * 
 * Modified by https://github.com/Linus404, 2025
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators.FreeOrderFlow;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FreeOrderFlow
{
    public enum PriceSource
    {
        Close,
        HLC3,
        HL2,
        OHLC4,
        HLCC4
    }
    public class FofVWAP : Indicator
	{
        private Series<double> cumVol;
		private Series<double> cumPV;
		private Series<double> cumPV2;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Free Order Flow VWAP with Standard Deviation Bands";
				Name										= "VWAP";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				IsSuspendedWhileInactive					= true;
				
				// TradingView style colors
				AddPlot(Brushes.DeepSkyBlue, "VWAP");
				AddPlot(Brushes.LimeGreen, "UpperBand1");
				AddPlot(Brushes.LimeGreen, "LowerBand1");
				AddPlot(Brushes.Orange, "UpperBand2");
				AddPlot(Brushes.Orange, "LowerBand2");
				
				// Default parameters
				PriceSourceInput = PriceSource.HLC3;
				ShowFirstBands = true;
				ShowSecondBands = true;
				FirstStdDev = 1.0;
				SecondStdDev = 2.0;
			}
			else if (State == State.DataLoaded)
			{
				cumVol = new Series<double>(this);
				cumPV = new Series<double>(this);
				cumPV2 = new Series<double>(this);
			} else if (State == State.Historical) {
				// Displays a message if the bartype is not intraday
				if (!Bars.BarsType.IsIntraday)
				{
					Draw.TextFixed(this, "NinjaScriptInfo", "VwapAR Indicator only supports Intraday charts", TextPosition.BottomRight);
					Log("VwapAR only supports Intraday charts", LogLevel.Error);
				}
			}
		}

		protected override void OnBarUpdate()
		{
			if(Bars.IsFirstBarOfSession)
			{
				if(CurrentBar > 0) 
				{
					Values[0].Reset(1);
					if(ShowFirstBands)
					{
						Values[1].Reset(1);
						Values[2].Reset(1);
					}
					if(ShowSecondBands)
					{
						Values[3].Reset(1);
						Values[4].Reset(1);
					}
				}
				cumVol[1] = 0;
				cumPV[1] = 0;
				cumPV2[1] = 0;
			}

			double price = GetPrice();
			cumPV[0] = cumPV[1] + (price * Volume[0]);
			cumPV2[0] = cumPV2[1] + (price * price * Volume[0]);
			cumVol[0] = cumVol[1] + Volume[0];

			double vwap = cumPV[0] / (cumVol[0] == 0 ? 1 : cumVol[0]);
			Values[0][0] = vwap;

			if(ShowFirstBands || ShowSecondBands)
			{
				double variance = (cumPV2[0] / (cumVol[0] == 0 ? 1 : cumVol[0])) - (vwap * vwap);
				double stdDev = variance > 0 ? Math.Sqrt(variance) : 0;

				if(ShowFirstBands)
				{
					Values[1][0] = vwap + (stdDev * FirstStdDev);
					Values[2][0] = vwap - (stdDev * FirstStdDev);
				}

				if(ShowSecondBands)
				{
					Values[3][0] = vwap + (stdDev * SecondStdDev);
					Values[4][0] = vwap - (stdDev * SecondStdDev);
				}
			}
		}

		private double GetPrice()
		{
			switch(PriceSourceInput)
			{
				case PriceSource.Close:
					return Close[0];
				case PriceSource.HLC3:
					return (High[0] + Low[0] + Close[0]) / 3.0;
				case PriceSource.HL2:
					return (High[0] + Low[0]) / 2.0;
				case PriceSource.OHLC4:
					return (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
				case PriceSource.HLCC4:
					return (High[0] + Low[0] + Close[0] + Close[0]) / 4.0;
				default:
					return (High[0] + Low[0] + Close[0]) / 3.0;
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "Price Source", Description = "Source for price calculation", Order = 1, GroupName = "Parameters")]
		public PriceSource PriceSourceInput { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show First Std Dev Bands", Description = "Show first standard deviation bands", Order = 2, GroupName = "Parameters")]
		public bool ShowFirstBands { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Second Std Dev Bands", Description = "Show second standard deviation bands", Order = 3, GroupName = "Parameters")]
		public bool ShowSecondBands { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "First Std Dev Multiplier", Description = "Multiplier for first standard deviation", Order = 4, GroupName = "Parameters")]
		public double FirstStdDev { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Second Std Dev Multiplier", Description = "Multiplier for second standard deviation", Order = 5, GroupName = "Parameters")]
		public double SecondStdDev { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> VWAP
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpperBand1
		{
			get { return Values[1]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LowerBand1
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpperBand2
		{
			get { return Values[3]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LowerBand2
		{
			get { return Values[4]; }
		}
		#endregion
	}
}
