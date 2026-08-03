using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Linq;
using System.IO;
using System.Runtime.Serialization.Json;
using SarmatPlugin.Core;
using SarmatPlugin.Infrastructure;
using SarmatPlugin.Integration;

namespace SarmatPlugin.Tests
{
    internal static class Program
    {
        private static int failures;
        private static void Main()
        {
            Run("Alert engine enforces ARMED and grace", ArmedAndGrace);
            Run("Alert engine debounce and recovery", DebounceRecovery);
            Run("Alert engine follows shared red thresholds", SharedAlertThresholds);
            Run("Critical priority and all reasons", Priority);
            Run("Audio alerts are announced on bad-state transitions", AudioAlertTransitions);
            Run("OBS authentication matches v5 example", ObsAuthentication);
            Run("OBS recording control request envelopes", ObsRecordingRequests);
            Run("OBS automation reacts only to ARMED edges", ObsArmingEdges);
            Run("OBS widget colors depend on ARMED and recording state", ObsWidgetColors);
            Run("Telemetry widget thresholds match Sarmat Monitor", TelemetryWidgetThresholds);
            Run("Takeoff mode warning only checks the arming transition", TakeoffModeWarning);
            Run("MAVLink silence watchdog requests bounded reconnects", MavlinkSilenceReconnect);
            Run("Ruijie OpenSSL AES round trip", CryptoRoundTrip);
            Run("Ruijie legacy auth page", LegacyAuthPage);
            Run("Ruijie disables Expect 100-continue", RuijieExpectContinue);
            Run("Aggregator telemetry uses compact MessagePack", AggregatorMessagePack);
            Run("Widget catalog defaults are valid and unique", WidgetDefaults);
            Run("Mission Planner scalar telemetry is discovered dynamically", DynamicTelemetry);
            Run("Mission Planner HUD visibility adapter", HudVisibility);
            Run("HUD settings dictionary JSON round trip", HudSettingsRoundTrip);
            Run("Sarmat GStreamer pipeline targets Mission Planner appsink", GStreamerPipeline);
            Run("Log sanitizer redacts credentials", Sanitizer);
            Console.WriteLine(failures == 0 ? "All tests passed." : failures + " test(s) failed.");
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        private static PluginSettings Fast() => new PluginSettings();
        private static TelemetrySnapshot T(bool armed=true, double battery=48, int sats=30, double hdop=.5,
            double current=20) => new TelemetrySnapshot { Armed=armed, BatteryVoltage=battery,
                Satellites=sats, Hdop=hdop, CurrentAmps=current };
        private static ObsStatus O(bool connected=true, bool recording=true) => new ObsStatus { Connected=connected, Recording=recording };
        private static RuijieStatus R(bool connected=true, bool stale=false) => new RuijieStatus { Connected=connected, Stale=stale };
        private static void ArmedAndGrace()
        {
            var e=new AlertEngine(); var s=Fast(); var now=DateTime.UtcNow;
            Equal(Severity.Inactive,e.Update(T(false),O(),R(),s,now).Severity);
            Equal(Severity.Ok,e.Update(T(true,battery:40),O(),R(),s,now).Severity);
            Equal(Severity.Ok,e.Update(T(true,battery:40),O(),R(),s,now.AddSeconds(3)).Severity);
            Equal(Severity.Critical,e.Update(T(true,battery:40),O(),R(),s,now.AddSeconds(5)).Severity);
        }
        private static void DebounceRecovery()
        {
            var e=new AlertEngine(); var s=Fast(); var n=DateTime.UtcNow;
            e.Update(T(),O(),R(),s,n);
            Equal(Severity.Ok,e.Update(T(battery:40),O(),R(),s,n.AddSeconds(3)).Severity);
            Equal(Severity.Critical,e.Update(T(battery:40),O(),R(),s,n.AddSeconds(5)).Severity);
            Equal(Severity.Critical,e.Update(T(battery:45),O(),R(),s,n.AddSeconds(6)).Severity);
            var restored=e.Update(T(battery:45),O(),R(),s,n.AddSeconds(8));
            Equal(Severity.Ok,restored.Severity); True(restored.Restored);
        }

        private static void ObsWidgetColors()
        {
            Equal(WidgetStatus.Good, WidgetStatusPolicy.Obs(true, O(recording:true)));
            Equal(WidgetStatus.Bad, WidgetStatusPolicy.Obs(true, O(recording:false)));
            Equal(WidgetStatus.Normal, WidgetStatusPolicy.Obs(false, O(recording:true)));
            Equal(WidgetStatus.Good, WidgetStatusPolicy.Obs(false, O(recording:false)));
            Equal(WidgetStatus.Bad, WidgetStatusPolicy.Obs(false, O(connected:false)));
        }
        private static void TelemetryWidgetThresholds()
        {
            Equal(WidgetStatus.Good, TelemetryStatusPolicy.Voltage(44));
            Equal(WidgetStatus.Normal, TelemetryStatusPolicy.Voltage(42));
            Equal(WidgetStatus.Bad, TelemetryStatusPolicy.Voltage(41.9));
            Equal(WidgetStatus.Good, TelemetryStatusPolicy.Current(80));
            Equal(WidgetStatus.Normal, TelemetryStatusPolicy.Current(120));
            Equal(WidgetStatus.Bad, TelemetryStatusPolicy.Current(120.1));
            Equal(WidgetStatus.Good, TelemetryStatusPolicy.Satellites(30));
            Equal(WidgetStatus.Normal, TelemetryStatusPolicy.Satellites(26));
            Equal(WidgetStatus.Bad, TelemetryStatusPolicy.Satellites(25));
            Equal(WidgetStatus.Good, TelemetryStatusPolicy.Hdop(0.6));
            Equal(WidgetStatus.Normal, TelemetryStatusPolicy.Hdop(0.8));
            Equal(WidgetStatus.Bad, TelemetryStatusPolicy.Hdop(0.81));
            Equal(WidgetStatus.Good, TelemetryStatusPolicy.LinkRssi(-70));
            Equal(WidgetStatus.Normal, TelemetryStatusPolicy.LinkRssi(-80));
            Equal(WidgetStatus.Bad, TelemetryStatusPolicy.LinkRssi(-81));
            Equal(WidgetStatus.Good, TelemetryStatusPolicy.DistanceToHome(25));
            Equal(WidgetStatus.Normal, TelemetryStatusPolicy.DistanceToHome(50));
            Equal(WidgetStatus.Bad, TelemetryStatusPolicy.DistanceToHome(50.1));
        }
        private static void SharedAlertThresholds()
        {
            var e=new AlertEngine(); var s=Fast(); var n=DateTime.UtcNow;
            Equal(Severity.Ok,e.Update(T(battery:40),O(),R(),s,n).Severity);
            Equal(Severity.Ok,e.Update(T(battery:40),O(),R(),s,n.AddSeconds(3)).Severity);
            Equal(Severity.Critical,e.Update(T(battery:40),O(),R(),s,n.AddSeconds(5)).Severity);
            Equal(Severity.Critical,e.Update(T(battery:42),O(),R(),s,n.AddSeconds(6)).Severity);
            Equal(Severity.Ok,e.Update(T(battery:42),O(),R(),s,n.AddSeconds(8)).Severity);
        }
        private static void Priority()
        {
            var e=new AlertEngine();var s=Fast();var n=DateTime.UtcNow;
            e.Update(T(battery:40,sats:10,hdop:2),O(false,false),R(false),s,n);
            e.Update(T(battery:40,sats:10,hdop:2),O(false,false),R(false),s,n.AddSeconds(3));
            var result=e.Update(T(battery:40,sats:10,hdop:2),O(false,false),R(false),s,n.AddSeconds(5));
            Equal(Severity.Critical,result.Severity); Equal(5,result.Reasons.Count);
        }
        private static void AudioAlertTransitions()
        {
            var tracker = new AlertTransitionTracker();
            var battery = new AlertReason { Kind=AlertKind.Battery, Severity=Severity.Critical };
            var sats = new AlertReason { Kind=AlertKind.Satellites, Severity=Severity.Warning };
            var hdop = new AlertReason { Kind=AlertKind.Hdop, Severity=Severity.Warning };
            Equal(2, tracker.SelectNew(new[] { battery, sats }, true).Count);
            Equal(0, tracker.SelectNew(new[] { battery, sats }, true).Count);
            Equal(0, tracker.SelectNew(new AlertReason[0], true).Count);
            Equal(1, tracker.SelectNew(new[] { battery }, true).Count);
            Equal(1, tracker.SelectNew(new[] { battery, hdop }, true).Count);
            Equal(0, tracker.SelectNew(new AlertReason[0], false).Count);
            Equal(1, tracker.SelectNew(new[] { battery }, true).Count);

            var settings = new PluginSettings { AudioAlertCooldownSeconds = 0 };
            settings.Normalize();
            Equal(10d, settings.AudioAlertCooldownSeconds);
        }
        private static void ObsAuthentication()
        {
            Equal("lHWJEH5mqVrESU/FA5vyrWKrpu/kWC/aALVmIhPmtlw=",
                ObsClient.Authentication("supersecret","cnZsPpNCQjJL5wWJ","xRM1rP2VjG6eG7Q5"));
        }
        private static void ObsRecordingRequests()
        {
            foreach (var requestType in new[] { "StartRecord", "StopRecord" })
            {
                var envelope = ObsClient.BuildRequest(requestType, "test-id");
                Equal(6, Convert.ToInt32(envelope["op"]));
                var data = (IDictionary<string, object>)envelope["d"];
                Equal(requestType, data["requestType"]);
                Equal("test-id", data["requestId"]);
            }
        }
        private static void ObsArmingEdges()
        {
            var tracker = new ObsArmingTransitionTracker(false);
            Equal(null, tracker.PendingCommand(false));

            Equal(true, tracker.PendingCommand(true));
            // A failed/disconnected attempt remains pending.
            Equal(true, tracker.PendingCommand(true));
            tracker.Confirm(true);
            // Manual Start/Stop while the ARMED state is unchanged produces no command.
            Equal(null, tracker.PendingCommand(true));

            Equal(false, tracker.PendingCommand(false));
            tracker.Confirm(false);
            Equal(null, tracker.PendingCommand(false));
        }
        private static void TakeoffModeWarning()
        {
            var tracker = new TakeoffModeWarningTracker();
            Equal(false, tracker.Update(false, "AltHold"));
            Equal(true, tracker.Update(true, "AltHold"));
            Equal(true, tracker.Update(true, "Loiter"));
            Equal(false, tracker.Update(true, "PostHold"));
            Equal(false, tracker.Update(true, "AltHold"));
            Equal(false, tracker.Update(false, "AltHold"));
            Equal(false, tracker.Update(true, "POSHOLD"));

            var correctAtTakeoff = new TakeoffModeWarningTracker();
            correctAtTakeoff.Update(false, "AltHold");
            Equal(false, correctAtTakeoff.Update(true, "Pos Hold"));
            Equal(false, correctAtTakeoff.Update(true, "AltHold"));
        }
        private static void MavlinkSilenceReconnect()
        {
            var watchdog = new MavlinkSilenceWatchdog();
            var now = DateTime.UtcNow;
            Equal(false, watchdog.Update(true, 10, now, 10));
            Equal(false, watchdog.Update(true, 10, now.AddSeconds(9), 10));
            Equal(true, watchdog.Update(true, 10, now.AddSeconds(10), 10));
            Equal(false, watchdog.Update(true, 10, now.AddSeconds(11), 10));
            Equal(false, watchdog.Update(true, 11, now.AddSeconds(12), 10));
            Equal(true, watchdog.Update(true, 11, now.AddSeconds(22), 10));
            Equal(false, watchdog.Update(false, 11, now.AddSeconds(23), 10));
            Equal(false, watchdog.Update(true, 11, now.AddSeconds(40), 10));
        }
        private static void CryptoRoundTrip()
        {
            var encrypted=RuijieCrypto.EncryptPassword("secret","key12345",new byte[]{1,2,3,4,5,6,7,8});
            Equal("secret",RuijieCrypto.DecryptOpenSsl(encrypted,"key12345"));
        }
        private static void LegacyAuthPage()
        {
            var parsed=RuijieClient.ParseAuthPage("<script>GibberishAES.enc(password.value, 'abcdef')</script>");
            Equal("abcdef",parsed.Key); True(!parsed.Modern);
        }
        private static void RuijieExpectContinue()
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://router/cgi-bin/luci/api/auth"))
            {
                request.Headers.ExpectContinue = true;
                RuijieClient.ConfigureLegacyRouterRequest(request);
                Equal(false, request.Headers.ExpectContinue);
                Equal(new Version(1, 1), request.Version);
            }
        }
        private static void AggregatorMessagePack()
        {
            var packet = MessagePackTelemetryEncoder.Encode(1, null, null, 2,
                null, null, null, -70, 4);
            var expected = new byte[] { 0x99, 0x01, 0xc0, 0xc0, 0x02, 0xc0, 0xc0, 0xc0, 0xd0, 0xba, 0x04 };
            Equal(BitConverter.ToString(expected), BitConverter.ToString(packet));

            var settings = new PluginSettings
            {
                AggregatorUrl = "  ws://127.0.0.1:8080/ws/station  ",
                AggregatorSecret = "  station-secret  ",
                AggregatorReconnectSeconds = 0
            };
            settings.Normalize();
            Equal("ws://127.0.0.1:8080/ws/station", settings.AggregatorUrl);
            Equal("station-secret", settings.AggregatorSecret);
            Equal(1d, settings.AggregatorReconnectSeconds);
        }
        private static void WidgetDefaults()
        {
            var settings = new PluginSettings();
            settings.Normalize();
            Equal(WidgetCatalog.Definitions.Count, settings.EnabledWidgets.Count);
            Equal(settings.EnabledWidgets.Count,
                settings.EnabledWidgets.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            True(settings.EnabledWidgets.All(WidgetCatalog.IsKnown));

            settings.EnabledWidgets = new List<string> { "obs", "unknown", "OBS" };
            settings.Normalize();
            Equal(1, settings.EnabledWidgets.Count);
            Equal("obs", settings.EnabledWidgets[0]);
        }
        private static void DynamicTelemetry()
        {
            var state = new FakeCurrentState();
            WidgetCatalog.Discover(state);
            True(WidgetCatalog.IsKnown("telemetry:roll"));
            True(WidgetCatalog.IsKnown("telemetry:flightmode"));
            True(!WidgetCatalog.IsKnown("telemetry:Complex"));
            var snapshot = new TelemetryReader(() => state).Read(
                new[] { "telemetry:roll", "telemetry:flightmode" });
            Equal("12.345", snapshot.AdditionalTelemetry["telemetry:roll"]);
            Equal("AUTO", snapshot.AdditionalTelemetry["telemetry:flightmode"]);
        }
        private sealed class FakeCurrentState
        {
            public double roll { get; set; } = 12.345;
            public string flightmode = "AUTO";
            public object Complex { get; set; } = new object();
        }
        private static void HudVisibility()
        {
            var hud = new FakeHud();
            var adapter = new HudVisibilityAdapter(hud);
            Equal(true, adapter.Read()["batteryon"]);
            adapter.Apply(new Dictionary<string, bool>
            {
                ["batteryon"] = false,
                ["displaygps"] = false
            });
            Equal(false, hud.batteryon);
            Equal(false, hud.displaygps);
            Equal(1, hud.ResizeCount);
            Equal(false, adapter.Apply(new Dictionary<string, bool>
            {
                ["batteryon"] = false,
                ["displaygps"] = false
            }));
            Equal(1, hud.ResizeCount);
        }
        private sealed class FakeHud
        {
            public bool batteryon { get; set; } = true;
            public bool displaygps { get; set; } = true;
            public int ResizeCount { get; private set; }
            public void doResize() { ResizeCount++; }
        }
        private static void HudSettingsRoundTrip()
        {
            var source = new PluginSettings
            {
                GStreamerWasStarted = true,
                AudioWarningSoundPath = @"C:\sounds\custom-warning.wav",
                HudElements = new Dictionary<string, bool>
                {
                    ["batteryon"] = false,
                    ["displayconninfo"] = false,
                    ["displaygps"] = true
                }
            };
            var serializer = new DataContractJsonSerializer(typeof(PluginSettings),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, source);
                stream.Position = 0;
                var loaded = (PluginSettings)serializer.ReadObject(stream);
                Equal(3, loaded.HudElements.Count);
                Equal(false, loaded.HudElements["batteryon"]);
                Equal(false, loaded.HudElements["displayconninfo"]);
                Equal(true, loaded.GStreamerWasStarted);
                Equal(@"C:\sounds\custom-warning.wav", loaded.AudioWarningSoundPath);
            }
        }
        private static void GStreamerPipeline()
        {
            var pipeline = GStreamerPipelineBuilder.Build(new PluginSettings());
            Equal("rtspsrc location=rtsp://192.168.69.5:554/stream=0 protocols=tcp latency=150 " +
                "drop-on-latency=true ! rtph264depay ! h264parse ! avdec_h264 ! queue " +
                "max-size-buffers=1 leaky=downstream ! videoconvert ! video/x-raw,format=BGRA ! " +
                "appsink name=outsink sync=false", pipeline);
            var customized = GStreamerPipelineBuilder.Build(new PluginSettings
            {
                CameraUrl = "rtsp://10.0.0.7/live",
                CameraProtocol = "udp",
                CameraLatencyMs = 250,
                CameraDecoder = "decodebin3"
            });
            True(customized.Contains("location=rtsp://10.0.0.7/live protocols=udp latency=250"));
            True(customized.Contains("! decodebin3 !"));
        }
        private static void Sanitizer()
        {
            var value=AppLog.Sanitize("{\"password\":\"secret\",\"token\":\"abcdef\"}");
            True(!value.Contains("secret")&&!value.Contains("abcdef"));
        }
        private static void Equal<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"expected {expected}, got {actual}"); }
        private static void True(bool value) { if(!value) throw new Exception("condition is false"); }
    }
}
