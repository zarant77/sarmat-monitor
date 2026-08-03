using System; using System.Linq; using OpenCvSharp; using SarmatVisionHold.Core;
namespace SarmatVisionHold.Vision
{
 public sealed class OpticalFlowTracker:IDisposable
 { private Mat previous; private DateTime previousAt;
  public FlowSample Process(Mat frame,DateTime at,double fps,double focal){using(var gray=new Mat()){Cv2.CvtColor(frame,gray,ColorConversionCodes.BGR2GRAY);if(previous==null){previous=gray.Clone();previousAt=at;return null;}var dt=(at-previousAt).TotalSeconds;if(dt<=0){return null;}var points=previous.GoodFeaturesToTrack(200,.01,8,null,3,false,.04);if(points.Length<4){Replace(gray,at);return new FlowSample{TimestampUtc=at,Dt=dt,Fps=fps};}using(var p0=Mat.FromArray(points))using(var p1=new Mat())using(var status=new Mat())using(var errors=new Mat()){Cv2.CalcOpticalFlowPyrLK(previous,gray,p0,p1,status,errors,new Size(21,21),3,new TermCriteria(CriteriaTypes.Eps|CriteriaTypes.Count,30,.01),OpticalFlowFlags.None,1e-4);var dx=new System.Collections.Generic.List<double>();var dy=new System.Collections.Generic.List<double>();var er=new System.Collections.Generic.List<double>();for(int i=0;i<points.Length;i++)if(status.At<byte>(i)!=0){var n=p1.At<Point2f>(i);dx.Add(n.X-points[i].X);dy.Add(n.Y-points[i].Y);er.Add(errors.At<float>(i));}var result=new FlowSample{TimestampUtc=at,Dt=dt,Fps=fps,TrackedPoints=dx.Count,RawX=Median(dx)/focal,RawY=Median(dy)/focal,Quality=FlowQualityEstimator.Estimate(dx.Count,points.Length,Median(er))};Replace(gray,at);return result;}}}
  private static double Median(System.Collections.Generic.List<double> x){if(x.Count==0)return 0;x.Sort();return x[x.Count/2];}private void Replace(Mat m,DateTime at){previous?.Dispose();previous=m.Clone();previousAt=at;}public void Dispose(){previous?.Dispose();previous=null;}
 }
}
