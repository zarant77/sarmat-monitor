using System;
using System.Reflection;
using SarmatVisionHold.Core;

namespace SarmatVisionHold.Integration
{
 public sealed class MissionPlannerVehicleGateway:IVehicleGateway
 {
  readonly Func<object> state;
  readonly Func<object> port;
  public MissionPlannerVehicleGateway(Func<object> state,Func<object> port){this.state=state;this.port=port;}

  public TelemetrySample ReadTelemetry(int channel)
  {
   var cs=state();var now=DateTime.UtcNow;var stamp=Date(cs,"datetime");if(stamp==default(DateTime))stamp=now;stamp=stamp.ToUniversalTime();var age=now-stamp;
   if(age<TimeSpan.Zero||age>TimeSpan.FromDays(1)){stamp=now;age=TimeSpan.Zero;}
   var range=D(cs,"sonarrange");var gx=D(cs,"gx");var gy=D(cs,"gy");var gz=D(cs,"gz");var roll=D(cs,"roll");var pitch=D(cs,"pitch");var yaw=D(cs,"yaw");
   return new TelemetrySample{TimestampUtc=stamp,LinkActive=port()!=null&&age.TotalSeconds<2,RollRate=gx,PitchRate=gy,YawRate=gz,GyroValid=F(gx)&&F(gy)&&F(gz)&&age.TotalSeconds<2,RollRad=Rad(roll),PitchRad=Rad(pitch),YawRad=Rad(yaw),AttitudeValid=F(roll)&&F(pitch)&&F(yaw)&&age.TotalSeconds<2,HeightMeters=Math.Max(0,range),HeightValid=F(range)&&range>0&&age.TotalSeconds<2,HeightTimestampUtc=stamp,HeightSource="MissionPlanner sonarrange",FlightMode=S(cs,"mode"),RcPwm=(int)D(cs,"ch"+channel+"in")};
  }
  public string CurrentMode=>S(state(),"mode");
  static bool F(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
  static double Rad(double degrees)=>F(degrees)?degrees*Math.PI/180:0;
  static double D(object o,string n){try{var v=Member(o,n);return v==null?0:Convert.ToDouble(v);}catch{return 0;}}
  static DateTime Date(object o,string n){try{var v=Member(o,n);return v is DateTime d?d:default(DateTime);}catch{return default(DateTime);}}
  static string S(object o,string n)=>Convert.ToString(Member(o,n));
  static object Member(object o,string n){if(o==null)return null;var f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.IgnoreCase;return o.GetType().GetProperty(n,f)?.GetValue(o,null)??o.GetType().GetField(n,f)?.GetValue(o);}
 }
}
