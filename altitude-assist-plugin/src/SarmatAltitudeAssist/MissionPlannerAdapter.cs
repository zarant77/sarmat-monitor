using System;using System.Reflection;using SarmatAltitudeAssist.Core;
namespace SarmatAltitudeAssist
{
 sealed class MissionPlannerTelemetrySource:ITelemetrySource
 {
  readonly Func<object> state,port;readonly AltitudeAssistSettings settings;double lastAltitude;string lastSource;
  public MissionPlannerTelemetrySource(Func<object> s,Func<object> p,AltitudeAssistSettings cfg){state=s;port=p;settings=cfg;}
  public TelemetrySnapshot Read(){var cs=state();var now=DateTime.UtcNow;var stamp=Date(cs,"datetime");if(stamp==default(DateTime))stamp=now;stamp=stamp.ToUniversalTime();var raw=(int)D(cs,"ch"+settings.VerticalRcChannel+"in");var alt=D(cs,"alt");var source="CurrentState.alt (relative/home)";var valid=Finite(alt)&&alt>=-2;var jump=lastSource==source&&Math.Abs(alt-lastAltitude)>100;lastAltitude=alt;lastSource=source;return new TelemetrySnapshot{TimestampUtc=stamp,Connected=port()!=null&&(now-stamp).TotalSeconds<2,HeartbeatHealthy=(now-stamp).TotalSeconds<2,Armed=B(cs,"armed"),Airborne=alt>=settings.MinimumActivationAltitudeMeters,AltitudeMeters=alt,AltitudeValid=valid&&!jump,AltitudeSource=source,VerticalSpeed=D(cs,"verticalspeed"),StickRaw=raw,StickNormalized=Normalize(raw),FlightMode=S(cs,"mode")};}
  double Normalize(int pwm){if(pwm<=0)return .5;double v=pwm>=settings.RcTrim?.5+.5*(pwm-settings.RcTrim)/Math.Max(1,settings.RcMax-settings.RcTrim):.5-.5*(settings.RcTrim-pwm)/Math.Max(1,settings.RcTrim-settings.RcMin);v=Math.Max(0,Math.Min(1,v));return settings.RcReversed?1-v:v;}
  static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);static object M(object o,string n){if(o==null)return null;var f=BindingFlags.Instance|BindingFlags.Public|BindingFlags.IgnoreCase;return o.GetType().GetProperty(n,f)?.GetValue(o,null)??o.GetType().GetField(n,f)?.GetValue(o);}static double D(object o,string n){try{return Convert.ToDouble(M(o,n));}catch{return double.NaN;}}static bool B(object o,string n){try{return Convert.ToBoolean(M(o,n));}catch{return false;}}static string S(object o,string n)=>Convert.ToString(M(o,n));static DateTime Date(object o,string n){var x=M(o,n);return x is DateTime?(DateTime)x:default(DateTime);}
 }
}
