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
        private readonly Func<long?> packetCount;
        private readonly SettingsStore store = new SettingsStore();
        private readonly AlertEngine alerts = new AlertEngine();
        private readonly TakeoffModeWarningTracker takeoffModeWarning = new TakeoffModeWarningTracker();
        private readonly MavlinkSilenceWatchdog mavlinkWatchdog = new MavlinkSilenceWatchdog();
        private readonly object sync = new object();
        private PluginSettings settings;
        private AppLog log;
        private AudioService audio;
        private CancellationTokenSource cancellation;
        private SarmatPanel panel;
        private SettingsForm settingsForm;
        private BatterySelectionForm batterySelectionForm;
        private readonly BatteryVoltageTracker batteryTracker = new BatteryVoltageTracker();
        private string batteryChargeSessionId;
        private int? batteryChargePercent;
        private DateTime batteryChargeUpdatedAt;
        private readonly FlightTracker flightTracker = new FlightTracker();
        private readonly BatteryDialogConnection batteryConnection = new BatteryDialogConnection();
        private readonly BatteryReplacementDetector batteryReplacement = new BatteryReplacementDetector();
        private bool batteryReselectionPending;
        private ObsStatus obs = new ObsStatus();
        private RuijieStatus ruijie = new RuijieStatus();
        private string aggregatorStatus = "Disabled";
        private bool disposed;
        private bool takeoffWarningVisible;
        private bool connectionInitialized;
        private bool wasConnected;
        private HudVisibilityAdapter hudVisibility;
        public event Action<bool> TakeoffWarningChanged;
        public event Action VehicleConnected;
        public event Action VehicleReconnectRequested;
        public event Action CameraSettingsChanged;
        public bool ShouldRestoreGStreamer => settings.CameraEnabled && settings.GStreamerWasStarted;
        public PluginSettings CurrentSettings => settings;

        public PluginRuntime(Func<object> currentState, Func<long?> packetCount = null)
        {
            this.currentState = currentState;
            this.packetCount = packetCount;
            WidgetCatalog.Discover(currentState?.Invoke());
            settings = store.Load();
            log = new AppLog(settings.DebugLogging);
            audio = new AudioService(settings);
        }

        public SarmatPanel CreatePanel()
        {
            panel = new SarmatPanel { Visible = true, Dock = DockStyle.Top };
            panel.SettingsRequested += PanelSettingsRequested;
            panel.BatterySelectionRequested += PanelBatterySelectionRequested;
            StartWorkers();
            return panel;
        }

        private void PanelSettingsRequested(object sender, EventArgs e) => ShowSettings();

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
            bool offerBattery;
            lock (sync)
            {
                offerBattery = batteryConnection.Update(telemetry.Connected, DateTime.UtcNow);
                if (settings.AggregatorEnabled && settings.BatteryTrackingEnabled && telemetry.Connected)
                {
                    if (batteryTracker.SessionId == null) batteryTracker.Connect(DateTime.UtcNow);
                }
                else if (!settings.AggregatorEnabled || !settings.BatteryTrackingEnabled || batteryConnection.Disconnected)
                { PersistVoltageEvents(); PersistFlightEvents(); batteryTracker.Reset(); flightTracker.Reset(); }
                if (offerBattery) batteryReselectionPending = true;
                offerBattery = batteryReselectionPending && telemetry.Connected && !telemetry.Armed &&
                    settings.AggregatorEnabled && settings.BatteryTrackingEnabled;
                if (offerBattery) batteryReselectionPending = false;
            }
            if (batteryConnection.Disconnected) CloseBatterySelection();
            if (offerBattery) { CloseBatterySelection(); ShowBatterySelection(); }
            UpdateVehicleReconnect(telemetry);
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
            {
                wasConnected = false;
            }
            var warning = takeoffModeWarning.Update(telemetry.Armed, telemetry.FlightMode,
                settings.TakeoffModeWarningEnabled, settings.SafeArmingModes);
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
            int? chargePercent;
            lock (sync) chargePercent = telemetry.Connected && batteryTracker.IsConfirmed &&
                batteryTracker.SessionId == batteryChargeSessionId && (DateTime.UtcNow - batteryChargeUpdatedAt).TotalSeconds <= 20
                ? batteryChargePercent : null;
            panel.Render(telemetry, currentObs, currentRuijie, snapshot, settings, chargePercent);
        }

        private void UpdateVehicleReconnect(TelemetrySnapshot telemetry)
        {
            if (!settings.VehicleAutoReconnectEnabled || packetCount == null)
            {
                mavlinkWatchdog.Reset();
                return;
            }

            long? count;
            try { count = packetCount(); }
            catch { count = null; }
            if (!mavlinkWatchdog.Update(telemetry.Connected, count, DateTime.UtcNow,
                settings.VehicleReconnectTimeoutSeconds)) return;

            log.Info("No MAVLink packets for " + settings.VehicleReconnectTimeoutSeconds.ToString("0") +
                " seconds; requesting Mission Planner reconnect");
            VehicleReconnectRequested?.Invoke();
        }

        private void StartWorkers()
        {
            StopWorkers(false);
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            lock (sync) { obs = new ObsStatus(); ruijie = new RuijieStatus(); }
            if (settings.ObsEnabled) Task.Run(() => ObsLoop(token), token).ContinueWith(t => LogFault("OBS worker", t), TaskScheduler.Default);
            if (settings.RuijieEnabled) Task.Run(() => RuijieLoop(token), token).ContinueWith(t => LogFault("Ruijie worker", t), TaskScheduler.Default);
            Task.Run(() => AggregatorLoop(token), token)
                .ContinueWith(t => LogFault("Aggregator worker", t), TaskScheduler.Default);
            if (settings.AggregatorEnabled && settings.BatteryTrackingEnabled)
            {
                var apiSettings = settings;
                Task.Run(() => BatteryEventLoop(apiSettings, token), token).ContinueWith(t => LogFault("Battery events worker", t), TaskScheduler.Default);
            }
        }
        private void StopWorkers(bool resetBattery = true)
        {
            if (resetBattery) CloseBatterySelection();
            lock (sync) { PersistVoltageEvents(); PersistFlightEvents(); if (resetBattery) { batteryTracker.Reset(); flightTracker.Reset(); batteryConnection.Reset(); batteryReplacement.Reset(); batteryReselectionPending = false; } }
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
                lock (sync)
                {
                    if (token.IsCancellationRequested) return;
                    obs = value;
                }
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
                        if (token.IsCancellationRequested) return;
                        if (value.Connected) ruijie = value;
                        else
                        {
                            value.LastSuccessUtc = ruijie.LastSuccessUtc;
                            value.Rssi = ruijie.Rssi;
                            value.QualityPercent = ruijie.QualityPercent;
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

        private Task AggregatorLoop(CancellationToken token)
        {
            var client = new AggregatorClient(settings, log, value =>
            {
                lock (sync) aggregatorStatus = value;
            });
            return client.RunAsync(
                () => new TelemetryReader(currentState).Read(),
                () => { lock (sync) return obs; },
                () => { lock (sync) return ruijie; }, token);
        }

        public void ShowSettings(IWin32Window owner = null)
        {
            if (disposed) return;
            if (settingsForm != null && !settingsForm.IsDisposed)
            {
                if (settingsForm.WindowState == FormWindowState.Minimized)
                    settingsForm.WindowState = FormWindowState.Normal;
                settingsForm.Activate();
                settingsForm.BringToFront();
                return;
            }

            settingsForm = new SettingsForm(settings,
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
                },
                async (url, secret, ct) =>
                {
                    await AggregatorClient.TestConnectionAsync(url, secret, ct)
                        .ConfigureAwait(false);
                    return "Current status: Connected";
                },
                () => { lock (sync) return aggregatorStatus; },
                () => audio.Test(), hudVisibility?.Read());
            DialogResult result;
            try
            {
                result = settingsForm.ShowDialog(owner ?? panel?.FindForm());
                if (result != DialogResult.OK || settingsForm.Result == null) return;
                var next = settingsForm.Result;
                var resetBattery = settings.AggregatorEnabled != next.AggregatorEnabled || settings.BatteryTrackingEnabled != next.BatteryTrackingEnabled ||
                    settings.AggregatorUrl != next.AggregatorUrl || settings.AggregatorSecret != next.AggregatorSecret;
                StopWorkers(resetBattery); // Flush with the old API identity before applying new settings.
                if (resetBattery) connectionInitialized = false;
                settings = settingsForm.Result;
            }
            finally
            {
                settingsForm?.Dispose();
                settingsForm = null;
            }
            store.Save(settings);
            hudVisibility?.Apply(settings.HudElements);
            log.DebugEnabled = settings.DebugLogging;
            audio.UpdateSettings(settings);
            alerts.Reset();
            StartWorkers();
            CameraSettingsChanged?.Invoke();
            Tick();
            log.Info("Settings updated");
        }

        private void ShowBatterySelection()
        {
            if (disposed || !settings.AggregatorEnabled || !settings.BatteryTrackingEnabled || panel == null) return;
            if (panel.InvokeRequired)
            {
                panel.BeginInvoke(new Action(ShowBatterySelection));
                return;
            }
            var telemetry = new TelemetryReader(currentState).Read();
            if (!telemetry.Connected || telemetry.Armed) return;
            if (batterySelectionForm != null && !batterySelectionForm.IsDisposed) { batterySelectionForm.Activate(); return; }
            string sessionId;
            lock (sync) sessionId = batteryTracker.SessionId;
            if (sessionId == null) return;
            batterySelectionForm = new BatterySelectionForm(settings, sessionId, () =>
            {
                lock (sync) return !disposed && settings.AggregatorEnabled && settings.BatteryTrackingEnabled &&
                    batteryTracker.SessionId == sessionId && new TelemetryReader(currentState).Read().Connected && !new TelemetryReader(currentState).Read().Armed;
            });
            batterySelectionForm.BatteryConfirmed += id => { lock (sync) { batteryTracker.Confirm(id); flightTracker.Confirm(id); PersistVoltageEvents(); PersistFlightEvents(); } };
            batterySelectionForm.FormClosed += (sender, args) => { if (ReferenceEquals(batterySelectionForm, sender)) batterySelectionForm = null; };
            // Modeless: telemetry, reconnect handling and Mission Planner remain responsive.
            batterySelectionForm.Show(panel.FindForm());
        }

        private void CloseBatterySelection()
        {
            var form = batterySelectionForm;
            if (form == null || form.IsDisposed) return;
            if (form.InvokeRequired) form.BeginInvoke(new Action(() => { if (!form.IsDisposed) form.Close(); }));
            else form.Close();
        }

        private void PanelBatterySelectionRequested(object sender, EventArgs e)
        {
            if (!settings.AggregatorEnabled || !settings.BatteryTrackingEnabled)
            {
                MessageBox.Show(panel.FindForm(), "Enable both Enabled and Automatic battery charge tracking on the API tab, then save the settings.", "Select battery");
                return;
            }
            if (!new TelemetryReader(currentState).Read().Connected)
            {
                MessageBox.Show(panel.FindForm(), "Connect Mission Planner to the drone first.", "Select battery");
                return;
            }
            if (batterySelectionForm == null || batterySelectionForm.IsDisposed)
            {
                lock (sync)
                {
                    PersistVoltageEvents(); PersistFlightEvents();
                    // A confirmed session cannot be rebound server-side. Explicit reselection
                    // starts a new binding without inventing another connection measurement.
                    if (batteryTracker.SessionId == null) batteryTracker.Connect(DateTime.UtcNow);
                    else if (batteryTracker.IsConfirmed) { batteryTracker.Connect(DateTime.UtcNow, false); flightTracker.Reset(); }
                    batteryConnection.MarkOffered();
                }
            }
            ShowBatterySelection();
        }

        private void LogFault(string worker, Task task)
        {
            if (task.IsFaulted && task.Exception != null && !disposed) log.Error(worker + " stopped", task.Exception.Flatten());
        }

        public void ObserveBatteryVoltage(double voltage, double? currentAmps = null)
        {
            lock (sync) if (!disposed && settings.AggregatorEnabled && settings.BatteryTrackingEnabled)
            {
                var now = DateTime.UtcNow;
                if (batteryReplacement.Voltage(voltage, currentAmps, now) && batteryTracker.IsConfirmed)
                {
                    RequireBatteryConfirmation(now, "Idle voltage increased; battery selection required");
                    // The new tracker needs a fresh heartbeat before it can accept this sample.
                    batteryTracker.Heartbeat(false, now);
                }
                // Do not assign a possible new pack's voltage/consumption to the previous battery.
                if (batteryReplacement.SuspectedReplacement && batteryTracker.IsConfirmed) return;
                batteryTracker.Voltage(voltage, now, currentAmps);
            }
        }
        private void RequireBatteryConfirmation(DateTime now, string reason)
        {
            // Flush samples to their old immutable binding before starting an unconfirmed session.
            PersistVoltageEvents(); PersistFlightEvents();
            batteryTracker.Connect(now); flightTracker.Reset();
            batteryChargeSessionId = null; batteryChargePercent = null;
            batteryReselectionPending = true;
            log.Info(reason);
        }
        public void ObserveBatteryHeartbeat(bool armed)
        {
            lock (sync) if (!disposed && settings.AggregatorEnabled && settings.BatteryTrackingEnabled)
            {
                var now = DateTime.UtcNow;
                if (batteryReplacement.Heartbeat(armed, now) && batteryTracker.IsConfirmed)
                    RequireBatteryConfirmation(now, "Fresh heartbeat after connection gap; battery selection required");
                batteryTracker.Heartbeat(armed, now); flightTracker.Heartbeat(armed, now);
                PersistFlightEvents();
            }
        }
        public void ResetBatteryConnection()
        {
            lock (sync) { PersistVoltageEvents(); PersistFlightEvents(); batteryTracker.Reset(); flightTracker.Reset(); batteryConnection.Reset(); batteryReplacement.Reset(); batteryReselectionPending = false; }
            wasConnected = false; connectionInitialized = false;
            CloseBatterySelection();
        }
        private void PersistVoltageEvents()
        {
            foreach (var item in batteryTracker.Ready())
            {
                try
                {
                    var outbox = new BatteryEventOutbox(AppPaths.Root, settings.AggregatorUrl, settings.AggregatorSecret);
                    outbox.Store(item); batteryTracker.Stored(item);
                }
                catch (Exception ex) { log.Warn("Could not persist battery voltage event: " + ex.Message); }
            }
        }
        private void PersistFlightEvents()
        {
            foreach (var item in flightTracker.Ready())
            {
                try
                {
                    var outbox = new FlightEventOutbox(AppPaths.Root, settings.AggregatorUrl, settings.AggregatorSecret);
                    outbox.Store(item); flightTracker.Stored(item);
                }
                catch (Exception ex) { log.Warn("Could not persist flight event: " + ex.Message); }
            }
        }
        private async Task BatteryEventLoop(PluginSettings apiSettings, CancellationToken token)
        {
            var outbox = new BatteryEventOutbox(AppPaths.Root, apiSettings.AggregatorUrl, apiSettings.AggregatorSecret);
            var flightOutbox = new FlightEventOutbox(AppPaths.Root, apiSettings.AggregatorUrl, apiSettings.AggregatorSecret);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    lock (sync) { if (token.IsCancellationRequested) return; PersistVoltageEvents(); PersistFlightEvents(); }
                    using (var api = new BatteryApiClient(apiSettings))
                    foreach (var path in outbox.Pending())
                    {
                        token.ThrowIfCancellationRequested();
                        if (await api.SendVoltageAsync(outbox.Read(path), token).ConfigureAwait(false)) outbox.Acknowledge(path);
                        else { outbox.Reject(path); log.Warn("Battery voltage event rejected; retained in " + path + ".rejected"); }
                    }
                    using (var api = new BatteryApiClient(apiSettings))
                    foreach (var path in flightOutbox.Pending())
                    {
                        token.ThrowIfCancellationRequested();
                        if (await api.SendFlightAsync(flightOutbox.Read(path), token).ConfigureAwait(false)) flightOutbox.Acknowledge(path);
                        else { flightOutbox.Reject(path); log.Warn("Flight event rejected; retained in " + path + ".rejected"); }
                    }
                    string chargeSessionId;
                    lock (sync) chargeSessionId = batteryTracker.IsConfirmed ? batteryTracker.SessionId : null;
                    if (chargeSessionId != null)
                    {
                        using (var api = new BatteryApiClient(apiSettings))
                        {
                            var percent = await api.LoadChargeAsync(chargeSessionId, token).ConfigureAwait(false);
                            lock (sync) if (!token.IsCancellationRequested && batteryTracker.SessionId == chargeSessionId && batteryTracker.IsConfirmed)
                            {
                                batteryChargeSessionId = chargeSessionId; batteryChargePercent = percent;
                                batteryChargeUpdatedAt = DateTime.UtcNow;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception ex) { log.Warn("Battery voltage delivery pending: " + ex.Message); }
                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (settingsForm != null)
            {
                settingsForm.Close();
                settingsForm.Dispose();
                settingsForm = null;
            }
            if (panel != null)
            {
                panel.SettingsRequested -= PanelSettingsRequested;
                panel.BatterySelectionRequested -= PanelBatterySelectionRequested;
                panel.Dispose();
                panel = null;
            }
            StopWorkers(); audio.Dispose(); log.Info("Plugin stopped"); log.Dispose();
        }
    }
}
