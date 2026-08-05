using System;
using System.Collections.Generic;

namespace SarmatVisionHold.Core
{
 public enum VisionHoldState { Disabled, WarmingUp, Ready, Active, Degraded, Lost }
 public sealed class TelemetrySample
 {
  public DateTime TimestampUtc; public bool LinkActive; public double RollRate; public double PitchRate; public double YawRate;
  public double RollRad; public double PitchRad; public double YawRad; public bool AttitudeValid; public bool GyroValid;
  public double HeightMeters; public bool HeightValid; public DateTime HeightTimestampUtc; public string HeightSource;
  public string FlightMode; public int RcPwm;
 }
 public sealed class FlowTrackSample
 {
  public double FromX; public double FromY; public double ToX; public double ToY;
  public bool Accepted; public double ForwardBackwardError;
 }
 public sealed class FlowSample
 {
  public DateTime TimestampUtc; public double Dt; public double RawX; public double RawY;
  public double CompensatedX; public double CompensatedY; public double VelocityX; public double VelocityY;
  public double RawPixelsX; public double RawPixelsY; public double CompensatedPixelsX; public double CompensatedPixelsY;
  public int FrameWidth; public int FrameHeight; public int TrackedPoints; public int InlierCount;
  public double MedianForwardBackwardError; public double CompensationResidualPixels;
  public double Quality; public double Fps; public double FrameAgeMs;
  public IReadOnlyList<FlowTrackSample> Tracks = new FlowTrackSample[0];
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
