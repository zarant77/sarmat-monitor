using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SarmatPlugin.Core;
using SarmatPlugin.Infrastructure;

namespace SarmatPlugin.Integration
{
    public sealed class RuijieClient : IDisposable
    {
        private static readonly Regex DirectKey = new Regex(
            @"GibberishAES\s*\.\s*enc\s*\(\s*[A-Za-z_$][A-Za-z0-9_$]*\s*\.\s*value\s*,\s*[""']([^""']+)[""']",
            RegexOptions.Singleline);
        private static readonly Regex EncryptedKey = new Regex(
            @"GibberishAES\s*\.\s*dec\s*\(\s*[""']([^""']+)[""']\s*,\s*[""']([^""']+)[""']\s*\)",
            RegexOptions.Singleline);
        private readonly PluginSettings settings;
        private readonly AppLog log;
        private readonly CookieContainer cookies = new CookieContainer();
        private readonly HttpClient http;
        private readonly SemaphoreSlim authLock = new SemaphoreSlim(1, 1);
        private string token;

        public RuijieClient(PluginSettings settings, AppLog log)
        {
            this.settings = settings; this.log = log;
            ServicePointManager.Expect100Continue = false;
            var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
            if (settings.RuijieAllowInsecureTls)
                handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => true;
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(settings.RuijieRequestTimeoutSeconds) };
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            if (Uri.TryCreate(BaseUrl, UriKind.Absolute, out var routerUri))
                ServicePointManager.FindServicePoint(routerUri).Expect100Continue = false;
        }

        public async Task<RuijieStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrEmpty(token)) await LoginAsync(cancellationToken).ConfigureAwait(false);
                try { return await FetchQualityAsync(cancellationToken).ConfigureAwait(false); }
                catch (RuijieAuthException)
                {
                    token = null;
                    await LoginAsync(cancellationToken).ConfigureAwait(false);
                    return await FetchQualityAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                log.Warn("Ruijie request failed: " + ex.Message);
                return new RuijieStatus { Connected = false, Error = ex.Message };
            }
        }

        public async Task LoginAsync(CancellationToken cancellationToken)
        {
            await authLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrEmpty(token)) return;
                for (var reload = 0; reload < 2; reload++)
                {
                    var page = await http.GetStringAsync(BaseUrl + "/cgi-bin/luci/").ConfigureAwait(false);
                    var parsed = ParseAuthPage(page);
                    var encrypted = RuijieCrypto.EncryptPassword(settings.RuijiePassword, parsed.Key);
                    var modern = parsed.Modern;
                    var response = await SubmitLoginAsync(encrypted, modern, cancellationToken).ConfigureAwait(false);
                    if (GetReload(response)) continue;
                    var code = MiniJson.Int(response, "code");
                    if (code != 0 && IsFormatError(response))
                        response = await SubmitLoginAsync(encrypted, !modern, cancellationToken).ConfigureAwait(false);
                    code = MiniJson.Int(response, "code");
                    var data = MiniJson.Object(response.TryGetValue("data", out var d) ? d : null);
                    var lockTime = MiniJson.Int(data, "lockTime");
                    if (lockTime > 0) throw new InvalidOperationException("Ruijie account locked for " + lockTime + " seconds");
                    if (code != 0) throw new InvalidOperationException("Ruijie login API code " + code + ": " +
                        (MiniJson.String(response, "msg") ?? MiniJson.String(data, "msg")));
                    token = CookieToken() ?? MiniJson.String(data, "sid") ?? MiniJson.String(data, "token");
                    if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Ruijie auth token missing");
                    log.Info("Ruijie login succeeded");
                    return;
                }
                throw new InvalidOperationException("Ruijie auth key reload loop");
            }
            finally { authLock.Release(); }
        }

        private async Task<IDictionary<string, object>> SubmitLoginAsync(string encrypted, bool modern, CancellationToken ct)
        {
            var parameters = new Dictionary<string, object>
            {
                ["username"] = settings.RuijieUsername, ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["encry"] = true,
                [modern ? "password" : "pwd"] = encrypted
            };
            return await PostAsync("/cgi-bin/luci/api/auth", null,
                new Dictionary<string, object> { ["method"] = "login", ["params"] = parameters }, ct).ConfigureAwait(false);
        }

        private async Task<RuijieStatus> FetchQualityAsync(CancellationToken ct)
        {
            var parameters = new Dictionary<string, object>
            {
                ["module"]="wdsLinkQuality", ["noParse"]=false, ["async"]=null, ["remoteIp"]=false, ["data"]=null, ["device"]="pc"
            };
            var response = await PostAsync("/cgi-bin/luci/api/cmd", token,
                new Dictionary<string, object> { ["method"]="devSta.get", ["params"]=parameters }, ct).ConfigureAwait(false);
            var code = MiniJson.Int(response, "code");
            if (code != 0)
            {
                var msg = MiniJson.String(response, "msg") ?? "";
                if (code == 401 || code == 403 || msg.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0) throw new RuijieAuthException(msg);
                throw new InvalidOperationException("Ruijie devSta.get API code " + code + ": " + msg);
            }
            var data = MiniJson.Object(response["data"]);
            var devices = data != null && data.TryGetValue("devList", out var listValue) ? listValue as IList<object> : null;
            if (devices == null || devices.Count == 0) throw new InvalidOperationException("Ruijie remote device list is empty");
            var values = new List<int>();
            foreach (var item in devices)
            {
                var device = MiniJson.Object(item);
                foreach (var key in new[] {"uplink_rssi_h","uplink_rssi_v","downlink_rssi_h","downlink_rssi_v"})
                    if (int.TryParse((MiniJson.String(device, key) ?? "").Trim().TrimEnd('%'),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out var rssi)) values.Add(rssi);
            }
            if (values.Count == 0) throw new InvalidOperationException("Ruijie remote device contains no valid RSSI values");
            var average = values.Sum() / values.Count;
            var score = Math.Max(0, Math.Min(100, (average + 90) * 100 / 40));
            if (int.TryParse((MiniJson.String(data, "chutil") ?? "").TrimEnd('%'), out var utilization))
                score -= utilization > 80 ? 20 : utilization > 50 ? 10 : 0;
            if (int.TryParse(MiniJson.String(data, "channf"), out var noise) && noise > -80) score -= 10;
            score = Math.Max(0, Math.Min(100, score));
            var quality = score >= 85 ? "Excellent" : score >= 70 ? "Good" : score >= 50 ? "Weak" : "Bad";
            return new RuijieStatus { Connected = true, Rssi = average, SignalQuality = quality, LastSuccessUtc = DateTime.UtcNow };
        }

        private async Task<IDictionary<string, object>> PostAsync(string path, string auth, object payload, CancellationToken ct)
        {
            var endpoint = BaseUrl + path + (string.IsNullOrEmpty(auth) ? "" : "?auth=" + Uri.EscapeDataString(auth));
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Content = new StringContent(MiniJson.Serialize(payload), Encoding.UTF8, "application/json");
                ConfigureLegacyRouterRequest(request);
                if (!string.IsNullOrEmpty(auth)) request.Headers.TryAddWithoutValidation("Cookie", "webauth=" + auth);
                using (var response = await http.SendAsync(request, ct).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    log.Debug("Ruijie HTTP " + (int)response.StatusCode + " " + AppLog.Sanitize(body));
                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        throw new RuijieAuthException(response.StatusCode.ToString());
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("Ruijie HTTP " + (int)response.StatusCode + ": " + AppLog.Sanitize(body));
                    return MiniJson.Object(MiniJson.Parse(body)) ?? throw new FormatException("Ruijie response is not an object");
                }
            }
        }

        internal static void ConfigureLegacyRouterRequest(HttpRequestMessage request)
        {
            // Old Ruijie/uHTTPd firmware responds with 417 when .NET Framework
            // performs the RFC 7231 Expect: 100-continue handshake.
            request.Headers.ExpectContinue = false;
            request.Version = HttpVersion.Version11;
        }

        private string BaseUrl => (settings.RuijieAddress ?? "").TrimEnd('/');
        private string CookieToken()
        {
            foreach (Cookie c in cookies.GetCookies(new Uri(BaseUrl)))
                if (string.Equals(c.Name, "webauth", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.Value)) return c.Value;
            return null;
        }
        private static bool GetReload(IDictionary<string, object> response)
        {
            var data = MiniJson.Object(response.TryGetValue("data", out var d) ? d : null);
            return MiniJson.Bool(data, "reload");
        }
        private static bool IsFormatError(IDictionary<string, object> response)
        {
            var data = MiniJson.Object(response.TryGetValue("data", out var d) ? d : null);
            var text = (MiniJson.String(response, "msg") ?? "") + " " + (MiniJson.String(data, "msg") ?? "");
            return text.IndexOf("param", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("format", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("request", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static (string Key, bool Modern) ParseAuthPage(string page)
        {
            var encrypted = EncryptedKey.Match(page ?? "");
            if (encrypted.Success) return (string.Concat(RuijieCrypto.DecryptOpenSsl(encrypted.Groups[1].Value,
                encrypted.Groups[2].Value).Where(c => !char.IsWhiteSpace(c))), true);
            var direct = DirectKey.Match(page ?? "");
            if (direct.Success) return (direct.Groups[1].Value.Trim(), false);
            throw new FormatException("Unsupported Ruijie authentication format");
        }
        public void Dispose() { http.Dispose(); authLock.Dispose(); }
        private sealed class RuijieAuthException : Exception { public RuijieAuthException(string message) : base(message) { } }
    }
}
