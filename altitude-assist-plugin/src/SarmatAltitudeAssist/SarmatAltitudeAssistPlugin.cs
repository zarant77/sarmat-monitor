using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using MissionPlanner.Plugin;
using SarmatAltitudeAssist.Core;

namespace SarmatAltitudeAssist
{
 public sealed class AltitudeAssistRuntime : IDisposable
 {
  readonly System.Threading.Timer timer;
  readonly string logPath;
  readonly object logLock = new object();
  AssistState loggedState;
  string loggedReason = "";
  public AltitudeAssistSettings Settings { get; }
  public AltitudeAssistController Controller { get; }
  public event Action Updated;

  public AltitudeAssistRuntime(AltitudeAssistSettings settings, ITelemetrySource telemetry, string log)
  {
   Settings = settings;
   logPath = log;
   Controller = new AltitudeAssistController(settings, telemetry, new NullVerticalControlOutput(new SystemClock()), new SystemClock());
   timer = new System.Threading.Timer(_ => { Controller.Tick(); WriteTransition(); Updated?.Invoke(); }, null, 100, 50);
  }

  void WriteTransition()
  {
   var c = Controller;
   if (c.State == loggedState && c.LastReason == loggedReason) return;
   loggedState = c.State;
   loggedReason = c.LastReason;
   var t = c.LastTelemetry;
   try
   {
    lock (logLock)
    {
     Directory.CreateDirectory(Path.GetDirectoryName(logPath));
     File.AppendAllText(logPath, $"{DateTime.UtcNow:O}\tstate={c.State}\talt={t?.AltitudeMeters ?? 0:F2}\ttarget={c.Target?.ToString("F2") ?? "none"}\tstickRaw={t?.StickRaw ?? 0}\tstick={t?.StickNormalized ?? .5:F3}\tcommand={c.DesiredCommand:F3}\tmode={t?.FlightMode ?? "none"}\tarmed={t?.Armed ?? false}\tsource={t?.AltitudeSource ?? "none"}\treason={c.LastReason}\r\n");
    }
   }
   catch { }
  }

  public void Dispose() { timer.Dispose(); Controller.Dispose(); WriteTransition(); }
 }

 public sealed class SarmatAltitudeAssistPlugin : Plugin
 {
  AltitudeAssistRuntime runtime;
  AltitudeAssistPanel panel;
  TabPage tab;
  TabControl tabs;
  IList original;
  Label hudWarning;
  string root;
  bool registrationQueued;

  public override string Name => "Sarmat Altitude Assist";
  public override string Version => "1.1.5";
  public override string Author => "Sarmat";

  public override bool Init() { loopratehz = 2; return true; }

  public override bool Loaded()
  {
   root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sarmat", "AltitudeAssist");
   try
   {
    Directory.CreateDirectory(Path.Combine(root, "logs"));
    StartupLog("Loaded() started");
    var settings = AltitudeAssistSettings.Load(Path.Combine(root, "settings.json"));
    var log = Path.Combine(root, "logs", DateTime.UtcNow.ToString("yyyyMMdd") + ".log");
    runtime = new AltitudeAssistRuntime(settings, new MissionPlannerTelemetrySource(() => Host.cs, () => Host.comPort, settings), log);
    panel = new AltitudeAssistPanel(runtime);
    runtime.Updated += UpdateHud;
    QueueRegistration();
    StartupLog("Runtime initialized; UI registration queued");
    return true;
   }
   catch (Exception ex)
   {
    StartupLog("Loaded() failed: " + ex);
    return false;
   }
  }

  public override bool Loop()
  {
   if (tab == null || tab.IsDisposed || tab.Parent == null) QueueRegistration();
   return true;
  }

  void QueueRegistration()
  {
   if (registrationQueued) return;
   var main = Host?.MainForm as Control;
   if (main == null || main.IsDisposed) return;
   registrationQueued = true;
   try { main.BeginInvoke((Action)TryRegister); }
   catch (Exception ex) { registrationQueued = false; StartupLog("BeginInvoke registration failed: " + ex.Message); }
  }

  void TryRegister()
  {
   registrationQueued = false;
   try
   {
    var main = Host.MainForm as Control;
    if (main == null || main.IsDisposed) return;
    tabs = Find(main, "tabControlactions") as TabControl;
    if (tabs == null) { StartupLog("FlightData tabControlactions is not ready"); return; }

    if (tab == null || tab.IsDisposed)
    {
     tab = new TabPage("Altitude Assist") { Name = "tabSarmatAltitudeAssist" };
     panel.Dock = DockStyle.Fill;
     tab.Controls.Add(panel);
    }

    var insertAt = FindSarmatIndex(tabs.TabPages) + 1;
    if (insertAt <= 0) insertAt = Math.Min(1, tabs.TabPages.Count);
    if (!tabs.TabPages.Contains(tab)) tabs.TabPages.Insert(Math.Min(insertAt, tabs.TabPages.Count), tab);

    var flightData = tabs.FindForm() == null ? null : FindAncestorWithField(tabs, "TabListOriginal");
    var field = flightData?.GetType().GetField("TabListOriginal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    original = field?.GetValue(flightData) as IList;
    if (original != null && !original.Contains(tab))
    {
     var originalIndex = FindSarmatIndex(original) + 1;
     if (originalIndex <= 0) originalIndex = Math.Min(1, original.Count);
     original.Insert(Math.Min(originalIndex, original.Count), tab);
    }

    if (hudWarning == null || hudWarning.IsDisposed)
    {
     var hud = Find(main, "hud1");
     if (hud != null)
     {
      hudWarning = new Label { Dock = DockStyle.Bottom, Height = 54, Visible = false, ForeColor = Color.White, BackColor = Color.FromArgb(165, 65, 0), Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 12, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
      hud.Controls.Add(hudWarning);
      hudWarning.BringToFront();
     }
    }
    StartupLog("Altitude Assist tab registered at index " + tabs.TabPages.IndexOf(tab));
   }
   catch (Exception ex) { StartupLog("UI registration failed: " + ex); }
  }

  static int FindSarmatIndex(TabControl.TabPageCollection pages)
  {
   for (var i = 0; i < pages.Count; i++) if (IsSarmatTab(pages[i])) return i;
   return -1;
  }

  static int FindSarmatIndex(IList pages)
  {
   for (var i = 0; i < pages.Count; i++) if (IsSarmatTab(pages[i] as TabPage)) return i;
   return -1;
  }

  static bool IsSarmatTab(TabPage page) => page != null && (string.Equals(page.Text, "Sarmat", StringComparison.OrdinalIgnoreCase) || string.Equals(page.Name, "tabSarmat", StringComparison.OrdinalIgnoreCase));

  static Control FindAncestorWithField(Control control, string fieldName)
  {
   for (var current = control; current != null; current = current.Parent)
    if (current.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null) return current;
   return null;
  }

  void UpdateHud()
  {
   if (hudWarning == null || hudWarning.IsDisposed) return;
   if (hudWarning.InvokeRequired) { try { hudWarning.BeginInvoke((Action)UpdateHud); } catch { } return; }
   var c = runtime.Controller;
   var auto = c.State == AssistState.CLIMBING || c.State == AssistState.DESCENDING;
   hudWarning.Visible = auto && DateTime.UtcNow.Millisecond < 500;
   if (auto)
   {
    var t = c.LastTelemetry;
    var direction = c.State == AssistState.CLIMBING ? "⚠ AUTO CLIMB ACTIVE" : "⚠ AUTO DESCENT ACTIVE";
    hudWarning.Text = $"{direction} — TARGET {c.Target:F0} m\r\n{t?.AltitudeMeters ?? 0:F0} m → {c.Target:F0} m";
   }
  }

  void StartupLog(string message)
  {
   try
   {
    var folder = string.IsNullOrEmpty(root) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sarmat", "AltitudeAssist") : root;
    Directory.CreateDirectory(Path.Combine(folder, "logs"));
    File.AppendAllText(Path.Combine(folder, "logs", "startup.log"), DateTime.UtcNow.ToString("O") + "\t" + message + "\r\n");
   }
   catch { }
  }

  static Control Find(Control control, string name)
  {
   if (control == null) return null;
   if (string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase)) return control;
   foreach (Control child in control.Controls) { var found = Find(child, name); if (found != null) return found; }
   return null;
  }

  public override bool Exit()
  {
   try
   {
    if (runtime != null) runtime.Updated -= UpdateHud;
    runtime?.Settings.Save(Path.Combine(root, "settings.json"));
    runtime?.Dispose();
    hudWarning?.Parent?.Controls.Remove(hudWarning);
    hudWarning?.Dispose();
    if (tab != null) { original?.Remove(tab); tabs?.TabPages.Remove(tab); tab.Dispose(); }
    panel?.Dispose();
    StartupLog("Plugin exited");
    return true;
   }
   catch (Exception ex) { StartupLog("Exit failed: " + ex); return false; }
  }
 }
}
