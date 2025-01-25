#region Using declarations
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using InvestiSoft.NinjaScript.VolumeProfile;
#endregion

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
                Description                 = @"Free Order Flow Volume Profile";
                Name                        = "Volume Profile";
                IsChartOnly                 = true;
                IsOverlay                   = true;
                DisplayInDataBox            = false;
                DrawOnPricePanel            = true;

                // Setup
                DisplayMode                 = FofVolumeProfileMode.Standard;
                ResolutionMode              = FofVolumeProfileResolution.Tick;
                Resolution                  = 1;
                ValueArea                   = 70;
                DisplayTotal                = true;

                // Visual
                Width                       = 60;
                Opacity                     = 40;
                ValueAreaOpacity            = 80;
                ShowPoc                     = true;
                ShowValueArea               = true;
                VolumeBrush                 = Brushes.CornflowerBlue;
                BuyBrush                    = Brushes.DarkCyan;
                SellBrush                   = Brushes.MediumVioletRed;
                PocStroke                   = new Stroke(Brushes.Goldenrod, 1);
                ValueAreaStroke             = new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Dash, 1);
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
                        (double key) => new FofVolumeProfileRow() {
                            buy = buyVolume,
                            sell = sellVolume
                        },
                        (double key, FofVolumeProfileRow oldValue) => new FofVolumeProfileRow()
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
                if(State == State.Realtime || IsFirstTickOfBar) {
                    Profiles.Last().CalculateValueArea(ValueArea / 100f);
                }

                if(CurrentBar == LastBar) return;
                LastBar = CurrentBar;

                Profiles.Last().EndBar = CurrentBar - 1;

                if(
                    Period == FofVolumeProfilePeriod.Bars ||
                    (Period == FofVolumeProfilePeriod.Sessions && Bars.IsFirstBarOfSession)
                )
                {
                    if(State != State.Realtime) {
                        Profiles.Last().CalculateValueArea(ValueArea / 100f);
                    }
                    Profiles.Add(new FofVolumeProfileData() { StartBar = CurrentBar });
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

            if (totalTextBrushDX == null)
            {
                totalTextBrushDX = chartControl.Properties.ChartText.ToDxBrush(RenderTarget);
            }
            foreach (var profile in Profiles)
            {
                if (
                    profile.MaxVolume == 0 ||
                    (profile.StartBar < ChartBars.FromIndex && profile.EndBar < ChartBars.FromIndex) ||
                    (profile.StartBar > ChartBars.ToIndex && profile.EndBar > ChartBars.ToIndex)
                ) continue;
                if(DisplayMode == FofVolumeProfileMode.BuySell)
                {
                    volProfileRenderer.RenderBuySellProfile(profile, buyBrushDX, sellBrushDX);
                }
                else
                {
                    volProfileRenderer.RenderProfile(profile, volumeBrushDX);
                }
                if(ShowPoc) volProfileRenderer.RenderPoc(profile, PocStroke.BrushDX, PocStroke.Width, PocStroke.StrokeStyle);
                if(ShowValueArea) volProfileRenderer.RenderValueArea(profile, ValueAreaStroke.BrushDX, ValueAreaStroke.Width, ValueAreaStroke.StrokeStyle);
                if(DisplayMode == FofVolumeProfileMode.Delta)
                {
                    volProfileRenderer.RenderDeltaProfile(profile, buyBrushDX, sellBrushDX);
                }
                if(DisplayTotal) {
                    volProfileRenderer.RenderTotalVolume(profile, totalTextBrushDX);
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

        [NinjaScriptProperty]
        [Display(Name = "Resolution", Description="Calculate profile from region",  Order = 3, GroupName = "Setup")]
        public int Resolution { get; set; }

        [Range(10, 90)]
        [Display(Name = "Value Area (%)", Description="Value area percentage",  Order = 7, GroupName = "Setup")]
        public int ValueArea { get; set; }

        [Display(Name = "Display Total Volume", Order = 8, GroupName = "Setup")]
        public bool DisplayTotal { get; set; }

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
