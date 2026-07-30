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
        private readonly object sync = new object();
        private StreamWriter writer;
        public bool DebugEnabled { get; set; }

        public AppLog(bool debug)
        {
            DebugEnabled = debug;
            Directory.CreateDirectory(AppPaths.LogDirectory);
            Rotate();
            writer = new StreamWriter(new FileStream(AppPaths.Log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                { AutoFlush = true };
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
            lock (sync)
            {
                if (writer == null) return;
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {Sanitize(message)}");
            }
        }

        private static void Rotate()
        {
            if (!File.Exists(AppPaths.Log) || new FileInfo(AppPaths.Log).Length < MaxBytes) return;
            var old = AppPaths.Log + ".1";
            if (File.Exists(old)) File.Delete(old);
            File.Move(AppPaths.Log, old);
        }

        public void Dispose()
        {
            lock (sync) { writer?.Dispose(); writer = null; }
        }
    }
}
