using System.IO;
using System.Runtime.Serialization.Json;
using SarmatPlugin.Core;

namespace SarmatPlugin.Infrastructure
{
    public sealed class SettingsStore
    {
        public PluginSettings Load()
        {
            Directory.CreateDirectory(AppPaths.Root);
            if (!File.Exists(AppPaths.Settings))
            {
                var created = new PluginSettings();
                Save(created);
                return created;
            }
            using (var stream = File.OpenRead(AppPaths.Settings))
            {
                var serializer = new DataContractJsonSerializer(typeof(PluginSettings),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                var settings = (PluginSettings)serializer.ReadObject(stream);
                settings.Normalize();
                return settings;
            }
        }

        public void Save(PluginSettings settings)
        {
            settings.Normalize();
            Directory.CreateDirectory(AppPaths.Root);
            var temp = AppPaths.Settings + ".tmp";
            using (var stream = File.Create(temp))
            {
                var serializer = new DataContractJsonSerializer(typeof(PluginSettings),
                    new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                serializer.WriteObject(stream, settings);
            }
            if (File.Exists(AppPaths.Settings))
                File.Replace(temp, AppPaths.Settings, AppPaths.Settings + ".bak", true);
            else
                File.Move(temp, AppPaths.Settings);
        }
    }
}
