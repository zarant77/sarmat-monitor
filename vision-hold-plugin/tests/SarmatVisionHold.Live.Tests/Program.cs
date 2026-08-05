using System;
using System.Collections.Generic;
using System.Linq;
using SarmatVisionHold.Core;
using SarmatVisionHold.Live;
using SarmatVisionHold.Replay.Camera;

internal static class Program
{
 static int Main()
 {
  try{FlightOutputIsLocked();CorrectImageToMavlinkAxes();StaleImuIsRejected();LargeGapResetsTimeline();Console.WriteLine("All 4 live diagnostic test groups passed.");return 0;}
  catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
 static void FlightOutputIsLocked(){False(FlightOutputSafety.MavlinkTransmissionEnabled);False(VisionHoldSettings.MavlinkTransmissionCompiled);var settings=new VisionHoldSettings{DiagnosticsOnly=false,EnableLiveControl=true};settings.Normalize();True(settings.DiagnosticsOnly);False(settings.EnableLiveControl);FlightOutputSafety.DemandDiagnosticsOnly();Console.WriteLine("PASS FlightOutputIsLocked");}
 static void CorrectImageToMavlinkAxes()
 {
  var pipeline=new LiveOpticalFlowDiagnosticPipeline();var settings=Settings();var at=Utc();var telemetry=Telemetry(at);pipeline.Process(Flow(at,0,0),telemetry,settings);at=at.AddMilliseconds(50);telemetry=Telemetry(at);var result=pipeline.Process(Flow(at,10,5),telemetry,settings);True(result.Model.Publishable,result.Reason);var camera=CameraIntrinsics.FromFov(640,480,90);Near(Math.Atan2(5,camera.Fy),result.Model.IntegratedX,1e-6);Near(-Math.Atan2(10,camera.Fx),result.Model.IntegratedY,1e-6);Eq((uint)50000,result.Model.IntegrationTimeUs);Console.WriteLine("PASS CorrectImageToMavlinkAxes");
 }
 static void StaleImuIsRejected(){var pipeline=new LiveOpticalFlowDiagnosticPipeline();var settings=Settings();var at=Utc();pipeline.Process(Flow(at,0,0),Telemetry(at),settings);at=at.AddMilliseconds(50);var t=Telemetry(at);t.GyroValid=false;var result=pipeline.Process(Flow(at,1,1),t,settings);False(result.Model.Publishable);Eq("imu_unavailable",result.Reason);Console.WriteLine("PASS StaleImuIsRejected");}
 static void LargeGapResetsTimeline(){var pipeline=new LiveOpticalFlowDiagnosticPipeline();var settings=Settings();var at=Utc();pipeline.Process(Flow(at,0,0),Telemetry(at),settings);at=at.AddMilliseconds(300);var gap=Flow(at,1,1);gap.Dt=.3;var result=pipeline.Process(gap,Telemetry(at),settings);False(result.Model.Publishable);Eq("frame_gap",result.Reason);at=at.AddMilliseconds(50);result=pipeline.Process(Flow(at,1,1),Telemetry(at),settings);True(result.Model.Publishable,result.Reason);Console.WriteLine("PASS LargeGapResetsTimeline");}
 static VisionHoldSettings Settings(){var s=new VisionHoldSettings{HorizontalFovDegrees=90,VerticalFovDegrees=0,CameraMountPitchDegrees=-90};s.Normalize();return s;}
 static TelemetrySample Telemetry(DateTime at)=>new TelemetrySample{TimestampUtc=at,HeightTimestampUtc=at,LinkActive=true,AttitudeValid=true,GyroValid=true,HeightValid=true,HeightMeters=2};
 static FlowSample Flow(DateTime at,double dx,double dy)
 {
  var tracks=Enumerable.Range(0,80).Select(i=>new FlowTrackSample{FromX=80+i%10*40,FromY=60+i/10*40,ToX=80+i%10*40+dx,ToY=60+i/10*40+dy,Accepted=true,ForwardBackwardError=.1}).ToArray();
  return new FlowSample{TimestampUtc=at,Dt=.05,FrameWidth=640,FrameHeight=480,Fps=20,TrackedPoints=80,InlierCount=80,Quality=1,Tracks=tracks};
 }
 static DateTime Utc()=>new DateTime(2030,1,1,0,0,0,DateTimeKind.Utc);
 static void True(bool value,string message=null){if(!value)throw new Exception(message??"expected true");}static void False(bool value,string message=null)=>True(!value,message??"expected false");static void Eq<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"expected {expected}, got {actual}");}static void Near(double expected,double actual,double epsilon){if(double.IsNaN(actual)||Math.Abs(expected-actual)>epsilon)throw new Exception($"expected {expected}, got {actual}");}
}
