using System;
using OpenCvSharp;
using SarmatVisionHold.Core;
namespace SarmatVisionHold.Vision
{
 public sealed class OpticalFlowTracker:IDisposable
 { readonly SparseOpticalFlowProcessor processor;DateTime previousAt;
  public OpticalFlowTracker(SparseOpticalFlowOptions options=null){processor=new SparseOpticalFlowProcessor(options);}
  public FlowSample Process(Mat frame,DateTime at,double fps,double focal){if(previousAt!=default(DateTime)&&at<=previousAt)return null;var dt=previousAt==default(DateTime)?0:(at-previousAt).TotalSeconds;previousAt=at;var result=processor.Process(frame);if(dt<=0)return null;var scale=Finite(focal)&&focal>0?focal:1;return new FlowSample{TimestampUtc=at,Dt=dt,Fps=Finite(fps)?Math.Max(0,fps):0,TrackedPoints=result.AcceptedPoints,RawX=result.MedianX/scale,RawY=result.MedianY/scale,Quality=result.Quality};}
  static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);public void Dispose()=>processor.Dispose();
 }
}
