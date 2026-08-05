using System;
using System.Linq;
using SarmatVisionHold.Core;
using SarmatVisionHold.Replay.Camera;
using SarmatVisionHold.Replay.Math;
using SarmatVisionHold.Replay.Processing;
using SarmatVisionHold.Vision;

namespace SarmatVisionHold.Live
{
 public sealed class LiveDiagnosticResult
 {
  public OpticalFlowRadModel Model;
  public RotationCompensationResult Compensation;
  public string State;
  public string Reason;
 }

 public sealed class LiveOpticalFlowDiagnosticPipeline
 {
  readonly RotationCompensator compensator=new RotationCompensator();
  readonly OpticalFlowRadBuilder builder=new OpticalFlowRadBuilder(new OpticalFlowRadBuilderOptions{EmitDegraded=false});
  TelemetrySample previousTelemetry;DateTime previousFrameUtc;long frameNumber;

  public LiveDiagnosticResult Process(FlowSample flow,TelemetrySample telemetry,VisionHoldSettings settings)
  {
   frameNumber++;
   if(flow==null||telemetry==null||settings==null)return Reject("missing_input");
   if(flow.Dt<=0||flow.Dt>.25){ResetTimeline(telemetry,flow.TimestampUtc);return Reject("frame_gap");}
   if(flow.FrameWidth<=0||flow.FrameHeight<=0)return Reject("invalid_resolution");
   if(!telemetry.GyroValid||!telemetry.AttitudeValid){Remember(telemetry,flow.TimestampUtc);return Reject("imu_unavailable");}
   if(previousTelemetry==null||previousFrameUtc==default(DateTime)){Remember(telemetry,flow.TimestampUtc);return Reject("warming_up");}
   if(flow.Tracks==null||flow.Tracks.Count==0){Remember(telemetry,flow.TimestampUtc);return Reject("tracking_lost");}

   CameraIntrinsics intrinsics;
   try{intrinsics=CameraIntrinsics.FromFov(flow.FrameWidth,flow.FrameHeight,settings.HorizontalFovDegrees,settings.VerticalFovDegrees);}
   catch{Remember(telemetry,flow.TimestampUtc);return Reject("invalid_intrinsics");}
   var mount=new CameraMount(Rad(settings.CameraMountRollDegrees),Rad(settings.CameraMountPitchDegrees),Rad(settings.CameraMountYawDegrees));
   var previousAttitude=Quaterniond.FromEuler(previousTelemetry.RollRad,previousTelemetry.PitchRad,previousTelemetry.YawRad);
   var currentAttitude=Quaterniond.FromEuler(telemetry.RollRad,telemetry.PitchRad,telemetry.YawRad);
   var averageBodyRate=new Vector3d((previousTelemetry.RollRate+telemetry.RollRate)*.5,(previousTelemetry.PitchRate+telemetry.PitchRate)*.5,(previousTelemetry.YawRate+telemetry.YawRate)*.5);
   var integratedCameraGyro=mount.BodyRateToCamera(averageBodyRate)*flow.Dt;
   var tracks=flow.Tracks.Where(x=>x!=null&&x.Accepted).Select(x=>new ReplayFlowTrack{FromX=x.FromX,FromY=x.FromY,ToX=x.ToX,ToY=x.ToY,Accepted=true}).ToList();
   var compensation=compensator.Compensate(tracks,intrinsics,mount.CameraToWorld(previousAttitude),mount.CameraToWorld(currentAttitude),integratedCameraGyro,RotationCompensationMode.Comparison);
   if(compensation.Vectors.Count==0){Remember(telemetry,flow.TimestampUtc);return Reject("compensation_failed",compensation);}

   AngularFlowSample angular;
   try{angular=FlowRadConverter.Convert(compensation.CompensatedFlowX,compensation.CompensatedFlowY,integratedCameraGyro,flow.Dt,intrinsics);}
   catch{Remember(telemetry,flow.TimestampUtc);return Reject("flow_conversion_failed",compensation);}
   flow.CompensatedPixelsX=compensation.CompensatedFlowX;flow.CompensatedPixelsY=compensation.CompensatedFlowY;flow.CompensationResidualPixels=compensation.ResidualPixels;flow.CompensatedX=angular.IntegratedFlowXRad;flow.CompensatedY=angular.IntegratedFlowYRad;

   var telemetryAge=Math.Max(0,(flow.TimestampUtc-telemetry.TimestampUtc.ToUniversalTime()).TotalSeconds);
   var distanceAge=telemetry.HeightTimestampUtc==default(DateTime)?double.PositiveInfinity:Math.Max(0,(flow.TimestampUtc-telemetry.HeightTimestampUtc.ToUniversalTime()).TotalSeconds);
   var quality=ReplayQualityMapper.Map(new QualityInput{TrackedPoints=flow.TrackedPoints,InlierCount=flow.InlierCount,ForwardBackwardError=flow.MedianForwardBackwardError,CompensationResidualPixels=compensation.ResidualPixels,FrameAgeSeconds=Math.Max(0,flow.FrameAgeMs/1000),TelemetryAgeSeconds=telemetryAge,ImuValid=telemetry.GyroValid&&telemetry.AttitudeValid,AltitudeValid=telemetry.HeightValid,SyncConfidence=compensation.Confidence,BlurTextureScore=flow.Quality});
   var status=quality==0||flow.InlierCount<=0?FlowTrackingStatus.LOST:quality<64?FlowTrackingStatus.DEGRADED:FlowTrackingStatus.OK;
   var model=builder.Build(new OpticalFlowRadBuildInput{TimeUsec=UnixMicroseconds(flow.TimestampUtc),SensorId=0,Flow=angular,TrackingStatus=status,Quality=quality,DistanceMeters=telemetry.HeightMeters,DistanceAgeSeconds=distanceAge,MaximumDistanceAgeSeconds=.5,SourceFrame=frameNumber,VideoTimestampSeconds=UnixSeconds(flow.TimestampUtc),TelemetryTimestampSeconds=UnixSeconds(telemetry.TimestampUtc)});
   Remember(telemetry,flow.TimestampUtc);
   return new LiveDiagnosticResult{Model=model,Compensation=compensation,State=model.Publishable?"DIAGNOSTIC_OK":"DIAGNOSTIC_REJECTED",Reason=model.RejectReason??compensation.Reason};
  }

  public void Reset(){previousTelemetry=null;previousFrameUtc=default(DateTime);builder.Reset();frameNumber=0;}
  void ResetTimeline(TelemetrySample telemetry,DateTime frameUtc){builder.Reset();Remember(telemetry,frameUtc);}
  void Remember(TelemetrySample telemetry,DateTime frameUtc){previousTelemetry=Copy(telemetry);previousFrameUtc=frameUtc;}
  LiveDiagnosticResult Reject(string reason,RotationCompensationResult compensation=null)=>new LiveDiagnosticResult{Model=new OpticalFlowRadModel{Publishable=false,RejectReason=reason,Distance=-1,Temperature=OpticalFlowRadBuilder.UnknownTemperature,SourceFrame=frameNumber},Compensation=compensation,State="DIAGNOSTIC_REJECTED",Reason=reason};
  static TelemetrySample Copy(TelemetrySample t)=>new TelemetrySample{TimestampUtc=t.TimestampUtc,RollRate=t.RollRate,PitchRate=t.PitchRate,YawRate=t.YawRate,RollRad=t.RollRad,PitchRad=t.PitchRad,YawRad=t.YawRad,AttitudeValid=t.AttitudeValid,GyroValid=t.GyroValid,HeightMeters=t.HeightMeters,HeightValid=t.HeightValid,HeightTimestampUtc=t.HeightTimestampUtc,HeightSource=t.HeightSource};
  static double Rad(double degrees)=>degrees*Math.PI/180;
  static double UnixSeconds(DateTime value){if(value==default(DateTime))return 0;return(value.ToUniversalTime()-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;}
  static ulong UnixMicroseconds(DateTime value){var seconds=UnixSeconds(value);return seconds>0&&seconds<ulong.MaxValue/1e6?(ulong)Math.Round(seconds*1e6):0;}
 }

 public static class FlightOutputSafety
 {
  public const bool MavlinkTransmissionEnabled=false;
  public static void DemandDiagnosticsOnly(){if(MavlinkTransmissionEnabled||VisionHoldSettings.MavlinkTransmissionCompiled)throw new InvalidOperationException("This build must remain diagnostics-only.");}
 }
}
