// 
// Copyright (C) 2023, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
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
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
#endregion

//This namespace holds Market Analyzer columns in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		private		Indicators.Indicator	indicator;

		public MarketAnalyzerColumn()
		{
			lock (NinjaScripts)
				NinjaScripts.Add(indicator = new Indicators.Indicator() { IsDataSeriesRequired = this.IsDataSeriesRequired, Parent = this });
		}

		[Browsable(false)]
		public new bool IsDataSeriesRequired
		{ 
			get { return base.IsDataSeriesRequired; }
			set 
			{ 
				base.IsDataSeriesRequired = value; 
				if (indicator != null)
					indicator.IsDataSeriesRequired = value;
			}
		}
	}
}
