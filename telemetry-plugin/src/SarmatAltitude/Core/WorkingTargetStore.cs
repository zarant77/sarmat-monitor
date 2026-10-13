using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SarmatAltitude.Core
{
    public static class WorkingTargetStore
    {
        [DataContract]
        private sealed class SavedTargets
        {
            [DataMember] public double? Altitude;
            [DataMember] public double? Azimuth;
        }
        public static void Load(string path, AltitudeTarget target)
        {
            if (!File.Exists(path)) return;
            using (var stream = File.OpenRead(path))
            {
                var saved = (SavedTargets)new DataContractJsonSerializer(typeof(SavedTargets)).ReadObject(stream);
                if (saved == null) throw new SerializationException("Invalid working targets");
                target.Restore(saved.Altitude, saved.Azimuth);
            }
        }
        public static void Save(string path, AltitudeTarget target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(SavedTargets)).WriteObject(stream,
                        new SavedTargets { Altitude = target.TargetMeters, Azimuth = target.TargetAzimuth });
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
