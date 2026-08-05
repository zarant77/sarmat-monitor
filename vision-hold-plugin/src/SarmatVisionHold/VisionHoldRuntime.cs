using System;
using System.Threading;
using OpenCvSharp;
using SarmatVisionHold.Core;
using SarmatVisionHold.Infrastructure;
using SarmatVisionHold.Integration;
using SarmatVisionHold.Live;
using SarmatVisionHold.Vision;

namespace SarmatVisionHold
{
 public sealed class VisionHoldRuntime:IDisposable
 {
  readonly VisionHoldSettings settings;readonly IVehicleGateway gateway;readonly CameraFrameSource camera=new CameraFrameSource();readonly OpticalFlowTracker tracker=new OpticalFlowTracker();readonly LiveOpticalFlowDiagnosticPipeline diagnostics=new LiveOpticalFlowDiagnosticPipeline();readonly RcSwitchListener rc;readonly VisionHoldStateMachine machine=new VisionHoldStateMachine();readonly VisionHoldLog log;readonly IClock clock=new SystemClock();Thread worker;volatile bool running;volatile bool manualRequested;
  public FlowSample LastFlow{get;private set;}public TelemetrySample LastTelemetry{get;private set;}public LiveDiagnosticResult LastDiagnostic{get;private set;}public VisionHoldState State=>machine.State;public string Reason=>machine.Reason;public bool Requested=>manualRequested||rc.State;public VisionHoldSettings Settings=>settings;public event Action Updated;

  public VisionHoldRuntime(VisionHoldSettings settings,IVehicleGateway gateway,string logPath)
  {
   this.settings=settings??throw new ArgumentNullException(nameof(settings));this.gateway=gateway??throw new ArgumentNullException(nameof(gateway));settings.Normalize();FlightOutputSafety.DemandDiagnosticsOnly();rc=new RcSwitchListener(settings,clock);log=new VisionHoldLog(logPath);machine.Changed+=(state,reason)=>log.Info($"State {state}: {reason}");rc.StateChanged+=on=>log.Info($"RC request: {on}");
  }
  public void Start(){camera.Start(settings.RtspUrl);running=true;worker=new Thread(Loop){IsBackground=true,Name="Sarmat Vision Hold diagnostics"};worker.Start();}
  public void SetManual(bool on){manualRequested=on;}
  void Loop()
  {
   while(running)
   {
    try
    {
     var now=clock.UtcNow;var telemetry=gateway.ReadTelemetry(settings.RcChannel);LastTelemetry=telemetry;if(telemetry.TimestampUtc!=default(DateTime))rc.Update(telemetry.RcPwm,telemetry.TimestampUtc.ToUniversalTime());rc.CheckStale(now);
     if(camera.TryGet(out Mat frame,out var capturedAt))
     {
      FlowSample flow;using(frame)flow=tracker.Process(frame,capturedAt,camera.Fps,settings.CameraFocalLengthPixels);
      if(flow!=null){flow.FrameAgeMs=Math.Max(0,(now-capturedAt).TotalMilliseconds);LastFlow=flow;LastDiagnostic=diagnostics.Process(flow,telemetry,settings);}
     }
     var health=HealthEvaluator.Evaluate(Requested,rc.State,camera.Working,LastFlow,telemetry,settings,clock);machine.Update(health);Updated?.Invoke();
    }
    catch(Exception ex){log.Error("Diagnostic pipeline",ex);machine.Fail("Diagnostic pipeline exception: "+ex.GetType().Name);}
    Thread.Sleep(20);
   }
  }
  public void Dispose(){running=false;worker?.Join(1000);machine.Stop();camera.Dispose();tracker.Dispose();diagnostics.Reset();log.Dispose();}
 }
}
