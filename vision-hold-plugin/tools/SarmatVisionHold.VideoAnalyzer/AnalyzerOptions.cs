using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using SarmatVisionHold.Vision;

namespace SarmatVisionHold.VideoAnalyzer
{
 [DataContract]
 internal sealed class AnalyzerOptions
 {
  [DataMember] public int MaxFeatures=300;[DataMember] public double QualityLevel=.01;[DataMember] public double MinimumDistance=8;
  [DataMember] public int LkWindowSize=21;[DataMember] public int PyramidLevels=3;[DataMember] public double ForwardBackwardErrorThreshold=1.5;
  [DataMember] public double OutlierThreshold=3;[DataMember] public int MinimumAcceptedPoints=20;
  [DataMember] public double RansacReprojectionThreshold=2;[DataMember] public double RansacConfidence=.99;[DataMember] public int RansacMaxIterations=2000;
  [DataMember] public double MinimumQuality=.35;[DataMember] public double TranslationThreshold=1;[DataMember] public double RotationThresholdDegrees=.25;[DataMember] public double ScaleThreshold=.003;[DataMember] public double DominanceRatio=1.5;[DataMember] public double SmoothingAlpha=.25;[DataMember] public int HysteresisFrames=3;
  [DataMember] public int RoiX=-1;[DataMember] public int RoiY=-1;[DataMember] public int RoiWidth=0;[DataMember] public int RoiHeight=0;[DataMember] public string MaskPath;
  public SparseOpticalFlowOptions FlowOptions(){var o=new SparseOpticalFlowOptions{MaxFeatures=MaxFeatures,QualityLevel=QualityLevel,MinimumDistance=MinimumDistance,LkWindowSize=LkWindowSize,PyramidLevels=PyramidLevels,ForwardBackwardErrorThreshold=ForwardBackwardErrorThreshold,OutlierThreshold=OutlierThreshold,MinimumAcceptedPoints=MinimumAcceptedPoints,RansacReprojectionThreshold=RansacReprojectionThreshold,RansacConfidence=RansacConfidence,RansacMaxIterations=RansacMaxIterations};o.Normalize();return o;}
  public MotionClassifierOptions ClassifierOptions()=>new MotionClassifierOptions{TranslationThreshold=TranslationThreshold,RotationThresholdDegrees=RotationThresholdDegrees,ScaleThreshold=ScaleThreshold,DominanceRatio=DominanceRatio,SmoothingAlpha=SmoothingAlpha,HysteresisFrames=HysteresisFrames};
  public static AnalyzerOptions Load(string path){using(var stream=File.OpenRead(path))return (AnalyzerOptions)new DataContractJsonSerializer(typeof(AnalyzerOptions)).ReadObject(stream);}
 }
 internal sealed class CommandLine
 { public string Input,Output,Config,Labels;public bool Preview,Help;public double Start,Duration=-1;public AnalyzerOptions Options=new AnalyzerOptions();
  public static CommandLine Parse(string[] args){var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var flags=new HashSet<string>(StringComparer.OrdinalIgnoreCase);for(var i=0;i<args.Length;i++){var key=args[i];if(key=="--preview"||key=="--help"||key=="-h"){flags.Add(key);continue;}if(!key.StartsWith("--")||i+1>=args.Length)throw new ArgumentException("Missing value for "+key);map[key]=args[++i];}var c=new CommandLine{Preview=flags.Contains("--preview"),Help=flags.Contains("--help")||flags.Contains("-h")};map.TryGetValue("--input",out c.Input);map.TryGetValue("--output",out c.Output);map.TryGetValue("--config",out c.Config);map.TryGetValue("--labels",out c.Labels);if(!string.IsNullOrWhiteSpace(c.Config))c.Options=AnalyzerOptions.Load(c.Config);Set(map,"--start",v=>c.Start=Math.Max(0,D(v)));Set(map,"--duration",v=>c.Duration=D(v));Set(map,"--max-features",v=>c.Options.MaxFeatures=I(v));Set(map,"--quality-level",v=>c.Options.QualityLevel=D(v));Set(map,"--minimum-quality",v=>c.Options.MinimumQuality=D(v));Set(map,"--translation-threshold",v=>c.Options.TranslationThreshold=D(v));Set(map,"--rotation-threshold",v=>c.Options.RotationThresholdDegrees=D(v));Set(map,"--scale-threshold",v=>c.Options.ScaleThreshold=D(v));Set(map,"--dominance-ratio",v=>c.Options.DominanceRatio=D(v));Set(map,"--smoothing-alpha",v=>c.Options.SmoothingAlpha=D(v));Set(map,"--hysteresis-frames",v=>c.Options.HysteresisFrames=I(v));Set(map,"--minimum-distance",v=>c.Options.MinimumDistance=D(v));Set(map,"--lk-window-size",v=>c.Options.LkWindowSize=I(v));Set(map,"--pyramid-levels",v=>c.Options.PyramidLevels=I(v));Set(map,"--fb-threshold",v=>c.Options.ForwardBackwardErrorThreshold=D(v));Set(map,"--outlier-threshold",v=>c.Options.OutlierThreshold=D(v));Set(map,"--ransac-threshold",v=>c.Options.RansacReprojectionThreshold=D(v));Set(map,"--ransac-confidence",v=>c.Options.RansacConfidence=D(v));Set(map,"--ransac-iterations",v=>c.Options.RansacMaxIterations=I(v));Set(map,"--minimum-accepted-points",v=>c.Options.MinimumAcceptedPoints=I(v));Set(map,"--mask",v=>c.Options.MaskPath=v);Set(map,"--roi",v=>{var p=v.Split(',');if(p.Length!=4)throw new ArgumentException("--roi must be x,y,width,height");c.Options.RoiX=I(p[0]);c.Options.RoiY=I(p[1]);c.Options.RoiWidth=I(p[2]);c.Options.RoiHeight=I(p[3]);});return c;}
  static void Set(Dictionary<string,string> m,string k,Action<string> set){if(m.TryGetValue(k,out var v))set(v);}static int I(string v)=>int.Parse(v,System.Globalization.CultureInfo.InvariantCulture);static double D(string v)=>double.Parse(v,System.Globalization.CultureInfo.InvariantCulture);
 }
}
