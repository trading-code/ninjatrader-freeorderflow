#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;
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
	public class FofVolumeProfile : Indicator
	{
		#region Internal types
		internal class VolumeProfileRow
		{
			public long buy = 0;
			public long sell = 0;
			public long total { get { return buy + sell; } }
		}

		internal class VolumeProfileData : ConcurrentDictionary<double, VolumeProfileRow>
		{
			public int StartBar { get; set; }
			public int EndBar { get; set; }
			public long MaxVolume { get; set; }
			public long TotalVolume { get; set; }
			public double VAH { get; set; }
			public double VAL { get; set; }
			public double POC { get; set; }
		}
		#endregion

		private List<VolumeProfileData> Profiles;
		private int LastBar;
		SharpDX.Direct2D1.Brush volumeBrushDX;
		SharpDX.Direct2D1.Brush buyBrushDX;
		SharpDX.Direct2D1.Brush sellBrushDX;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= @"Free Order Flow Volume Profile";
				Name						= "Volume Profile";
				IsChartOnly					= true;
				IsOverlay					= true;
				DisplayInDataBox			= false;
				DrawOnPricePanel			= true;

				// Setup
				ResolutionMode				= FofVolumeProfileResolution.Tick;
				Resolution					= 1;
				ValueArea					= 70;

				// Visual
				Width						= 60;
				Opacity						= 40;
				ValueAreaOpacity			= 80;
				ShowPoc						= true;
				ShowValueArea				= true;
				VolumeBrush					= Brushes.CornflowerBlue;
				BuyBrush					= Brushes.DarkCyan;
				SellBrush					= Brushes.MediumVioletRed;
				PocStroke					= new Stroke(Brushes.Goldenrod, 1);
				ValueAreaStroke				= new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Dash, 1);
			}
			else if (State == State.Configure)
			{
				Calculate = Calculate.OnEachTick;
				// Add lower timeframe data series
				AddDataSeries((ResolutionMode == FofVolumeProfileResolution.Tick) ? BarsPeriodType.Tick : BarsPeriodType.Minute, Resolution);

				// Init volume profiles list
				Profiles = new List<VolumeProfileData>();
				Profiles.Add(new VolumeProfileData() { StartBar = 0 });
			}
			else if (State == State.Historical)
			{
				SetZOrder(1);
			}
		}

		#region Calculations
		protected override void OnBarUpdate()
		{
			if(BarsInProgress == 1)
			{
				long buyVolume, sellVolume;

				if(ResolutionMode == FofVolumeProfileResolution.Tick && Resolution == 1)
				{
					// 1 tick uses bid and ask price
					var ask = BarsArray[1].GetAsk(CurrentBar);
					var bid = BarsArray[1].GetBid(CurrentBar);

					buyVolume = (Closes[1][0] >= ask) ? (long) Volumes[1][0] : 0;
					sellVolume = (Closes[1][0] <= bid) ? (long) Volumes[1][0] : 0;
				}
				else
				{
					buyVolume = Closes[1][0] > Opens[1][0] ? (long) Volumes[1][0] : 0;
					sellVolume = Closes[1][0] < Opens[1][0] ? (long) Volumes[1][0] : 0;
				}

				if(Profiles.Count > 0)
				{
					var profile = Profiles.Last();
					var row = profile.AddOrUpdate(
						Close[0],
						(double key) => new VolumeProfileRow() {
							buy = buyVolume,
							sell = sellVolume
						},
						(double key, VolumeProfileRow oldValue) => new VolumeProfileRow()
						{
							buy = buyVolume + oldValue.buy,
							sell = sellVolume + oldValue.sell
						}
					);
					// caculate POC
					if(row.total > profile.MaxVolume)
					{
						profile.MaxVolume = row.total;
						profile.POC = Close[0];
					}
					// calculate total volume for use in VAL and VAH
					profile.TotalVolume += (buyVolume + sellVolume);
				}
			}
			else // BarsInProgress == 0
			{
				if(State == State.Realtime || IsFirstTickOfBar) CalculateValueArea(Profiles.Last());

				if(CurrentBar == LastBar) return;
				LastBar = CurrentBar;

				Profiles.Last().EndBar = CurrentBar;

				if(
					Period == FofVolumeProfilePeriod.Bars ||
					(Period == FofVolumeProfilePeriod.Sessions && Bars.IsFirstBarOfSession)
				)
				{
					if(State != State.Realtime) CalculateValueArea(Profiles.Last());
					Profiles.Add(new VolumeProfileData() { StartBar = CurrentBar });
				}
			}
		}

		private void CalculateValueArea(VolumeProfileData profile)
		{
			if(profile.Count == 0 || profile.POC == 0) return;

			// Calculate the total trading volume
			List<double> priceList = profile.Keys.OrderBy(p => p).ToList();
			int SmoothVA = 2;
			long upVol = 0;
			long downVol = 0;
			long valueVol = (long) (profile.TotalVolume * ValueArea / 100f);
			long areaVol = profile[profile.POC].total;
			int highIdx = priceList.IndexOf(profile.POC);
			int lowIdx = highIdx;

			while(areaVol < valueVol)
			{
				if(upVol == 0)
				{
					for (int n = 0; (n < SmoothVA && highIdx < priceList.Count - 1); n++)
					{
						highIdx++;
						upVol += profile[priceList[highIdx]].total;
					}
				}

				if(downVol == 0)
				{
					for (int n = 0; (n < SmoothVA && lowIdx > 0); n++)
					{
						lowIdx--;
						downVol += profile[priceList[lowIdx]].total;
					}
				}

				if(upVol > downVol)
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
			profile.VAH = priceList[highIdx];
			profile.VAL = priceList[lowIdx];
		}
		#endregion

		#region Rendering
		private SharpDX.RectangleF GetBarRect(ChartScale chartScale, VolumeProfileData profile, double price, long volume, bool fullwidth = false) {
			// bar height and Y
			float y = (float) chartScale.GetYByValueWpf(price + TickSize);
			float tickHeight = (float) chartScale.GetYByValueWpf(price) - y;
			float barHeight = (float) tickHeight;
			float ypos = y;

			// bar width and X
			int lastBar = Math.Min(profile.EndBar, ChartBars.ToIndex);
			int firstBar = Math.Max(profile.StartBar, ChartBars.FromIndex);
			int chartBarWidth = (int) (ChartControl.BarWidth + ChartControl.BarMarginLeft);
			int startX = (
				firstBar != 0 && firstBar == ChartBars.FromIndex &&
				Period == FofVolumeProfilePeriod.Sessions
			) ? chartBarWidth : ChartControl.GetXByBarIndex(ChartBars, firstBar) + (ChartControl.BarMarginLeft / 2);
			int endX = ChartControl.GetXByBarIndex(ChartBars, lastBar);
			float xpos = (float) startX - chartBarWidth;
			int maxWidth = Math.Max(endX - startX, chartBarWidth);
			float barPercent = volume / (float) profile.MaxVolume;
			float barWidth = (fullwidth) ? maxWidth : (
				(float) maxWidth * barPercent * (
					(Period == FofVolumeProfilePeriod.Bars) ? 1 : (Width / 100f)
				)
			);
			return new SharpDX.RectangleF(xpos, ypos, barWidth, barHeight);
		}

		private void RenderVolumeProfile(ChartScale chartScale, VolumeProfileData profile)
		{
			foreach (KeyValuePair<double, VolumeProfileRow> row in profile)
			{
				var rect = GetBarRect(chartScale, profile, row.Key, row.Value.total);
				if(ShowValueArea && row.Key >= profile.VAL && row.Key <= profile.VAH)
				{
					volumeBrushDX.Opacity = ValueAreaOpacity / 100f;
				}
				else
				{
					volumeBrushDX.Opacity = Opacity / 100f;
				}
				RenderTarget.FillRectangle(rect, volumeBrushDX);
			}
		}

		private void RenderPoc(ChartScale chartScale, VolumeProfileData profile)
		{
			var pocRect = GetBarRect(chartScale, profile, profile.POC, profile.MaxVolume);
			RenderTarget.FillRectangle(pocRect, PocStroke.BrushDX);

			pocRect = GetBarRect(chartScale, profile, profile.POC, profile.MaxVolume, true);
			pocRect.Y += pocRect.Height / 2;
			RenderTarget.DrawLine(
				pocRect.TopLeft,
				pocRect.TopRight,
				PocStroke.BrushDX,
				PocStroke.Width,
				PocStroke.StrokeStyle
			);
		}

		private void RenderValueArea(ChartScale chartScale, VolumeProfileData profile)
		{
			// draw VAH
			if(profile.ContainsKey(profile.VAH)) {
				var vahRect = GetBarRect(chartScale, profile, profile.VAH, profile[profile.VAH].total, true);
				vahRect.Y += vahRect.Height / 2;
				RenderTarget.DrawLine(
					vahRect.TopLeft,
					vahRect.TopRight,
					ValueAreaStroke.BrushDX,
					ValueAreaStroke.Width,
					ValueAreaStroke.StrokeStyle
				);
			}
			// draw VAL
			if(profile.ContainsKey(profile.VAL)) {
				var valRect = GetBarRect(chartScale, profile, profile.VAL, profile[profile.VAL].total, true);
				valRect.Y += valRect.Height / 2;
				RenderTarget.DrawLine(
					valRect.TopLeft,
					valRect.TopRight,
					ValueAreaStroke.BrushDX,
					ValueAreaStroke.Width,
					ValueAreaStroke.StrokeStyle
				);
			}
		}

		private void RenderBuySellProfile(ChartScale chartScale, VolumeProfileData profile)
		{
			foreach (KeyValuePair<double, VolumeProfileRow> row in profile)
			{
				var buyRect = GetBarRect(chartScale, profile, row.Key, row.Value.buy);
				var sellRect = GetBarRect(chartScale, profile, row.Key, row.Value.sell);
				buyRect.X = sellRect.Right;
				if(ShowValueArea && row.Key >= profile.VAL && row.Key <= profile.VAH)
				{
					buyBrushDX.Opacity = ValueAreaOpacity / 100f;
					sellBrushDX.Opacity = ValueAreaOpacity / 100f;
				}
				else
				{
					buyBrushDX.Opacity = Opacity / 100f;
					sellBrushDX.Opacity = Opacity / 100f;
				}
				RenderTarget.FillRectangle(buyRect, buyBrushDX);
				RenderTarget.FillRectangle(sellRect, sellBrushDX);
			}
		}

		private void RenderDeltaProfile(ChartScale chartScale, VolumeProfileData profile)
		{
			foreach (KeyValuePair<double, VolumeProfileRow> row in profile)
			{
				var volumeDelta = Math.Abs(row.Value.buy - row.Value.sell);
				var rect = GetBarRect(chartScale, profile, row.Key, volumeDelta);
				if(ShowValueArea && row.Key >= profile.VAL && row.Key <= profile.VAH)
				{
					buyBrushDX.Opacity = ValueAreaOpacity / 100f;
					sellBrushDX.Opacity = ValueAreaOpacity / 100f;
				}
				else
				{
					buyBrushDX.Opacity = Opacity / 100f;
					sellBrushDX.Opacity = Opacity/ 100f;
				}
				RenderTarget.FillRectangle(rect, (row.Value.buy > row.Value.sell) ? buyBrushDX : sellBrushDX);
			}
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;

			foreach(var profile in Profiles)
			{
				if(
					profile.MaxVolume == 0 ||
					(profile.StartBar < ChartBars.FromIndex && profile.EndBar < ChartBars.FromIndex) ||
					(profile.StartBar > ChartBars.ToIndex && profile.EndBar > ChartBars.ToIndex)
				) continue;
				if(DisplayMode == FofVolumeProfileMode.BuySell)
				{
					RenderBuySellProfile(chartScale, profile);
				}
				else
				{
					RenderVolumeProfile(chartScale, profile);
				}
				if(ShowPoc) RenderPoc(chartScale, profile);
				if(ShowValueArea) RenderValueArea(chartScale, profile);
				if(DisplayMode == FofVolumeProfileMode.Delta)
				{
					RenderDeltaProfile(chartScale, profile);
				}
			}
		}

		public override void OnRenderTargetChanged()
		{
			if(volumeBrushDX != null) volumeBrushDX.Dispose();
			if(buyBrushDX != null) buyBrushDX.Dispose();
			if(sellBrushDX != null) sellBrushDX.Dispose();
			if (RenderTarget != null) {
				volumeBrushDX = VolumeBrush.ToDxBrush(RenderTarget);
				buyBrushDX = BuyBrush.ToDxBrush(RenderTarget);
				sellBrushDX = SellBrush.ToDxBrush(RenderTarget);
				PocStroke.RenderTarget = RenderTarget;
				ValueAreaStroke.RenderTarget = RenderTarget;
			}
		}
		#endregion

		#region Properties
		// Setup
		[Display(Name = "Display mode", Description="Profile mode to render", Order = 1, GroupName = "Setup")]
		public FofVolumeProfileMode DisplayMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Profile Period", Description="Calculate profile from region", Order = 1, GroupName = "Setup")]
		public FofVolumeProfilePeriod Period { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Resolution Mode", Description="Calculate profile from region",  Order = 2, GroupName = "Setup")]
		public FofVolumeProfileResolution ResolutionMode { get; set; }

		[Display(Name = "Resolution", Description="Calculate profile from region",  Order = 3, GroupName = "Setup")]
		public int Resolution { get; set; }

		[Range(10, 90)]
		[Display(Name = "Value Area (%)", Description="Value area percentage",  Order = 7, GroupName = "Setup")]
		public int ValueArea { get; set; }

		// Visual
		[Display(Name = "Profile width (%)", Description="Width of bars relative to range",  Order = 1, GroupName = "Visual")]
		public int Width { get; set; }

		[Range(1, 100)]
		[Display(Name = "Profile opacity (%)", Description="Opacity of bars out value area",  Order = 2, GroupName = "Visual")]
		public int Opacity { get; set; }

		[Range(1, 100)]
		[Display(Name = "Value area opacity (%)", Description="Opacity of bars in value area",  Order = 2, GroupName = "Visual")]
		public int ValueAreaOpacity { get; set; }

		[Display(Name = "Show POC", Description="Show PoC line",  Order = 5, GroupName = "Setup")]
		public bool ShowPoc { get; set; }

		[Display(Name = "Show Value Area", Description="Show value area high and low lines",  Order = 6, GroupName = "Setup")]
		public bool ShowValueArea { get; set; }

		[XmlIgnore]
		[Display(Name = "Color for profile", Order = 10, GroupName = "Visual")]
		public Brush VolumeBrush { get; set; }

		[Browsable(false)]
		public string VolumeBrushSerialize
		{
			get { return Serialize.BrushToString(VolumeBrush); }
			set { VolumeBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Color for buy", Order = 11, GroupName = "Visual")]
		public Brush BuyBrush { get; set; }

		[Browsable(false)]
		public string BuyBrushSerialize
		{
			get { return Serialize.BrushToString(BuyBrush); }
			set { BuyBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Color for sell", Order = 12, GroupName = "Visual")]
		public Brush SellBrush { get; set; }

		[Browsable(false)]
		public string SellBrushSerialize
		{
			get { return Serialize.BrushToString(SellBrush); }
			set { SellBrush = Serialize.StringToBrush(value); }
		}

		// Lines
		[Display(Name = "POC", Order = 8, GroupName = "Lines")]
		public Stroke PocStroke { get; set; }

		[Display(Name = "Value Area", Order = 9, GroupName = "Lines")]
		public Stroke ValueAreaStroke { get; set; }
		#endregion
	}
}

public enum FofVolumeProfileMode { Standard, BuySell, Delta };
public enum FofVolumeProfilePeriod { Sessions, Bars };
public enum FofVolumeProfileResolution { Tick, Minute };

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private FreeOrderFlow.FofVolumeProfile[] cacheFofVolumeProfile;
		public FreeOrderFlow.FofVolumeProfile FofVolumeProfile(FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode)
		{
			return FofVolumeProfile(Input, period, resolutionMode);
		}

		public FreeOrderFlow.FofVolumeProfile FofVolumeProfile(ISeries<double> input, FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode)
		{
			if (cacheFofVolumeProfile != null)
				for (int idx = 0; idx < cacheFofVolumeProfile.Length; idx++)
					if (cacheFofVolumeProfile[idx] != null && cacheFofVolumeProfile[idx].Period == period && cacheFofVolumeProfile[idx].ResolutionMode == resolutionMode && cacheFofVolumeProfile[idx].EqualsInput(input))
						return cacheFofVolumeProfile[idx];
			return CacheIndicator<FreeOrderFlow.FofVolumeProfile>(new FreeOrderFlow.FofVolumeProfile(){ Period = period, ResolutionMode = resolutionMode }, input, ref cacheFofVolumeProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.FreeOrderFlow.FofVolumeProfile FofVolumeProfile(FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode)
		{
			return indicator.FofVolumeProfile(Input, period, resolutionMode);
		}

		public Indicators.FreeOrderFlow.FofVolumeProfile FofVolumeProfile(ISeries<double> input , FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode)
		{
			return indicator.FofVolumeProfile(input, period, resolutionMode);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.FreeOrderFlow.FofVolumeProfile FofVolumeProfile(FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode)
		{
			return indicator.FofVolumeProfile(Input, period, resolutionMode);
		}

		public Indicators.FreeOrderFlow.FofVolumeProfile FofVolumeProfile(ISeries<double> input , FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode)
		{
			return indicator.FofVolumeProfile(input, period, resolutionMode);
		}
	}
}

#endregion
