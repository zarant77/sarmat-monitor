using System;using OpenCvSharp;using SarmatVisionHold.Vision;
namespace SarmatVisionHold.KestrelClient.Models {
 public sealed class Vector3Data { public double X,Y,Z; }
 public sealed class AttitudeData { public double RollRad,PitchRad,YawRad; }
 public sealed class CameraData { public int Width,Height;public double FovDegrees,MountRollRad,MountPitchRad,MountYawRad; }
 public sealed class StateData { public bool Paused,Crashed,OnGround; }
 public sealed class AxesData { public double Roll,Pitch,Yaw,Throttle; }
 public sealed class InputData { public AxesData Axes; }
 public sealed class KestrelTelemetry { public int ProtocolVersion;public Guid SessionId;public uint FrameNumber;public ulong TimestampUs;public double SimulationTimeSeconds;public Vector3Data Position,VelocityWorld,VelocityBody,AngularRateBody,WindWorld;public AttitudeData Attitude;public CameraData Camera;public StateData State;public InputData Input;public long PhysicsTick,RenderFrame;public int Seed;public double CaptureFps;public long TotalDroppedFrames;public DateTime ReceivedUtc; }
 public sealed class KestrelFrame:IDisposable { public int ProtocolVersion;public Guid SessionId;public uint FrameNumber;public ulong TimestampUs;public int Width,Height,Encoding;public byte[] Jpeg;public Mat Image;public DateTime ReceivedUtc;public void Dispose(){Image?.Dispose();} }
 public sealed class SynchronizedSample:IDisposable { public KestrelFrame Frame;public KestrelTelemetry Telemetry;public double NetworkLatencyMs=>Math.Max(0,(Frame.ReceivedUtc>Telemetry.ReceivedUtc?Frame.ReceivedUtc:Telemetry.ReceivedUtc).Subtract(Telemetry.ReceivedUtc).TotalMilliseconds);public void Dispose()=>Frame?.Dispose(); }
 public enum ExpectedMotion { Unknown,Still,Translating,Rotating,Mixed }
 public enum ValidationStatus { PASS,PARTIAL,FAIL,UNKNOWN }
 public sealed class GroundTruthMotion { public ExpectedMotion Classification;public string TextureDirection="UNKNOWN",RotationDirection="STILL";public double CameraX,CameraY,YawRate; }
 public sealed class ValidationResult { public ValidationStatus Status;public string Reason;public GroundTruthMotion Expected;public MotionDecision Detected; }
}
