using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly AlertTransitionTracker tracker = new AlertTransitionTracker();
        private readonly Queue<Severity> pending = new Queue<Severity>();
        private PluginSettings settings;
        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool workerRunning;
        private bool disposed;
        private DateTime nextAlertUtc;

        public AudioService(PluginSettings settings) { this.settings = settings; }
        public void UpdateSettings(PluginSettings value) { lock (sync) settings = value; }

        public void Update(SafetySnapshot snapshot, bool armed)
        {
            lock (sync)
            {
                if (!armed)
                {
                    tracker.Reset();
                    nextAlertUtc = default;
                    CancelLocked();
                    return;
                }
                var newlyActive = tracker.SelectNew(snapshot.Reasons, true);
                if (!CanPlay)
                {
                    CancelPlaybackLocked();
                    return;
                }
                if (newlyActive.Count == 0 || DateTime.UtcNow < nextAlertUtc) return;
                pending.Enqueue(newlyActive.Max(x => x.Severity));
                nextAlertUtc = DateTime.UtcNow.AddSeconds(settings.AudioAlertCooldownSeconds);
                StartWorkerLocked();
            }
        }

        public void Test()
        {
            lock (sync)
            {
                if (!CanPlay) return;
                pending.Enqueue(Severity.Warning);
                StartWorkerLocked();
            }
        }

        public void Stop() { lock (sync) CancelPlaybackLocked(); }
        private bool CanPlay => !disposed && settings.AudioEnabled && settings.AudioVolume > 0;

        private void StartWorkerLocked()
        {
            if (workerRunning || pending.Count == 0 || !CanPlay) return;
            workerRunning = true;
            var token = cancellation.Token;
            Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    double volume;
                    string soundPath;
                    lock (sync)
                    {
                        if (pending.Count == 0 || !CanPlay) { workerRunning = false; return; }
                        pending.Dequeue();
                        volume = settings.AudioVolume;
                        soundPath = settings.AudioWarningSoundPath;
                    }
                    PlayOnce(volume, soundPath, token);
                }
            }, token).ContinueWith(_ =>
            {
                lock (sync) workerRunning = false;
            }, TaskScheduler.Default);
        }

        private static void PlayOnce(double volume, string soundPath, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            var warning = LoadScaledWarning(soundPath, volume);
            using (var scaled = new MemoryStream(warning, false))
            using (var player = new SoundPlayer(scaled)) player.PlaySync();
        }

        private static byte[] LoadScaledWarning(string soundPath, double volume)
        {
            if (!string.IsNullOrWhiteSpace(soundPath))
            {
                try { return ScalePcmWav(File.ReadAllBytes(soundPath), volume); }
                catch { }
            }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SarmatPlugin.Assets.warning.wav"))
                return ScalePcmWav(ReadAll(stream), volume);
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
                var id = Encoding.ASCII.GetString(output, offset, 4); var length = BitConverter.ToInt32(output, offset + 4); var body = offset + 8;
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
        private void CancelPlaybackLocked()
        {
            pending.Clear(); cancellation.Cancel(); cancellation.Dispose(); cancellation = new CancellationTokenSource(); workerRunning = false;
        }
        private void CancelLocked() { CancelPlaybackLocked(); }
        public void Dispose() { lock (sync) { disposed = true; tracker.Reset(); CancelLocked(); cancellation.Dispose(); } }
    }
}
