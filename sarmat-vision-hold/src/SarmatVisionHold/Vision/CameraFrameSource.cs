using System; using System.Threading; using OpenCvSharp;
namespace SarmatVisionHold.Vision
{
 public sealed class CameraFrameSource:IDisposable
 { private VideoCapture capture; private Thread thread; private volatile bool running; private readonly object gate=new object(); private Mat latest; public DateTime LastFrameUtc{get;private set;} public double Fps{get;private set;} public bool Working=>running&&capture!=null&&capture.IsOpened();
  public void Start(string url){Stop();capture=new VideoCapture(url,VideoCaptureAPIs.FFMPEG);running=true;thread=new Thread(ReadLoop){IsBackground=true,Name="SarmatVisionHold RTSP"};thread.Start();}
  private void ReadLoop(){var last=DateTime.UtcNow;double ema=0;while(running){using(var m=new Mat()){if(!capture.Read(m)||m.Empty()){Thread.Sleep(20);continue;}var now=DateTime.UtcNow;var dt=(now-last).TotalSeconds;last=now;if(dt>0)ema=ema==0?1/dt:ema*.9+(1/dt)*.1;lock(gate){latest?.Dispose();latest=m.Clone();LastFrameUtc=now;Fps=ema;}}}}
  public bool TryGet(out Mat frame,out DateTime timestamp){lock(gate){timestamp=LastFrameUtc;frame=latest?.Clone();return frame!=null;}}
  public void Stop(){running=false;thread?.Join(500);thread=null;capture?.Release();capture?.Dispose();capture=null;lock(gate){latest?.Dispose();latest=null;}}
  public void Dispose()=>Stop();
 }
}
