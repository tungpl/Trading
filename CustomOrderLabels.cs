#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class CustomOrderLabels : Indicator
	{
		private SolidColorBrush targetBgBrush;
		private SolidColorBrush targetTxtBrush;
		private SolidColorBrush stopBgBrush;
		private SolidColorBrush stopTxtBrush;
		private SolidColorBrush outlineBrush;
		private TextFormat textFormat;
		
		private string chartTraderAccountName = string.Empty;
		private bool isCheckingAccount = false;
		private NinjaTrader.Gui.Tools.AccountSelector accountSelector;
		private System.Windows.Controls.SelectionChangedEventHandler accountSelectionChangedHandler;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description					= "Nhãn lệnh chuyên nghiệp - Mặc định chữ TGT màu đen";
				Name						= "CustomOrderLabels";
				Calculate					= Calculate.OnPriceChange;
				IsOverlay					= true;
				DisplayInDataBox			= false;
				PaintPriceMarkers			= false;
				
				XOffset 					= 900; 
				YOffset 					= 0;  
				LabelWidth					= 220; 
				MyFontSize                  = 16; 
				Commission                  = 1.0; 

				// MÀU MẶC ĐỊNH ĐÃ CẬP NHẬT: Chữ TGT là màu Đen (Black)
				TargetColor                 = System.Windows.Media.Brushes.Lime;
				TargetFontColor             = System.Windows.Media.Brushes.Black; 
				StopColor                   = System.Windows.Media.Brushes.Tomato;
				StopFontColor               = System.Windows.Media.Brushes.White;
			}
			else if (State == State.Terminated)
			{
				DisposeBrushes();
				if (accountSelector != null && accountSelectionChangedHandler != null && ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(new Action(() => {
						try { accountSelector.SelectionChanged -= accountSelectionChangedHandler; } catch { }
					}));
				}
			}
		}

		private void DisposeBrushes()
		{
			if (targetBgBrush != null) { targetBgBrush.Dispose(); targetBgBrush = null; }
			if (targetTxtBrush != null) { targetTxtBrush.Dispose(); targetTxtBrush = null; }
			if (stopBgBrush != null) { stopBgBrush.Dispose(); stopBgBrush = null; }
			if (stopTxtBrush != null) { stopTxtBrush.Dispose(); stopTxtBrush = null; }
			if (outlineBrush != null) { outlineBrush.Dispose(); outlineBrush = null; }
			if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
		}

		public override void OnRenderTargetChanged()
		{
			base.OnRenderTargetChanged();
			if (RenderTarget != null)
			{
				try
				{
					DisposeBrushes();
					targetBgBrush = new SolidColorBrush(RenderTarget, TargetColor.ToSharpDXColor());
					targetTxtBrush = new SolidColorBrush(RenderTarget, TargetFontColor.ToSharpDXColor());
					stopBgBrush = new SolidColorBrush(RenderTarget, StopColor.ToSharpDXColor());
					stopTxtBrush = new SolidColorBrush(RenderTarget, StopFontColor.ToSharpDXColor());
					outlineBrush = new SolidColorBrush(RenderTarget, SharpDX.Color.DimGray);
					
					textFormat = new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, SharpDX.DirectWrite.FontStretch.Normal, (float)MyFontSize)
					{
						TextAlignment = SharpDX.DirectWrite.TextAlignment.Center,
						ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center
					};
				} catch { }
			}
		}

		protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			if (Bars == null || chartControl == null || targetBgBrush == null || stopBgBrush == null || textFormat == null) return;

			try
			{
				if (accountSelector == null && !isCheckingAccount)
				{
					isCheckingAccount = true;
					chartControl.Dispatcher.InvokeAsync(new Action(() =>
					{
						try
						{
							System.Windows.Window window = System.Windows.Window.GetWindow(chartControl.Parent);
							if (window != null)
							{
								accountSelector = window.FindFirst("ChartTraderControlAccountSelector") as NinjaTrader.Gui.Tools.AccountSelector;
								if (accountSelector != null)
								{
									if (accountSelector.SelectedAccount != null)
										chartTraderAccountName = accountSelector.SelectedAccount.Name;
									
									accountSelectionChangedHandler = (o, e) => 
									{
										if (accountSelector.SelectedAccount != null)
											chartTraderAccountName = accountSelector.SelectedAccount.Name;
										chartControl.InvalidateVisual();
									};
									accountSelector.SelectionChanged += accountSelectionChangedHandler;
								}
							}
						}
						catch { }
						finally { isCheckingAccount = false; }
					}));
				}

				if (string.IsNullOrEmpty(chartTraderAccountName)) return;

				lock (Account.All)
				{
					Account currentAccount = Account.All.FirstOrDefault(a => a.Name == chartTraderAccountName);
					if (currentAccount == null) return;

					Position pos = currentAccount.Positions.FirstOrDefault(p => p.Instrument.FullName == Instrument.FullName);
					int posQuantity = (pos != null && pos.MarketPosition != MarketPosition.Flat) ? pos.Quantity : 0;
					double entryPrice = (posQuantity > 0) ? pos.AveragePrice : 0;
					MarketPosition marketPos = (pos != null) ? pos.MarketPosition : MarketPosition.Flat;

					var targetGroups = new Dictionary<double, int>();
					var stopGroups = new Dictionary<double, int>();

					foreach (Order order in currentAccount.Orders)
					{
						if (order.Instrument.FullName != Instrument.FullName) continue;
						if (order.OrderState != OrderState.Working && order.OrderState != OrderState.Accepted && 
							order.OrderState != OrderState.TriggerPending && order.OrderState != OrderState.Submitted)
							continue;

						bool isStop = (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit || 
									   order.Name.ToLower().Contains("stop") || order.Name.ToLower().Contains("stp"));
						
						double price = isStop ? (order.StopPrice > 0 ? order.StopPrice : order.LimitPrice) : (order.LimitPrice > 0 ? order.LimitPrice : order.StopPrice);
						if (price <= 0) continue; 

						if (isStop) { if (stopGroups.ContainsKey(price)) stopGroups[price] += order.Quantity; else stopGroups[price] = order.Quantity; }
						else { if (targetGroups.ContainsKey(price)) targetGroups[price] += order.Quantity; else targetGroups[price] = order.Quantity; }
					}

					foreach (var kvp in targetGroups) DrawLabel(chartControl, chartScale, kvp.Key, kvp.Value, false, posQuantity, entryPrice, marketPos);
					foreach (var kvp in stopGroups) DrawLabel(chartControl, chartScale, kvp.Key, kvp.Value, true, posQuantity, entryPrice, marketPos);
				}
			} catch { }
		}

		private void DrawLabel(ChartControl chartControl, ChartScale chartScale, double orderPrice, int qty, bool isStop, int posQuantity, double entryPrice, MarketPosition marketPos)
		{
			string typePrefix = isStop ? string.Format("STP({0})", qty) : string.Format("TGT({0})", qty);
			SolidColorBrush bgBrush = isStop ? stopBgBrush : targetBgBrush;
			SolidColorBrush txtBrush = isStop ? stopTxtBrush : targetTxtBrush;
			string displayStr = "";

			if (posQuantity > 0 && entryPrice > 0)
			{
				double diff = (marketPos == MarketPosition.Long) ? (orderPrice - entryPrice) : (entryPrice - orderPrice);
				int ticks = (int)Math.Round(diff / TickSize);
				double tickValue = TickSize * Instrument.MasterInstrument.PointValue;
				double netPnL = (ticks * tickValue * qty) - (qty * Commission);
				string sign = netPnL < 0 ? "-" : "";
				displayStr = string.Format("{0} | {1} T | {2}${3:N2}", typePrefix, ticks, sign, Math.Abs(netPnL));
			}
			else { displayStr = string.Format("{0} | {1}", typePrefix, orderPrice); }
			
			float boxH = (float)MyFontSize + 14f; 
			float yPos = chartScale.GetYByValue(orderPrice);
			float xPos = (float)(chartControl.CanvasRight - XOffset); 
			
			Vector2 lineStart = new Vector2(xPos + (float)LabelWidth, yPos + YOffset);
			Vector2 lineEnd = new Vector2(chartControl.CanvasRight, yPos + YOffset);
			RenderTarget.DrawLine(lineStart, lineEnd, bgBrush, 2.0f);

			RectangleF rect = new RectangleF(xPos, yPos - (boxH / 2f) + YOffset, (float)LabelWidth, boxH);
			RenderTarget.FillRectangle(rect, bgBrush);
			RenderTarget.DrawRectangle(rect, outlineBrush, 1.0f); 
			
			TextLayout layout = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, displayStr, textFormat, (float)LabelWidth, boxH);
			RenderTarget.DrawTextLayout(new Vector2(xPos, yPos - (boxH / 2f) + YOffset), layout, txtBrush);
			layout.Dispose();
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name="1. X Offset (Lùi trái)", Order=1, GroupName="Giao diện")]
		public int XOffset { get; set; }

		[NinjaScriptProperty]
		[Display(Name="2. Y Offset (Chỉnh dọc)", Order=2, GroupName="Giao diện")]
		public int YOffset { get; set; }

		[NinjaScriptProperty]
		[Display(Name="3. Độ rộng nhãn", Order=3, GroupName="Giao diện")]
		public int LabelWidth { get; set; }

		[NinjaScriptProperty]
		[Range(8, 72)]
		[Display(Name="4. Cỡ chữ (Font Size)", Order=4, GroupName="Giao diện")]
		public int MyFontSize { get; set; }

		[NinjaScriptProperty]
		[Display(Name="5. Phí giao dịch (1 Hợp đồng)", Order=5, GroupName="Giao dịch")]
		public double Commission { get; set; }

		[XmlIgnore]
		[Display(Name="1. Màu nền TGT", Order=1, GroupName="Màu sắc")]
		public System.Windows.Media.Brush TargetColor { get; set; }
		[Browsable(false)] public string TargetColorSerializable { get { return Serialize.BrushToString(TargetColor); } set { TargetColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name="2. Màu chữ TGT", Order=2, GroupName="Màu sắc")]
		public System.Windows.Media.Brush TargetFontColor { get; set; }
		[Browsable(false)] public string TargetFontColorSerializable { get { return Serialize.BrushToString(TargetFontColor); } set { TargetFontColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name="3. Màu nền STP", Order=3, GroupName="Màu sắc")]
		public System.Windows.Media.Brush StopColor { get; set; }
		[Browsable(false)] public string StopColorSerializable { get { return Serialize.BrushToString(StopColor); } set { StopColor = Serialize.StringToBrush(value); } }

		[XmlIgnore]
		[Display(Name="4. Màu chữ STP", Order=4, GroupName="Màu sắc")]
		public System.Windows.Media.Brush StopFontColor { get; set; }
		[Browsable(false)] public string StopFontColorSerializable { get { return Serialize.BrushToString(StopFontColor); } set { StopFontColor = Serialize.StringToBrush(value); } }
		#endregion
	}
}

public static class ColorExtensions
{
	public static SharpDX.Color ToSharpDXColor(this System.Windows.Media.Brush brush)
	{
		if (brush is System.Windows.Media.SolidColorBrush)
		{
			var color = (brush as System.Windows.Media.SolidColorBrush).Color;
			return new SharpDX.Color(color.R, color.G, color.B, color.A);
		}
		return SharpDX.Color.White;
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private CustomOrderLabels[] cacheCustomOrderLabels;
		public CustomOrderLabels CustomOrderLabels(int xOffset, int yOffset, int labelWidth, int myFontSize, double commission)
		{
			return CustomOrderLabels(Input, xOffset, yOffset, labelWidth, myFontSize, commission);
		}

		public CustomOrderLabels CustomOrderLabels(ISeries<double> input, int xOffset, int yOffset, int labelWidth, int myFontSize, double commission)
		{
			if (cacheCustomOrderLabels != null)
				for (int idx = 0; idx < cacheCustomOrderLabels.Length; idx++)
					if (cacheCustomOrderLabels[idx] != null && cacheCustomOrderLabels[idx].XOffset == xOffset && cacheCustomOrderLabels[idx].YOffset == yOffset && cacheCustomOrderLabels[idx].LabelWidth == labelWidth && cacheCustomOrderLabels[idx].MyFontSize == myFontSize && cacheCustomOrderLabels[idx].Commission == commission && cacheCustomOrderLabels[idx].EqualsInput(input))
						return cacheCustomOrderLabels[idx];
			return CacheIndicator<CustomOrderLabels>(new CustomOrderLabels(){ XOffset = xOffset, YOffset = yOffset, LabelWidth = labelWidth, MyFontSize = myFontSize, Commission = commission }, input, ref cacheCustomOrderLabels);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.CustomOrderLabels CustomOrderLabels(int xOffset, int yOffset, int labelWidth, int myFontSize, double commission)
		{
			return indicator.CustomOrderLabels(Input, xOffset, yOffset, labelWidth, myFontSize, commission);
		}

		public Indicators.CustomOrderLabels CustomOrderLabels(ISeries<double> input , int xOffset, int yOffset, int labelWidth, int myFontSize, double commission)
		{
			return indicator.CustomOrderLabels(input, xOffset, yOffset, labelWidth, myFontSize, commission);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.CustomOrderLabels CustomOrderLabels(int xOffset, int yOffset, int labelWidth, int myFontSize, double commission)
		{
			return indicator.CustomOrderLabels(Input, xOffset, yOffset, labelWidth, myFontSize, commission);
		}

		public Indicators.CustomOrderLabels CustomOrderLabels(ISeries<double> input , int xOffset, int yOffset, int labelWidth, int myFontSize, double commission)
		{
			return indicator.CustomOrderLabels(input, xOffset, yOffset, labelWidth, myFontSize, commission);
		}
	}
}

#endregion
