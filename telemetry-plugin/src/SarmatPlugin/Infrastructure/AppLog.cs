using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SarmatPlugin.Infrastructure
{
    public sealed class AppLog : IDisposable
    {
        private const long MaxBytes = 5 * 1024 * 1024;
        private static readonly Regex Secrets = new Regex(
            "(?i)(password|pwd|token|sid|auth|authorization|cookie|webauth|key)([\"'\\s:=]+)([^\"'&\\s,;}]+)",
            RegexOptions.Compiled);
        private static readonly object Sync = new object();
        public bool DebugEnabled { get; set; }

        public AppLog(bool debug)
        {
            DebugEnabled = debug;
            lock (Sync)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                Rotate();
            }
        }

        public void Debug(string message) { if (DebugEnabled) Write("DEBUG", message); }
        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message, Exception error = null) =>
            Write("ERROR", error == null ? message : message + ": " + error);

        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var sanitized = Secrets.Replace(value, "$1$2<redacted>");
            return sanitized.Length > 16384 ? sanitized.Substring(0, 16384) + "...(truncated)" : sanitized;
        }

        private void Write(string level, string message)
        {
            lock (Sync)
            {
                // Open for each record so every AppLog instance shares one serialized file
                // position. Multiple StreamWriters otherwise corrupt and reorder the log.
                File.AppendAllText(AppPaths.Log,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {Sanitize(message)}{Environment.NewLine}");
            }
        }

        private static void Rotate()
        {
            if (!File.Exists(AppPaths.Log) || new FileInfo(AppPaths.Log).Length < MaxBytes) return;
            var old = AppPaths.Log + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(AppPaths.Log, old);
        }

        public void Dispose() { }
    }
}
