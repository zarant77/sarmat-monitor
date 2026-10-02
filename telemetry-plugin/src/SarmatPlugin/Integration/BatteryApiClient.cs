using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SarmatPlugin.Core;
using SarmatPlugin.Infrastructure;

namespace SarmatPlugin.Integration
{
    internal sealed class BatteryApiClient : IDisposable
    {
        private readonly HttpClient client;
        private readonly Uri flightEvents;
        internal static Uri ApiRoot(string websocketUrl)
        {
            var uri = new Uri(websocketUrl);
            if (uri.Scheme != "ws" && uri.Scheme != "wss") throw new ArgumentException("API URL must start with ws:// or wss://");
            var builder = new UriBuilder(uri) { Scheme = uri.Scheme == "wss" ? "https" : "http", Path = "/station/batteries", Query = "", Fragment = "" };
            builder.Port = uri.IsDefaultPort ? -1 : uri.Port;
            return builder.Uri;
        }
        internal static Uri FlightEventsRoot(string websocketUrl)
        {
            var uri = ApiRoot(websocketUrl);
            return new Uri(uri, "/station/flights/events");
        }
        public BatteryApiClient(PluginSettings settings)
        {
            if (!settings.AggregatorEnabled || !settings.BatteryTrackingEnabled)
                throw new InvalidOperationException("Battery API is disabled");
            if (string.IsNullOrWhiteSpace(settings.AggregatorSecret)) throw new ArgumentException("Station secret is required");
            client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = ApiRoot(settings.AggregatorUrl), Timeout = TimeSpan.FromSeconds(20) };
            flightEvents = FlightEventsRoot(settings.AggregatorUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.AggregatorSecret);
        }
        public async Task<IDictionary<string, object>> LoadAsync(CancellationToken token)
        {
            using (var response = await client.GetAsync(client.BaseAddress, token))
            {
                response.EnsureSuccessStatusCode();
                return MiniJson.Object(MiniJson.Parse(await response.Content.ReadAsStringAsync()))
                    ?? throw new InvalidOperationException("Invalid battery API response");
            }
        }
        public async Task<int?> LoadChargeAsync(string sessionId, CancellationToken token)
        {
            var id = Guid.Parse(sessionId).ToString();
            using (var response = await client.GetAsync(new Uri(client.BaseAddress.AbsoluteUri + "/sessions/" + id + "/charge"), token))
            {
                response.EnsureSuccessStatusCode();
                var result = MiniJson.Object(MiniJson.Parse(await response.Content.ReadAsStringAsync()));
                if (MiniJson.String(result, "sessionId") != sessionId) throw new InvalidOperationException("Battery charge session mismatch");
                return ParseChargePercent(result);
            }
        }
        internal static int? ParseChargePercent(IDictionary<string, object> result)
        {
            object raw;
            if (result == null || !result.TryGetValue("currentCharge", out raw)) return null;
            var charge = MiniJson.Object(raw);
            if (charge == null || !charge.TryGetValue("chargePercent", out raw) || raw == null) return null;
            if (!(raw is double) && !(raw is int) && !(raw is long) && !(raw is decimal)) return null;
            var value = Convert.ToDouble(raw);
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 100) return null;
            return (int)Math.Round(value);
        }
        public async Task<bool> SelectAsync(string id, string droneId, string expectedId, string expectedSince, string sessionId, CancellationToken token)
        {
            var json = MiniJson.Serialize(new Dictionary<string, object> { ["batteryId"] = id,
                ["droneId"] = droneId, ["expectedActiveId"] = expectedId, ["expectedActiveSince"] = expectedSince, ["sessionId"] = sessionId });
            using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await client.PutAsync(new Uri(client.BaseAddress.AbsoluteUri + "/active"), body, token))
            {
                if (response.StatusCode == HttpStatusCode.Conflict) return false;
                response.EnsureSuccessStatusCode();
                var result = MiniJson.Object(MiniJson.Parse(await response.Content.ReadAsStringAsync()));
                if (MiniJson.String(result, "sessionId") != sessionId || MiniJson.String(result, "droneId") != droneId)
                    throw new InvalidOperationException("Server must be updated to support drone flight recording");
                return true;
            }
        }
        public async Task<bool> SendFlightAsync(string json, CancellationToken token)
        {
            using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(flightEvents, body, token))
            {
                if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.NotFound) return false;
                response.EnsureSuccessStatusCode();
                var receipt = MiniJson.Object(MiniJson.Parse(await response.Content.ReadAsStringAsync()));
                if (MiniJson.String(receipt, "id") != MiniJson.String(MiniJson.Object(MiniJson.Parse(json)), "id"))
                    throw new InvalidOperationException("Missing flight event receipt");
                return true;
            }
        }
        public async Task<bool> SendVoltageAsync(string json, CancellationToken token)
        {
            using (var body = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(new Uri(client.BaseAddress.AbsoluteUri + "/voltage-events"), body, token))
            {
                // Permanent validation/binding errors are retained separately for inspection.
                if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.NotFound) return false;
                response.EnsureSuccessStatusCode();
                var receipt = MiniJson.Object(MiniJson.Parse(await response.Content.ReadAsStringAsync()));
                if (MiniJson.String(receipt, "id") != MiniJson.String(MiniJson.Object(MiniJson.Parse(json)), "id"))
                    throw new InvalidOperationException("Missing voltage event receipt");
                return true;
            }
        }
        public void Dispose() => client.Dispose();
    }
}
