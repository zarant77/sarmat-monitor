using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SarmatVisionHold.Core
{
 [DataContract]
 public sealed class VisionHoldSettings
 {
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
  [DataMember] public int NonGpsEkfSourceSet = 2;
  [DataMember] public string ActiveMode = "FlowHold";
  [DataMember] public string FallbackMode = "AltHold";
  public void Normalize() { RcChannel=Math.Max(1,Math.Min(18,RcChannel)); RcDebounceMs=Math.Max(0,RcDebounceMs); RcStaleMs=Math.Max(100,RcStaleMs); MaxFrameAgeMs=Math.Max(50,MaxFrameAgeMs); MinimumFps=Math.Max(1,MinimumFps); MinimumFlowQuality=Math.Max(0,Math.Min(1,MinimumFlowQuality)); CameraFocalLengthPixels=Math.Max(1,CameraFocalLengthPixels); }
  public static VisionHoldSettings Load(string path) { try { using(var s=File.OpenRead(path)){ var v=(VisionHoldSettings)new DataContractJsonSerializer(typeof(VisionHoldSettings)).ReadObject(s); v.Normalize(); return v; } } catch { return new VisionHoldSettings(); } }
  public void Save(string path) { Normalize(); Directory.CreateDirectory(Path.GetDirectoryName(path)); using(var s=File.Create(path)) new DataContractJsonSerializer(typeof(VisionHoldSettings)).WriteObject(s,this); }
 }
}
