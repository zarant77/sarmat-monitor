using System;
using System.Drawing;
using System.Windows.Forms;

namespace SarmatVisionHold.UI
{
 public sealed class VisionHoldPanel:UserControl
 {
  readonly VisionHoldRuntime runtime;readonly Label values=new Label();readonly Button toggle=new Button();
  public VisionHoldPanel(VisionHoldRuntime runtime)
  {
   this.runtime=runtime;Dock=DockStyle.Fill;AutoScroll=true;var title=new Label{Text="Sarmat Vision Hold — diagnostics only",Font=new Font(SystemFonts.MessageBoxFont.FontFamily,14,FontStyle.Bold),Dock=DockStyle.Top,Height=36};var warning=new Label{Text="FLIGHT OUTPUT LOCKED: no MAVLink flow, EKF or mode commands are transmitted.",ForeColor=Color.DarkRed,Dock=DockStyle.Top,Height=34};toggle.Text="Enable diagnostic evaluation";toggle.Dock=DockStyle.Top;toggle.Height=34;toggle.Click+=(s,e)=>{runtime.SetManual(!runtime.Requested);RefreshValues();};values.Dock=DockStyle.Fill;values.Font=new Font(FontFamily.GenericMonospace,9);values.Padding=new Padding(8);Controls.Add(values);Controls.Add(toggle);Controls.Add(warning);Controls.Add(title);runtime.Updated+=OnUpdated;RefreshValues();
  }
  void OnUpdated(){if(IsDisposed)return;if(InvokeRequired)BeginInvoke((Action)RefreshValues);else RefreshValues();}
  void RefreshValues()
  {
   var f=runtime.LastFlow;var t=runtime.LastTelemetry;var d=runtime.LastDiagnostic;var m=d?.Model;
   values.Text=$"State: {runtime.State}\r\nBlock reason: {runtime.Reason}\r\nRC{runtime.Settings.RcChannel}: {t?.RcPwm??0} ({(runtime.Requested?"ON":"OFF")})\r\nFPS: {f?.Fps??0:F1}\r\nFrame age: {f?.FrameAgeMs??0:F0} ms\r\nTracked / inliers: {f?.TrackedPoints??0} / {f?.InlierCount??0}\r\nTracker quality: {f?.Quality??0:F2}\r\nRaw px X/Y: {f?.RawPixelsX??0:F3} / {f?.RawPixelsY??0:F3}\r\nCompensated px X/Y: {f?.CompensatedPixelsX??0:F3} / {f?.CompensatedPixelsY??0:F3}\r\nCompensation residual: {f?.CompensationResidualPixels??0:F3} px\r\nGyro valid: {t?.GyroValid??false}  attitude valid: {t?.AttitudeValid??false}\r\nRange: {t?.HeightMeters??0:F2} m ({t?.HeightSource??"none"})\r\nDiagnostic: {d?.State??"warming_up"} / {d?.Reason??"no sample"}\r\nOPTICAL_FLOW_RAD X/Y: {m?.IntegratedX??0:F6} / {m?.IntegratedY??0:F6}\r\nIntegrated gyro X/Y/Z: {m?.IntegratedXgyro??0:F6} / {m?.IntegratedYgyro??0:F6} / {m?.IntegratedZgyro??0:F6}\r\nIntegration: {m?.IntegrationTimeUs??0} us  quality: {m?.Quality??0}\r\nPublishable model: {m?.Publishable??false}\r\nMAVLink transmission: COMPILE-TIME LOCKED";
  }
  protected override void Dispose(bool disposing){if(disposing)runtime.Updated-=OnUpdated;base.Dispose(disposing);}
 }
}
