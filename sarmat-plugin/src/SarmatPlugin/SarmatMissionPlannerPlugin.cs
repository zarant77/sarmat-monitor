using System;
using System.Reflection;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Windows.Forms;
using MissionPlanner.Plugin;
using SarmatPlugin.Infrastructure;

namespace SarmatPlugin
{
    public sealed class SarmatMissionPlannerPlugin : Plugin
    {
        private PluginRuntime runtime;
        private object flightData;
        private SarmatPlugin.UI.SarmatPanel panel;
        private ToolStripButton sarmatButton;
        private AppLog lifecycleLog;
        private Label takeoffModeWarning;
        private bool vehicleReconnectInProgress;
        internal const string SarmatGStreamerPipeline =
            "rtspsrc location=rtsp://192.168.69.5:554/stream=0 latency=100 ! application/x-rtp ! " +
            "decodebin3 ! queue max-size-buffers=1 leaky=2 ! videoconvert ! " +
            "video/x-raw,format=BGRA ! appsink name=outsink sync=false";

        public override string Name => "Sarmat Plugin";
        public override string Version => "1.0.0";
        public override string Author => "Sarmat";

        public override bool Init()
        {
            try
            {
                lifecycleLog = new AppLog(true);
                lifecycleLog.Info("Init started");
                loopratehz = 4;
                lifecycleLog.Info("Init completed; loopratehz=4");
                return true;
            }
            catch (Exception ex)
            {
                TryLog("Init failed", ex);
                // Logging availability must not decide whether Mission Planner loads the plugin.
                loopratehz = 4;
                return true;
            }
        }

        public override bool Loaded()
        {
            TryLog("Loaded started");
            try
            {
                var main = Host.MainForm;
                if (main == null) throw new InvalidOperationException("Mission Planner MainForm is unavailable");
                OnUi(main, () =>
                {
                    TryLog("UI thread setup started");
                    runtime = new PluginRuntime(() => Host.cs, () => Host.comPort?.packetcount);
                    runtime.TakeoffWarningChanged += SetTakeoffWarningVisible;
                    runtime.VehicleConnected += RestoreVideoOnConnect;
                    runtime.VehicleConnected += ReconnectJoystickOnConnect;
                    runtime.LimaModeRequested += SetLimaFlightMode;
                    runtime.VehicleReconnectRequested += ReconnectVehicle;
                    panel = runtime.CreatePanel();
                    panel.VideoSourceRequested += PanelVideoSourceRequested;

                    sarmatButton = new ToolStripButton
                    {
                        Name = "MenuSarmatPlugin",
                        Text = "Sarmat",
                        ToolTipText = "Open Sarmat settings",
                        AutoSize = true,
                        DisplayStyle = ToolStripItemDisplayStyle.Text
                    };
                    sarmatButton.Click += SarmatButtonClick;
                    main.MainMenu.Items.Add(sarmatButton);
                    TryLog("UI registered: MainMenu/MenuSarmatPlugin");

                    ConfigureOptionalFlightDataUi();
                });
                TryLog("Loaded completed");
                return true;
            }
            catch (Exception ex)
            {
                TryLog("Loaded failed", ex);
                var main = Host?.MainForm;
                if (main != null) OnUi(main, CleanupUiAndRuntime); else CleanupUiAndRuntime();
                return false;
            }
        }

        public override bool Loop()
        {
            try { runtime?.Tick(); } catch { }
            return true;
        }

        public override bool Exit()
        {
            TryLog("Exit started");
            try
            {
                var main = Host?.MainForm;
                if (main != null) OnUi(main, CleanupUiAndRuntime); else CleanupUiAndRuntime();
                TryLog("Exit completed");
                lifecycleLog?.Dispose();
                lifecycleLog = null;
                return true;
            }
            catch (Exception ex)
            {
                TryLog("Exit failed", ex);
                lifecycleLog?.Dispose();
                lifecycleLog = null;
                return false;
            }
        }

        private void ConfigureOptionalFlightDataUi()
        {
            try
            {
                var mainType = Host.MainForm.GetType();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                flightData = mainType.GetProperty("FlightData", flags)?.GetValue(Host.MainForm, null) ??
                    mainType.GetField("FlightData", flags)?.GetValue(Host.MainForm);
                var hud = flightData?.GetType().GetField("myhud",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
                runtime.ConfigureHud(hud);
                if (hud is Control hudControl) InstallTakeoffWarning(hudControl);
                else TryLog("Optional HUD integration skipped: HUD control is unavailable");
            }
            catch (Exception ex)
            {
                TryLog("Optional FlightData/HUD integration failed; Sarmat entry remains available", ex);
            }
        }

        private void SarmatButtonClick(object sender, EventArgs e)
        {
            try { runtime?.ShowSettings(Host.MainForm); }
            catch (Exception ex) { TryLog("Opening Sarmat UI failed", ex); }
        }

        private void PanelVideoSourceRequested(object sender, EventArgs e) => StartSarmatVideo();

        private void CleanupUiAndRuntime()
        {
            if (sarmatButton != null)
            {
                sarmatButton.Click -= SarmatButtonClick;
                sarmatButton.Owner?.Items.Remove(sarmatButton);
                sarmatButton.Dispose();
                sarmatButton = null;
            }
            if (panel != null) panel.VideoSourceRequested -= PanelVideoSourceRequested;
            if (runtime != null)
            {
                runtime.TakeoffWarningChanged -= SetTakeoffWarningVisible;
                runtime.VehicleConnected -= RestoreVideoOnConnect;
                runtime.VehicleConnected -= ReconnectJoystickOnConnect;
                runtime.LimaModeRequested -= SetLimaFlightMode;
                runtime.VehicleReconnectRequested -= ReconnectVehicle;
                runtime.Dispose();
                runtime = null;
            }
            panel = null;
            if (takeoffModeWarning != null)
            {
                takeoffModeWarning.Parent?.Controls.Remove(takeoffModeWarning);
                takeoffModeWarning.Dispose();
                takeoffModeWarning = null;
            }
            flightData = null;
        }

        private void TryLog(string message, Exception error = null)
        {
            try
            {
                if (lifecycleLog != null)
                {
                    if (error == null) lifecycleLog.Info(message); else lifecycleLog.Error(message, error);
                }
                else using (var log = new AppLog(true))
                {
                    if (error == null) log.Info(message); else log.Error(message, error);
                }
            }
            catch { }
        }

        private static Control FindNamedControl(Control root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase)) return root;
            foreach (Control child in root.Controls)
            {
                var found = FindNamedControl(child, name);
                if (found != null) return found;
            }
            return null;
        }
        private static void OnUi(Control control, Action action)
        {
            if (control.InvokeRequired) control.Invoke(action); else action();
        }

        private void InstallTakeoffWarning(Control hud)
        {
            if (hud == null) throw new InvalidOperationException("Mission Planner HUD control is unavailable");
            takeoffModeWarning = new Label
            {
                Name = "SarmatTakeoffModeWarning",
                Text = "WARNING: TAKEOFF MODE IS NOT PostHold",
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(220, 190, 0, 0),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 15, FontStyle.Bold),
                Visible = false
            };
            hud.Controls.Add(takeoffModeWarning);
            takeoffModeWarning.BringToFront();
        }

        private void SetTakeoffWarningVisible(bool visible)
        {
            var warning = takeoffModeWarning;
            if (warning == null || warning.IsDisposed) return;
            OnUi(warning, () =>
            {
                warning.Visible = visible;
                if (visible) warning.BringToFront();
            });
        }

        private void ReconnectVehicle()
        {
            var main = Host.MainForm;
            if (main == null || vehicleReconnectInProgress) return;
            vehicleReconnectInProgress = true;
            main.BeginInvoke((Action)(() =>
            {
                try
                {
                    var flags = BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static;
                    var mainType = main.GetType();
                    var comPort = (object)Host.comPort;
                    if (comPort == null) return;
                    var portName = Convert.ToString(mainType.GetField("comPortName", flags)?.GetValue(null));
                    var baudValue = mainType.GetField("comPortBaud", flags)?.GetValue(null);
                    var baud = Convert.ToString(baudValue);
                    if (string.IsNullOrWhiteSpace(portName))
                        throw new InvalidOperationException("Mission Planner connection port is unavailable");

                    var disconnect = mainType.GetMethod("doDisconnect", flags);
                    var connect = mainType.GetMethod("doConnect", flags, null,
                        new[] { comPort.GetType(), typeof(string), typeof(string), typeof(bool), typeof(bool) }, null);
                    if (disconnect == null || connect == null)
                        throw new MissingMethodException("Mission Planner reconnect API is unavailable");
                    disconnect.Invoke(main, new[] { comPort });
                    connect.Invoke(main, new[] { comPort, portName, baud, (object)true, false });
                }
                catch (Exception ex)
                {
                    try { using (var log = new AppLog(true)) log.Error("Vehicle reconnect failed", ex); } catch { }
                }
                finally { vehicleReconnectInProgress = false; }
            }));
        }

        private void StartSarmatVideo()
        {
            try
            {
                SaveMissionPlannerSetting("gstreamer_url", SarmatGStreamerPipeline);
                if (flightData == null) throw new InvalidOperationException("Mission Planner Flight Data is unavailable");

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var stream = flightData.GetType().GetField("hudGStreamer", flags)?.GetValue(null);
                if (stream == null) throw new InvalidOperationException("Mission Planner GStreamer service is unavailable");
                var type = stream.GetType();
                var launch = type.GetMethod("LookForGstreamer", flags)?.Invoke(null, null);
                SetStaticMember(type, "GstLaunch", launch);
                var exists = GetStaticMember(type, "GstLaunchExists");
                if (!(exists is bool available) || !available)
                    throw new InvalidOperationException(
                        "GStreamer was not found. Install it using Mission Planner's GStreamer video command first.");

                type.GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance)?.Invoke(stream, null);
                type.GetMethod("Start", BindingFlags.Public | BindingFlags.Instance)?.Invoke(
                    stream, new object[] { SarmatGStreamerPipeline });
                SetHudSixteenByNine();
                runtime?.MarkGStreamerStarted();
            }
            catch (TargetInvocationException ex)
            {
                ShowVideoError(ex.InnerException ?? ex);
            }
            catch (Exception ex)
            {
                ShowVideoError(ex);
            }
        }

        private void RestoreVideoOnConnect()
        {
            if (runtime?.ShouldRestoreGStreamer != true) return;
            var control = flightData as Control;
            if (control == null) return;
            OnUi(control, StartSarmatVideo);
        }

        private void ReconnectJoystickOnConnect()
        {
            var main = Host.MainForm;
            if (main == null) return;
            OnUi(main, () =>
            {
                try
                {
                    var flags = BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static;
                    var mainType = main.GetType();
                    var joystickProperty = mainType.GetProperty("joystick", flags);
                    if (joystickProperty == null) return;
                    var current = joystickProperty.GetValue(null, null);
                    if (current != null)
                    {
                        var currentType = current.GetType();
                        var enabled = currentType.GetField("enabled", flags);
                        var valid = currentType.GetMethod("IsJoystickValid", flags);
                        if (enabled != null && (bool)enabled.GetValue(current) &&
                            valid != null && (bool)valid.Invoke(current, null)) return;
                        currentType.GetMethod("UnAcquireJoyStick", flags)?.Invoke(current, null);
                        (current as IDisposable)?.Dispose();
                        joystickProperty.SetValue(null, null, null);
                    }

                    var name = GetMissionPlannerSetting("joystick_name");
                    if (string.IsNullOrWhiteSpace(name)) return;
                    var joystickBase = joystickProperty.PropertyType;
                    var getDevices = joystickBase.GetMethod("getDevices", flags);
                    var devices = (getDevices?.Invoke(null, null) as System.Collections.IEnumerable)?
                        .Cast<object>().Select(x => Convert.ToString(x)).ToArray();
                    if (devices == null || !devices.Any(x =>
                        string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) return;

                    var create = joystickBase.GetMethod("Create", flags);
                    if (create == null) return;
                    var callbackType = create.GetParameters()[0].ParameterType;
                    var returnType = callbackType.GetMethod("Invoke").ReturnType;
                    var comPort = (object)Host.comPort;
                    var callback = Expression.Lambda(callbackType,
                        Expression.Constant(comPort, returnType)).Compile();
                    var joystick = create.Invoke(null, new object[] { callback });
                    if (joystick == null) return;
                    var started = joystick.GetType().GetMethod("start", flags)?
                        .Invoke(joystick, new object[] { name });
                    if (!(started is bool) || !(bool)started)
                    {
                        (joystick as IDisposable)?.Dispose();
                        return;
                    }
                    joystick.GetType().GetField("enabled", flags)?.SetValue(joystick, true);
                    joystickProperty.SetValue(null, joystick, null);
                }
                catch
                {
                    // USB joystick restoration is best-effort and must not block vehicle connection.
                }
            });
        }

        private void SetLimaFlightMode(string mode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mode)) return;
                var comPort = (object)Host.comPort;
                if (comPort == null) throw new InvalidOperationException("Mission Planner MAVLink connection is unavailable");
                var setMode = comPort.GetType().GetMethod("setMode",
                    BindingFlags.Instance | BindingFlags.Public, null,
                    new[] { typeof(string) }, null);
                if (setMode == null) throw new MissingMethodException(comPort.GetType().FullName, "setMode(string)");
                setMode.Invoke(comPort, new object[] { mode });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to switch Lima flight mode:\r\n" + ex.Message,
                    "Sarmat Plugin", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveMissionPlannerSetting(string key, string value)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var config = Host.GetType().GetProperty("config", flags)?.GetValue(Host, null);
            if (config == null) throw new InvalidOperationException("Mission Planner settings service is unavailable");
            var indexer = config.GetType().GetProperty("Item", flags, null, typeof(string),
                new[] { typeof(string) }, null);
            if (indexer == null) throw new InvalidOperationException("Mission Planner setting indexer is unavailable");
            indexer.SetValue(config, value, new object[] { key });
        }

        private string GetMissionPlannerSetting(string key)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var config = Host.GetType().GetProperty("config", flags)?.GetValue(Host, null);
            if (config == null) return null;
            var indexer = config.GetType().GetProperty("Item", flags, null, typeof(string),
                new[] { typeof(string) }, null);
            return Convert.ToString(indexer?.GetValue(config, new object[] { key }));
        }

        private void SetHudSixteenByNine()
        {
            var staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var hud = flightData.GetType().GetField("myhud", staticFlags)?.GetValue(null);
            if (hud == null) throw new InvalidOperationException("Mission Planner HUD is unavailable");
            var instanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var hudType = hud.GetType();
            var property = hudType.GetProperty("SixteenXNine", instanceFlags);
            if (property != null)
                property.SetValue(hud, true, null);
            else
            {
                var field = hudType.GetField("SixteenXNine", instanceFlags);
                if (field == null) throw new MissingMemberException(hudType.FullName, "SixteenXNine");
                field.SetValue(hud, true);
            }
            var resize = hudType.GetMethod("doResize", instanceFlags);
            if (resize == null) throw new MissingMethodException(hudType.FullName, "doResize");
            resize.Invoke(hud, null);
        }

        private static object GetStaticMember(Type type, string name)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            return type.GetProperty(name, flags)?.GetValue(null, null) ??
                   type.GetField(name, flags)?.GetValue(null);
        }

        private static void SetStaticMember(Type type, string name, object value)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var property = type.GetProperty(name, flags);
            if (property != null) { property.SetValue(null, value, null); return; }
            var field = type.GetField(name, flags);
            if (field != null) { field.SetValue(null, value); return; }
            throw new MissingMemberException(type.FullName, name);
        }

        private static void ShowVideoError(Exception error)
        {
            MessageBox.Show("Unable to start Sarmat RTSP video:\r\n" + error.Message,
                "Sarmat Plugin", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
