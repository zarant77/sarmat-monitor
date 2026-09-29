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
        private readonly FlowLayoutPanel batteryChoices = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        private readonly FlowLayoutPanel droneChoices = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        private readonly Label status = new Label { Dock = DockStyle.Top, Height = 55, Text = "Loading equipment…" };
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
            Text = "Select drone and battery"; Width = 620; Height = 500;
            MinimumSize = new System.Drawing.Size(420, 300);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
            var cancel = new Button { Text = "Cancel", AutoSize = true };
            cancel.Click += (s, e) => Close();
            buttons.Controls.AddRange(new Control[] { save, retry, cancel });
            var selections = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            selections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); selections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var dronesBox = new GroupBox { Text = "Drone", Dock = DockStyle.Fill }; dronesBox.Controls.Add(droneChoices);
            var batteriesBox = new GroupBox { Text = "Battery", Dock = DockStyle.Fill }; batteriesBox.Controls.Add(batteryChoices);
            selections.Controls.Add(dronesBox, 0, 0); selections.Controls.Add(batteriesBox, 1, 0);
            Controls.Add(selections); Controls.Add(status); Controls.Add(buttons);
            Padding = new Padding(12);
            Shown += async (s, e) => await LoadBatteries();
            retry.Click += async (s, e) => await LoadBatteries();
            save.Click += async (s, e) => await SaveSelection();
            FormClosed += (s, e) => cancellation.Cancel();
        }
        private void Busy(bool value)
        {
            busy = value; retry.Enabled = !value; batteryChoices.Enabled = droneChoices.Enabled = !value;
            save.Enabled = !value && batteryChoices.Controls.OfType<RadioButton>().Any(r => r.Checked) && droneChoices.Controls.OfType<RadioButton>().Any(r => r.Checked);
        }
        private async Task LoadBatteries(string notice = null)
        {
            if (busy || cancellation.IsCancellationRequested) return;
            Busy(true); status.Text = "Loading equipment…";
            try
            {
                using (var api = new BatteryApiClient(settings))
                {
                    var data = await api.LoadAsync(cancellation.Token);
                    if (cancellation.IsCancellationRequested) return;
                    activeId = MiniJson.String(data, "activeBatteryId"); activeSince = MiniJson.String(data, "activeSince");
                    if (!data.TryGetValue("batteries", out var items) || !(items is IList rows))
                        throw new InvalidOperationException("Invalid battery list");
                    foreach (Control control in batteryChoices.Controls.Cast<Control>().ToArray()) control.Dispose();
                    foreach (Control control in droneChoices.Controls.Cast<Control>().ToArray()) control.Dispose();
                    foreach (var item in rows)
                    {
                        var row = MiniJson.Object(item); var id = MiniJson.String(row, "id");
                        var radio = new RadioButton { AutoSize = true, Tag = id, Checked = id == activeId,
                            Text = MiniJson.String(row, "label") + " · " + MiniJson.String(row, "serialNumber") + (id == activeId ? " (active)" : "") };
                        radio.CheckedChanged += (s, e) => { if (!busy) Busy(false); };
                        batteryChoices.Controls.Add(radio);
                    }
                    if (!data.TryGetValue("drones", out var droneItems) || !(droneItems is IList droneRows))
                        throw new InvalidOperationException("Server must be updated to support drone selection");
                    foreach (var item in droneRows)
                    {
                        var row = MiniJson.Object(item); var id = MiniJson.String(row, "id");
                        var radio = new RadioButton { AutoSize = true, Tag = id,
                            Text = MiniJson.String(row, "name") + " · " + MiniJson.String(row, "model") };
                        radio.CheckedChanged += (s, e) => { if (!busy) Busy(false); };
                        droneChoices.Controls.Add(radio);
                    }
                    status.Text = notice ?? (rows.Count == 0 || droneRows.Count == 0 ? "No available drone or battery for this crew. Add equipment in the app, then reload." : "Select the connected drone and its battery before arming.");
                }
            }
            catch (Exception ex) { if (!cancellation.IsCancellationRequested) { batteryChoices.Controls.Clear(); droneChoices.Controls.Clear(); status.Text = "Could not load equipment. " + ex.Message; } }
            finally { if (!cancellation.IsCancellationRequested) Busy(false); }
        }
        private async Task SaveSelection()
        {
            var selected = batteryChoices.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Checked);
            var selectedDrone = droneChoices.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Checked);
            if (busy || selected == null || selectedDrone == null) return;
            if (!canConfirm()) { status.Text = "Drone must be connected and disarmed before confirming."; return; }
            Busy(true); status.Text = "Saving selection…";
            bool conflict = false;
            try
            {
                using (var api = new BatteryApiClient(settings))
                {
                    if (await api.SelectAsync((string)selected.Tag, (string)selectedDrone.Tag, activeId, activeSince, sessionId, cancellation.Token))
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
