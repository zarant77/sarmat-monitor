using System;
using System.IO;
using System.Media;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SarmatPlugin.Core;

namespace SarmatPlugin.Infrastructure
{
    public sealed class AudioService : IDisposable
    {
        private readonly object sync = new object();
        private PluginSettings settings;
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private Severity active;
        private bool disposed;

        public AudioService(PluginSettings settings) { this.settings = settings; }
        public void UpdateSettings(PluginSettings value) { lock (sync) settings = value; }

        public void Update(SafetySnapshot snapshot, bool armed)
        {
            lock (sync)
            {
                if (!armed || snapshot.Severity < Severity.Warning || !CanPlay)
                {
                    CancelLocked();
                    if (snapshot.Restored && armed && CanPlay) StartLocked(Severity.Ok, false);
                    return;
                }
                if (active != snapshot.Severity)
                {
                    CancelLocked();
                    StartLocked(snapshot.Severity, true);
                }
            }
        }

        public void Test(Severity severity)
        {
            lock (sync) { CancelLocked(); StartLocked(severity, false); }
        }
        public void Stop() { lock (sync) CancelLocked(); }
        private bool CanPlay => !disposed && settings.AudioEnabled && !settings.AudioMuted && settings.AudioVolume > 0;
        private void StartLocked(Severity severity, bool repeat)
        {
            if (!CanPlay) return;
            active = severity;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var interval = TimeSpan.FromSeconds(settings.RepeatIntervalSeconds);
            Task.Run(async () =>
            {
                do
                {
                    await PlayPatternAsync(severity, settings.AudioVolume, token).ConfigureAwait(false);
                    if (!repeat) break;
                    await Task.Delay(interval, token).ConfigureAwait(false);
                } while (!token.IsCancellationRequested);
            }, token).ContinueWith(_ => { }, TaskScheduler.Default);
        }
        private static async Task PlayPatternAsync(Severity severity, double volume, CancellationToken token)
        {
            var count = severity == Severity.Critical ? 3 : severity == Severity.Warning ? 1 : 2;
            for (var i = 0; i < count && !token.IsCancellationRequested; i++)
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SarmatPlugin.Assets.warning.wav"))
                using (var scaled = new MemoryStream(ScalePcmWav(ReadAll(stream), volume), false))
                using (var player = new SoundPlayer(scaled))
                {
                    player.PlaySync();
                }
                if (i + 1 < count) await Task.Delay(220, token).ConfigureAwait(false);
            }
        }
        internal static byte[] ScalePcmWav(byte[] source, double volume)
        {
            if (source == null || source.Length < 44 || Encoding.ASCII.GetString(source, 0, 4) != "RIFF" ||
                Encoding.ASCII.GetString(source, 8, 4) != "WAVE") throw new InvalidDataException("Invalid WAV resource");
            volume = Math.Max(0, Math.Min(1, volume));
            var output = (byte[])source.Clone();
            var format = 0; var bits = 0; var dataOffset = 0; var dataLength = 0; var offset = 12;
            while (offset + 8 <= output.Length)
            {
                var id = Encoding.ASCII.GetString(output, offset, 4);
                var length = BitConverter.ToInt32(output, offset + 4);
                var body = offset + 8;
                if (length < 0 || body + length > output.Length) throw new InvalidDataException("Truncated WAV chunk");
                if (id == "fmt " && length >= 16) { format = BitConverter.ToUInt16(output, body); bits = BitConverter.ToUInt16(output, body + 14); }
                if (id == "data") { dataOffset = body; dataLength = length; break; }
                offset = body + length + (length & 1);
            }
            if (format != 1 || bits != 16 || dataOffset == 0) return output;
            for (var i = dataOffset; i + 1 < dataOffset + dataLength; i += 2)
            {
                var sample = BitConverter.ToInt16(output, i);
                var scaled = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(sample * volume)));
                var bytes = BitConverter.GetBytes(scaled); output[i] = bytes[0]; output[i + 1] = bytes[1];
            }
            return output;
        }
        private static byte[] ReadAll(Stream input)
        {
            using (var output = new MemoryStream()) { input.CopyTo(output); return output.ToArray(); }
        }
        private void CancelLocked()
        {
            active = Severity.Inactive;
            cancellation.Cancel(); cancellation.Dispose(); cancellation = new CancellationTokenSource();
        }
        public void Dispose() { lock (sync) { disposed = true; CancelLocked(); cancellation.Dispose(); } }
    }
}
