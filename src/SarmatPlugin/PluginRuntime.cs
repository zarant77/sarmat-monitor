using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SarmatPlugin.Core;
using SarmatPlugin.Infrastructure;
using SarmatPlugin.Integration;
using SarmatPlugin.UI;

namespace SarmatPlugin
{
    internal sealed class PluginRuntime : IDisposable
    {
        private readonly Func<object> currentState;
        private readonly SettingsStore store = new SettingsStore();
        private readonly AlertEngine alerts = new AlertEngine();
        private readonly TakeoffModeWarningTracker takeoffModeWarning = new TakeoffModeWarningTracker();
        private readonly RisingEdgeTrigger limaTrigger = new RisingEdgeTrigger();
        private readonly object sync = new object();
        private PluginSettings settings;
        private AppLog log;
        private AudioService audio;
        private CancellationTokenSource cancellation;
        private SarmatPanel panel;
        private ObsStatus obs = new ObsStatus();
        private RuijieStatus ruijie = new RuijieStatus();
        private bool disposed;
        private bool takeoffWarningVisible;
        private bool connectionInitialized;
        private bool wasConnected;
        private HudVisibilityAdapter hudVisibility;
        public event Action<bool> TakeoffWarningChanged;
        public event Action VehicleConnected;
        public event Action<string> LimaModeRequested;
        public bool ShouldRestoreGStreamer => settings.GStreamerWasStarted;

        public PluginRuntime(Func<object> currentState)
        {
            this.currentState = currentState;
            WidgetCatalog.Discover(currentState?.Invoke());
            settings = store.Load();
            log = new AppLog(settings.DebugLogging);
            audio = new AudioService(settings);
        }

        public SarmatPanel CreatePanel()
        {
            panel = new SarmatPanel { Visible = settings.ShowPanel, Dock = DockStyle.Top };
            panel.SettingsRequested += (s, e) => ShowSettings();
            if (settings.StartAutomatically) StartWorkers();
            return panel;
        }

        public void ConfigureHud(object hud)
        {
            hudVisibility = new HudVisibilityAdapter(hud);
            if (settings.HudElements.Count > 0) hudVisibility.Apply(settings.HudElements);
        }

        public void MarkGStreamerStarted()
        {
            if (settings.GStreamerWasStarted) return;
            settings.GStreamerWasStarted = true;
            store.Save(settings);
        }

        public void Tick()
        {
            if (disposed || panel == null) return;
            var telemetry = new TelemetryReader(currentState).Read(settings.EnabledWidgets);
            UpdateLima();
            // Mission Planner can restore its own HUD flags after Activate/connect.
            // Reconcile on every tick; the adapter only redraws when a value differs.
            hudVisibility?.Apply(settings.HudElements);
            if (!connectionInitialized)
            {
                connectionInitialized = true;
                wasConnected = telemetry.Connected;
                if (telemetry.Connected) VehicleConnected?.Invoke();
            }
            else if (telemetry.Connected && !wasConnected)
            {
                wasConnected = true;
                hudVisibility?.Apply(settings.HudElements);
                VehicleConnected?.Invoke();
            }
            else if (!telemetry.Connected)
                wasConnected = false;
            var warning = takeoffModeWarning.Update(telemetry.Armed, telemetry.FlightMode);
            if (warning != takeoffWarningVisible)
            {
                takeoffWarningVisible = warning;
                TakeoffWarningChanged?.Invoke(warning);
            }
            RuijieStatus currentRuijie;
            ObsStatus currentObs;
            lock (sync)
            {
                currentRuijie = ruijie;
                currentObs = obs;
                if (currentRuijie.LastSuccessUtc != default &&
                    (DateTime.UtcNow-currentRuijie.LastSuccessUtc).TotalSeconds >= settings.RuijieStaleSeconds)
                    currentRuijie.Stale = true;
            }
            var snapshot = alerts.Update(telemetry, currentObs, currentRuijie, settings, DateTime.UtcNow);
            audio.Update(snapshot, telemetry.Armed);
            panel.Render(telemetry, currentObs, currentRuijie, snapshot, settings);
        }

        private void UpdateLima()
        {
            if (!settings.LimaEnabled)
            {
                limaTrigger.Reset();
                return;
            }
            var pwm = new TelemetryReader(currentState).ReadRcInput(settings.LimaRcChannel);
            if (pwm < 800 || pwm > 2200)
            {
                limaTrigger.Reset();
                return;
            }
            var pressed = settings.LimaPressedWhenHigh
                ? pwm >= settings.LimaPwmThreshold
                : pwm <= settings.LimaPwmThreshold;
            if (limaTrigger.Update(pressed)) LimaModeRequested?.Invoke(settings.LimaFlightMode);
        }

        private void StartWorkers()
        {
            StopWorkers();
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            Task.Run(() => ObsLoop(token), token).ContinueWith(t => LogFault("OBS worker", t), TaskScheduler.Default);
            Task.Run(() => RuijieLoop(token), token).ContinueWith(t => LogFault("Ruijie worker", t), TaskScheduler.Default);
        }
        private void StopWorkers()
        {
            cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
            audio.Stop();
        }
        private async Task ObsLoop(CancellationToken token)
        {
            var client = new ObsClient(settings, log);
            var transitions = new ObsArmingTransitionTracker(
                new TelemetryReader(currentState).Read().Armed);
            while (!token.IsCancellationRequested)
            {
                var armed = new TelemetryReader(currentState).Read().Armed;
                var pending = transitions.PendingCommand(armed);
                var value = pending.HasValue
                    ? await client.SynchronizeRecordingAsync(pending.Value, token).ConfigureAwait(false)
                    : await client.QueryAsync(token).ConfigureAwait(false);
                if (pending.HasValue && value.Connected)
                    transitions.Confirm(armed);
                lock (sync) obs = value;
                await Task.Delay(TimeSpan.FromSeconds(value.Connected ? 1 : settings.ObsReconnectSeconds), token).ConfigureAwait(false);
            }
        }
        private async Task RuijieLoop(CancellationToken token)
        {
            using (var client = new RuijieClient(settings, log))
            {
                while (!token.IsCancellationRequested)
                {
                    var value = await client.GetStatusAsync(token).ConfigureAwait(false);
                    lock (sync)
                    {
                        if (value.Connected) ruijie = value;
                        else
                        {
                            value.LastSuccessUtc = ruijie.LastSuccessUtc;
                            value.Rssi = ruijie.Rssi;
                            value.SignalQuality = ruijie.SignalQuality;
                            value.Stale = value.LastSuccessUtc != default &&
                                (DateTime.UtcNow-value.LastSuccessUtc).TotalSeconds >= settings.RuijieStaleSeconds;
                            ruijie = value;
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(settings.RuijiePollSeconds), token).ConfigureAwait(false);
                }
            }
        }

        private void ShowSettings()
        {
            if (panel == null) return;
            var form = new SettingsForm(settings,
                async ct =>
                {
                    var result = await new ObsClient(settings, log).QueryAsync(ct).ConfigureAwait(false);
                    return result.Connected
                        ? "Current status: Connected; recording: " + (result.Recording == true ? "Yes" : "No")
                        : "Current status: Disconnected — " + result.Error;
                },
                async ct =>
                {
                    using (var client = new RuijieClient(settings, log))
                    {
                        var result = await client.GetStatusAsync(ct).ConfigureAwait(false);
                        return result.Connected
                            ? $"Current status: Connected; RSSI: {result.Rssi} dBm; quality: {result.SignalQuality}"
                            : "Current status: Disconnected — " + result.Error;
                    }
                }, severity => audio.Test(severity), hudVisibility?.Read());
            if (form.ShowDialog(panel.FindForm()) != DialogResult.OK || form.Result == null) return;
            settings = form.Result;
            store.Save(settings);
            hudVisibility?.Apply(settings.HudElements);
            log.DebugEnabled = settings.DebugLogging;
            audio.UpdateSettings(settings);
            panel.Visible = settings.ShowPanel;
            alerts.Reset();
            if (settings.StartAutomatically) StartWorkers(); else StopWorkers();
            log.Info("Settings updated");
        }

        private void LogFault(string worker, Task task)
        {
            if (task.IsFaulted && task.Exception != null && !disposed) log.Error(worker + " stopped", task.Exception.Flatten());
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            StopWorkers(); audio.Dispose(); panel?.Dispose(); log.Info("Plugin stopped"); log.Dispose();
        }
    }
}
