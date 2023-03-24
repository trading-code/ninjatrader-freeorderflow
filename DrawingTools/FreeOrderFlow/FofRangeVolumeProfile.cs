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
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Xml.Serialization;
using Brush = System.Windows.Media.Brush;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators.FreeOrderFlow;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.DrawingTools
{
	public class FofRangeVolumeProfile : Rectangle
	{
		#region Icon
		public override object Icon {
			get {
				Grid icon = new Grid { Height = 16, Width = 16, UseLayoutRounding = true };
				RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);
				icon.Children.Add(new Path {
					Stroke = Application.Current.TryFindResource("MenuBorderBrush") as Brush,
					StrokeThickness = 1,
					Data = Geometry.Parse("M 0 1 H 10 V 3 H 0 M 0 5 H 13 V 7 H 0 M 0 9 H 8 V 11 H 0 M 0 13 H 4 V 15 H 0 M 0 0 V 16 M 16 0 V 16")
				});
				return icon;
			}
		}
		#endregion

		private double MaxPrice;
		private double MinPrice;
		private int StartBar = -1;
		private int EndBar = -1;
		private BarsRequest BarsRequest;
		private VolumeProfileData profile;
		private SharpDX.Direct2D1.Brush volumeBrushDX;
		private SharpDX.Direct2D1.Brush buyBrushDX;
		private SharpDX.Direct2D1.Brush sellBrushDX;
		private ChartControl ChartControl;
		private ChartBars ChartBars { get { return AttachedTo.ChartObject as ChartBars; } }
		private bool isLoading;
		
		#region OnStateChange
		protected override void OnStateChange()
		{
			base.OnStateChange();
			if (State == State.SetDefaults)
			{				
				Name						= "Volume Profile (Free Order Flow)";
				Description					= @"Free Order Flow Range Volume Profile";
				AreaOpacity					= 5;
				AreaBrush					= Brushes.Silver;
				OutlineStroke				= new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1, 50);

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
				ZOrderType = DrawingToolZOrder.AlwaysDrawnLast;
				ZOrder = -1;
			}
		}
		#endregion
		
		#region Calculation
		private void CaculateVolumeProfile(ChartControl chartControl, ChartScale chartScale)
		{
			isLoading = true;
			Bars chartBars = (AttachedTo.ChartObject as ChartBars).Bars;
			profile = new VolumeProfileData() {
				StartBar = StartBar, EndBar = EndBar
			};
			var bars = (AttachedTo.ChartObject as ChartBars).Bars;
			
			if (BarsRequest != null)
			{
				BarsRequest = null;
			}
			
			var startTime = StartBar > EndBar ? EndAnchor.Time : StartAnchor.Time;
			var endTime = StartBar > EndBar ? StartAnchor.Time : EndAnchor.Time;
			BarsRequest = new BarsRequest(chartBars.Instrument, startTime, endTime);
			BarsRequest.BarsPeriod = new BarsPeriod() { BarsPeriodType = BarsPeriodType.Tick, Value = 1 };
			BarsRequest.Request((request, errorCode, errorMessage) =>
			{
				if (request != BarsRequest || State >= State.Terminated) return;
				if (errorCode != Cbi.ErrorCode.NoError)
				{
					request.Dispose();
					request = null;
					return;
				}
				// calculate volume profile from bars
				for (int i = 0; i < request.Bars.Count; i++)
				{
					if(
						request.Bars.BarsSeries.GetTime(i) < startTime ||
						request.Bars.BarsSeries.GetTime(i) > endTime
					) continue;
					double ask = request.Bars.BarsSeries.GetAsk(i);
					double bid = request.Bars.BarsSeries.GetBid(i);
					double close = request.Bars.BarsSeries.GetClose(i);
					long volume = request.Bars.BarsSeries.GetVolume(i);
					
					long buyVolume = (close >= ask) ? volume : 0;
					long sellVolume = (close <= bid) ? volume : 0;
					
					var row = profile.AddOrUpdate(
						close,
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
						profile.POC = close;
					}
					// calculate total volume for use in VAL and VAH
					profile.TotalVolume += (buyVolume + sellVolume);
				}

				profile.CalculateValueArea(ValueArea / 100f);
				isLoading = false;
			});
		}
		
		private void CalcAnchorPrice() {
			MaxPrice = ChartBars.Bars.GetHigh(StartBar);
			MinPrice = ChartBars.Bars.GetLow(StartBar);

			for(int i = StartBar + 1; i <= EndBar; i++) {
				MaxPrice = Math.Max(ChartBars.Bars.GetHigh(i), MaxPrice);
				MinPrice = Math.Min(ChartBars.Bars.GetLow(i), MinPrice);
			}

			StartAnchor.Price = MaxPrice;
			EndAnchor.Price = MinPrice;
		}
		#endregion
		
		#region Rendering
		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
			ChartControl = chartControl;

			if(StartAnchor.SlotIndex < 0 || EndAnchor.SlotIndex < 0) return;
			if(DrawingState == DrawingState.Normal) {
				// check anchor changed
				if(StartBar != (int) StartAnchor.SlotIndex || EndBar != (int) EndAnchor.SlotIndex) {
					StartBar = (int) Math.Min(StartAnchor.SlotIndex, EndAnchor.SlotIndex);
					EndBar = (int) Math.Max(StartAnchor.SlotIndex, EndAnchor.SlotIndex);
					if(EndBar >= ChartBars.Count) EndBar = ChartBars.Count - 1;
					CalcAnchorPrice();
					CaculateVolumeProfile(chartControl, chartScale);
				}
			}

			base.OnRender(chartControl, chartScale);

			if(profile != null && profile.TotalVolume > 0) {
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
			else
			if(isLoading)
			{
				var shapeRect = GetAnchorsRect(chartControl, chartScale);
				// create text label
				var textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();
				textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				textFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
				var textLayout = new SharpDX.DirectWrite.TextLayout(
					Core.Globals.DirectWriteFactory,
					"Calculating",
					textFormat,
					(float) shapeRect.Width,
					(float) shapeRect.Height
				);
				var textDxBrush = chartControl.Properties.ChartText.ToDxBrush(RenderTarget);
				RenderTarget.DrawTextLayout(
					new SharpDX.Vector2((float) shapeRect.X, (float) shapeRect.Y),
					textLayout,
					textDxBrush
				);
				textDxBrush.Dispose();
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
		
		private Rect GetAnchorsRect(ChartControl chartControl, ChartScale chartScale)
		{
			if (StartAnchor == null || EndAnchor == null)
				return new Rect();
			
			ChartPanel chartPanel	= chartControl.ChartPanels[chartScale.PanelIndex];
			Point startPoint		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point endPoint 			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			//rect doesnt handle negative width/height so we need to determine and wind it up ourselves
			// make sure to always use smallest left/top anchor for start
			double left 	= Math.Min(endPoint.X, startPoint.X);
			double top 		= Math.Min(endPoint.Y, startPoint.Y);
			double width 	= Math.Abs(endPoint.X - startPoint.X);
			double height 	= Math.Abs(endPoint.Y - startPoint.Y);
			return new Rect(left, top, width, height);
		}

		private void RenderVolumeProfile(ChartScale chartScale, VolumeProfileData profile)
		{
			var volumeProfileRender = new FofVolumeProfileRender(ChartControl, chartScale, ChartBars);

			foreach (KeyValuePair<double, VolumeProfileRow> row in profile)
			{
				var rect = volumeProfileRender.GetBarRect(profile, row.Key, row.Value.total);
				rect.Width *= Width / 100f;

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
			var volumeProfileRender = new FofVolumeProfileRender(ChartControl, chartScale, ChartBars);

			var rect = volumeProfileRender.GetBarRect(profile, profile.POC, profile.MaxVolume);
			rect.Width *= Width / 100f;
			RenderTarget.FillRectangle(rect, PocStroke.BrushDX);

			rect = volumeProfileRender.GetBarRect(profile, profile.POC, profile.MaxVolume, true);
			rect.Y += rect.Height / 2;
			RenderTarget.DrawLine(
				rect.TopLeft,
				rect.TopRight,
				PocStroke.BrushDX,
				PocStroke.Width,
				PocStroke.StrokeStyle
			);
		}
		
		private void RenderValueArea(ChartScale chartScale, VolumeProfileData profile)
		{
			var volumeProfileRender = new FofVolumeProfileRender(ChartControl, chartScale, ChartBars);
			Print(profile.VAH);
			// draw VAH
			if(profile.ContainsKey(profile.VAH)) {
				var vahRect = volumeProfileRender.GetBarRect(profile, profile.VAH, profile[profile.VAH].total, true);
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
				var valRect = volumeProfileRender.GetBarRect(profile, profile.VAL, profile[profile.VAL].total, true);
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
			var volumeProfileRender = new FofVolumeProfileRender(ChartControl, chartScale, ChartBars);

			foreach (KeyValuePair<double, VolumeProfileRow> row in profile)
			{
				var buyRect = volumeProfileRender.GetBarRect(profile, row.Key, row.Value.buy);
				buyRect.Width *= Width / 100f;
				var sellRect = volumeProfileRender.GetBarRect(profile, row.Key, row.Value.sell);
				sellRect.Width *= Width / 100f;
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
			var volumeProfileRender = new FofVolumeProfileRender(ChartControl, chartScale, ChartBars);

			foreach (KeyValuePair<double, VolumeProfileRow> row in profile)
			{
				var volumeDelta = Math.Abs(row.Value.buy - row.Value.sell);
				var rect = volumeProfileRender.GetBarRect(profile, row.Key, volumeDelta);
				rect.Width *= Width / 100f;
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
		#endregion

		#region Properties
		[Display(Name = "Display mode", Description="Profile mode to render", Order = 1, GroupName = "Setup")]
		public FofVolumeProfileMode DisplayMode { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Resolution Mode", Description="Calculate profile from region",  Order = 2, GroupName = "Setup")]
		public FofVolumeProfileResolution ResolutionMode { get; set; }
		
		[NinjaScriptProperty]
		[Display(Name = "Resolution", Description="Calculate profile from region",  Order = 3, GroupName = "Setup")]
		public int Resolution { get; set; }

		[Range(10, 90)]
		[Display(Name = "Value Area (%)", Description="Value area percentage",  Order = 7, GroupName = "Setup")]
		public int ValueArea { get; set; }

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
