using System;
namespace SarmatVisionHold.Core
{
 public sealed class VisionHoldStateMachine
 {
  public VisionHoldState State {get;private set;}=VisionHoldState.Disabled; public string Reason {get;private set;}="Disabled"; private int healthyFrames;
  public event Action<VisionHoldState,string> Changed;
  public void Update(HealthSnapshot h){ VisionHoldState next; string reason=h.BlockReason;
   if(!h.Requested){next=h.CanDiagnose?(healthyFrames>=5?VisionHoldState.Ready:VisionHoldState.WarmingUp):VisionHoldState.Disabled; healthyFrames=h.CanDiagnose?healthyFrames+1:0;}
   else if(!h.CanDiagnose){next=State==VisionHoldState.Active||State==VisionHoldState.Degraded?VisionHoldState.Lost:VisionHoldState.Degraded;healthyFrames=0;}
   else if(!h.LiveAllowed){next=VisionHoldState.Ready;reason="Diagnostics only / live control disabled";healthyFrames++;}
   else if(!h.MavlinkActive){next=VisionHoldState.Lost;reason="MAVLink unavailable";healthyFrames=0;}
   else {healthyFrames++;next=healthyFrames>=5?VisionHoldState.Active:VisionHoldState.WarmingUp;}
   if(next!=State||reason!=Reason){State=next;Reason=reason??next.ToString();Changed?.Invoke(State,Reason);} }
  public void Stop(){healthyFrames=0;Set(VisionHoldState.Lost,"Plugin stopped");}
  private void Set(VisionHoldState s,string r){if(s==State&&r==Reason)return;State=s;Reason=r;Changed?.Invoke(s,r);}
 }
}
