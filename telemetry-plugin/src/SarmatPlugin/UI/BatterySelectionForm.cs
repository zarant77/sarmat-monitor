using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SarmatPlugin.Core;
using SarmatPlugin.Infrastructure;
using SarmatPlugin.Integration;

namespace SarmatPlugin.UI
{
    internal sealed class BatterySelectionForm : Form
    {
        private readonly PluginSettings settings;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly FlowLayoutPanel choices = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        private readonly Label status = new Label { Dock = DockStyle.Top, Height = 55, Text = "Loading batteries…" };
        private readonly Button save = new Button { Text = "Confirm", AutoSize = true, Enabled = false };
        private readonly Button retry = new Button { Text = "Reload", AutoSize = true };
        private string activeId, activeSince;
        private bool busy;
        private readonly string sessionId;
        private readonly Func<bool> canConfirm;
        public event Action<string> BatteryConfirmed;

        public BatterySelectionForm(PluginSettings settings, string sessionId, Func<bool> canConfirm)
        {
            this.settings = settings;
            this.sessionId = sessionId;
            this.canConfirm = canConfirm;
            Text = "Select battery in drone"; Width = 540; Height = 430;
            MinimumSize = new System.Drawing.Size(420, 300);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
            var cancel = new Button { Text = "Cancel", AutoSize = true };
            cancel.Click += (s, e) => Close();
            buttons.Controls.AddRange(new Control[] { save, retry, cancel });
            Controls.Add(choices); Controls.Add(status); Controls.Add(buttons);
            Padding = new Padding(12);
            Shown += async (s, e) => await LoadBatteries();
            retry.Click += async (s, e) => await LoadBatteries();
            save.Click += async (s, e) => await SaveSelection();
            FormClosed += (s, e) => cancellation.Cancel();
        }
        private void Busy(bool value)
        {
            busy = value; retry.Enabled = !value; choices.Enabled = !value;
            save.Enabled = !value && choices.Controls.OfType<RadioButton>().Any(r => r.Checked);
        }
        private async Task LoadBatteries(string notice = null)
        {
            if (busy || cancellation.IsCancellationRequested) return;
            Busy(true); status.Text = "Loading batteries…";
            try
            {
                using (var api = new BatteryApiClient(settings))
                {
                    var data = await api.LoadAsync(cancellation.Token);
                    if (cancellation.IsCancellationRequested) return;
                    activeId = MiniJson.String(data, "activeBatteryId"); activeSince = MiniJson.String(data, "activeSince");
                    if (!data.TryGetValue("batteries", out var items) || !(items is IList rows))
                        throw new InvalidOperationException("Invalid battery list");
                    foreach (Control control in choices.Controls.Cast<Control>().ToArray()) control.Dispose();
                    foreach (var item in rows)
                    {
                        var row = MiniJson.Object(item); var id = MiniJson.String(row, "id");
                        var radio = new RadioButton { AutoSize = true, Tag = id, Checked = id == activeId,
                            Text = MiniJson.String(row, "label") + " · " + MiniJson.String(row, "serialNumber") + (id == activeId ? " (active)" : "") };
                        radio.CheckedChanged += (s, e) => { if (!busy) Busy(false); };
                        choices.Controls.Add(radio);
                    }
                    status.Text = notice ?? (rows.Count == 0 ? "No available batteries for this crew. Add a battery in the app, then reload." : "Select the battery installed in the drone. The active battery from the app is preselected.");
                }
            }
            catch (Exception ex) { if (!cancellation.IsCancellationRequested) { choices.Controls.Clear(); status.Text = "Could not load batteries. " + ex.Message; } }
            finally { if (!cancellation.IsCancellationRequested) Busy(false); }
        }
        private async Task SaveSelection()
        {
            var selected = choices.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Checked);
            if (busy || selected == null) return;
            if (!canConfirm()) { status.Text = "Drone connection is unavailable. Wait for reconnection before confirming."; return; }
            Busy(true); status.Text = "Saving selection…";
            bool conflict = false;
            try
            {
                using (var api = new BatteryApiClient(settings))
                {
                    if (await api.SelectAsync((string)selected.Tag, activeId, activeSince, sessionId, cancellation.Token))
                    { if (!cancellation.IsCancellationRequested && canConfirm()) { BatteryConfirmed?.Invoke(sessionId); Close(); }
                      else if (!cancellation.IsCancellationRequested) status.Text = "Connection changed. Reload before confirming again."; }
                    else conflict = true;
                }
            }
            catch (Exception ex) { if (!cancellation.IsCancellationRequested) status.Text = "Could not save selection. Reload before retrying. " + ex.Message; }
            finally { if (!cancellation.IsCancellationRequested) Busy(false); }
            if (conflict) await LoadBatteries("The active battery changed or is unavailable. Review the updated list and confirm again.");
        }
    }
}
