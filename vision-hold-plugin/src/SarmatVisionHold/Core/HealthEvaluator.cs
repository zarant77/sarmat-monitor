using System;
namespace SarmatVisionHold.Core
{
 public static class HealthEvaluator
 { static bool F(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
  public static bool ValidHeight(double h)=>F(h)&&h>0;
  public static bool Fresh(DateTime sample,IClock clock,int maxAgeMs)=>sample!=default(DateTime)&&(clock.UtcNow-sample.ToUniversalTime()).TotalMilliseconds>=0&&(clock.UtcNow-sample.ToUniversalTime()).TotalMilliseconds<=maxAgeMs;
  public static HealthSnapshot Evaluate(bool requested,bool pilotRequested,bool stream,FlowSample flow,TelemetrySample telemetry,VisionHoldSettings s,IClock clock)
  {var frames=flow!=null&&Fresh(flow.TimestampUtc,clock,s.MaxFrameAgeMs);var rc=telemetry!=null&&telemetry.RcPwm>0&&Fresh(telemetry.TimestampUtc,clock,s.RcStaleMs);var height=telemetry!=null&&telemetry.HeightValid&&ValidHeight(telemetry.HeightMeters);var quality=FlowQualityEstimator.Pass(flow,s);var h=new HealthSnapshot{Requested=requested,PilotRequested=pilotRequested,StreamWorking=stream,FramesFresh=frames,FlowGood=quality,HeightValid=height,RcFresh=rc,MavlinkActive=telemetry!=null&&telemetry.LinkActive,LiveAllowed=VisionHoldSettings.MavlinkTransmissionCompiled&&!s.DiagnosticsOnly&&s.EnableLiveControl};h.BlockReason=Reason(h);return h;}
  public static string Reason(HealthSnapshot h){if(!h.RcFresh)return "RC telemetry stale or unavailable";if(!h.StreamWorking)return "RTSP unavailable";if(!h.FramesFresh)return "Frame stale";if(!h.FlowGood)return "Flow quality/FPS below minimum";if(!h.HeightValid)return "Relative height invalid";if(!h.MavlinkActive)return "MAVLink unavailable";return null;}
 }
}
