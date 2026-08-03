using System;using System.Drawing;using System.Windows.Forms;
namespace SarmatVisionHold.UI
{
 public sealed class VisionHoldPanel:UserControl
 { readonly VisionHoldRuntime runtime;readonly Label values=new Label();readonly Button toggle=new Button();readonly CheckBox diagnostics=new CheckBox();
  public VisionHoldPanel(VisionHoldRuntime r){runtime=r;Dock=DockStyle.Fill;AutoScroll=true;var title=new Label{Text="Sarmat Vision Hold",Font=new Font(SystemFonts.MessageBoxFont.FontFamily,14,FontStyle.Bold),Dock=DockStyle.Top,Height=36};toggle.Text="Manual activation (test)";toggle.Dock=DockStyle.Top;toggle.Height=34;toggle.Click+=(s,e)=>{runtime.SetManual(!runtime.Requested);RefreshValues();};diagnostics.Text="Diagnostics only";diagnostics.Checked=r.Settings.DiagnosticsOnly;diagnostics.Dock=DockStyle.Top;diagnostics.CheckedChanged+=(s,e)=>r.Settings.DiagnosticsOnly=diagnostics.Checked;values.Dock=DockStyle.Fill;values.Font=new Font(FontFamily.GenericMonospace,9);values.Padding=new Padding(8);Controls.Add(values);Controls.Add(toggle);Controls.Add(diagnostics);Controls.Add(title);runtime.Updated+=OnUpdated;RefreshValues();}
  void OnUpdated(){if(IsDisposed)return;if(InvokeRequired)BeginInvoke((Action)RefreshValues);else RefreshValues();}
  void RefreshValues(){var f=runtime.LastFlow;var t=runtime.LastTelemetry;values.Text=$"State: {runtime.State}\r\nBlock reason: {runtime.Reason}\r\nRC{runtime.Settings.RcChannel}: {t?.RcPwm ?? 0} ({(runtime.Requested?"ON":"OFF")})\r\nFPS: {f?.Fps ?? 0:F1}\r\nFrame age: {f?.FrameAgeMs ?? 0:F0} ms\r\nTracked points: {f?.TrackedPoints ?? 0}\r\nFlow quality: {f?.Quality ?? 0:F2}\r\nRaw X/Y: {f?.RawX ?? 0:F5} / {f?.RawY ?? 0:F5} rad\r\nCompensated X/Y: {f?.CompensatedX ?? 0:F5} / {f?.CompensatedY ?? 0:F5} rad\r\nVelocity X/Y: {f?.VelocityX ?? 0:F2} / {f?.VelocityY ?? 0:F2} m/s\r\nHeight: {t?.HeightMeters ?? 0:F2} m\r\nLive control: {(runtime.Settings.EnableLiveControl?"ENABLED":"LOCKED")}";}
  protected override void Dispose(bool disposing){if(disposing)runtime.Updated-=OnUpdated;base.Dispose(disposing);}
 }
}
