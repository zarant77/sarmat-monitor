using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Linq;
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
            Run("Alert engine hysteresis", Hysteresis);
            Run("Critical priority and all reasons", Priority);
            Run("OBS authentication matches v5 example", ObsAuthentication);
            Run("OBS recording control request envelopes", ObsRecordingRequests);
            Run("OBS automation reacts only to ARMED edges", ObsArmingEdges);
            Run("Ruijie OpenSSL AES round trip", CryptoRoundTrip);
            Run("Ruijie legacy auth page", LegacyAuthPage);
            Run("Ruijie disables Expect 100-continue", RuijieExpectContinue);
            Run("Widget catalog defaults are valid and unique", WidgetDefaults);
            Run("Mission Planner scalar telemetry is discovered dynamically", DynamicTelemetry);
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
        private static PluginSettings Fast() => new PluginSettings
        {
            AlertsEnabled=true, MinimumSatellites=20, MaximumHdop=.8, MinimumBatteryVoltage=44,
            ActivationDebounceSeconds=2, RecoveryDebounceSeconds=2, ArmedGracePeriodSeconds=3
        };
        private static TelemetrySnapshot T(bool armed=true, double battery=48, int sats=25, double hdop=.5) =>
            new TelemetrySnapshot { Armed=armed, BatteryVoltage=battery, Satellites=sats, Hdop=hdop };
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
            var e=new AlertEngine(); var s=Fast(); s.ArmedGracePeriodSeconds=0; var n=DateTime.UtcNow;
            e.Update(T(),O(),R(),s,n);
            Equal(Severity.Ok,e.Update(T(battery:43),O(),R(),s,n).Severity);
            Equal(Severity.Critical,e.Update(T(battery:43),O(),R(),s,n.AddSeconds(2)).Severity);
            Equal(Severity.Critical,e.Update(T(battery:45),O(),R(),s,n.AddSeconds(3)).Severity);
            var restored=e.Update(T(battery:45),O(),R(),s,n.AddSeconds(5));
            Equal(Severity.Ok,restored.Severity); True(restored.Restored);
        }
        private static void Hysteresis()
        {
            var e=new AlertEngine(); var s=Fast(); s.ArmedGracePeriodSeconds=0;s.ActivationDebounceSeconds=0;s.RecoveryDebounceSeconds=0;var n=DateTime.UtcNow;
            Equal(Severity.Critical,e.Update(T(battery:43),O(),R(),s,n).Severity);
            Equal(Severity.Critical,e.Update(T(battery:44.2),O(),R(),s,n.AddSeconds(1)).Severity);
            Equal(Severity.Ok,e.Update(T(battery:44.5),O(),R(),s,n.AddSeconds(2)).Severity);
        }
        private static void Priority()
        {
            var e=new AlertEngine();var s=Fast();s.ArmedGracePeriodSeconds=0;s.ActivationDebounceSeconds=0;var n=DateTime.UtcNow;
            var result=e.Update(T(battery:43,sats:10,hdop:2),O(false,false),R(false),s,n);
            Equal(Severity.Critical,result.Severity); Equal(5,result.Reasons.Count);
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
        private static void GStreamerPipeline()
        {
            var pipeline = SarmatMissionPlannerPlugin.SarmatGStreamerPipeline;
            True(pipeline.Contains("rtsp://192.168.69.5:554/stream=0"));
            True(pipeline.Contains("video/x-raw,format=BGRA"));
            True(pipeline.Contains("appsink name=outsink sync=false"));
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
