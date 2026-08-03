using System;
namespace SarmatVisionHold.Core
{
 public interface IFlowPublisher { void Publish(FlowSample flow,TelemetrySample telemetry); }
 public interface IFallbackAction { void Enter(string reason); }
 public sealed class FlowSafetyCoordinator
 { readonly VisionHoldStateMachine machine;readonly IFlowPublisher publisher;readonly IFallbackAction fallback;public int PublishedCount{get;private set;}public FlowSafetyCoordinator(VisionHoldStateMachine m,IFlowPublisher p,IFallbackAction f){machine=m;publisher=p;fallback=f;}
  public void Tick(HealthSnapshot health,FlowSample flow,TelemetrySample telemetry){var was=machine.State;machine.Update(health);if(machine.State!=VisionHoldState.Active){if(was==VisionHoldState.Active)fallback.Enter(machine.Reason);return;}try{publisher.Publish(flow,telemetry);PublishedCount++;}catch(Exception ex){machine.Fail("Publisher exception: "+ex.GetType().Name);fallback.Enter(machine.Reason);}}
  public void TrackerFailed(Exception ex){machine.Fail("Tracker exception: "+ex.GetType().Name);fallback.Enter(machine.Reason);}public void Stop(){machine.Stop();fallback.Enter(machine.Reason);}
 }
}
