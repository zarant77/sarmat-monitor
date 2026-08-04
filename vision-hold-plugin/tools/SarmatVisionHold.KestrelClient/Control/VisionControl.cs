using System;
using Newtonsoft.Json;

namespace SarmatVisionHold.KestrelClient.Control {
 public enum VisionControlMode { OFF, DIAGNOSTICS, CONTROL }
 public enum VisionControlState { OFF, DIAGNOSTICS, ARMING, ACTIVE, PILOT_OVERRIDE, DEGRADED, LOST, DISCONNECTED, FAILSAFE, SATURATED, CALIBRATING }
 public enum VisionVelocityUnits { Pxps, Mps }

 public sealed class VisionControlOptions {
  public VisionControlMode Mode=VisionControlMode.DIAGNOSTICS; public VisionVelocityUnits VelocityUnits=VisionVelocityUnits.Mps;
  public double KpX=.06,KpY=.06,KiX=.025,KiY=.025,KdX=.012,KdY=.012;
  public double PositionKpX=.04,PositionKpY=.04,MaxPositionErrorMeters=30;
  public double MaxCommand=.75,MaxIntegralContribution=.35,IntegralClampX=20,IntegralClampY=20,IntegralLeakPerSecond;
  public double DeadbandVelocity=.08,MaxSlewRatePerSecond=3,MinimumQuality=.15,FlowLowPassAlpha=.35,DerivativeLowPassAlpha=.15;
  public double PilotOverrideDeadband=.08,RotationRejectThreshold=3,BackCalculationGain=.5;
  public int StaleFrameTimeoutMs=250,RecoveryGoodFrames=5,AcknowledgementTimeoutMs=500; public uint MaxFrameGap=10;
  public double CameraMountRotationDegrees; public bool RequireAcknowledgement=true,EnableControlCalibration,EnablePositionHold=true;
 }
 public sealed class VisionControlInput {
  public Guid SessionId; public uint FrameNumber; public ulong TimestampUs; public double DeltaSeconds,TextureFlowX,TextureFlowY,Quality,RotationDegrees,FrameAgeMs,PilotRoll,PilotPitch;
  public double AltitudeMeters,HorizontalFovDegrees,VerticalFovDegrees,YawDegrees; public int ImageWidth,ImageHeight;
  public bool Lost,Degraded,Paused,OnGround,Crashed,Connected=true;
 }
 public sealed class VisionControlOutput {
  public VisionControlState State; public double RotatedFlowX,RotatedFlowY,CameraMotionX,CameraMotionY,ErrorX,ErrorY;
  public double PTermX,PTermY,ITermX,ITermY,DTermX,DTermY,PositionX,PositionY,PositionTermX,PositionTermY,RawRoll,RawPitch,ClampedRoll,ClampedPitch,RollCommand,PitchCommand,QualityMultiplier;
  public bool Enabled,SlewLimited,Saturated,Acknowledged,IntegratorFrozen; public string Reason; public ulong SentSequence,AppliedSequence;
 }
 public sealed class VisionControlMessage {
  [JsonProperty("type")]public string Type="visionControl"; [JsonProperty("protocolVersion")]public int ProtocolVersion=1; [JsonProperty("sessionId")]public Guid SessionId;
  [JsonProperty("sourceFrameNumber")]public uint SourceFrameNumber; [JsonProperty("sourceTimestampUs")]public ulong SourceTimestampUs; [JsonProperty("commandSequence")]public ulong CommandSequence;
  [JsonProperty("enabled")]public bool Enabled; [JsonProperty("mode")]public string Mode="position_hold"; [JsonProperty("rollCommand")]public double RollCommand; [JsonProperty("pitchCommand")]public double PitchCommand;
  [JsonProperty("quality")]public double Quality; [JsonProperty("state")]public string State; [JsonProperty("reason")]public string Reason;
 }
 public sealed class VisionControlStatus {
  [JsonProperty("type")]public string Type; [JsonProperty("protocolVersion")]public int ProtocolVersion; [JsonProperty("sessionId")]public Guid SessionId;
  [JsonProperty("lastReceivedSequence")]public ulong LastReceivedSequence; [JsonProperty("lastAppliedSequence")]public ulong LastAppliedSequence; [JsonProperty("accepted")]public bool Accepted;
  [JsonProperty("appliedRollCommand")]public double AppliedRollCommand; [JsonProperty("appliedPitchCommand")]public double AppliedPitchCommand; [JsonProperty("pilotOverride")]public bool PilotOverride;
  [JsonProperty("controllerEnabled")]public bool ControllerEnabled; [JsonProperty("commandAgeMs")]public double CommandAgeMs; [JsonProperty("rejectReason")]public string RejectReason; [JsonIgnore]public DateTime ReceivedUtc;
  public static bool TryParse(string json,out VisionControlStatus status){status=null;try{var parsed=JsonConvert.DeserializeObject<VisionControlStatus>(json);if(parsed==null||parsed.Type!="visionControlStatus"||parsed.ProtocolVersion!=1||parsed.SessionId==Guid.Empty||!Finite(parsed.AppliedRollCommand)||!Finite(parsed.AppliedPitchCommand)||!Finite(parsed.CommandAgeMs))return false;parsed.ReceivedUtc=DateTime.UtcNow;status=parsed;return true;}catch{return false;}}static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
 }
 public sealed class VisionControlLowPassFilter { bool set;double value;public double Update(double x,double alpha){if(!set){value=x;set=true;}else value+=Clamp01(alpha)*(x-value);return value;}public void Reset(){set=false;value=0;}static double Clamp01(double x)=>Math.Max(0,Math.Min(1,x)); }
 public sealed class VisionControlRateLimiter { double value;public double Update(double target,double rate,double dt,out bool limited){var max=Math.Max(0,rate)*Math.Max(0,dt);var delta=target-value;limited=Math.Abs(delta)>max;if(limited)delta=Math.Sign(delta)*max;return value+=delta;}public void Reset(){value=0;} }
 public static class VisionCoordinateMapper {
  public static void RotateTextureFlow(double x,double y,double mountDegrees,out double rotatedX,out double rotatedY){var r=mountDegrees*Math.PI/180;var c=Math.Cos(r);var s=Math.Sin(r);rotatedX=c*x-s*y;rotatedY=s*x+c*y;}
  public static void TextureToCameraMotion(double x,double y,double mountDegrees,out double lateral,out double forward){double rx,ry;RotateTextureFlow(x,y,mountDegrees,out rx,out ry);lateral=-rx;forward=ry;}
  public static void ConvertVelocityUnits(VisionControlInput i,VisionVelocityUnits units,ref double x,ref double y){if(units!=VisionVelocityUnits.Mps)return;if(i.AltitudeMeters<=0||i.ImageWidth<=0||i.ImageHeight<=0||i.HorizontalFovDegrees<=0)return;var hf=i.HorizontalFovDegrees*Math.PI/180;var vf=i.VerticalFovDegrees>0?i.VerticalFovDegrees*Math.PI/180:2*Math.Atan(Math.Tan(hf/2)*i.ImageHeight/i.ImageWidth);x*=2*i.AltitudeMeters*Math.Tan(hf/2)/i.ImageWidth;y*=2*i.AltitudeMeters*Math.Tan(vf/2)/i.ImageHeight;}
 }

 public sealed class VisionVelocityDampingController {
  readonly VisionControlOptions o; readonly VisionControlLowPassFilter fx=new VisionControlLowPassFilter(),fy=new VisionControlLowPassFilter(),dx=new VisionControlLowPassFilter(),dy=new VisionControlLowPassFilter(); readonly VisionControlRateLimiter rx=new VisionControlRateLimiter(),ry=new VisionControlRateLimiter();
  Guid session;uint frame;ulong timestamp,sequence;double previousErrorX,previousErrorY,integralX,integralY,holdPositionWorldX,holdPositionWorldZ;int good,totalOutputs,saturatedOutputs;VisionControlStatus acknowledgement;ulong observedAppliedSequence;DateTime commandStreamStartedUtc,lastAppliedProgressUtc;
  public VisionControlMode Mode{get;private set;} public double IntegralX=>integralX;public double IntegralY=>integralY;public double SaturationPercentage=>totalOutputs==0?0:100d*saturatedOutputs/totalOutputs;public ulong LastSentSequence=>sequence;public ulong LastAppliedSequence=>acknowledgement?.LastAppliedSequence??0;
  public VisionVelocityDampingController(VisionControlOptions options){o=options??throw new ArgumentNullException(nameof(options));Mode=o.Mode;}
  public void SetMode(VisionControlMode mode){Mode=mode;if(mode!=VisionControlMode.CONTROL)Reset();}
  public void UpdateAcknowledgement(VisionControlStatus status){if(status==null||status.ProtocolVersion!=1||status.SessionId!=session||!Finite(status.AppliedRollCommand)||!Finite(status.AppliedPitchCommand))return;status.ReceivedUtc=DateTime.UtcNow;acknowledgement=status;if(status.Accepted&&status.ControllerEnabled&&status.LastAppliedSequence>observedAppliedSequence){observedAppliedSequence=status.LastAppliedSequence;lastAppliedProgressUtc=status.ReceivedUtc;}}
  public VisionControlOutput Update(VisionControlInput i){
   var bad=Failsafe(i);if(bad!=null){if(bad=="low_quality"||bad=="rotation_rejected")return Release(StateFor(bad),bad,false);return Release(StateFor(bad),bad,true);}
   var sessionChanged=session!=Guid.Empty&&session!=i.SessionId;var rollback=frame!=0&&(i.FrameNumber<=frame||i.TimestampUs<=timestamp);var gap=frame!=0&&i.FrameNumber-frame>o.MaxFrameGap;
   if(sessionChanged||rollback||gap){Reset();session=i.SessionId;if(rollback)return Release(VisionControlState.FAILSAFE,"source_rollback",true);if(gap)return Release(VisionControlState.FAILSAFE,"frame_gap",true);}
   session=i.SessionId;frame=i.FrameNumber;timestamp=i.TimestampUs;
   if(Math.Abs(i.PilotRoll)>o.PilotOverrideDeadband||Math.Abs(i.PilotPitch)>o.PilotOverrideDeadband)return Release(VisionControlState.PILOT_OVERRIDE,"pilot_override",true);
   good++;if(good<o.RecoveryGoodFrames)return Release(VisionControlState.ARMING,"recovering",false);
   var acknowledged=HasFreshAppliedAcknowledgement();if(Mode==VisionControlMode.CONTROL&&o.RequireAcknowledgement&&sequence>0&&AcknowledgementTimedOut())return Release(VisionControlState.FAILSAFE,"command_not_applied",false);
   double rotatedX,rotatedY;VisionCoordinateMapper.RotateTextureFlow(i.TextureFlowX,i.TextureFlowY,o.CameraMountRotationDegrees,out rotatedX,out rotatedY);var cameraX=-rotatedX;var cameraY=rotatedY;VisionCoordinateMapper.ConvertVelocityUnits(i,o.VelocityUnits,ref cameraX,ref cameraY);
   cameraX=fx.Update(cameraX,o.FlowLowPassAlpha);cameraY=fy.Update(cameraY,o.FlowLowPassAlpha);var errorX=Dead(-cameraX);var errorY=Dead(-cameraY);var dt=i.DeltaSeconds;
   var positionVelocityX=cameraX;var positionVelocityY=cameraY;if(o.VelocityUnits!=VisionVelocityUnits.Mps)VisionCoordinateMapper.ConvertVelocityUnits(i,VisionVelocityUnits.Mps,ref positionVelocityX,ref positionVelocityY);var yaw=i.YawDegrees*Math.PI/180;var cy=Math.Cos(yaw);var sy=Math.Sin(yaw);if(o.EnablePositionHold&&!i.Degraded){holdPositionWorldX=Clamp(holdPositionWorldX+(positionVelocityX*cy+positionVelocityY*sy)*dt,o.MaxPositionErrorMeters);holdPositionWorldZ=Clamp(holdPositionWorldZ+(positionVelocityX*sy-positionVelocityY*cy)*dt,o.MaxPositionErrorMeters);}var positionBodyX=holdPositionWorldX*cy+holdPositionWorldZ*sy;var positionBodyY=holdPositionWorldX*sy-holdPositionWorldZ*cy;var positionTermX=o.EnablePositionHold?Clamp(-o.PositionKpX*positionBodyX,o.MaxCommand):0;var positionTermY=o.EnablePositionHold?Clamp(-o.PositionKpY*positionBodyY,o.MaxCommand):0;
   var derivativeX=dx.Update((errorX-previousErrorX)/dt,o.DerivativeLowPassAlpha);var derivativeY=dy.Update((errorY-previousErrorY)/dt,o.DerivativeLowPassAlpha);previousErrorX=errorX;previousErrorY=errorY;
   var freeze=i.Degraded;var candidateX=Leak(integralX,dt)+errorX*dt;var candidateY=Leak(integralY,dt)+errorY*dt;candidateX=Clamp(candidateX,o.IntegralClampX);candidateY=Clamp(candidateY,o.IntegralClampY);
   var pX=o.KpX*errorX;var pY=o.KpY*errorY;var dX=o.KdX*derivativeX;var dY=o.KdY*derivativeY;var oldIX=Clamp(o.KiX*integralX,o.MaxIntegralContribution);var oldIY=Clamp(o.KiY*integralY,o.MaxIntegralContribution);var newIX=Clamp(o.KiX*candidateX,o.MaxIntegralContribution);var newIY=Clamp(o.KiY*candidateY,o.MaxIntegralContribution);
   var proposedX=pX+newIX+dX+positionTermX;var proposedY=pY+newIY+dY+positionTermY;var saturatedX=Math.Abs(proposedX)>o.MaxCommand;var saturatedY=Math.Abs(proposedY)>o.MaxCommand;
   if(!freeze&&(!saturatedX||Math.Sign(errorX)!=Math.Sign(proposedX)))integralX=candidateX;else if(saturatedX&&o.BackCalculationGain>0&&o.KiX!=0)integralX=Clamp(integralX+o.BackCalculationGain*(Clamp(proposedX,o.MaxCommand)-proposedX)*dt/o.KiX,o.IntegralClampX);
   if(!freeze&&(!saturatedY||Math.Sign(errorY)!=Math.Sign(proposedY)))integralY=candidateY;else if(saturatedY&&o.BackCalculationGain>0&&o.KiY!=0)integralY=Clamp(integralY+o.BackCalculationGain*(Clamp(proposedY,o.MaxCommand)-proposedY)*dt/o.KiY,o.IntegralClampY);
   var iX=freeze?oldIX:Clamp(o.KiX*integralX,o.MaxIntegralContribution);var iY=freeze?oldIY:Clamp(o.KiY*integralY,o.MaxIntegralContribution);var rawX=pX+iX+dX+positionTermX;var rawY=pY+iY+dY+positionTermY;var quality=Quality(i.Quality);var clampedX=Clamp(rawX*quality,o.MaxCommand);var clampedY=Clamp(rawY*quality,o.MaxCommand);bool limitedX,limitedY;var roll=rx.Update(clampedX,o.MaxSlewRatePerSecond,dt,out limitedX);var pitch=ry.Update(clampedY,o.MaxSlewRatePerSecond,dt,out limitedY);var saturated=saturatedX||saturatedY;totalOutputs++;if(saturated)saturatedOutputs++;
   var state=Mode==VisionControlMode.DIAGNOSTICS?VisionControlState.DIAGNOSTICS:i.Degraded?VisionControlState.DEGRADED:saturated?VisionControlState.SATURATED:VisionControlState.ACTIVE;
   return new VisionControlOutput{State=state,Enabled=Mode==VisionControlMode.CONTROL,RotatedFlowX=rotatedX,RotatedFlowY=rotatedY,CameraMotionX=cameraX,CameraMotionY=cameraY,ErrorX=errorX,ErrorY=errorY,PositionX=positionBodyX,PositionY=positionBodyY,PositionTermX=positionTermX,PositionTermY=positionTermY,PTermX=pX,PTermY=pY,ITermX=iX,ITermY=iY,DTermX=dX,DTermY=dY,RawRoll=rawX,RawPitch=rawY,ClampedRoll=clampedX,ClampedPitch=clampedY,RollCommand=Finite(roll)?roll:0,PitchCommand=Finite(pitch)?pitch:0,QualityMultiplier=quality,SlewLimited=limitedX||limitedY,Saturated=saturated,Acknowledged=acknowledged,IntegratorFrozen=freeze,AppliedSequence=LastAppliedSequence};
  }
  public VisionControlOutput CalibrationOutput(VisionControlInput i){var phase=(int)((i.TimestampUs/1000000)%8);var roll=phase<2?.08:phase<4?-.08:0;var pitch=phase>=4&&phase<6?.08:phase>=6?-.08:0;return new VisionControlOutput{State=VisionControlState.CALIBRATING,Enabled=true,RollCommand=roll,PitchCommand=pitch,RawRoll=roll,RawPitch=pitch,ClampedRoll=roll,ClampedPitch=pitch,Acknowledged=HasFreshAppliedAcknowledgement(),AppliedSequence=LastAppliedSequence,IntegratorFrozen=true};}
  public VisionControlMessage Message(VisionControlInput i,VisionControlOutput x){if(x.Enabled&&commandStreamStartedUtc==default(DateTime))commandStreamStartedUtc=DateTime.UtcNow;var message=new VisionControlMessage{SessionId=i.SessionId,SourceFrameNumber=i.FrameNumber,SourceTimestampUs=i.TimestampUs,CommandSequence=++sequence,Enabled=x.Enabled,RollCommand=x.Enabled?x.RollCommand:0,PitchCommand=x.Enabled?x.PitchCommand:0,Quality=Finite(i.Quality)?Math.Max(0,Math.Min(1,i.Quality)):0,State=x.State.ToString(),Reason=x.Reason};x.SentSequence=message.CommandSequence;return message;}
  public VisionControlOutput ExplicitRelease(string reason="release")=>Release(Mode==VisionControlMode.OFF?VisionControlState.OFF:VisionControlState.FAILSAFE,reason,true);
  public void Reset(){fx.Reset();fy.Reset();dx.Reset();dy.Reset();rx.Reset();ry.Reset();previousErrorX=previousErrorY=integralX=integralY=holdPositionWorldX=holdPositionWorldZ=0;good=0;frame=0;timestamp=0;acknowledgement=null;observedAppliedSequence=0;commandStreamStartedUtc=lastAppliedProgressUtc=default(DateTime);}
  bool HasFreshAppliedAcknowledgement(){return acknowledgement!=null&&acknowledgement.Accepted&&acknowledgement.ControllerEnabled&&acknowledgement.LastAppliedSequence>0&&acknowledgement.LastAppliedSequence<=sequence&&(DateTime.UtcNow-acknowledgement.ReceivedUtc).TotalMilliseconds<=o.AcknowledgementTimeoutMs;}
  bool AcknowledgementTimedOut(){var since=lastAppliedProgressUtc!=default(DateTime)?lastAppliedProgressUtc:commandStreamStartedUtc;return since!=default(DateTime)&&(DateTime.UtcNow-since).TotalMilliseconds>o.AcknowledgementTimeoutMs;}
  string Failsafe(VisionControlInput i){if(Mode==VisionControlMode.OFF)return "control_disabled";if(!i.Connected)return "disconnected";if(i.Paused)return "paused";if(i.OnGround)return "on_ground";if(i.Crashed)return "crashed";if(i.Lost)return "lost";if(!Finite(i.TextureFlowX)||!Finite(i.TextureFlowY)||!Finite(i.Quality)||!Finite(i.RotationDegrees)||!Finite(i.DeltaSeconds)||i.DeltaSeconds<=0||i.DeltaSeconds>.5)return "invalid_dt_or_value";if(i.FrameAgeMs>o.StaleFrameTimeoutMs)return "stale_frame";if(i.Quality<o.MinimumQuality)return "low_quality";if(Math.Abs(i.RotationDegrees)>o.RotationRejectThreshold)return "rotation_rejected";return null;}
  VisionControlOutput Release(VisionControlState state,string reason,bool resetIntegral){rx.Reset();ry.Reset();if(resetIntegral){integralX=integralY=previousErrorX=previousErrorY=holdPositionWorldX=holdPositionWorldZ=0;fx.Reset();fy.Reset();dx.Reset();dy.Reset();good=0;}return new VisionControlOutput{State=state,Reason=reason,AppliedSequence=LastAppliedSequence,IntegratorFrozen=!resetIntegral};}
  double Dead(double x)=>Math.Abs(x)<o.DeadbandVelocity?0:x;double Leak(double x,double dt)=>o.IntegralLeakPerSecond<=0?x:x*Math.Exp(-o.IntegralLeakPerSecond*dt);double Quality(double q)=>Math.Max(0,Math.Min(1,(q-o.MinimumQuality)/Math.Max(.0001,1-o.MinimumQuality)));static double Clamp(double x,double max)=>Math.Max(-Math.Abs(max),Math.Min(Math.Abs(max),x));static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);static VisionControlState StateFor(string r)=>r=="lost"?VisionControlState.LOST:r=="disconnected"?VisionControlState.DISCONNECTED:r=="low_quality"||r=="rotation_rejected"?VisionControlState.DEGRADED:r=="control_disabled"?VisionControlState.OFF:VisionControlState.FAILSAFE;
 }
}
