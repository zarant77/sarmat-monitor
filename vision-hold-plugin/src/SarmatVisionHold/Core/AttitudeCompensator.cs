using System;
namespace SarmatVisionHold.Core
{
 public sealed class AttitudeCompensator
 { readonly double maxRate;public AttitudeCompensator(double maxRate=20){this.maxRate=Safe(maxRate)?Math.Max(0,maxRate):20;}
  public bool Compensate(FlowSample f,TelemetrySample t){if(f==null||t==null||!Safe(f.RawX)||!Safe(f.RawY)||!Safe(f.Dt)||f.Dt<0||!Rate(t.RollRate)||!Rate(t.PitchRate)||!Rate(t.YawRate)){if(f!=null){f.CompensatedX=Safe(f.RawX)?f.RawX:0;f.CompensatedY=Safe(f.RawY)?f.RawY:0;}return false;}var x=f.RawX-t.PitchRate*f.Dt;var y=f.RawY+t.RollRate*f.Dt;var a=-t.YawRate*f.Dt;var c=Math.Cos(a);var s=Math.Sin(a);f.CompensatedX=FiniteOrZero(x*c-y*s);f.CompensatedY=FiniteOrZero(x*s+y*c);return true;}
  bool Rate(double v)=>Safe(v)&&Math.Abs(v)<=maxRate;static bool Safe(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);static double FiniteOrZero(double v)=>Safe(v)?v:0;
 }
}
