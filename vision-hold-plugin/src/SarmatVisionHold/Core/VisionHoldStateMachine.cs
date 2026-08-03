using System;
namespace SarmatVisionHold.Core
{
 public sealed class VisionHoldStateMachine
 { readonly int warmupSamples;int healthy;bool lostLatch;bool previousRequest;public VisionHoldState State{get;private set;}=VisionHoldState.Disabled;public string Reason{get;private set;}="Disabled";public event Action<VisionHoldState,string> Changed;
  public VisionHoldStateMachine(int warmupSamples=5){this.warmupSamples=Math.Max(1,warmupSamples);}
  public void Update(HealthSnapshot h){if(h==null){Fail("Health unavailable");return;}var rising=h.Requested&&!previousRequest;previousRequest=h.Requested;
   if(!h.Requested){lostLatch=false;healthy=h.CanDiagnose?Math.Min(warmupSamples,healthy+1):0;Set(h.CanDiagnose?(healthy>=warmupSamples?VisionHoldState.Ready:VisionHoldState.WarmingUp):VisionHoldState.Disabled,h.BlockReason??(h.CanDiagnose?"Warming up":"Disabled"));return;}
   if(lostLatch){Set(VisionHoldState.Lost,"Pilot must switch OFF before retry");return;}
   if(!h.CanDiagnose){healthy=0;if(State==VisionHoldState.Active){lostLatch=true;Set(VisionHoldState.Lost,h.BlockReason??"Readiness lost");}else Set(VisionHoldState.Degraded,h.BlockReason??"Not ready");return;}
   if(!h.LiveAllowed){healthy=Math.Min(warmupSamples,healthy+1);Set(VisionHoldState.Ready,"Diagnostics only / live control disabled");return;}
   if(!h.MavlinkActive){healthy=0;if(State==VisionHoldState.Active){lostLatch=true;Set(VisionHoldState.Lost,"MAVLink unavailable");}else Set(VisionHoldState.Degraded,"MAVLink unavailable");return;}
   healthy=Math.Min(warmupSamples,healthy+1);if(healthy<warmupSamples){Set(VisionHoldState.WarmingUp,"Warming up");return;}if(rising||State==VisionHoldState.WarmingUp||State==VisionHoldState.Ready||State==VisionHoldState.Degraded)Set(VisionHoldState.Active,"Active");}
  public void Fail(string reason){healthy=0;if(State==VisionHoldState.Active)lostLatch=true;Set(State==VisionHoldState.Active||lostLatch?VisionHoldState.Lost:VisionHoldState.Degraded,reason);}
  public void Stop(){healthy=0;lostLatch=true;Set(VisionHoldState.Lost,"Plugin stopped");}
  void Set(VisionHoldState s,string r){if(s==State&&r==Reason)return;State=s;Reason=r;Changed?.Invoke(s,r);}
 }
}
