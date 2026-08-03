using System;
namespace SarmatVisionHold.Core
{
 public sealed class GroundVelocityEstimator
 { readonly double maxSpeed;public GroundVelocityEstimator(double maxSpeed=15){this.maxSpeed=F(maxSpeed)?Math.Max(0,maxSpeed):15;}
  public bool Estimate(FlowSample f,TelemetrySample t){if(f==null||t==null)return false;return Set(f,f.CompensatedX,f.CompensatedY,f.Dt,t.HeightMeters,t.HeightValid);}
  public bool EstimatePixels(FlowSample f,double pixelX,double pixelY,double dt,double height,double horizontalFovDegrees,int width,int heightPixels){if(f==null||!F(horizontalFovDegrees)||horizontalFovDegrees<=0||horizontalFovDegrees>=180||width<=0||heightPixels<=0)return Clear(f);var focal=width/(2*Math.Tan(horizontalFovDegrees*Math.PI/360));if(!F(focal)||focal<=0)return Clear(f);return Set(f,pixelX/focal,pixelY/focal,dt,height,true);}
  bool Set(FlowSample f,double x,double y,double dt,double h,bool valid){if(!valid||!F(x)||!F(y)||!F(dt)||dt<=0||!HealthEvaluator.ValidHeight(h))return Clear(f);f.VelocityX=Clamp(x*h/dt);f.VelocityY=Clamp(y*h/dt);return true;}
  bool Clear(FlowSample f){if(f!=null){f.VelocityX=0;f.VelocityY=0;}return false;}double Clamp(double v){if(!F(v))return 0;return Math.Max(-maxSpeed,Math.Min(maxSpeed,v));}static bool F(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
 }
}
