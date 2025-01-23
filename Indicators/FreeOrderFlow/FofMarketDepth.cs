#region Using declarations
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
	public class FofMarketDepth : Indicator
	{
		private ConcurrentDictionary<double, long> AskRows;
		private ConcurrentDictionary<double, long> BidRows;
		private SharpDX.Direct2D1.Brush askDxBrush;
		private SharpDX.Direct2D1.Brush bidDxBrush;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Free Order Flow Market Depth";
				Name						= "Market Depth";
				Calculate					= Calculate.OnBarClose;
				IsOverlay					= true;
				DisplayInDataBox			= true;
				DrawOnPricePanel			= true;
				AskColor					= Brushes.Plum;
				BidColor					= Brushes.LightSkyBlue;
				BarOpacity					= 75;
				Width						= 50;
			}
			else if (State == State.Configure)
			{
				AskRows = new ConcurrentDictionary<double, long>();
				BidRows = new ConcurrentDictionary<double, long>();
			}
			else if (State == State.Historical)
			{
				SetZOrder(-1);
			}
		}

		protected override void OnMarketDepth(MarketDepthEventArgs marketDepthUpdate)
		{
			ConcurrentDictionary<double, long> domRow = null;

			if (marketDepthUpdate.MarketDataType == MarketDataType.Ask)
				domRow = AskRows;
			if (marketDepthUpdate.MarketDataType == MarketDataType.Bid)
				domRow = BidRows;

			if (domRow == null) return;

			if (marketDepthUpdate.Operation == Operation.Add || marketDepthUpdate.Operation == Operation.Update)
			{
				domRow.AddOrUpdate(
					marketDepthUpdate.Price,
					marketDepthUpdate.Volume,
					(k, v) => marketDepthUpdate.Volume
				);
			}
			else if (marketDepthUpdate.Operation == Operation.Remove)
			{
				domRow.AddOrUpdate(marketDepthUpdate.Price, 0, (k, v) => 0);
			}
			ForceRefresh();
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;

			// get max volume in visible range
			var visibleAskRows = AskRows.Where(kv => (
				kv.Key <= chartScale.MaxValue && kv.Key >= chartScale.MinValue && kv.Value > 0
			)).Select(kv => kv.Value);
			var visibleBidRows = BidRows.Where(kv => (
				kv.Key <= chartScale.MaxValue && kv.Key >= chartScale.MinValue && kv.Value > 0
			)).Select(kv => kv.Value);
			long maxAskVolume = visibleAskRows.Count() > 0 ? visibleAskRows.Max() : 0;
			long maxBidVolume = visibleBidRows.Count() > 0 ? visibleBidRows.Max() : 0;
			long maxVolume = Math.Max(maxAskVolume, maxBidVolume);
			if(maxVolume == 0) return;

			foreach (KeyValuePair<double, long> row in AskRows)
			{
				if(row.Value == 0) continue;
				SharpDX.RectangleF askBarRect = GetBarRect(chartScale, row.Key, row.Value, maxVolume);
				RenderTarget.FillRectangle(askBarRect, askDxBrush);
			}

			foreach (KeyValuePair<double, long> row in BidRows)
			{
				if(row.Value == 0) continue;
				SharpDX.RectangleF bidBarRect = GetBarRect(chartScale, row.Key, row.Value, maxVolume);
				RenderTarget.FillRectangle(bidBarRect, bidDxBrush);
			}
		}

		public override void OnRenderTargetChanged()
		{
			if(askDxBrush != null) askDxBrush.Dispose();
			if(bidDxBrush != null) bidDxBrush.Dispose();
			if(RenderTarget != null)
			{
				askDxBrush = AskColor.ToDxBrush(RenderTarget);
				bidDxBrush = BidColor.ToDxBrush(RenderTarget);
				askDxBrush.Opacity = BarOpacity / 100f;
				bidDxBrush.Opacity = BarOpacity / 100f;
			}
		}

		private SharpDX.RectangleF GetBarRect(ChartScale chartScale, double price, long volume, long maxVolume) {
			float ypos = (float) chartScale.GetYByValue(price + TickSize);
			float barHeight = (float) chartScale.GetYByValue(price) - ypos;
			float barWidth = Width * volume / (float) maxVolume;
			float xpos = ChartControl.CanvasRight - barWidth;
			return new SharpDX.RectangleF(xpos, ypos, barWidth, barHeight);
		}

		#region Properties
		[XmlIgnore]
		[Display(Name="Ask Color", Order=1, GroupName="Visual")]
		public Brush AskColor { get; set; }

		[Browsable(false)]
		public string AskColorSerializable
		{
			get { return Serialize.BrushToString(AskColor); }
			set { AskColor = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name="Bid Color", Order=2, GroupName="Visual")]
		public Brush BidColor { get; set; }

		[Browsable(false)]
		public string BidColorSerializable
		{
			get { return Serialize.BrushToString(BidColor); }
			set { BidColor = Serialize.StringToBrush(value); }
		}

		[Display(Name="Width", Description="Histogram width.", Order=3, GroupName="Visual")]
		public int Width { get; set; }

		[Range(10, 100)]
		[Display(Name="Bar Opacity %", Description="Histogram bar opacity.", Order=5, GroupName="Visual")]
		public int BarOpacity { get; set; }
		#endregion
	}
}
