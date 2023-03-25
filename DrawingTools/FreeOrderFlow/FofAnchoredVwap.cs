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
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.DrawingTools.FreeOrderFlow
{
	public class FofAnchoredVwap : DrawingTool
	{
		#region Icon
		public override object Icon {
			get {
				Grid icon = new Grid { Height = 16, Width = 16, UseLayoutRounding = true, SnapsToDevicePixels = true };
				RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);
				icon.Children.Add(new Path {
					Stroke = Application.Current.TryFindResource("MenuBorderBrush") as Brush,
					StrokeThickness = 1,
					Data = Geometry.Parse("M 2 5 H 4 V 13 H 2 V 5 Z M 3 13 V 16 M 7 3 H 9 V 12 H 7 V 3 Z M 8 2 V 0 M 12 3 H 14 V 11 H 12 V 3 Z M 13 11 V 13 M 0 13 C 3 3 12 12 15 3")
				});
				return icon;
			}
		}
		#endregion

		#region Private properties
		private static float MinimumSize = 5f;
		private InputPriceType calculatedInputPrice = InputPriceType.Median;

		private double BarWidth
		{
			get
			{
				if (ChartBars != null && ChartBars.Properties.ChartStyle != null)
					return ChartBars.Properties.ChartStyle.BarWidth;
				return MinimumSize;
			}
		}

		private ChartBars ChartBars
		{
			get
			{
				ChartBars chartBars = AttachedTo.ChartObject as ChartBars;
				if (chartBars == null)
				{
					Gui.NinjaScript.IChartBars iChartBars = AttachedTo.ChartObject as Gui.NinjaScript.IChartBars;
					if (iChartBars != null) chartBars = iChartBars.ChartBars;
				}
				return chartBars;
			}
		}

		private Dictionary<int, double> vwap = new Dictionary<int, double>();
		private double cumVol;
		private double cumPV;
		private int StartBar = -1;
		private int EndBar = -1;
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name				= "Anchored VWAP (Free Order Flow)";
				Description			= @"Plot VWAP from anchored bar";
				InputPrice			= InputPriceType.Median;
				Stroke				= new Stroke(Brushes.DodgerBlue, 1);
				Anchor = new ChartAnchor
				{
					DisplayName = Custom.Resource.NinjaScriptDrawingToolAnchor,
					IsEditing	= true,
				};
				Anchor.IsYPropertyVisible = false;
			}
			else if (State == State.Terminated) Dispose();
		}

		#region DrawingTool methods
		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			if (DrawingState == DrawingState.Building)
				return Cursors.Pen;
			if (DrawingState == DrawingState.Moving)
				return IsLocked ? Cursors.No : Cursors.SizeAll;
			// this is fired whenever the chart marker is selected.
			// so if the mouse is anywhere near our marker, show a moving icon only. point is already in device pixels
			// we want to check at least 6 pixels away, or by padding x 2 if its more (It could be 0 on some objects like square)
			Point anchorPointPixels = Anchor.GetPoint(chartControl, chartPanel, chartScale);
			Vector distToMouse = point - anchorPointPixels;
			return distToMouse.Length <= GetSelectionSensitivity(chartControl) ?
				IsLocked ?  Cursors.Arrow : Cursors.SizeAll :
				null;
		}

		public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			if (Anchor.IsEditing) return new Point[0];

			ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];
			Point anchorPoint = Anchor.GetPoint(chartControl, chartPanel, chartScale);
			return new[]{ anchorPoint };
		}

		public double GetSelectionSensitivity(ChartControl chartControl)
		{
			return Math.Max(15d, 10d * (BarWidth / 5d));
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			switch (DrawingState)
			{
				case DrawingState.Building:
					dataPoint.CopyDataValues(Anchor);
					Anchor.IsEditing = false;
					DrawingState = DrawingState.Normal;
					IsSelected = false;
					DetectPriceInput();
					break;
				case DrawingState.Normal:
					// make sure they clicked near us. use GetCursor incase something has more than one point, like arrows
					Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					if (GetCursor(chartControl, chartPanel, chartScale, point) != null)
						DrawingState = DrawingState.Moving;
					else
						IsSelected = false;
					break;
			}
		}

		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (DrawingState != DrawingState.Moving || IsLocked && DrawingState != DrawingState.Building)
				return;
			Anchor.Time = dataPoint.Time;
			Anchor.Price = dataPoint.Price;
		}

		public override void OnMouseUp(ChartControl control, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (DrawingState == DrawingState.Editing || DrawingState == DrawingState.Moving) {
				DrawingState = DrawingState.Normal;
				DetectPriceInput();
				ForceRefresh();
			}
		}

		public override void OnCalculateMinMax()
		{
			MinValue = double.MaxValue;
			MaxValue = double.MinValue;

			if (!this.IsVisible) return;

			MinValue = Anchor.Price;
			MaxValue = Anchor.Price;
		}

		public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
		{
			int lastBar = chartControl.BarsArray[0].ToIndex;
			if (
				lastTimeOnChart > Anchor.Time &&
				(
					Anchor.Price < chartScale.MaxValue && Anchor.Price > chartScale.MinValue
				) || (
					!vwap.ContainsKey(lastBar) || (vwap[lastBar] < chartScale.MaxValue && vwap[lastBar] > chartScale.MinValue)
				)
			) return true;
			return false;
		}

		public override IEnumerable<ChartAnchor> Anchors
		{
			get { return new[]{Anchor}; }
		}
		#endregion

		#region Calculations
		private double GetInputPrice(int currentBar)
		{
			switch(InputPrice)
			{
				case InputPriceType.High:
					return ChartBars.Bars.GetHigh(currentBar);
				case InputPriceType.Low:
					return ChartBars.Bars.GetLow(currentBar);
				default:
					return (ChartBars.Bars.GetHigh(currentBar) + ChartBars.Bars.GetLow(currentBar)) / 2;
			}
		}

		private void DetectPriceInput() {
			var high = ChartBars.Bars.GetHigh((int) Anchor.SlotIndex);
			var low = ChartBars.Bars.GetLow((int) Anchor.SlotIndex);
			var tickSize = AttachedTo.Instrument.MasterInstrument.TickSize;

			if(Anchor.Price < low + tickSize) {
				InputPrice = InputPriceType.Low;
			} else if(Anchor.Price > high - tickSize) {
				InputPrice = InputPriceType.High;
			} else {
				InputPrice = InputPriceType.Median;
			}
		}

		private void CalculateVWAP() {
			int startIndex = -1;

			if(calculatedInputPrice != InputPrice || StartBar != (int) Anchor.SlotIndex) {
				// recalculate
				cumVol = 0;
				cumPV = 0;
				startIndex = (int) Anchor.SlotIndex;
			} else if(EndBar < ChartBars.Bars.Count - 1) {
				// new bars added
				startIndex = EndBar;
			}

			if(startIndex >= 0){
				for(int i = startIndex; i < ChartBars.Bars.Count; i++) {
					cumPV += GetInputPrice(i) * ChartBars.Bars.GetVolume(i);
					cumVol += ChartBars.Bars.GetVolume(i);
					vwap[i] = cumPV / (cumVol == 0 ? 1 : cumVol);
				}
				StartBar = (int) Anchor.SlotIndex;
				EndBar = ChartBars.Bars.Count - 1;
			}

			calculatedInputPrice = InputPrice;
		}
		#endregion

		#region Rendering
		public override void OnRenderTargetChanged()
		{
			if (Stroke == null) return;
			if (RenderTarget != null) Stroke.RenderTarget = RenderTarget;
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (Anchor.IsEditing) return;

			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

			if(DrawingState != DrawingState.Moving) {
				Anchor.Price = GetInputPrice((int) Anchor.SlotIndex);
			}

			ChartPanel panel = chartControl.ChartPanels[chartScale.PanelIndex];
			Point pixelPoint = Anchor.GetPoint(chartControl, panel, chartScale);
			// center rendering on anchor is done by radius method of drawing here
			float radius = Math.Max((float) BarWidth, MinimumSize);
			// render anchor point
			RenderTarget.FillEllipse(
				new SharpDX.Direct2D1.Ellipse(pixelPoint.ToVector2(), radius, radius),
				Stroke.BrushDX
			);

			if(DrawingState == DrawingState.Normal && (
				InputPrice != calculatedInputPrice ||
				StartBar != (int)Anchor.SlotIndex ||
				EndBar != ChartBars.ToIndex
			)) {
				CalculateVWAP();
			}

			if(StartBar == (int)Anchor.SlotIndex) {
				RenderVWAP(chartControl, chartScale);
			}
		}

		private void RenderVWAP(ChartControl chartControl, ChartScale chartScale) {
			if(chartControl.BarsArray.Count < 1) return;

			for(int i = StartBar + 1; i < EndBar; i++) {
				if(!vwap.ContainsKey(i - 1)) continue;
				if(i > ChartBars.ToIndex) break;
				SharpDX.Vector2 startPoint = new SharpDX.Vector2(
					chartControl.GetXByBarIndex(ChartBars, i - 1),
					chartScale.GetYByValue(vwap[i - 1])
				);
				SharpDX.Vector2 endPoint = new SharpDX.Vector2(
					chartControl.GetXByBarIndex(ChartBars, i),
					chartScale.GetYByValue(vwap[i])
				);
				RenderTarget.DrawLine(startPoint, endPoint, Stroke.BrushDX, Stroke.Width, Stroke.StrokeStyle);
			}
		}
		#endregion

		#region Properties
		public ChartAnchor Anchor { get; set; }

		[Display(ResourceType=typeof(Custom.Resource), Name = "Input Price Type", GroupName = "NinjaScriptGeneral", Order = 1)]
		public InputPriceType InputPrice { get; set; }

		[Display(ResourceType=typeof(Custom.Resource), Name = "Line", GroupName = "NinjaScriptGeneral", Order = 2)]
		public Stroke Stroke { get; set; }
		#endregion
	}
	public enum InputPriceType { High, Low, Median };
}
