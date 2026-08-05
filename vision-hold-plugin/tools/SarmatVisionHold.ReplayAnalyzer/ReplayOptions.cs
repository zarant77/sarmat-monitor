using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using SarmatVisionHold.Replay.Processing;

namespace SarmatVisionHold.ReplayAnalyzer
{
    [DataContract]
    public sealed class ReplayOptions
    {
        [DataMember] public string Video;
        [DataMember] public string Tlog;
        [DataMember] public string Output;
        [DataMember] public string Config;
        [DataMember] public bool Preview;
        [DataMember] public bool Headless;
        [DataMember] public bool AutoSync;
        [DataMember] public bool SaveAnnotatedVideo;
        [DataMember] public double StartSeconds;
        [DataMember] public double DurationSeconds = -1;
        [DataMember] public double VideoOffsetMilliseconds;
        [DataMember] public int CameraWidth;
        [DataMember] public int CameraHeight;
        [DataMember] public double HorizontalFovDegrees = 80;
        [DataMember] public double VerticalFovDegrees;
        [DataMember] public double CameraMountRollDegrees;
        [DataMember] public double CameraMountPitchDegrees = -90;
        [DataMember] public double CameraMountYawDegrees;
        [DataMember] public string AltitudeSource = "auto";
        [DataMember] public double MaximumSyncErrorMilliseconds = 30;
        [DataMember] public double GapResetSeconds = .25;
        [DataMember] public double AutoSyncMinimumOffsetSeconds = -10;
        [DataMember] public double AutoSyncMaximumOffsetSeconds = 10;
        [DataMember] public double AutoSyncStepSeconds = .02;
        [DataMember] public double AutoSyncConfidenceThreshold = .35;
        [DataMember] public RotationCompensationMode RotationMode = RotationCompensationMode.Comparison;
        [DataMember] public bool EmitDegraded = true;
        [DataMember] public string LogLevel = "info";
        public bool Help;

        public static ReplayOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i];
                if (key == "--preview" || key == "--headless" || key == "--auto-sync" || key == "--save-annotated-video" || key == "--help" || key == "-h") { flags.Add(key); continue; }
                if (!key.StartsWith("--") || i + 1 >= args.Length) throw new ArgumentException("Missing value for " + key);
                values[key] = args[++i];
            }
            ReplayOptions o;
            string config;
            if (values.TryGetValue("--config", out config)) { o = Load(config); o.Config = Path.GetFullPath(config); } else o = new ReplayOptions();
            Set(values, "--video", v => o.Video = v); Set(values, "--tlog", v => o.Tlog = v); Set(values, "--output", v => o.Output = v);
            Set(values, "--start", v => o.StartSeconds = D(v)); Set(values, "--duration", v => o.DurationSeconds = D(v)); Set(values, "--video-offset-ms", v => o.VideoOffsetMilliseconds = D(v));
            Set(values, "--camera-width", v => o.CameraWidth = I(v)); Set(values, "--camera-height", v => o.CameraHeight = I(v)); Set(values, "--horizontal-fov", v => o.HorizontalFovDegrees = D(v)); Set(values, "--vertical-fov", v => o.VerticalFovDegrees = D(v));
            Set(values, "--camera-mount-roll", v => o.CameraMountRollDegrees = D(v)); Set(values, "--camera-mount-pitch", v => o.CameraMountPitchDegrees = D(v)); Set(values, "--camera-mount-yaw", v => o.CameraMountYawDegrees = D(v));
            Set(values, "--altitude-source", v => o.AltitudeSource = v); Set(values, "--max-sync-error-ms", v => o.MaximumSyncErrorMilliseconds = D(v)); Set(values, "--rotation-mode", v => o.RotationMode = (RotationCompensationMode)Enum.Parse(typeof(RotationCompensationMode), v.Replace("-", ""), true)); Set(values, "--log-level", v => o.LogLevel = v);
            o.Preview = flags.Contains("--preview"); o.Headless = flags.Contains("--headless"); if (o.Headless) o.Preview = false;
            o.AutoSync = flags.Contains("--auto-sync") || o.AutoSync; o.SaveAnnotatedVideo = flags.Contains("--save-annotated-video") || o.SaveAnnotatedVideo; o.Help = flags.Contains("--help") || flags.Contains("-h");
            o.Normalize(); return o;
        }

        public void Normalize()
        {
            StartSeconds = PositiveOrZero(StartSeconds, 0); if (!Finite(DurationSeconds)) DurationSeconds = -1;
            MaximumSyncErrorMilliseconds = Positive(MaximumSyncErrorMilliseconds, 30); GapResetSeconds = Positive(GapResetSeconds, .25);
            AutoSyncStepSeconds = Positive(AutoSyncStepSeconds, .02); AutoSyncConfidenceThreshold = Clamp(Finite(AutoSyncConfidenceThreshold) ? AutoSyncConfidenceThreshold : .35, 0, 1);
            if (AutoSyncMinimumOffsetSeconds > AutoSyncMaximumOffsetSeconds) { var swap = AutoSyncMinimumOffsetSeconds; AutoSyncMinimumOffsetSeconds = AutoSyncMaximumOffsetSeconds; AutoSyncMaximumOffsetSeconds = swap; }
            if (string.IsNullOrWhiteSpace(AltitudeSource)) AltitudeSource = "auto";
        }
        public void ValidateFiles()
        {
            if (string.IsNullOrWhiteSpace(Video) || string.IsNullOrWhiteSpace(Tlog) || string.IsNullOrWhiteSpace(Output)) throw new ArgumentException("--video, --tlog and --output are required.");
            Video = Path.GetFullPath(Video); Tlog = Path.GetFullPath(Tlog); Output = Path.GetFullPath(Output);
            if (!File.Exists(Video)) throw new FileNotFoundException("Video not found", Video);
            if (!File.Exists(Tlog)) throw new FileNotFoundException("TLOG not found", Tlog);
        }
        public static ReplayOptions Load(string path)
        {
            using (var stream = File.OpenRead(path)) return (ReplayOptions)new DataContractJsonSerializer(typeof(ReplayOptions)).ReadObject(stream);
        }
        public void SaveResolved(string path)
        {
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(ReplayOptions), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).WriteObject(stream, this);
        }
        static void Set(Dictionary<string, string> values, string key, Action<string> setter) { string value; if (values.TryGetValue(key, out value)) setter(value); }
        static int I(string value) => int.Parse(value, CultureInfo.InvariantCulture);
        static double D(string value) => double.Parse(value, CultureInfo.InvariantCulture);
        static double Positive(double value, double fallback) => Finite(value) && value > 0 ? value : fallback;
        static double PositiveOrZero(double value, double fallback) => Finite(value) && value >= 0 ? value : fallback;
        static double Clamp(double value, double lo, double hi) => System.Math.Max(lo, System.Math.Min(hi, value));
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
