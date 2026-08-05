using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SarmatVisionHold.Core
{
 [DataContract]
 public sealed class VisionHoldSettings
 {
  public const bool MavlinkTransmissionCompiled = false;
  [DataMember] public string RtspUrl = "rtsp://127.0.0.1:8554/live";
  [DataMember] public bool DiagnosticsOnly = true;
  [DataMember] public bool EnableLiveControl = false;
  [DataMember] public int RcChannel = 9;
  [DataMember] public int RcEnableThreshold = 1700;
  [DataMember] public int RcDisableThreshold = 1300;
  [DataMember] public bool RcInverted = false;
  [DataMember] public int RcDebounceMs = 300;
  [DataMember] public int RcStaleMs = 1000;
  [DataMember] public int MaxFrameAgeMs = 300;
  [DataMember] public double MinimumFps = 8;
  [DataMember] public int MinimumTrackedPoints = 20;
  [DataMember] public double MinimumFlowQuality = .35;
  [DataMember] public double CameraFocalLengthPixels = 500;
  [DataMember] public double HorizontalFovDegrees = 70;
  [DataMember] public double VerticalFovDegrees = 0;
  [DataMember] public double CameraMountRollDegrees = 0;
  [DataMember] public double CameraMountPitchDegrees = -90;
  [DataMember] public double CameraMountYawDegrees = 0;
  [DataMember] public int FrameWidth = 1280;
  [DataMember] public int FrameHeight = 720;
  [DataMember] public double MaximumGroundSpeed = 15;
  [DataMember] public double MaximumImuRate = 20;
  [DataMember] public int NonGpsEkfSourceSet = 2;
  [DataMember] public string ActiveMode = "FlowHold";
  [DataMember] public string FallbackMode = "AltHold";
  public void Normalize() { RcChannel=Math.Max(1,Math.Min(18,RcChannel));RcEnableThreshold=Math.Max(801,Math.Min(2200,RcEnableThreshold));RcDisableThreshold=Math.Max(800,Math.Min(RcEnableThreshold-1,RcDisableThreshold)); RcDebounceMs=Math.Max(0,Math.Min(10000,RcDebounceMs)); RcStaleMs=Math.Max(100,Math.Min(60000,RcStaleMs)); MaxFrameAgeMs=Math.Max(0,Math.Min(10000,MaxFrameAgeMs)); MinimumFps=Finite(MinimumFps)?Math.Max(0,Math.Min(240,MinimumFps)):8; MinimumTrackedPoints=Math.Max(0,Math.Min(10000,MinimumTrackedPoints)); MinimumFlowQuality=Finite(MinimumFlowQuality)?Math.Max(0,Math.Min(1,MinimumFlowQuality)):.35; CameraFocalLengthPixels=Finite(CameraFocalLengthPixels)?Math.Max(1,CameraFocalLengthPixels):500;HorizontalFovDegrees=Finite(HorizontalFovDegrees)?Math.Max(1,Math.Min(179,HorizontalFovDegrees)):70;VerticalFovDegrees=Finite(VerticalFovDegrees)?Math.Max(0,Math.Min(179,VerticalFovDegrees)):0;CameraMountRollDegrees=Angle(CameraMountRollDegrees);CameraMountPitchDegrees=Angle(CameraMountPitchDegrees,-90);CameraMountYawDegrees=Angle(CameraMountYawDegrees);FrameWidth=Math.Max(1,FrameWidth);FrameHeight=Math.Max(1,FrameHeight);MaximumGroundSpeed=Finite(MaximumGroundSpeed)?Math.Max(0,MaximumGroundSpeed):15;MaximumImuRate=Finite(MaximumImuRate)?Math.Max(0,MaximumImuRate):20;DiagnosticsOnly=true;EnableLiveControl=false; }
  private static double Angle(double value,double fallback=0)=>Finite(value)?Math.Max(-360,Math.Min(360,value)):fallback;
  private static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
  public static VisionHoldSettings Load(string path) { try { using(var s=File.OpenRead(path)){ var v=(VisionHoldSettings)new DataContractJsonSerializer(typeof(VisionHoldSettings)).ReadObject(s); v.Normalize(); return v; } } catch { return new VisionHoldSettings(); } }
  public void Save(string path) { Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(path)); using(var s=File.Create(path)) new DataContractJsonSerializer(typeof(VisionHoldSettings)).WriteObject(s,this); }
 }
}
