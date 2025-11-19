/*
 * FofAnchoredVwap.cs
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

		public enum PriceSource
		{
			Close,
			HLC3,
			HL2,
			OHLC4,
			HLCC4
		}

		#region Private properties
		private static float MinimumSize = 5f;
		private PriceSource calculatedPriceSource = PriceSource.HLC3;

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
		private double cumPV2;
		private int StartBar = -1;
		private int EndBar = -1;
		private Dictionary<int, double> upperBand1 = new Dictionary<int, double>();
		private Dictionary<int, double> lowerBand1 = new Dictionary<int, double>();
		private Dictionary<int, double> upperBand2 = new Dictionary<int, double>();
		private Dictionary<int, double> lowerBand2 = new Dictionary<int, double>();
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name				= "Anchored VWAP (Fof)";
				Description			= @"Plot VWAP from anchored bar with Standard Deviation Bands";
				PriceSourceInput	= PriceSource.HLC3;
				Stroke				= new Stroke(Brushes.DeepSkyBlue, 1);
				UpperBand1Stroke	= new Stroke(Brushes.LimeGreen, 1);
				LowerBand1Stroke	= new Stroke(Brushes.LimeGreen, 1);
				UpperBand2Stroke	= new Stroke(Brushes.Orange, 1);
				LowerBand2Stroke	= new Stroke(Brushes.Orange, 1);
				ShowFirstBands		= true;
				ShowSecondBands		= true;
				FirstStdDev			= 1.0;
				SecondStdDev		= 2.0;
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
					DetectPriceSource();
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
				DetectPriceSource();
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
		private double GetPrice(int currentBar)
		{
			switch(PriceSourceInput)
			{
				case PriceSource.Close:
					return ChartBars.Bars.GetClose(currentBar);
				case PriceSource.HLC3:
					return (ChartBars.Bars.GetHigh(currentBar) + ChartBars.Bars.GetLow(currentBar) + ChartBars.Bars.GetClose(currentBar)) / 3.0;
				case PriceSource.HL2:
					return (ChartBars.Bars.GetHigh(currentBar) + ChartBars.Bars.GetLow(currentBar)) / 2.0;
				case PriceSource.OHLC4:
					return (ChartBars.Bars.GetOpen(currentBar) + ChartBars.Bars.GetHigh(currentBar) + ChartBars.Bars.GetLow(currentBar) + ChartBars.Bars.GetClose(currentBar)) / 4.0;
				case PriceSource.HLCC4:
					return (ChartBars.Bars.GetHigh(currentBar) + ChartBars.Bars.GetLow(currentBar) + ChartBars.Bars.GetClose(currentBar) + ChartBars.Bars.GetClose(currentBar)) / 4.0;
				default:
					return (ChartBars.Bars.GetHigh(currentBar) + ChartBars.Bars.GetLow(currentBar) + ChartBars.Bars.GetClose(currentBar)) / 3.0;
			}
		}

		private void DetectPriceSource() {
			var high = ChartBars.Bars.GetHigh((int) Anchor.SlotIndex);
			var low = ChartBars.Bars.GetLow((int) Anchor.SlotIndex);
			var close = ChartBars.Bars.GetClose((int) Anchor.SlotIndex);
			var tickSize = AttachedTo.Instrument.MasterInstrument.TickSize;

			if(Math.Abs(Anchor.Price - close) < tickSize) {
				PriceSourceInput = PriceSource.Close;
			} else if(Math.Abs(Anchor.Price - low) < tickSize) {
				PriceSourceInput = PriceSource.HL2;
			} else if(Math.Abs(Anchor.Price - high) < tickSize) {
				PriceSourceInput = PriceSource.HL2;
			} else {
				PriceSourceInput = PriceSource.HLC3;
			}
		}

		private void CalculateVWAP() {
			int startIndex = -1;

			if(calculatedPriceSource != PriceSourceInput || StartBar != (int) Anchor.SlotIndex) {
				// recalculate
				cumVol = 0;
				cumPV = 0;
				cumPV2 = 0;
				startIndex = (int) Anchor.SlotIndex;
			} else if(EndBar < ChartBars.Bars.Count - 1) {
				// new bars added
				startIndex = EndBar;
			}

			if(startIndex >= 0){
				for(int i = startIndex; i < ChartBars.Bars.Count; i++) {
					double price = GetPrice(i);
					double volume = ChartBars.Bars.GetVolume(i);
					cumPV += price * volume;
					cumPV2 += price * price * volume;
					cumVol += volume;
					vwap[i] = cumPV / (cumVol == 0 ? 1 : cumVol);

					if(ShowFirstBands || ShowSecondBands)
					{
						double variance = (cumPV2 / (cumVol == 0 ? 1 : cumVol)) - (vwap[i] * vwap[i]);
						double stdDev = variance > 0 ? Math.Sqrt(variance) : 0;

						if(ShowFirstBands)
						{
							upperBand1[i] = vwap[i] + (stdDev * FirstStdDev);
							lowerBand1[i] = vwap[i] - (stdDev * FirstStdDev);
						}

						if(ShowSecondBands)
						{
							upperBand2[i] = vwap[i] + (stdDev * SecondStdDev);
							lowerBand2[i] = vwap[i] - (stdDev * SecondStdDev);
						}
					}
				}
				StartBar = (int) Anchor.SlotIndex;
				EndBar = ChartBars.Bars.Count - 1;
			}

			calculatedPriceSource = PriceSourceInput;
		}
		#endregion

		#region Rendering
		public override void OnRenderTargetChanged()
		{
			if (Stroke != null && RenderTarget != null) Stroke.RenderTarget = RenderTarget;
			if (UpperBand1Stroke != null && RenderTarget != null) UpperBand1Stroke.RenderTarget = RenderTarget;
			if (LowerBand1Stroke != null && RenderTarget != null) LowerBand1Stroke.RenderTarget = RenderTarget;
			if (UpperBand2Stroke != null && RenderTarget != null) UpperBand2Stroke.RenderTarget = RenderTarget;
			if (LowerBand2Stroke != null && RenderTarget != null) LowerBand2Stroke.RenderTarget = RenderTarget;
		}

		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (Anchor.IsEditing) return;

			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

			if(DrawingState != DrawingState.Moving) {
				Anchor.Price = GetPrice((int) Anchor.SlotIndex);
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
				PriceSourceInput != calculatedPriceSource ||
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
				
				// Render VWAP line
				SharpDX.Vector2 startPoint = new SharpDX.Vector2(
					chartControl.GetXByBarIndex(ChartBars, i - 1),
					chartScale.GetYByValue(vwap[i - 1])
				);
				SharpDX.Vector2 endPoint = new SharpDX.Vector2(
					chartControl.GetXByBarIndex(ChartBars, i),
					chartScale.GetYByValue(vwap[i])
				);
				RenderTarget.DrawLine(startPoint, endPoint, Stroke.BrushDX, Stroke.Width, Stroke.StrokeStyle);

				// Render First Standard Deviation Bands
				if(ShowFirstBands && upperBand1.ContainsKey(i - 1) && upperBand1.ContainsKey(i) && lowerBand1.ContainsKey(i - 1) && lowerBand1.ContainsKey(i)) {
					// Upper Band 1
					SharpDX.Vector2 startUpperBand1 = new SharpDX.Vector2(
						chartControl.GetXByBarIndex(ChartBars, i - 1),
						chartScale.GetYByValue(upperBand1[i - 1])
					);
					SharpDX.Vector2 endUpperBand1 = new SharpDX.Vector2(
						chartControl.GetXByBarIndex(ChartBars, i),
						chartScale.GetYByValue(upperBand1[i])
					);
					RenderTarget.DrawLine(startUpperBand1, endUpperBand1, UpperBand1Stroke.BrushDX, UpperBand1Stroke.Width, UpperBand1Stroke.StrokeStyle);

					// Lower Band 1
					SharpDX.Vector2 startLowerBand1 = new SharpDX.Vector2(
						chartControl.GetXByBarIndex(ChartBars, i - 1),
						chartScale.GetYByValue(lowerBand1[i - 1])
					);
					SharpDX.Vector2 endLowerBand1 = new SharpDX.Vector2(
						chartControl.GetXByBarIndex(ChartBars, i),
						chartScale.GetYByValue(lowerBand1[i])
					);
					RenderTarget.DrawLine(startLowerBand1, endLowerBand1, LowerBand1Stroke.BrushDX, LowerBand1Stroke.Width, LowerBand1Stroke.StrokeStyle);
				}

				// Render Second Standard Deviation Bands
				if(ShowSecondBands && upperBand2.ContainsKey(i - 1) && upperBand2.ContainsKey(i) && lowerBand2.ContainsKey(i - 1) && lowerBand2.ContainsKey(i)) {
					// Upper Band 2
					SharpDX.Vector2 startUpperBand2 = new SharpDX.Vector2(
						chartControl.GetXByBarIndex(ChartBars, i - 1),
						chartScale.GetYByValue(upperBand2[i - 1])
					);
					SharpDX.Vector2 endUpperBand2 = new SharpDX.Vector2(
						chartControl.GetXByBarIndex(ChartBars, i),
						chartScale.GetYByValue(upperBand2[i])
					);
					RenderTarget.DrawLine(startUpperBand2, endUpperBand2, UpperBand2Stroke.BrushDX, UpperBand2Stroke.Width, UpperBand2Stroke.StrokeStyle);

					// Lower Band 2
					SharpDX.Vector2 startLowerBand2 = new SharpDX.Vector2(
						chartControl.GetXByBarIndex(ChartBars, i - 1),
						chartScale.GetYByValue(lowerBand2[i - 1])
					);
					SharpDX.Vector2 endLowerBand2 = new SharpDX.Vector2(
						chartControl.GetXByBarIndex(ChartBars, i),
						chartScale.GetYByValue(lowerBand2[i])
					);
					RenderTarget.DrawLine(startLowerBand2, endLowerBand2, LowerBand2Stroke.BrushDX, LowerBand2Stroke.Width, LowerBand2Stroke.StrokeStyle);
				}
			}
		}
		#endregion

		#region Properties
		public ChartAnchor Anchor { get; set; }

		[Display(Name = "Price Source", Description = "Source for price calculation", Order = 1, GroupName = "Parameters")]
		public PriceSource PriceSourceInput { get; set; }

		[Display(Name = "Show First Std Dev Bands", Description = "Show first standard deviation bands", Order = 2, GroupName = "Parameters")]
		public bool ShowFirstBands { get; set; }

		[Display(Name = "Show Second Std Dev Bands", Description = "Show second standard deviation bands", Order = 3, GroupName = "Parameters")]
		public bool ShowSecondBands { get; set; }

		[Display(Name = "First Std Dev Multiplier", Description = "Multiplier for first standard deviation", Order = 4, GroupName = "Parameters")]
		public double FirstStdDev { get; set; }

		[Display(Name = "Second Std Dev Multiplier", Description = "Multiplier for second standard deviation", Order = 5, GroupName = "Parameters")]
		public double SecondStdDev { get; set; }

		[Display(ResourceType=typeof(Custom.Resource), Name = "Line", GroupName = "NinjaScriptGeneral", Order = 10)]
		public Stroke Stroke { get; set; }

		[Display(Name = "Upper Band 1 Stroke", GroupName = "NinjaScriptGeneral", Order = 11)]
		public Stroke UpperBand1Stroke { get; set; }

		[Display(Name = "Lower Band 1 Stroke", GroupName = "NinjaScriptGeneral", Order = 12)]
		public Stroke LowerBand1Stroke { get; set; }

		[Display(Name = "Upper Band 2 Stroke", GroupName = "NinjaScriptGeneral", Order = 13)]
		public Stroke UpperBand2Stroke { get; set; }

		[Display(Name = "Lower Band 2 Stroke", GroupName = "NinjaScriptGeneral", Order = 14)]
		public Stroke LowerBand2Stroke { get; set; }
		#endregion
	}
}
