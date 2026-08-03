using System;

namespace SarmatVisionHold.Core
{
 public enum VisionHoldState { Disabled, WarmingUp, Ready, Active, Degraded, Lost }
 public sealed class TelemetrySample
 {
  public DateTime TimestampUtc; public bool LinkActive; public double RollRate; public double PitchRate; public double YawRate;
  public double HeightMeters; public bool HeightValid; public string FlightMode; public int RcPwm;
 }
 public sealed class FlowSample
 {
  public DateTime TimestampUtc; public double Dt; public double RawX; public double RawY;
  public double CompensatedX; public double CompensatedY; public double VelocityX; public double VelocityY;
  public int TrackedPoints; public double Quality; public double Fps; public double FrameAgeMs;
 }
 public sealed class HealthSnapshot
 {
  public bool Requested; public bool PilotRequested; public bool FramesFresh; public bool StreamWorking;
  public bool FlowGood; public bool HeightValid; public bool MavlinkActive; public bool RcFresh = true; public bool LiveAllowed;
  public string BlockReason;
  public bool CanDiagnose => StreamWorking && FramesFresh && FlowGood && HeightValid && RcFresh;
  public bool CanActivate => CanDiagnose && MavlinkActive && LiveAllowed;
 }
}
