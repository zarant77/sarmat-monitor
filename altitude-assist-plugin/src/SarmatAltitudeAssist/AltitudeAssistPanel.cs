using System;using System.Drawing;using System.Windows.Forms;using SarmatAltitudeAssist.Core;
namespace SarmatAltitudeAssist
{
 sealed class AltitudeAssistPanel:UserControl
 {
  static readonly Color Bg=Color.FromArgb(245,245,245),TextColor=Color.FromArgb(25,25,25);readonly AltitudeAssistRuntime runtime;readonly Label status=new Label();readonly NumericUpDown working=new NumericUpDown(),descent=new NumericUpDown();
  public AltitudeAssistPanel(AltitudeAssistRuntime r)
  {runtime=r;Dock=DockStyle.Fill;AutoScroll=true;BackColor=Bg;ForeColor=TextColor;
   var settings=new TableLayoutPanel{Dock=DockStyle.Top,Height=68,ColumnCount=2,RowCount=2,BackColor=Bg,Padding=new Padding(6,6,6,4)};settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));settings.RowStyles.Add(new RowStyle(SizeType.Percent,50));settings.RowStyles.Add(new RowStyle(SizeType.Percent,50));Configure(working,2,10000,(decimal)r.Settings.WorkingAltitudeMeters);Configure(descent,1,9999,(decimal)r.Settings.DescentAltitudeMeters);working.ValueChanged+=(s,e)=>ApplySettings();descent.ValueChanged+=(s,e)=>ApplySettings();settings.Controls.Add(FieldLabel("Working altitude, m"),0,0);settings.Controls.Add(working,1,0);settings.Controls.Add(FieldLabel("Descent altitude, m"),0,1);settings.Controls.Add(descent,1,1);
   var help=new Label{Dock=DockStyle.Top,Height=72,Padding=new Padding(8,5,8,5),ForeColor=Color.FromArgb(45,45,45),BackColor=Color.FromArgb(232,232,232),Font=new Font(SystemFonts.MessageBoxFont.FontFamily,8.5f),Text="Controls:\r\nThrottle UP ×3: auto climb    Throttle DOWN ×3: auto descent\r\nAny throttle movement during AUTO: cancel immediately"};
   var rule=new Panel{Dock=DockStyle.Top,Height=1,BackColor=Color.FromArgb(205,205,205)};
   status.Dock=DockStyle.Fill;status.Font=new Font(FontFamily.GenericMonospace,9);status.Padding=new Padding(8);status.ForeColor=TextColor;status.BackColor=Bg;Controls.Add(status);Controls.Add(rule);Controls.Add(help);Controls.Add(settings);r.Updated+=RefreshStatus;ApplySettings();RefreshStatus();}
  static Label FieldLabel(string text)=>new Label{Text=text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=TextColor,BackColor=Bg};
  static void Configure(NumericUpDown c,decimal min,decimal max,decimal value){c.Minimum=min;c.Maximum=max;c.Value=Math.Max(min,Math.Min(max,value));c.Dock=DockStyle.Fill;c.ForeColor=Color.Black;c.BackColor=Color.White;}
  void ApplySettings(){runtime.Settings.WorkingAltitudeMeters=(double)working.Value;runtime.Settings.DescentAltitudeMeters=(double)descent.Value;}
  void RefreshStatus(){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke((Action)RefreshStatus);}catch{}return;}var c=runtime.Controller;var t=c.LastTelemetry;var g=c.Gesture;status.Text=$"State: {StateText(c.State)}\r\nCurrent altitude: {t?.AltitudeMeters??0:F1} m\r\nTarget: {(c.Target.HasValue?c.Target.Value.ToString("F1")+" m":"—")}\r\nThrottle: {t?.StickNormalized??.5:F2}\r\nGesture: {GestureText(g.Direction)} {g.Count}/3\r\nFlight mode: {t?.FlightMode??"—"}\r\nLast event: {c.LastReason}";}
  static string StateText(AssistState state){switch(state){case AssistState.CLIMBING:return "AUTO CLIMB";case AssistState.DESCENDING:return "AUTO DESCENT";case AssistState.MANUAL_OVERRIDE:return "CANCELLED BY PILOT";case AssistState.FAILSAFE:return "FAILSAFE";case AssistState.HOLD:return "TARGET REACHED";default:return "IDLE";}}
  static string GestureText(GestureDirection direction)=>direction==GestureDirection.Up?"UP":direction==GestureDirection.Down?"DOWN":"NONE";
  protected override void Dispose(bool disposing){if(disposing)runtime.Updated-=RefreshStatus;base.Dispose(disposing);}
 }
}
