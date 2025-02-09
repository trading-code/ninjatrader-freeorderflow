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
using InvestiSoft.NinjaScript.VolumeProfile;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FreeOrderFlow
{
    public class FofVolumeProfile : Indicator
    {
        private List<FofVolumeProfileData> Profiles;
        private int LastBar;
        private SharpDX.Direct2D1.Brush volumeBrushDX;
        private SharpDX.Direct2D1.Brush buyBrushDX;
        private SharpDX.Direct2D1.Brush sellBrushDX;
        private SharpDX.Direct2D1.Brush totalTextBrushDX;

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Free Order Flow Volume Profile";
                Name = "Volume Profile";
                IsChartOnly = true;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;

                // Setup
                DisplayMode = FofVolumeProfileMode.Standard;
                ResolutionMode = FofVolumeProfileResolution.Tick;
                Resolution = 1;
                ValueArea = 70;
                DisplayTotal = true;

                // Visual
                Width = 60;
                Opacity = 40;
                ValueAreaOpacity = 80;
                ShowPoc = true;
                ShowValueArea = true;
                VolumeBrush = Brushes.CornflowerBlue;
                BuyBrush = Brushes.DarkCyan;
                SellBrush = Brushes.MediumVioletRed;
                PocStroke = new Stroke(Brushes.Goldenrod, 1);
                ValueAreaStroke = new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Dash, 1);
            }
            else if (State == State.Configure)
            {
                Calculate = Calculate.OnEachTick;
                // Add lower timeframe data series
                AddDataSeries((ResolutionMode == FofVolumeProfileResolution.Tick) ? BarsPeriodType.Tick : BarsPeriodType.Minute, Resolution);

                // Init volume profiles list
                Profiles = new List<FofVolumeProfileData>()
                {
                    new FofVolumeProfileData() { StartBar = 0 }
                };
            }
            else if (State == State.Historical)
            {
                SetZOrder(-1);
            }
        }
        #endregion

        #region Calculations
        protected override void OnBarUpdate()
        {
            var profile = Profiles.Last();
            if (BarsInProgress == 1)
            {
                long buyVolume, sellVolume, otherVolume;

                if (ResolutionMode == FofVolumeProfileResolution.Tick && Resolution == 1)
                {
                    // 1 tick uses bid and ask price
                    var ask = BarsArray[1].GetAsk(CurrentBar);
                    var bid = BarsArray[1].GetBid(CurrentBar);

                    buyVolume = (Closes[1][0] >= ask) ? (long)Volumes[1][0] : 0;
                    sellVolume = (Closes[1][0] <= bid) ? (long)Volumes[1][0] : 0;
                    otherVolume = (Closes[1][0] < ask && Closes[1][0] > bid) ? (long)Volumes[1][0] : 0;
                }
                else
                {
                    buyVolume = Closes[1][0] > Opens[1][0] ? (long)Volumes[1][0] : 0;
                    sellVolume = Closes[1][0] < Opens[1][0] ? (long)Volumes[1][0] : 0;
                    otherVolume = 0;
                }

                profile.UpdateRow(Closes[1][0], buyVolume, sellVolume, otherVolume);
            }
            else // BarsInProgress == 0
            {
                if (State == State.Realtime || IsFirstTickOfBar)
                {
                    profile.CalculateValueArea(ValueArea / 100f);
                }

                // update profile end bar
                if (CurrentBar == profile.EndBar) return;
                profile.EndBar = CurrentBar;

                if (
                    IsFirstTickOfBar &&
                    (Period == FofVolumeProfilePeriod.Bars ||
                    (Period == FofVolumeProfilePeriod.Sessions && Bars.IsFirstBarOfSession))
                )
                {
                    // on new bar fisrt tick or new session first tick
                    if (State != State.Realtime)
                    {
                        profile.CalculateValueArea(ValueArea / 100f);
                    }
                    Profiles.Add(new FofVolumeProfileData() { StartBar = CurrentBar, EndBar = CurrentBar });
                }
            }
        }
        #endregion

        #region Rendering
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            var volProfileRenderer = new FofVolumeProfileChartRenderer(ChartControl, chartScale, ChartBars, RenderTarget)
            {
                Opacity = Opacity / 100f,
                ValueAreaOpacity = ValueAreaOpacity / 100f,
                WidthPercent = Width / 100f
            };
            totalTextBrushDX = chartControl.Properties.ChartText.ToDxBrush(RenderTarget);
            foreach (var profile in Profiles)
            {
                if (
                    profile.MaxVolume == 0 ||
                    (profile.StartBar < ChartBars.FromIndex && profile.EndBar < ChartBars.FromIndex) ||
                    (profile.StartBar > ChartBars.ToIndex && profile.EndBar > ChartBars.ToIndex)
                ) continue;
                if (DisplayMode == FofVolumeProfileMode.BuySell)
                {
                    volProfileRenderer.RenderBuySellProfile(profile, buyBrushDX, sellBrushDX);
                }
                else
                {
                    volProfileRenderer.RenderProfile(profile, volumeBrushDX);
                }
                if (ShowPoc) volProfileRenderer.RenderPoc(profile, PocStroke.BrushDX, PocStroke.Width, PocStroke.StrokeStyle, DisplayTotal);
                if (ShowValueArea) volProfileRenderer.RenderValueArea(profile, ValueAreaStroke.BrushDX, ValueAreaStroke.Width, ValueAreaStroke.StrokeStyle, DisplayTotal);
                if (DisplayMode == FofVolumeProfileMode.Delta)
                {
                    volProfileRenderer.RenderDeltaProfile(profile, buyBrushDX, sellBrushDX);
                }
                if (DisplayTotal)
                {
                    volProfileRenderer.RenderTotalVolume(profile, totalTextBrushDX);
                }
            }
        }

        public override void OnRenderTargetChanged()
        {
            if (volumeBrushDX != null) volumeBrushDX.Dispose();
            if (buyBrushDX != null) buyBrushDX.Dispose();
            if (sellBrushDX != null) sellBrushDX.Dispose();
            if (RenderTarget != null)
            {
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
        [Display(Name = "Display mode", Description = "Profile mode to render", Order = 1, GroupName = "Setup")]
        public FofVolumeProfileMode DisplayMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profile Period", Description = "Calculate profile from region", Order = 1, GroupName = "Setup")]
        public FofVolumeProfilePeriod Period { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Resolution Mode", Description = "Calculate profile from region", Order = 2, GroupName = "Setup")]
        public FofVolumeProfileResolution ResolutionMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Resolution", Description = "Calculate profile from region", Order = 3, GroupName = "Setup")]
        public int Resolution { get; set; }

        [Range(10, 90)]
        [Display(Name = "Value Area (%)", Description = "Value area percentage", Order = 7, GroupName = "Setup")]
        public float ValueArea { get; set; }

        [Display(Name = "Display Total Volume", Order = 8, GroupName = "Setup")]
        public bool DisplayTotal { get; set; }

        // Visual
        [Display(Name = "Profile width (%)", Description = "Width of bars relative to range", Order = 1, GroupName = "Visual")]
        public int Width { get; set; }

        [Range(1, 100)]
        [Display(Name = "Profile opacity (%)", Description = "Opacity of bars out value area", Order = 2, GroupName = "Visual")]
        public int Opacity { get; set; }

        [Range(1, 100)]
        [Display(Name = "Value area opacity (%)", Description = "Opacity of bars in value area", Order = 2, GroupName = "Visual")]
        public int ValueAreaOpacity { get; set; }

        [Display(Name = "Show POC", Description = "Show PoC line", Order = 5, GroupName = "Setup")]
        public bool ShowPoc { get; set; }

        [Display(Name = "Show Value Area", Description = "Show value area high and low lines", Order = 6, GroupName = "Setup")]
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

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private FreeOrderFlow.FofVolumeProfile[] cacheFofVolumeProfile;
		public FreeOrderFlow.FofVolumeProfile FofVolumeProfile(FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode, int resolution)
		{
			return FofVolumeProfile(Input, period, resolutionMode, resolution);
		}

		public FreeOrderFlow.FofVolumeProfile FofVolumeProfile(ISeries<double> input, FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode, int resolution)
		{
			if (cacheFofVolumeProfile != null)
				for (int idx = 0; idx < cacheFofVolumeProfile.Length; idx++)
					if (cacheFofVolumeProfile[idx] != null && cacheFofVolumeProfile[idx].Period == period && cacheFofVolumeProfile[idx].ResolutionMode == resolutionMode && cacheFofVolumeProfile[idx].Resolution == resolution && cacheFofVolumeProfile[idx].EqualsInput(input))
						return cacheFofVolumeProfile[idx];
			return CacheIndicator<FreeOrderFlow.FofVolumeProfile>(new FreeOrderFlow.FofVolumeProfile(){ Period = period, ResolutionMode = resolutionMode, Resolution = resolution }, input, ref cacheFofVolumeProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.FreeOrderFlow.FofVolumeProfile FofVolumeProfile(FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode, int resolution)
		{
			return indicator.FofVolumeProfile(Input, period, resolutionMode, resolution);
		}

		public Indicators.FreeOrderFlow.FofVolumeProfile FofVolumeProfile(ISeries<double> input , FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode, int resolution)
		{
			return indicator.FofVolumeProfile(input, period, resolutionMode, resolution);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.FreeOrderFlow.FofVolumeProfile FofVolumeProfile(FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode, int resolution)
		{
			return indicator.FofVolumeProfile(Input, period, resolutionMode, resolution);
		}

		public Indicators.FreeOrderFlow.FofVolumeProfile FofVolumeProfile(ISeries<double> input , FofVolumeProfilePeriod period, FofVolumeProfileResolution resolutionMode, int resolution)
		{
			return indicator.FofVolumeProfile(input, period, resolutionMode, resolution);
		}
	}
}

#endregion
