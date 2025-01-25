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
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FreeOrderFlow
{
	public class FofCumulativeDelta : Indicator
	{
		private double buys;
		private double sells;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Free Order Flow Aggression Delta";
				Name						= "Aggression Delta";
				Calculate					= Calculate.OnEachTick;
				DrawOnPricePanel			= false;
				IsOverlay					= false;
				DisplayInDataBox			= false;
				DrawOnPricePanel			= false;
				PaintPriceMarkers			= true;
				ScaleJustification			= ScaleJustification.Right;
				PositiveBrush				= Brushes.Green;
				NegativeBrush				= Brushes.Red;
			}
			else if (State == State.Configure)
			{
				AddLine(Brushes.Gray, 0, "Zero Line");
				AddPlot(Brushes.Gray, "Delta");
				Plots[0].PlotStyle = PlotStyle.Bar;
				Plots[0].AutoWidth = true;
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{

		}

		protected override void OnBarUpdate()
		{
			if(IsTickReplays[0] == true) return; // not work with tick replay
			if(BarsInProgress == 0) {
				// reset volume on real time bar
				if(IsFirstTickOfBar && State != State.Historical) {
					buys = 0;
					sells = 0;
				}

				Values[0][0] = buys - sells;
				PlotBrushes[0][0] = (Values[0][0] > 0) ? PositiveBrush : NegativeBrush;

				// reset volume after update historical bars
				if(State == State.Historical) {
					buys = 0;
					sells = 0;
				}
			}

			// Load buys and sells on historical bars
			if(BarsInProgress == 1) {
				double price = BarsArray[1].GetClose(CurrentBar);
				double ask = BarsArray[1].GetAsk(CurrentBar);
				double bid = BarsArray[1].GetBid(CurrentBar);
				double volume = BarsArray[1].GetVolume(CurrentBar);
				if(price >= ask) {
					buys += volume;
				}
				if(price <= bid) {
					sells += volume;
				}
			}
		}

		#region Properties
		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Positive Color", GroupName = "Visual")]
		public Brush PositiveBrush { get; set; }

		[Browsable(false)]
		public string PositiveBrushSerializable
		{
			get { return Serialize.BrushToString(PositiveBrush); }
			set { PositiveBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Negative Color", GroupName = "Visual")]
		public Brush NegativeBrush { get; set; }

		[Browsable(false)]
		public string NegativeBrushSerializable
		{
			get { return Serialize.BrushToString(NegativeBrush); }
			set { NegativeBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}
