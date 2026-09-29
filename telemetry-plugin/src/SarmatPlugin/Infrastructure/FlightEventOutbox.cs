using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SarmatPlugin.Infrastructure
{
    internal sealed class FlightEventOutbox
    {
        private readonly string directory;
        public FlightEventOutbox(string root, string endpoint, string secret)
        {
            using (var hash = SHA256.Create())
                directory = Path.Combine(root, "flight-events", BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(endpoint + "\n" + secret))).Replace("-", ""));
        }
        public void Store(Dictionary<string, object> item)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.Parse((string)item["id"]).ToString() + ".json");
            if (File.Exists(path)) return;
            var temp = path + ".tmp";
            File.WriteAllText(temp, MiniJson.Serialize(item), Encoding.UTF8);
            File.Move(temp, path);
        }
        public string[] Pending() => Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json").OrderBy(File.GetCreationTimeUtc).ToArray() : Array.Empty<string>();
        public string Read(string path) => File.ReadAllText(path, Encoding.UTF8);
        public void Acknowledge(string path) => File.Delete(path);
        public void Reject(string path) => File.Move(path, path + ".rejected");
    }
}
