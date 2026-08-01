using System;
using System.IO;

namespace SarmatPlugin.Infrastructure
{
    public static class AppPaths
    {
        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SarmatPlugin");
        public static string Settings => Path.Combine(Root, "settings.json");
        public static string LogDirectory => Path.Combine(Root, "logs");
        public static string Log => Path.Combine(LogDirectory, "sarmat-plugin.log");
        public static string AudioCache => Path.Combine(Root, "audio-cache");
    }
}
