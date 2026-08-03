using System;

namespace SarmatVisionHold.Core
{
 public sealed class RcSwitchListener
 {
  private readonly VisionHoldSettings settings; private bool state; private bool? candidate; private DateTime candidateSince;
  public bool State => state; public int LastPwm { get; private set; } public DateTime LastSampleUtc { get; private set; }
  public event Action<bool> StateChanged;
  public RcSwitchListener(VisionHoldSettings settings) { this.settings=settings; }
  public void Update(int pwm, DateTime now) { LastPwm=pwm; LastSampleUtc=now; if(pwm<=0){ ForceOff(); return; } bool? desired=null; if(!settings.RcInverted){if(pwm>=settings.RcEnableThreshold)desired=true;else if(pwm<=settings.RcDisableThreshold)desired=false;}else{if(pwm<=settings.RcDisableThreshold)desired=true;else if(pwm>=settings.RcEnableThreshold)desired=false;} if(!desired.HasValue||desired.Value==state){candidate=null;return;} if(candidate!=desired){candidate=desired;candidateSince=now;return;} if((now-candidateSince).TotalMilliseconds>=settings.RcDebounceMs){state=desired.Value;candidate=null;StateChanged?.Invoke(state);} }
  public void CheckStale(DateTime now) { if(LastSampleUtc==default(DateTime)||(now-LastSampleUtc).TotalMilliseconds>settings.RcStaleMs) ForceOff(); }
  private void ForceOff(){candidate=null;if(state){state=false;StateChanged?.Invoke(false);}}
 }
}
