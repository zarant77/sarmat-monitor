using System;
using System.Collections;
using System.Reflection;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Linq.Expressions;
using System.Windows.Forms;
using MissionPlanner.Plugin;
using SarmatPlugin.Infrastructure;
using SarmatPlugin.Core;

namespace SarmatPlugin
{
    public sealed class SarmatMissionPlannerPlugin : Plugin
    {
        private PluginRuntime runtime;
        private object flightData;
        private SarmatPlugin.UI.SarmatPanel panel;
        private TabControl hostTabs;
        private TabPage sarmatTab;
        private IList originalTabs;
        private AppLog lifecycleLog;
        private Label takeoffModeWarning;
        private bool vehicleReconnectInProgress;
        private object savedVehicleBaseStream;
        private string savedVehiclePortName;
        private string savedVehicleBaud;
        private string savedVehicleHost;
        private int savedVehicleNetworkPort;
        private bool tabRegistrationPendingLogged;
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
                    InitializeRuntime();
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

        private void InitializeRuntime()
        {
            try
            {
                TryLog("Runtime initialization started");
                runtime = new PluginRuntime(() => Host.cs, () => Host.comPort?.packetcount);
                runtime.TakeoffWarningChanged += SetTakeoffWarningVisible;
                runtime.VehicleConnected += RestoreVideoOnConnect;
                runtime.VehicleConnected += ReconnectJoystickOnConnect;
                runtime.VehicleConnected += SaveVehicleConnectionOnConnect;
                runtime.VehicleReconnectRequested += ReconnectVehicle;
                panel = runtime.CreatePanel();
                panel.VideoSourceRequested += PanelVideoSourceRequested;
                panel.VehicleReconnectRequested += PanelVehicleReconnectRequested;
                ConfigureOptionalFlightDataUi();
                if (!RegisterSarmatTab())
                {
                    tabRegistrationPendingLogged = true;
                    TryLog("Sarmat tab registration deferred: FlightData controls are not ready yet");
                }
                TryLog("Runtime initialization completed");
            }
            catch (Exception ex)
            {
                TryLog("Runtime initialization failed", ex);
                DisposeRuntime();
                throw;
            }
        }

        private bool RegisterSarmatTab()
        {
            try
            {
                if (sarmatTab != null && !sarmatTab.IsDisposed && hostTabs != null &&
                    !hostTabs.IsDisposed && hostTabs.TabPages.Contains(sarmatTab)) return true;
                if (sarmatTab != null) RemoveSarmatTab();
                ResolveFlightData();
                var main = Host.MainForm as Control;
                hostTabs = FindNamedControl(flightData as Control, "tabControlactions") as TabControl ??
                    FindNamedControl(main, "tabControlactions") as TabControl;
                if (hostTabs == null) return false;

                var existing = hostTabs.TabPages.Cast<TabPage>().FirstOrDefault(page =>
                    string.Equals(page.Name, "tabSarmatPlugin", StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    sarmatTab = existing;
                    TryLog("Sarmat tab already registered");
                    return true;
                }

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                originalTabs = flightData?.GetType().GetField("TabListOriginal", flags)?.GetValue(flightData) as IList ??
                    flightData?.GetType().GetProperty("TabListOriginal", flags)?.GetValue(flightData, null) as IList;
                sarmatTab = new TabPage("Sarmat")
                {
                    Name = "tabSarmatPlugin",
                    Padding = new Padding(3),
                    UseVisualStyleBackColor = true
                };
                panel.Dock = DockStyle.Fill;
                sarmatTab.Controls.Add(panel);
                hostTabs.TabPages.Insert(0, sarmatTab);
                if (originalTabs != null && !originalTabs.Contains(sarmatTab))
                    originalTabs.Insert(0, sarmatTab);
                TryLog("UI registered: FlightData/tabControlactions/tabSarmatPlugin");
                return true;
            }
            catch (Exception ex)
            {
                TryLog("Sarmat tab registration attempt failed; it will be retried", ex);
                RemoveSarmatTab();
                return false;
            }
        }

        private void RemoveSarmatTab()
        {
            if (sarmatTab == null) return;
            if (originalTabs != null && originalTabs.Contains(sarmatTab))
                originalTabs.Remove(sarmatTab);
            hostTabs?.TabPages.Remove(sarmatTab);
            // Detach the panel: PluginRuntime owns and disposes it.
            if (panel != null && sarmatTab.Controls.Contains(panel))
                sarmatTab.Controls.Remove(panel);
            sarmatTab.Dispose();
            sarmatTab = null;
            hostTabs = null;
            originalTabs = null;
        }

        public override bool Loop()
        {
            try
            {
                if (runtime != null && (sarmatTab == null || sarmatTab.IsDisposed || hostTabs == null ||
                    hostTabs.IsDisposed || !hostTabs.TabPages.Contains(sarmatTab)))
                {
                    var main = Host?.MainForm;
                    if (main != null) OnUi(main, () =>
                    {
                        if (RegisterSarmatTab() && tabRegistrationPendingLogged)
                        {
                            TryLog("Deferred Sarmat tab registration completed");
                            tabRegistrationPendingLogged = false;
                        }
                    });
                }
                runtime?.Tick();
            }
            catch (Exception ex) { TryLog("Plugin loop failed", ex); }
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
                ResolveFlightData();
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

        private void ResolveFlightData()
        {
            if (flightData != null) return;
            var mainType = Host.MainForm.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            flightData = mainType.GetProperty("FlightData", flags)?.GetValue(Host.MainForm, null) ??
                mainType.GetField("FlightData", flags)?.GetValue(Host.MainForm);
        }

        private void PanelVideoSourceRequested(object sender, EventArgs e) => StartSarmatVideo();
        private void PanelVehicleReconnectRequested(object sender, EventArgs e) => ReconnectVehicle();

        private void CleanupUiAndRuntime()
        {
            RemoveSarmatTab();
            if (panel != null) panel.VideoSourceRequested -= PanelVideoSourceRequested;
            DisposeRuntime();
            panel = null;
            if (takeoffModeWarning != null)
            {
                takeoffModeWarning.Parent?.Controls.Remove(takeoffModeWarning);
                takeoffModeWarning.Dispose();
                takeoffModeWarning = null;
            }
            flightData = null;
        }

        private void DisposeRuntime()
        {
            if (panel != null)
            {
                panel.VideoSourceRequested -= PanelVideoSourceRequested;
                panel.VehicleReconnectRequested -= PanelVehicleReconnectRequested;
            }
            if (runtime != null)
            {
                runtime.TakeoffWarningChanged -= SetTakeoffWarningVisible;
                runtime.VehicleConnected -= RestoreVideoOnConnect;
                runtime.VehicleConnected -= ReconnectJoystickOnConnect;
                runtime.VehicleConnected -= SaveVehicleConnectionOnConnect;
                runtime.VehicleReconnectRequested -= ReconnectVehicle;
                runtime.Dispose();
                runtime = null;
            }
            panel = null;
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
                    SaveVehicleConnection(mainType, comPort, flags);
                    var portName = savedVehiclePortName;
                    var baud = savedVehicleBaud;
                    if (string.IsNullOrWhiteSpace(portName))
                        throw new InvalidOperationException("Mission Planner connection port is unavailable");

                    var disconnect = mainType.GetMethod("doDisconnect", flags);
                    var connect = mainType.GetMethod("doConnect", flags, null,
                        new[] { comPort.GetType(), typeof(string), typeof(string), typeof(bool), typeof(bool) }, null);
                    if (disconnect == null || connect == null)
                        throw new MissingMethodException("Mission Planner reconnect API is unavailable");
                    disconnect.Invoke(main, new[] { comPort });
                    var baseStream = comPort.GetType().GetProperty("BaseStream",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (savedVehicleBaseStream != null && baseStream != null && baseStream.CanWrite)
                        baseStream.SetValue(comPort, savedVehicleBaseStream, null);
                    RestoreVehicleNetworkClient(savedVehicleBaseStream);
                    connect.Invoke(main, new[] { comPort, "preset", baud, (object)true, false });
                }
                catch (Exception ex)
                {
                    try { using (var log = new AppLog(true)) log.Error("Vehicle reconnect failed", ex); } catch { }
                }
                finally { vehicleReconnectInProgress = false; }
            }));
        }

        private void SaveVehicleConnectionOnConnect()
        {
            try
            {
                var main = Host.MainForm;
                var comPort = (object)Host.comPort;
                if (main == null || comPort == null) return;
                var flags = BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static;
                SaveVehicleConnection(main.GetType(), comPort, flags);
            }
            catch (Exception ex)
            {
                TryLog("Unable to remember vehicle connection", ex);
            }
        }

        private void SaveVehicleConnection(Type mainType, object comPort, BindingFlags flags)
        {
            var baseStream = comPort.GetType().GetProperty("BaseStream",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(comPort, null);
            if (baseStream != null)
            {
                savedVehicleBaseStream = baseStream;
                var client = GetMember(baseStream, "client");
                var socket = client == null ? null : GetMember(client, "Client");
                var remoteEndPoint = socket == null ? null : GetMember(socket, "RemoteEndPoint") as IPEndPoint;
                if (remoteEndPoint != null)
                {
                    savedVehicleHost = remoteEndPoint.Address.ToString();
                    savedVehicleNetworkPort = remoteEndPoint.Port;
                }
            }
            var portName = Convert.ToString(mainType.GetField("comPortName", flags)?.GetValue(null));
            if (!string.IsNullOrWhiteSpace(portName)) savedVehiclePortName = portName;
            var baud = Convert.ToString(mainType.GetField("comPortBaud", flags)?.GetValue(null));
            if (!string.IsNullOrWhiteSpace(baud)) savedVehicleBaud = baud;
        }

        private void RestoreVehicleNetworkClient(object baseStream)
        {
            if (baseStream == null || string.IsNullOrWhiteSpace(savedVehicleHost) ||
                savedVehicleNetworkPort <= 0) return;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var member = (MemberInfo)baseStream.GetType().GetField("client", flags) ??
                baseStream.GetType().GetProperty("client", flags);
            var clientType = member is FieldInfo field ? field.FieldType :
                (member as PropertyInfo)?.PropertyType;
            if (clientType == null) return;
            var client = Activator.CreateInstance(clientType,
                new object[] { savedVehicleHost, savedVehicleNetworkPort });
            if (member is FieldInfo clientField)
                clientField.SetValue(baseStream, client);
            else
                ((PropertyInfo)member).SetValue(baseStream, client, null);
        }

        private static object GetMember(object target, string name)
        {
            if (target == null) return null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return target.GetType().GetProperty(name, flags)?.GetValue(target, null) ??
                target.GetType().GetField(name, flags)?.GetValue(target);
        }

        private void StartSarmatVideo()
        {
            try
            {
                var pipeline = GStreamerPipelineBuilder.Build(runtime?.CurrentSettings);
                SaveMissionPlannerSetting("gstreamer_url", pipeline);
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
                    stream, new object[] { pipeline });
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
