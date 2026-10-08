using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;
using MissionPlanner.Plugin;
using SarmatAltitude.Core;

namespace SarmatAltitude
{
    // Independent Mission Planner plugin with its own installable assembly.
    public sealed class AltitudeMissionPlannerPlugin : Plugin, IMessageFilter
    {
        [DataContract]
        public sealed class Options
        {
            [DataMember] public int Increase = (int)(Keys.Shift | Keys.Up);
            [DataMember] public int Decrease = (int)(Keys.Shift | Keys.Down);
            [DataMember] public double Step = 50;
            [DataMember] public int ShortcutDefaultsVersion = 1;
        }
        private readonly AltitudeTarget target = new AltitudeTarget();
        private Options options = new Options();
        private TabPage tab;
        private TabControl tabs;
        private IList originalTabs;
        private Label indicator;
        private Font indicatorFont;
        private Control hud;
        private System.Windows.Forms.Timer timer;
        private MissionPlanner.MAVLinkInterface port;
        private int systemId, componentId, axisChannel;
        private double initialStick, deadzone;
        private volatile bool active;
        private bool starting, closing, awaitingGuided;
        private DateTime guidedDeadline;
        private MAVLink.mavlink_set_position_target_global_int_t guidedCommand;
        private string previousMode;
        private int manualMovement;
        private long heartbeatTicks, positionTicks, stickTicks;
        private readonly object sampleLock = new object();
        private double latestStick;
        private double latitude, longitude;
        private string status = "";
        private static string SettingsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SarmatPlugin");
        private static string SettingsPath => Path.Combine(SettingsRoot, "altitude-settings.json");
        public override string Name => "Sarmat Altitude";
        public override string Version => "1.0.0";
        public override string Author => "Sarmat";
        public override bool Init() { loopratehz = 1; return true; }
        public override bool Loaded()
        {
            var main = Host.MainForm as Control;
            if (main == null) return false;
            Ui(() =>
            {
                try
                {
                    if (File.Exists(SettingsPath))
                        using (var stream = File.OpenRead(SettingsPath))
                            options = (Options)new DataContractJsonSerializer(typeof(Options)).ReadObject(stream);
                    if (options == null || options.Step < 1 || options.Step > 10000 ||
                        double.IsNaN(options.Step) || double.IsInfinity(options.Step) ||
                        !ValidShortcut((Keys)options.Increase) || !ValidShortcut((Keys)options.Decrease) ||
                        options.Increase == options.Decrease) options = new Options();
                }
                catch { options = new Options(); }
                if (options.ShortcutDefaultsVersion == 0 && options.Increase == (int)(Keys.Control | Keys.Up) &&
                    options.Decrease == (int)(Keys.Control | Keys.Down))
                {
                    options.Increase = (int)(Keys.Shift | Keys.Up);
                    options.Decrease = (int)(Keys.Shift | Keys.Down);
                }
                options.ShortcutDefaultsVersion = 1;
                indicator = new Label { AutoSize = true, BackColor = Color.FromArgb(0, 160, 0),
                    ForeColor = Color.White, Padding = new Padding(8, 4, 8, 4),
                    TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                    Text = "Target alt: —" };
                AttachIndicator();
                Application.AddMessageFilter(this);
                timer = new System.Windows.Forms.Timer { Interval = 50 };
                timer.Tick += Tick;
                timer.Start();
                RegisterTab();
            });
            return true;
        }
        public override bool Loop() { return true; }
        private void Ui(Action action)
        {
            var main = Host.MainForm as Control;
            if (main == null || main.IsDisposed) return;
            if (main.InvokeRequired) main.Invoke(action); else action();
        }
        private void AttachIndicator()
        {
            var main = Host.MainForm as Control;
            if (main == null || indicator == null || indicator.IsDisposed) return;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var flightData = main.GetType().GetProperty("FlightData", flags)?.GetValue(main, null) ??
                main.GetType().GetField("FlightData", flags)?.GetValue(main);
            var resolved = flightData?.GetType().GetField("myhud",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Control ??
                Find(flightData as Control, "hud1");
            if (resolved == null || resolved.IsDisposed) return; // Retry when Flight Data is ready.
            if (hud != resolved || indicator.Parent != resolved)
            {
                if (hud != null) hud.Resize -= HudResize;
                hud = resolved;
                hud.Controls.Add(indicator);
                hud.Resize += HudResize;
            }
            HudResize(null, EventArgs.Empty);
        }
        private void HudResize(object sender, EventArgs e)
        {
            if (indicator == null || hud == null || hud.IsDisposed) return;
            // Match the native VIBE/EKF badge row when Mission Planner exposes its hit zone.
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var zoneValue = hud.GetType().GetField("vibehitzone", flags)?.GetValue(hud) ??
                hud.GetType().GetField("ekfhitzone", flags)?.GetValue(hud);
            var zone = zoneValue is Rectangle rectangle ? rectangle : Rectangle.Empty;
            var badgeHeight = Math.Max(22, hud.ClientSize.Height / 30 + 8);
            var margin = 5;
            if (zone.Height > 0 && zone.Bottom <= hud.ClientSize.Height && zone.Top > hud.ClientSize.Height / 2)
            {
                badgeHeight = zone.Height;
                margin = Math.Max(3, hud.ClientSize.Height - zone.Bottom - 2);
            }
            var fontSize = Math.Max(11, badgeHeight * 0.6f);
            if (indicatorFont == null || Math.Abs(indicatorFont.Size - fontSize) > 0.1f)
            {
                var previousFont = indicatorFont;
                indicatorFont = new Font(SystemFonts.MessageBoxFont.FontFamily, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                indicator.Font = indicatorFont;
                previousFont?.Dispose();
            }
            indicator.MinimumSize = new Size(0, badgeHeight);
            indicator.MaximumSize = new Size(Math.Max(1, hud.ClientSize.Width - margin * 2), 0);
            indicator.Location = new Point(margin, Math.Max(0, hud.ClientSize.Height - indicator.Height - margin));
            indicator.BringToFront();
        }
        private static Control Find(Control root, string name)
        {
            if (root == null) return null;
            if (root.Name == name) return root;
            foreach (Control child in root.Controls) { var found = Find(child, name); if (found != null) return found; }
            return null;
        }
        private void RegisterTab()
        {
            if (tab != null && !tab.IsDisposed && tabs != null && tabs.TabPages.Contains(tab))
            {
                var sarmat = tabs.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Name == "tabSarmatPlugin");
                if (sarmat != null && tabs.TabPages.IndexOf(tab) != tabs.TabPages.IndexOf(sarmat) + 1)
                {
                    tabs.TabPages.Remove(tab); tabs.TabPages.Insert(tabs.TabPages.IndexOf(sarmat) + 1, tab);
                    if (originalTabs != null && originalTabs.Contains(tab) && originalTabs.Contains(sarmat))
                    { originalTabs.Remove(tab); originalTabs.Insert(originalTabs.IndexOf(sarmat) + 1, tab); }
                }
                return;
            }
            if (tab != null) RemoveTab();
            var main = Host.MainForm as Control;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var flightData = main.GetType().GetProperty("FlightData", flags)?.GetValue(main, null) ??
                main.GetType().GetField("FlightData", flags)?.GetValue(main);
            tabs = Find(flightData as Control, "tabControlactions") as TabControl ?? Find(main, "tabControlactions") as TabControl;
            if (tabs == null) return;
            originalTabs = flightData?.GetType().GetField("TabListOriginal", flags)?.GetValue(flightData) as IList ??
                flightData?.GetType().GetProperty("TabListOriginal", flags)?.GetValue(flightData, null) as IList;
            tab = new TabPage("Altitude") { Name = "tabSarmatAltitude", AutoScroll = false,
                UseVisualStyleBackColor = false };
            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, Padding = new Padding(8) };
            var up = ShortcutBox((Keys)options.Increase);
            var down = ShortcutBox((Keys)options.Decrease);
            var step = new NumericUpDown { Minimum = 1, Maximum = 10000, DecimalPlaces = 1,
                Value = (decimal)options.Step, Width = 180 };
            layout.Controls.Add(new Label { Text = "Increase target altitude", AutoSize = true }); layout.Controls.Add(up);
            layout.Controls.Add(new Label { Text = "Decrease target altitude", AutoSize = true }); layout.Controls.Add(down);
            layout.Controls.Add(new Label { Text = "Altitude step, m", AutoSize = true }); layout.Controls.Add(step);
            var save = new Button { Name = "AltitudeSave", Text = "Save", Dock = DockStyle.Fill };
            save.Click += (s, e) =>
            {
                if ((Keys)up.Tag == (Keys)down.Tag) { status = "Choose different shortcuts"; Render(); return; }
                var next = new Options { Increase = (int)(Keys)up.Tag, Decrease = (int)(Keys)down.Tag, Step = (double)step.Value };
                try
                {
                    Directory.CreateDirectory(SettingsRoot);
                    using (var stream = File.Create(SettingsPath)) new DataContractJsonSerializer(typeof(Options)).WriteObject(stream, next);
                    options = next; status = "Settings saved";
                }
                catch (Exception) { status = "Could not save settings"; }
                Render();
            };
            var guide = new Label { AutoSize = true, Location = new Point(12, 24), MaximumSize = new Size(240, 0), Text =
                "Target starts at 0 m above Home.\nShortcuts add or subtract the step.\nEnter applies; throttle movement pauses.\nEnter resumes the saved target.\n\n○ Idle   ◷ Waiting   ▶ Active\n\nClick a shortcut field to change it." };
            var help = new GroupBox { Text = "Quick guide", Width = 264,
                Margin = new Padding(3, 16, 3, 3), Padding = new Padding(12) };
            help.Controls.Add(guide);
            layout.Controls.Add(help);
            layout.SizeChanged += (s, e) =>
            {
                help.Width = Math.Max(120, layout.ClientSize.Width - layout.Padding.Horizontal - 24);
                guide.MaximumSize = new Size(Math.Max(1, help.Width - 24), 0);
                help.Height = guide.Height + 38;
            };
            help.Height = guide.Height + 38;
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(12, 8, 12, 10) };
            footer.Controls.Add(save);
            tab.Controls.Add(layout);
            tab.Controls.Add(footer);
            ApplySettingsColors(tab);
            tab.VisibleChanged += (s, e) => { if (tab != null && tab.Visible) ApplySettingsColors(tab); };
            var index = tabs.TabPages.Cast<TabPage>().ToList().FindIndex(p => p.Name == "tabSarmatPlugin");
            tabs.TabPages.Insert(index < 0 ? 0 : index + 1, tab);
            if (originalTabs != null && !originalTabs.Contains(tab)) originalTabs.Insert(Math.Min(index < 0 ? 0 : index + 1, originalTabs.Count), tab);
        }
        private static void ApplySettingsColors(Control control)
        {
            control.BackColor = control is TextBoxBase || control is NumericUpDown || control is Button
                ? Color.FromArgb(48, 53, 58) : Color.FromArgb(34, 38, 42);
            control.ForeColor = Color.White;
            if (control is Button button)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(100, 108, 116);
                if (button.Name == "AltitudeSave")
                {
                    button.BackColor = Color.FromArgb(0, 140, 60);
                    button.FlatAppearance.BorderColor = Color.FromArgb(0, 170, 75);
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 160, 70);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 115, 45);
                }
            }
            foreach (Control child in control.Controls) ApplySettingsColors(child);
        }
        private static bool ValidShortcut(Keys key) => (key & Keys.Modifiers) != Keys.None &&
            (key & Keys.KeyCode) != Keys.Enter && (key & Keys.KeyCode) != Keys.ControlKey &&
            (key & Keys.KeyCode) != Keys.ShiftKey && (key & Keys.KeyCode) != Keys.Menu && (key & Keys.KeyCode) != Keys.None;
        private static TextBox ShortcutBox(Keys value)
        {
            var box = new TextBox { ReadOnly = true, Width = 220, Tag = value, Text = new KeysConverter().ConvertToString(value) };
            box.KeyDown += (s, e) =>
            {
                e.SuppressKeyPress = true;
                if (!ValidShortcut(e.KeyData)) return;
                box.Tag = e.KeyData; box.Text = new KeysConverter().ConvertToString(e.KeyData);
            };
            return box;
        }
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != 0x100 && message.Msg != 0x104) return false;
            var main = Host.MainForm as Form;
            if (main == null || Form.ActiveForm != main || (tab != null && tab.ContainsFocus)) return false;
            var key = (Keys)(int)message.WParam | Control.ModifierKeys;
            bool adjust = key == (Keys)options.Increase || key == (Keys)options.Decrease;
            bool submit = key == Keys.Enter && target.Pending;
            if (!adjust && !submit) return false;
            // Never steal Enter or hotkeys from text entry and native settings editors.
            Control focus = main;
            while (focus is ContainerControl container && container.ActiveControl != null) focus = container.ActiveControl;
            if (focus is TextBoxBase || focus is NumericUpDown || focus is ComboBox || focus is DataGridView) return false;
            if ((message.LParam.ToInt64() & (1L << 30)) != 0) return true;
            try
            {
                if (adjust)
                {
                    target.Adjust(options.Step,
                        key == (Keys)options.Increase ? 1 : -1);
                    status = ""; Render();
                }
                else Start();
            }
            catch (Exception ex) { status = ex.Message; Render(); }
            return true;
        }
        private static bool Fresh(long ticks) => ticks > 0 && DateTime.UtcNow.Ticks - ticks < TimeSpan.FromSeconds(3).Ticks;
        private double Parameter(string name, double fallback)
        {
            var value = port.MAV.param[name];
            return value == null ? fallback : (double)value;
        }
        private bool Copter => Host.cs.firmware.ToString().IndexOf("Copter", StringComparison.OrdinalIgnoreCase) >= 0;
        private void Start()
        {
            if (starting || !target.Pending || !target.TargetMeters.HasValue) return;
            if (port == null || !port.BaseStream.IsOpen || port.logreadmode || !Host.cs.armed ||
                !Fresh(Interlocked.Read(ref heartbeatTicks)) || !Fresh(Interlocked.Read(ref positionTicks)))
                throw new InvalidOperationException("Connect the vehicle, ARM it and wait for live telemetry");
            if (!Copter) throw new InvalidOperationException("ArduCopter only");
            double lat, lng;
            lock (sampleLock) { lat = latitude; lng = longitude; }
            if (Host.cs.gpsstatus < 3 || lat == 0 && lng == 0)
                throw new InvalidOperationException("GPS fix required");
            axisChannel = (int)Parameter("RCMAP_THROTTLE", 3);
            if (axisChannel < 1 || axisChannel > 18) throw new InvalidOperationException("Throttle channel unavailable");
            var joystick = MissionPlanner.MainV2.joystick;
            if (joystick != null && joystick.enabled) initialStick = joystick.getValueForChannel(axisChannel);
            else
            {
                lock (sampleLock) initialStick = latestStick;
                if (!Fresh(Interlocked.Read(ref stickTicks))) throw new InvalidOperationException("Throttle input unavailable; check RC_CHANNELS");
            }
            if (initialStick < 800 || initialStick > 2200) throw new InvalidOperationException("Invalid throttle input");
            deadzone = Math.Max(10, Parameter("RC" + axisChannel + "_DZ", 30));
            if (!active) previousMode = Host.cs.mode;
            active = true; starting = true; Interlocked.Exchange(ref manualMovement, 0);
            awaitingGuided = true; guidedDeadline = DateTime.UtcNow.AddSeconds(5);
            try
            {
                guidedCommand = GuidedAltitudeCommand.Create((byte)systemId, (byte)componentId,
                    lat, lng, target.TargetMeters.Value);
                // The position setpoint must arrive AFTER Guided has been confirmed.
                // setGuidedModeWP sends it immediately and swallows transmission exceptions.
                if (string.Equals(Host.cs.mode, "Guided", StringComparison.OrdinalIgnoreCase))
                    SendGuidedTarget();
                else port.setMode((byte)systemId, (byte)componentId, "Guided");
                target.Submitted(); status = awaitingGuided ? "Waiting for Guided mode" : "Automatic altitude control";
                if (Interlocked.Exchange(ref manualMovement, 0) != 0) Cancel("Cancelled by throttle input", true, true);
            }
            catch
            {
                Cancel("Altitude command failed", true);
                throw;
            }
            finally { starting = false; Render(); }
        }
        private void SendGuidedTarget()
        {
            port.sendPacket(guidedCommand, (byte)systemId, (byte)componentId);
            awaitingGuided = false;
        }
        private void ObserveStick(double value)
        {
            if (value < 800 || value > 2200) return;
            lock (sampleLock) latestStick = value;
            Interlocked.Exchange(ref stickTicks, DateTime.UtcNow.Ticks);
            if (active && AltitudeTarget.StickMoved(initialStick, value, deadzone)) Interlocked.Exchange(ref manualMovement, 1);
        }
        private void Received(object sender, MAVLink.MAVLinkMessage packet)
        {
            if (packet.sysid != systemId || packet.compid != componentId) return;
            if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT) Interlocked.Exchange(ref heartbeatTicks, DateTime.UtcNow.Ticks);
            if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.GLOBAL_POSITION_INT)
            {
                var position = (MAVLink.mavlink_global_position_int_t)packet.data;
                lock (sampleLock)
                { latitude = position.lat / 1e7; longitude = position.lon / 1e7; }
                Interlocked.Exchange(ref positionTicks, DateTime.UtcNow.Ticks);
            }
            if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.RC_CHANNELS || packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_RAW)
                ReadStickPacket(packet, false);
        }
        private void Sent(object sender, MAVLink.MAVLinkMessage packet)
        {
            if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_OVERRIDE) ReadStickPacket(packet, true);
        }
        private void ReadStickPacket(MAVLink.MAVLinkMessage packet, bool outgoing)
        {
            try
            {
                var data = packet.data;
                var type = data.GetType();
                if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.RC_CHANNELS_RAW &&
                    Convert.ToInt32(type.GetField("port").GetValue(data)) != 0) return;
                if (outgoing && (Convert.ToInt32(type.GetField("target_system").GetValue(data)) != systemId ||
                    Convert.ToInt32(type.GetField("target_component").GetValue(data)) != componentId)) return;
                int channel = axisChannel;
                if (channel == 0) return;
                var field = type.GetField("chan" + channel + "_raw");
                if (field != null) ObserveStick(Convert.ToDouble(field.GetValue(data)));
            }
            catch { /* Unsupported packet shape is ignored, never treated as a centered stick. */ }
        }
        private void Tick(object sender, EventArgs e)
        {
            if (closing || starting) return;
            try
            {
                RegisterTab();
                AttachIndicator();
                var next = Host.comPort;
                if (next != port || next != null && (systemId != next.sysidcurrent || componentId != next.compidcurrent))
                {
                    Cancel("Vehicle changed", false);
                    DetachPort(); port = next;
                    target.Reset(); heartbeatTicks = positionTicks = stickTicks = 0;
                    if (port != null)
                    {
                        systemId = port.sysidcurrent; componentId = port.compidcurrent;
                        axisChannel = (int)Parameter("RCMAP_THROTTLE", 3);
                        port.OnPacketReceived += Received; port.OnPacketSent += Sent;
                    }
                }
                if (active)
                {
                    var joystick = MissionPlanner.MainV2.joystick;
                    if (joystick != null && joystick.enabled) ObserveStick(joystick.getValueForChannel(axisChannel));
                    if (Interlocked.Exchange(ref manualMovement, 0) != 0) Cancel("Cancelled by throttle input", true, true);
                    else if (!Host.cs.armed || port == null || !port.BaseStream.IsOpen || !Fresh(Interlocked.Read(ref heartbeatTicks)))
                        Cancel("Altitude control stopped: link lost or DISARMED", false);
                    else if (string.Equals(Host.cs.mode, "Guided", StringComparison.OrdinalIgnoreCase))
                    {
                        if (awaitingGuided)
                        {
                            try { SendGuidedTarget(); }
                            catch
                            {
                                Cancel("Altitude command failed", true, true);
                                Render(); return;
                            }
                        }
                        status = "Automatic altitude control";
                    }
                    else if (!awaitingGuided) Cancel("Flight mode changed", false);
                    else if (DateTime.UtcNow >= guidedDeadline) Cancel("Guided mode not confirmed", true);
                }
                else if (port != null)
                {
                    var mapped = (int)Parameter("RCMAP_THROTTLE", 3);
                    if (mapped != axisChannel) { axisChannel = mapped; Interlocked.Exchange(ref stickTicks, 0); }
                    if (!port.BaseStream.IsOpen || !Fresh(Interlocked.Read(ref heartbeatTicks))) target.Reset();
                }
                Render();
            }
            catch (Exception) { status = "Altitude control error"; Render(); }
        }
        private void Cancel(string reason, bool restore, bool preserveTarget = false)
        {
            if (!active) return;
            // Only relinquish a Guided mode owned by this plugin; never overwrite a pilot mode change.
            if (restore && port != null && port.sysidcurrent == systemId && port.compidcurrent == componentId &&
                port.BaseStream.IsOpen && Host.cs.armed && (awaitingGuided || string.Equals(Host.cs.mode, "Guided", StringComparison.OrdinalIgnoreCase)))
            {
                var manualModes = new[] { "Stabilize", "AltHold", "Loiter", "PosHold", "Sport", "Drift" };
                var mode = manualModes.Any(m => string.Equals(m, previousMode, StringComparison.OrdinalIgnoreCase)) ? previousMode : "AltHold";
                try { port.setMode((byte)systemId, (byte)componentId, mode); }
                catch (Exception) { Interlocked.Exchange(ref manualMovement, 1); status = "Could not restore manual mode"; return; }
            }
            active = false; awaitingGuided = false;
            if (preserveTarget) target.Interrupted(); else target.Reset();
            status = reason;
        }
        private void Render()
        {
            if (indicator == null || indicator.IsDisposed) return;
            var symbol = active ? (awaitingGuided ? "◷" : "▶") : "○";
            var normalStatuses = new[] { "Waiting for Guided mode", "Automatic altitude control", "Settings saved",
                "Cancelled by throttle input", "Vehicle changed", "Flight mode changed", "Plugin disabled" };
            var message = string.IsNullOrEmpty(status) || normalStatuses.Contains(status) ? "" : " - " + status;
            indicator.Text = "Target alt: " + (target.TargetMeters.HasValue ? target.TargetMeters.Value.ToString("0.#") : "—") +
                "  " + symbol + message;
            HudResize(null, EventArgs.Empty);
        }
        private void DetachPort()
        {
            if (port == null) return;
            port.OnPacketReceived -= Received; port.OnPacketSent -= Sent;
        }
        private void RemoveTab()
        {
            if (tab == null) return;
            if (originalTabs != null && originalTabs.Contains(tab)) originalTabs.Remove(tab);
            tabs?.TabPages.Remove(tab); tab.Dispose(); tab = null;
        }
        public override bool Exit()
        {
            Ui(() =>
            {
                closing = true; Cancel("Plugin disabled", true); DetachPort();
                Application.RemoveMessageFilter(this); timer?.Stop(); timer?.Dispose(); RemoveTab();
                if (hud != null) hud.Resize -= HudResize;
                indicator?.Parent?.Controls.Remove(indicator); indicator?.Dispose();
                indicatorFont?.Dispose(); indicatorFont = null;
            });
            return true;
        }
    }
}
