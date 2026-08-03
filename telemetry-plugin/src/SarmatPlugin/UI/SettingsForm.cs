using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Forms;
using SarmatPlugin.Core;

namespace SarmatPlugin.UI
{
    public sealed class SettingsForm : Form
    {
        private readonly PluginSettings settings;
        private readonly Dictionary<string, Control> fields = new Dictionary<string, Control>();
        private readonly Func<CancellationToken, Task<string>> testObs;
        private readonly Func<CancellationToken, Task<string>> testRuijie;
        private readonly Func<string, string, CancellationToken, Task<string>> testAggregator;
        private readonly Func<string> aggregatorStatus;
        private readonly Action testAudio;
        private CheckedListBox widgetList;
        private int draggedWidgetIndex = -1;
        private Point widgetDragStart;
        public PluginSettings Result { get; private set; }

        public SettingsForm(PluginSettings source, Func<CancellationToken, Task<string>> testObs,
            Func<CancellationToken, Task<string>> testRuijie,
            Func<string, string, CancellationToken, Task<string>> testAggregator,
            Func<string> aggregatorStatus, Action testAudio,
            IReadOnlyDictionary<string, bool> currentHudElements = null)
        {
            settings = source; this.testObs = testObs; this.testRuijie = testRuijie;
            this.testAggregator = testAggregator; this.aggregatorStatus = aggregatorStatus;
            this.testAudio = testAudio;
            Text = "Sarmat Plugin Settings"; Width = 660; Height = 700; StartPosition = FormStartPosition.CenterParent;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page("General", General()));
            tabs.TabPages.Add(Page("Widgets", Widgets()));
            tabs.TabPages.Add(Page("Mission Planner UI", MissionPlannerUi(currentHudElements)));
            tabs.TabPages.Add(Page("Ruijie", Ruijie()));
            tabs.TabPages.Add(Page("Camera", Camera()));
            tabs.TabPages.Add(Page("Monitor", Aggregator()));
            tabs.TabPages.Add(Page("OBS", Obs()));
            tabs.TabPages.Add(Page("Audio", Audio()));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            ok.Click += (s, e) => Result = Read();
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            Controls.Add(tabs); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
        }

        private Control Obs()
        {
            var p = Grid();
            TextBox(p, "WebSocket endpoint", "ObsEndpoint", settings.ObsEndpoint);
            TextBox(p, "Password", "ObsPassword", settings.ObsPassword);
            Number(p, "Reconnect interval (s)", "ObsReconnectSeconds", settings.ObsReconnectSeconds, 1, 300, 1);
            TestRow(p, "Test connection", testObs);
            return p;
        }
        private Control Ruijie()
        {
            var p = Grid();
            TextBox(p, "Router address", "RuijieAddress", settings.RuijieAddress);
            TextBox(p, "Username", "RuijieUsername", settings.RuijieUsername);
            TextBox(p, "Password", "RuijiePassword", settings.RuijiePassword);
            Number(p, "Poll interval (s)", "RuijiePollSeconds", settings.RuijiePollSeconds, .5m, 300, 1);
            Number(p, "Request timeout (s)", "RuijieRequestTimeoutSeconds", settings.RuijieRequestTimeoutSeconds, 1, 300, 1);
            Number(p, "Stale timeout (s)", "RuijieStaleSeconds", settings.RuijieStaleSeconds, 1, 3600, 1);
            Check(p, "Allow insecure TLS", "RuijieAllowInsecureTls", settings.RuijieAllowInsecureTls);
            TestRow(p, "Test connection", testRuijie);
            return p;
        }
        private Control Aggregator()
        {
            var p = Grid();
            Check(p, "Enabled", "AggregatorEnabled", settings.AggregatorEnabled);
            TextBox(p, "WebSocket URL", "AggregatorUrl", settings.AggregatorUrl);
            TextBox(p, "Station secret", "AggregatorSecret", settings.AggregatorSecret);
            Number(p, "Reconnect interval (s)", "AggregatorReconnectSeconds",
                settings.AggregatorReconnectSeconds, 1, 300, 0);
            var test = new Button { Text = "Test connection", AutoSize = true };
            var status = new Label
            {
                Text = "Current status: " + (aggregatorStatus?.Invoke() ?? "Unknown"),
                AutoSize = true,
                Padding = new Padding(4)
            };
            test.Click += async (s, e) =>
            {
                test.Enabled = false;
                status.Text = "Testing…";
                try
                {
                    using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                        status.Text = await testAggregator(T("AggregatorUrl"),
                            T("AggregatorSecret"), cancellation.Token);
                }
                catch (Exception ex) { status.Text = "Current status: Disconnected — " + ex.Message; }
                finally { test.Enabled = true; }
            };
            p.Controls.Add(test);
            p.Controls.Add(status);
            return p;
        }
        private Control Audio()
        {
            var p = Grid();
            Check(p, "Enabled", "AudioEnabled", settings.AudioEnabled);
            Number(p, "Volume (0–100%)", "AudioVolume", settings.AudioVolume * 100, 0, 100, 0);
            Number(p, "Alert cooldown (s)", "AudioAlertCooldownSeconds",
                settings.AudioAlertCooldownSeconds, 1, 300, 0);
            FilePath(p, "Warning sound (WAV)", "AudioWarningSoundPath", settings.AudioWarningSoundPath);
            ButtonRow(p, "Test sound", testAudio);
            return p;
        }
        private Control Camera()
        {
            var p = Grid();
            TextBox(p, "RTSP URL", "CameraUrl", settings.CameraUrl);
            Combo(p, "Protocol", "CameraProtocol", settings.CameraProtocol,
                new[] { "tcp", "udp", "udp-mcast", "http" });
            Number(p, "Latency (ms)", "CameraLatencyMs", settings.CameraLatencyMs, 0, 10000, 0);
            Check(p, "Drop frames when latency is exceeded", "CameraDropOnLatency", settings.CameraDropOnLatency);
            TextBox(p, "RTP depayloader", "CameraDepayloader", settings.CameraDepayloader);
            TextBox(p, "Parser", "CameraParser", settings.CameraParser);
            TextBox(p, "Decoder", "CameraDecoder", settings.CameraDecoder);
            Number(p, "Queue max buffers", "CameraQueueMaxBuffers", settings.CameraQueueMaxBuffers, 1, 1000, 0);
            Combo(p, "Queue leaky mode", "CameraQueueLeaky", settings.CameraQueueLeaky,
                new[] { "downstream", "upstream", "no" });
            TextBox(p, "Video converter", "CameraConverter", settings.CameraConverter);
            Combo(p, "Raw pixel format", "CameraRawFormat", settings.CameraRawFormat,
                new[] { "BGRA", "BGRx", "RGBA", "RGBx" });
            TextBox(p, "App sink name", "CameraAppSinkName", settings.CameraAppSinkName);
            Check(p, "Synchronize appsink", "CameraSync", settings.CameraSync);
            return p;
        }
        private Control General()
        {
            var p = Grid();
            Check(p, "Reconnect vehicle after MAVLink silence", "VehicleAutoReconnectEnabled",
                settings.VehicleAutoReconnectEnabled);
            Number(p, "Vehicle reconnect timeout (s)", "VehicleReconnectTimeoutSeconds",
                settings.VehicleReconnectTimeoutSeconds, 3, 300, 0);
            Check(p, "Debug logging", "DebugLogging", settings.DebugLogging);
            return p;
        }
        private Control Widgets()
        {
            widgetList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                HorizontalScrollbar = true,
                BorderStyle = BorderStyle.FixedSingle,
                AllowDrop = true
            };
            var enabled = new HashSet<string>(settings.EnabledWidgets ??
                WidgetCatalog.DefaultIds, StringComparer.OrdinalIgnoreCase);
            var definitions = WidgetCatalog.Definitions.ToDictionary(x => x.Id,
                StringComparer.OrdinalIgnoreCase);
            var ordered = (settings.EnabledWidgets ?? WidgetCatalog.DefaultIds)
                .Where(definitions.ContainsKey).Select(x => definitions[x])
                .Concat(WidgetCatalog.Definitions.Where(x => !enabled.Contains(x.Id)))
                .Distinct().ToArray();
            widgetList.BeginUpdate();
            foreach (var widget in ordered)
                widgetList.Items.Add(widget, enabled.Contains(widget.Id));
            widgetList.EndUpdate();
            widgetList.MouseDown += WidgetListMouseDown;
            widgetList.MouseMove += WidgetListMouseMove;
            widgetList.DragOver += WidgetListDragOver;
            widgetList.DragDrop += WidgetListDragDrop;
            var hint = new Label
            {
                Text = "Check widgets to show. Drag items to change their order on the panel.",
                Dock = DockStyle.Top,
                Height = 28,
                AutoEllipsis = true
            };
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            panel.Controls.Add(widgetList);
            panel.Controls.Add(hint);
            return panel;
        }
        private Control MissionPlannerUi(IReadOnlyDictionary<string, bool> current)
        {
            var p = Grid();
            foreach (var item in HudElementCatalog.Elements)
            {
                bool value;
                if (!settings.HudElements.TryGetValue(item.Key, out value) &&
                    (current == null || !current.TryGetValue(item.Key, out value)))
                    value = true;
                Check(p, item.Value, "Hud:" + item.Key, value);
            }
            return p;
        }

        private PluginSettings Read()
        {
            return new PluginSettings
            {
                ObsEndpoint=T("ObsEndpoint"), ObsPassword=T("ObsPassword"),
                ObsReconnectSeconds=N("ObsReconnectSeconds"), RuijieAddress=T("RuijieAddress"), RuijieUsername=T("RuijieUsername"),
                RuijiePassword=T("RuijiePassword"), RuijiePollSeconds=N("RuijiePollSeconds"),
                RuijieRequestTimeoutSeconds=N("RuijieRequestTimeoutSeconds"), RuijieStaleSeconds=N("RuijieStaleSeconds"),
                RuijieAllowInsecureTls=B("RuijieAllowInsecureTls"),
                AggregatorEnabled=B("AggregatorEnabled"), AggregatorUrl=T("AggregatorUrl"),
                AggregatorSecret=T("AggregatorSecret"),
                AggregatorReconnectSeconds=N("AggregatorReconnectSeconds"),
                AudioEnabled=B("AudioEnabled"), AudioVolume=N("AudioVolume")/100,
                AudioAlertCooldownSeconds=N("AudioAlertCooldownSeconds"),
                AudioWarningSoundPath=T("AudioWarningSoundPath"),
                VehicleAutoReconnectEnabled=B("VehicleAutoReconnectEnabled"),
                VehicleReconnectTimeoutSeconds=N("VehicleReconnectTimeoutSeconds"),
                DebugLogging=B("DebugLogging"),
                EnabledWidgets=widgetList == null
                    ? WidgetCatalog.DefaultIds.ToList()
                    : widgetList.Items.Cast<WidgetDefinition>()
                        .Where((x, index) => widgetList.GetItemChecked(index))
                        .Select(x => x.Id).ToList(),
                HudElements=HudElementCatalog.Elements.ToDictionary(x => x.Key,
                    x => B("Hud:" + x.Key), StringComparer.OrdinalIgnoreCase),
                GStreamerWasStarted=settings.GStreamerWasStarted
                ,CameraUrl=T("CameraUrl"), CameraProtocol=T("CameraProtocol"),
                CameraLatencyMs=(int)N("CameraLatencyMs"), CameraDropOnLatency=B("CameraDropOnLatency"),
                CameraDepayloader=T("CameraDepayloader"), CameraParser=T("CameraParser"),
                CameraDecoder=T("CameraDecoder"), CameraQueueMaxBuffers=(int)N("CameraQueueMaxBuffers"),
                CameraQueueLeaky=T("CameraQueueLeaky"), CameraConverter=T("CameraConverter"),
                CameraRawFormat=T("CameraRawFormat"), CameraAppSinkName=T("CameraAppSinkName"),
                CameraSync=B("CameraSync")
            };
        }
        private void WidgetListMouseDown(object sender, MouseEventArgs e)
        {
            draggedWidgetIndex = widgetList.IndexFromPoint(e.Location);
            widgetDragStart = e.Location;
        }
        private void WidgetListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || draggedWidgetIndex < 0) return;
            var drag = new Rectangle(widgetDragStart.X - SystemInformation.DragSize.Width / 2,
                widgetDragStart.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width, SystemInformation.DragSize.Height);
            if (!drag.Contains(e.Location))
                widgetList.DoDragDrop(widgetList.Items[draggedWidgetIndex], DragDropEffects.Move);
        }
        private void WidgetListDragOver(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(typeof(WidgetDefinition))
                ? DragDropEffects.Move : DragDropEffects.None;
        }
        private void WidgetListDragDrop(object sender, DragEventArgs e)
        {
            if (draggedWidgetIndex < 0 || draggedWidgetIndex >= widgetList.Items.Count) return;
            var point = widgetList.PointToClient(new Point(e.X, e.Y));
            var target = widgetList.IndexFromPoint(point);
            if (target == ListBox.NoMatches)
                target = widgetList.Items.Count;
            else if (point.Y > widgetList.GetItemRectangle(target).Top +
                widgetList.GetItemRectangle(target).Height / 2)
                target++;
            var item = (WidgetDefinition)widgetList.Items[draggedWidgetIndex];
            var isChecked = widgetList.GetItemChecked(draggedWidgetIndex);
            widgetList.Items.RemoveAt(draggedWidgetIndex);
            if (target > draggedWidgetIndex) target--;
            target = Math.Max(0, Math.Min(target, widgetList.Items.Count));
            widgetList.Items.Insert(target, item);
            widgetList.SetItemChecked(target, isChecked);
            widgetList.SelectedIndex = target;
            draggedWidgetIndex = target;
        }
        private string T(string k) => fields.TryGetValue(k, out var c) ? c.Text : "";
        private bool B(string k) => fields.TryGetValue(k, out var c) && ((CheckBox)c).Checked;
        private double N(string k) => fields.TryGetValue(k, out var c) ? (double)((NumericUpDown)c).Value : 0;
        private static TabPage Page(string title, Control control) { var p = new TabPage(title); p.Controls.Add(control); return p; }
        private static TableLayoutPanel Grid() => new TableLayoutPanel { Dock=DockStyle.Fill, AutoScroll=true, ColumnCount=2,
            Padding=new Padding(10), AutoSize=true };
        private void Check(TableLayoutPanel p, string label, string key, bool value)
        {
            var c = new CheckBox { Text=label, Checked=value, AutoSize=true }; fields[key]=c; p.Controls.Add(c); p.SetColumnSpan(c,2);
        }
        private void TextBox(TableLayoutPanel p, string label, string key, string value, bool password=false)
        {
            p.Controls.Add(new Label {Text=label,AutoSize=true}); var c=new TextBox {Text=value??"",Width=360,UseSystemPasswordChar=password};
            fields[key]=c; p.Controls.Add(c);
        }
        private void Number(TableLayoutPanel p, string label, string key, double value, decimal min, decimal max, int decimals)
        {
            p.Controls.Add(new Label {Text=label,AutoSize=true}); var c=new NumericUpDown {Minimum=min,Maximum=max,
                DecimalPlaces=decimals,Increment=decimals==0?1:(decimal)Math.Pow(10,-decimals),Value=Math.Max(min,Math.Min(max,(decimal)value)),Width=120};
            fields[key]=c; p.Controls.Add(c);
        }
        private void Combo(TableLayoutPanel p, string label, string key, string value, string[] options)
        {
            p.Controls.Add(new Label { Text=label, AutoSize=true });
            var c=new ComboBox { Width=220, DropDownStyle=ComboBoxStyle.DropDown, Text=value??"" };
            c.Items.AddRange(options); fields[key]=c; p.Controls.Add(c);
        }
        private void FilePath(TableLayoutPanel p, string label, string key, string value)
        {
            p.Controls.Add(new Label { Text=label, AutoSize=true });
            var row=new FlowLayoutPanel { AutoSize=true, Margin=new Padding(0), WrapContents=false };
            var path=new TextBox { Text=value??"", Width=330 };
            var browse=new Button { Text="Browse…", AutoSize=true };
            browse.Click += (s,e) =>
            {
                using (var dialog=new OpenFileDialog
                {
                    Title="Select warning sound",
                    Filter="WAV audio (*.wav)|*.wav|All files (*.*)|*.*",
                    CheckFileExists=true
                })
                {
                    if (!string.IsNullOrWhiteSpace(path.Text))
                    {
                        try { dialog.InitialDirectory=System.IO.Path.GetDirectoryName(path.Text); }
                        catch { }
                    }
                    if (dialog.ShowDialog(this)==DialogResult.OK) path.Text=dialog.FileName;
                }
            };
            row.Controls.Add(path); row.Controls.Add(browse); fields[key]=path; p.Controls.Add(row);
        }
        private void ButtonRow(TableLayoutPanel p, string label, Action action)
        {
            var b=new Button {Text=label,AutoSize=true}; b.Click+=(s,e)=>action(); p.Controls.Add(b); p.SetColumnSpan(b,2);
        }
        private void TestRow(TableLayoutPanel p, string label, Func<CancellationToken,Task<string>> test)
        {
            var b=new Button {Text=label,AutoSize=true}; var status=new Label {Text="Current status: not tested",AutoSize=true,Padding=new Padding(4)};
            b.Click += async (s,e) => { b.Enabled=false; status.Text="Testing…"; try { using(var c=new CancellationTokenSource(TimeSpan.FromSeconds(20))) status.Text=await test(c.Token); } catch(Exception ex){status.Text=ex.Message;} finally{b.Enabled=true;} };
            p.Controls.Add(b); p.Controls.Add(status);
        }
    }
}
