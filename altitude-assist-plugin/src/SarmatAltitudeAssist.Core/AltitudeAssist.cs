using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SarmatAltitudeAssist.Core
{
 public enum AssistState { IDLE, ARMING_AUTO, CLIMBING, DESCENDING, TARGET_REACHED, HOLD, MANUAL_OVERRIDE, CANCELLED, FAILSAFE }
 public enum GestureDirection { None, Up, Down }
 public interface IClock { DateTime UtcNow { get; } }
 public sealed class SystemClock:IClock { public DateTime UtcNow=>DateTime.UtcNow; }
 [DataContract] public sealed class AltitudeAssistSettings
 {
  [DataMember] public double WorkingAltitudeMeters=500; [DataMember] public double DescentAltitudeMeters=50;
  [DataMember] public int GestureWindowMs=1500; [DataMember] public int GesturePulseMinMs=70; [DataMember] public int GesturePulseMaxMs=500;
  [DataMember] public double StickHighThreshold=.75; [DataMember] public double StickLowThreshold=.25; [DataMember] public double StickNeutralMin=.40; [DataMember] public double StickNeutralMax=.60;
  [DataMember] public double TargetToleranceMeters=2; [DataMember] public double SlowdownDistanceMeters=20;
  [DataMember] public int MaximumAutoDurationSeconds=180; [DataMember] public int TelemetryTimeoutMs=500;
  [DataMember] public double MinimumActivationAltitudeMeters=3; [DataMember] public double MaxClimbCommand=.45; [DataMember] public double MaxDescentCommand=.35; [DataMember] public double MinimumUsefulCommand=.08;
  [DataMember] public int VerticalRcChannel=3; [DataMember] public int RcMin=1000; [DataMember] public int RcTrim=1500; [DataMember] public int RcMax=2000; [DataMember] public bool RcReversed=false;
  public string Validate(){if(!Finite(WorkingAltitudeMeters)||!Finite(DescentAltitudeMeters)||WorkingAltitudeMeters<=DescentAltitudeMeters||DescentAltitudeMeters<1||WorkingAltitudeMeters>10000)return "Invalid target altitudes";if(GesturePulseMinMs<20||GesturePulseMaxMs<=GesturePulseMinMs||GestureWindowMs<GesturePulseMaxMs||TelemetryTimeoutMs<100||TargetToleranceMeters<=0||SlowdownDistanceMeters<=TargetToleranceMeters)return "Invalid timing/controller settings";if(!(StickLowThreshold<StickNeutralMin&&StickNeutralMin<StickNeutralMax&&StickNeutralMax<StickHighThreshold))return "Invalid stick thresholds";return null;}
  static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
  public static AltitudeAssistSettings Load(string path){try{using(var s=File.OpenRead(path))return (AltitudeAssistSettings)new DataContractJsonSerializer(typeof(AltitudeAssistSettings)).ReadObject(s);}catch{return new AltitudeAssistSettings();}}
  public void Save(string path){Directory.CreateDirectory(Path.GetDirectoryName(path));using(var s=File.Create(path))new DataContractJsonSerializer(typeof(AltitudeAssistSettings)).WriteObject(s,this);}
 }
 public sealed class GestureStatus { public GestureDirection Direction;public int Count;public double AgeMs; }
 public sealed class TripleStickGestureRecognizer
 {
  enum Phase { NeedNeutral, InPulse, NeedRelease }
  readonly AltitudeAssistSettings s;readonly IClock clock;Phase phase=Phase.NeedNeutral;GestureDirection active=GestureDirection.None,sequence=GestureDirection.None;DateTime pulseStart,sequenceStart;int count;
  public TripleStickGestureRecognizer(AltitudeAssistSettings settings,IClock clock){s=settings;this.clock=clock;}
  public GestureStatus Status=>new GestureStatus{Direction=sequence,Count=count,AgeMs=count==0?0:(clock.UtcNow-sequenceStart).TotalMilliseconds};
  public GestureDirection Update(double value)
  {
   var now=clock.UtcNow;if(count>0&&(now-sequenceStart).TotalMilliseconds>s.GestureWindowMs)Reset();var neutral=value>=s.StickNeutralMin&&value<=s.StickNeutralMax;var direction=value>=s.StickHighThreshold?GestureDirection.Up:value<=s.StickLowThreshold?GestureDirection.Down:GestureDirection.None;
   if(phase==Phase.NeedNeutral){if(neutral)phase=Phase.InPulse;return GestureDirection.None;}
   if(phase==Phase.InPulse){if(direction!=GestureDirection.None){if(count>0&&direction!=sequence)Reset();active=direction;pulseStart=now;phase=Phase.NeedRelease;}return GestureDirection.None;}
   if(direction!=GestureDirection.None&&direction!=active){Reset();return GestureDirection.None;}if(!neutral)return GestureDirection.None;
   var ms=(now-pulseStart).TotalMilliseconds;phase=Phase.InPulse;if(ms<s.GesturePulseMinMs||ms>s.GesturePulseMaxMs){Reset();return GestureDirection.None;}if(count==0){sequence=active;sequenceStart=now;}count++;active=GestureDirection.None;if(count<3)return GestureDirection.None;var result=sequence;Reset();phase=Phase.InPulse;return result;
  }
  public void Reset(){phase=Phase.NeedNeutral;active=sequence=GestureDirection.None;count=0;}
 }
 public sealed class TelemetrySnapshot
 { public DateTime TimestampUtc;public bool Connected,Armed,Airborne,AltitudeValid,HeartbeatHealthy;public double AltitudeMeters,VerticalSpeed,StickNormalized;public int StickRaw;public string AltitudeSource,FlightMode; }
 public interface ITelemetrySource { TelemetrySnapshot Read(); }
 public interface IVerticalControlOutput { void Apply(double command);void Release();double LastCommand{get;}DateTime LastUpdateUtc{get;} }
 public sealed class NullVerticalControlOutput:IVerticalControlOutput { readonly IClock clock;public NullVerticalControlOutput(IClock c){clock=c;}public double LastCommand{get;private set;}public DateTime LastUpdateUtc{get;private set;}public void Apply(double c){LastCommand=0;LastUpdateUtc=clock.UtcNow;}public void Release(){LastCommand=0;LastUpdateUtc=clock.UtcNow;} }
 public sealed class AltitudeAssistController:IDisposable
 {
  static readonly HashSet<string> Modes=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"ALT_HOLD","LOITER","POSHOLD","GUIDED","BRAKE","SPORT"};
  readonly AltitudeAssistSettings s;readonly ITelemetrySource telemetry;readonly IVerticalControlOutput output;readonly IClock clock;readonly TripleStickGestureRecognizer gestures;DateTime started;bool requireNeutral;public AssistState State{get;private set;}=AssistState.IDLE;public TelemetrySnapshot LastTelemetry{get;private set;}public double? Target{get;private set;}public double DesiredCommand{get;private set;}public string LastReason{get;private set;}="";public GestureStatus Gesture=>gestures.Status;public bool OutputLocked=>true;
  public AltitudeAssistController(AltitudeAssistSettings settings,ITelemetrySource source,IVerticalControlOutput result,IClock time){s=settings;telemetry=source;output=result;clock=time;gestures=new TripleStickGestureRecognizer(settings,time);}
  public void Tick(){try{LastTelemetry=telemetry.Read();if(LastTelemetry==null){Fail("telemetry unavailable");return;}var auto=State==AssistState.CLIMBING||State==AssistState.DESCENDING;if(auto&&IsManual(LastTelemetry.StickNormalized)){Cancel("MANUAL_OVERRIDE",AssistState.MANUAL_OVERRIDE);requireNeutral=true;return;}if(requireNeutral){if(IsNeutral(LastTelemetry.StickNormalized)){requireNeutral=false;State=AssistState.IDLE;}return;}if(auto){var health=HealthError();if(health!=null){Fail(health);return;}if((clock.UtcNow-started).TotalSeconds>s.MaximumAutoDurationSeconds){Fail("auto timeout");return;}RunAuto();return;}var g=gestures.Update(LastTelemetry.StickNormalized);if(g!=GestureDirection.None)Start(g);}catch(Exception ex){Fail("control loop: "+ex.GetType().Name);}}
  public void Start(GestureDirection direction){LastTelemetry=LastTelemetry??telemetry.Read();var error=HealthError();if(error!=null){Reject(error);return;}if(direction==GestureDirection.Up){if(LastTelemetry.AltitudeMeters>=s.WorkingAltitudeMeters-s.TargetToleranceMeters){Reject("ALREADY AT WORKING ALTITUDE");return;}Target=s.WorkingAltitudeMeters;State=AssistState.CLIMBING;}else{if(LastTelemetry.AltitudeMeters<=s.DescentAltitudeMeters+s.TargetToleranceMeters){Reject("ALREADY AT DESCENT ALTITUDE");return;}Target=s.DescentAltitudeMeters;State=AssistState.DESCENDING;}started=clock.UtcNow;LastReason=State==AssistState.CLIMBING?"AUTO_CLIMB_STARTED":"AUTO_DESCENT_STARTED";gestures.Reset();}
  public void Cancel(string reason="CANCELLED",AssistState state=AssistState.CANCELLED){DesiredCommand=0;output.Release();Target=null;State=state;LastReason=reason;gestures.Reset();}
  void RunAuto(){var error=Target.Value-LastTelemetry.AltitudeMeters;if(Math.Abs(error)<=s.TargetToleranceMeters){DesiredCommand=0;output.Release();State=AssistState.TARGET_REACHED;LastReason="TARGET_REACHED";Target=null;State=AssistState.HOLD;return;}if(State==AssistState.CLIMBING&&error<0||State==AssistState.DESCENDING&&error>0){Fail("target crossed");return;}var max=error>0?s.MaxClimbCommand:s.MaxDescentCommand;var scale=Math.Min(1,Math.Abs(error)/s.SlowdownDistanceMeters);DesiredCommand=Math.Sign(error)*Math.Max(s.MinimumUsefulCommand,max*scale);output.Apply(DesiredCommand);}
  string HealthError(){if(s.Validate()!=null)return s.Validate();if(LastTelemetry==null||!LastTelemetry.Connected)return "vehicle disconnected";if(!LastTelemetry.HeartbeatHealthy)return "heartbeat lost";if(!LastTelemetry.Armed)return "vehicle disarmed";if(!LastTelemetry.Airborne||LastTelemetry.AltitudeMeters<s.MinimumActivationAltitudeMeters)return "vehicle not airborne";if(!LastTelemetry.AltitudeValid||double.IsNaN(LastTelemetry.AltitudeMeters)||double.IsInfinity(LastTelemetry.AltitudeMeters))return "invalid altitude";if((clock.UtcNow-LastTelemetry.TimestampUtc).TotalMilliseconds>s.TelemetryTimeoutMs)return "telemetry stale";if(!Modes.Contains(LastTelemetry.FlightMode??""))return "UNSUPPORTED FLIGHT MODE";return null;}
  bool IsNeutral(double x)=>x>=s.StickNeutralMin&&x<=s.StickNeutralMax;bool IsManual(double x)=>x<=s.StickLowThreshold||x>=s.StickHighThreshold;
  void Reject(string reason){Cancel(reason,AssistState.CANCELLED);}void Fail(string reason){Cancel("FAILSAFE: "+reason,AssistState.FAILSAFE);}public void Dispose(){try{Cancel("plugin shutdown");}finally{output.Release();}}
 }
}
