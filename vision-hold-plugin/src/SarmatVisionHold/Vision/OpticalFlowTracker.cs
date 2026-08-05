using System;
using System.Linq;
using OpenCvSharp;
using SarmatVisionHold.Core;
namespace SarmatVisionHold.Vision
{
 public sealed class OpticalFlowTracker:IDisposable
 { readonly SparseOpticalFlowProcessor processor;DateTime previousAt;
  public OpticalFlowTracker(SparseOpticalFlowOptions options=null){processor=new SparseOpticalFlowProcessor(options);}
  public FlowSample Process(Mat frame,DateTime at,double fps,double focal){if(previousAt!=default(DateTime)&&at<=previousAt)return null;var dt=previousAt==default(DateTime)?0:(at-previousAt).TotalSeconds;previousAt=at;var result=processor.Process(frame);if(dt<=0)return null;var scale=Finite(focal)&&focal>0?focal:1;var accepted=result.Tracks.Where(x=>x.Accepted).Select(x=>new FlowTrackSample{FromX=x.From.X,FromY=x.From.Y,ToX=x.To.X,ToY=x.To.Y,Accepted=true,ForwardBackwardError=x.ForwardBackwardError}).ToArray();var medianFb=accepted.Length==0?0:Median(accepted.Select(x=>x.ForwardBackwardError).ToArray());return new FlowSample{TimestampUtc=at,Dt=dt,Fps=Finite(fps)?Math.Max(0,fps):0,FrameWidth=frame.Width,FrameHeight=frame.Height,TrackedPoints=result.TrackedPoints,InlierCount=result.InlierCount,RawPixelsX=result.MedianX,RawPixelsY=result.MedianY,RawX=result.MedianX/scale,RawY=result.MedianY/scale,MedianForwardBackwardError=medianFb,Quality=result.Quality,Tracks=accepted};}
  static double Median(double[] values){if(values==null||values.Length==0)return 0;Array.Sort(values);var m=values.Length/2;return values.Length%2==0?(values[m-1]+values[m])/2:values[m];}
  static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);public void Dispose()=>processor.Dispose();
 }
}
