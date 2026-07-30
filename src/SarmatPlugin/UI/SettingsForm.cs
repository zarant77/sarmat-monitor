using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly Action<Severity> testAudio;
        public PluginSettings Result { get; private set; }

        public SettingsForm(PluginSettings source, Func<CancellationToken, Task<string>> testObs,
            Func<CancellationToken, Task<string>> testRuijie, Action<Severity> testAudio)
        {
            settings = source; this.testObs = testObs; this.testRuijie = testRuijie; this.testAudio = testAudio;
            Text = "Sarmat Plugin Settings"; Width = 660; Height = 700; StartPosition = FormStartPosition.CenterParent;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page("Alerts", Alerts()));
            tabs.TabPages.Add(Page("OBS Studio", Obs()));
            tabs.TabPages.Add(Page("Ruijie", Ruijie()));
            tabs.TabPages.Add(Page("Audio", Audio()));
            tabs.TabPages.Add(Page("General", General()));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            ok.Click += (s, e) => Result = Read();
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            Controls.Add(tabs); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
        }

        private Control Alerts()
        {
            var p = Grid();
            Check(p, "Alerts enabled", "AlertsEnabled", settings.AlertsEnabled);
            Number(p, "Minimum satellites", "MinimumSatellites", settings.MinimumSatellites, 1, 100, 0);
            Number(p, "Maximum HDOP", "MaximumHdop", settings.MaximumHdop, .05m, 100, 2);
            Number(p, "Minimum battery voltage (V)", "MinimumBatteryVoltage", settings.MinimumBatteryVoltage, 1, 1000, 1);
            Number(p, "Safe dist to home (m)", "SafeDistanceToHomeMeters", settings.SafeDistanceToHomeMeters, 1, 100000, 0);
            Number(p, "Activation debounce (s)", "ActivationDebounceSeconds", settings.ActivationDebounceSeconds, 0, 60, 1);
            Number(p, "Recovery debounce (s)", "RecoveryDebounceSeconds", settings.RecoveryDebounceSeconds, 0, 60, 1);
            Number(p, "Repeat interval (s)", "RepeatIntervalSeconds", settings.RepeatIntervalSeconds, 1, 3600, 1);
            Number(p, "ARMED grace period (s)", "ArmedGracePeriodSeconds", settings.ArmedGracePeriodSeconds, 0, 60, 1);
            return p;
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
        private Control Audio()
        {
            var p = Grid();
            Check(p, "Enabled", "AudioEnabled", settings.AudioEnabled);
            Check(p, "Muted", "AudioMuted", settings.AudioMuted);
            Number(p, "Volume (0–100%)", "AudioVolume", settings.AudioVolume * 100, 0, 100, 0);
            ButtonRow(p, "Test warning", () => testAudio(Severity.Warning));
            ButtonRow(p, "Test critical", () => testAudio(Severity.Critical));
            ButtonRow(p, "Test restored", () => testAudio(Severity.Ok));
            return p;
        }
        private Control General()
        {
            var p = Grid();
            Check(p, "Show panel", "ShowPanel", settings.ShowPanel);
            Check(p, "Start automatically", "StartAutomatically", settings.StartAutomatically);
            Check(p, "Debug logging", "DebugLogging", settings.DebugLogging);
            Number(p, "Header font size (pt)", "HeaderFontSize", settings.HeaderFontSize, 6, 24, 1);
            Number(p, "Value font size (pt)", "ValueFontSize", settings.ValueFontSize, 8, 40, 1);
            return p;
        }

        private PluginSettings Read()
        {
            return new PluginSettings
            {
                AlertsEnabled=B("AlertsEnabled"), MinimumSatellites=(int)N("MinimumSatellites"), MaximumHdop=N("MaximumHdop"),
                MinimumBatteryVoltage=N("MinimumBatteryVoltage"), SafeDistanceToHomeMeters=N("SafeDistanceToHomeMeters"),
                ActivationDebounceSeconds=N("ActivationDebounceSeconds"),
                RecoveryDebounceSeconds=N("RecoveryDebounceSeconds"), RepeatIntervalSeconds=N("RepeatIntervalSeconds"),
                ArmedGracePeriodSeconds=N("ArmedGracePeriodSeconds"), ObsEndpoint=T("ObsEndpoint"), ObsPassword=T("ObsPassword"),
                ObsReconnectSeconds=N("ObsReconnectSeconds"), RuijieAddress=T("RuijieAddress"), RuijieUsername=T("RuijieUsername"),
                RuijiePassword=T("RuijiePassword"), RuijiePollSeconds=N("RuijiePollSeconds"),
                RuijieRequestTimeoutSeconds=N("RuijieRequestTimeoutSeconds"), RuijieStaleSeconds=N("RuijieStaleSeconds"),
                RuijieAllowInsecureTls=B("RuijieAllowInsecureTls"), AudioEnabled=B("AudioEnabled"), AudioMuted=B("AudioMuted"),
                AudioVolume=N("AudioVolume")/100, ShowPanel=B("ShowPanel"), StartAutomatically=B("StartAutomatically"),
                DebugLogging=B("DebugLogging"), HeaderFontSize=N("HeaderFontSize"), ValueFontSize=N("ValueFontSize")
            };
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
