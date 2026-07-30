using System;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;
using MissionPlanner.Plugin;
using SarmatPlugin.Infrastructure;

namespace SarmatPlugin
{
    public sealed class SarmatMissionPlannerPlugin : Plugin
    {
        private PluginRuntime runtime;
        private TabControl hostTabs;
        private TabPage sarmatTab;
        private IList originalTabs;
        private object flightData;
        private SarmatPlugin.UI.SarmatPanel panel;
        internal const string SarmatGStreamerPipeline =
            "rtspsrc location=rtsp://192.168.69.5:554/stream=0 latency=100 ! application/x-rtp ! " +
            "decodebin3 ! queue max-size-buffers=1 leaky=2 ! videoconvert ! " +
            "video/x-raw,format=BGRA ! appsink name=outsink sync=false";

        public override string Name => "Sarmat Plugin";
        public override string Version => "1.0.0";
        public override string Author => "Sarmat";

        public override bool Init()
        {
            loopratehz = 4;
            return true;
        }

        public override bool Loaded()
        {
            try
            {
                runtime = new PluginRuntime(() => Host.cs);
                panel = runtime.CreatePanel();
                panel.VideoSourceRequested += (s, e) => StartSarmatVideo();
                var mainType = Host.MainForm.GetType();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                flightData = mainType.GetProperty("FlightData", flags)?.GetValue(Host.MainForm, null) ??
                    mainType.GetField("FlightData", flags)?.GetValue(Host.MainForm);
                hostTabs = FindNamedControl(flightData as Control, "tabControlactions") as TabControl;
                if (hostTabs == null) throw new InvalidOperationException("Mission Planner Flight Data action tabs were not found");

                originalTabs = flightData?.GetType().GetField("TabListOriginal", flags)?.GetValue(flightData) as IList;
                OnUi(hostTabs, () =>
                {
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
                });
                return true;
            }
            catch (Exception ex)
            {
                try { using (var log = new AppLog(true)) log.Error("Plugin load failed", ex); } catch { }
                runtime?.Dispose(); runtime = null;
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
            try
            {
                if (hostTabs != null && sarmatTab != null) OnUi(hostTabs, () =>
                {
                    if (originalTabs != null && originalTabs.Contains(sarmatTab))
                        originalTabs.Remove(sarmatTab);
                    hostTabs.TabPages.Remove(sarmatTab);
                    sarmatTab.Dispose();
                });
                runtime?.Dispose();
                return true;
            }
            catch { return false; }
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
                MessageBox.Show("Sarmat RTSP video source started.", "Sarmat Plugin",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
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
