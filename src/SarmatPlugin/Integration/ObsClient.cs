using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SarmatPlugin.Core;
using SarmatPlugin.Infrastructure;

namespace SarmatPlugin.Integration
{
    public sealed class ObsClient
    {
        private readonly PluginSettings settings;
        private readonly AppLog log;
        public ObsClient(PluginSettings settings, AppLog log) { this.settings = settings; this.log = log; }

        public Task<ObsStatus> QueryAsync(CancellationToken ct) => ExecuteAsync(null, ct);

        public Task<ObsStatus> SynchronizeRecordingAsync(bool shouldRecord, CancellationToken ct) =>
            ExecuteAsync(shouldRecord, ct);

        private async Task<ObsStatus> ExecuteAsync(bool? shouldRecord, CancellationToken ct)
        {
            using (var socket = new ClientWebSocket())
            {
                try
                {
                    await socket.ConnectAsync(new Uri(settings.ObsEndpoint), ct).ConfigureAwait(false);
                    var hello = await ReceiveObjectAsync(socket, ct).ConfigureAwait(false);
                    if (MiniJson.Int(hello, "op") != 0) throw new InvalidOperationException("Unexpected OBS hello opcode");
                    var helloData = MiniJson.Object(hello["d"]);
                    var identifyData = new Dictionary<string, object> { ["rpcVersion"] = 1 };
                    if (helloData.TryGetValue("authentication", out var authValue))
                    {
                        var auth = MiniJson.Object(authValue);
                        identifyData["authentication"] = Authentication(settings.ObsPassword,
                            MiniJson.String(auth, "salt"), MiniJson.String(auth, "challenge"));
                    }
                    await SendAsync(socket, new Dictionary<string, object>
                    {
                        ["op"] = 1, ["d"] = identifyData
                    }, ct).ConfigureAwait(false);
                    var identified = await ReceiveObjectAsync(socket, ct).ConfigureAwait(false);
                    if (MiniJson.Int(identified, "op") != 2) throw new InvalidOperationException("OBS identification failed");

                    var recording = await GetRecordingStatusAsync(socket, ct).ConfigureAwait(false);
                    if (shouldRecord.HasValue && recording != shouldRecord.Value)
                    {
                        var requestType = shouldRecord.Value ? "StartRecord" : "StopRecord";
                        await SendRequestAsync(socket, requestType, ct).ConfigureAwait(false);
                        recording = shouldRecord.Value;
                        log.Info("OBS recording " + (recording ? "started after ARMED" : "stopped after DISARMED"));
                    }
                    return new ObsStatus { Connected = true, Recording = recording, UpdatedUtc = DateTime.UtcNow };
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    log.Warn("OBS request failed: " + ex.Message);
                    return new ObsStatus { Connected = false, Recording = null, Error = ex.Message, UpdatedUtc = DateTime.UtcNow };
                }
            }
        }

        private static async Task<bool> GetRecordingStatusAsync(ClientWebSocket socket, CancellationToken ct)
        {
            var data = await SendRequestAsync(socket, "GetRecordStatus", ct).ConfigureAwait(false);
            var responseData = MiniJson.Object(data.TryGetValue("responseData", out var value) ? value : null);
            return MiniJson.Bool(responseData, "outputActive");
        }

        private static async Task<IDictionary<string, object>> SendRequestAsync(
            ClientWebSocket socket, string requestType, CancellationToken ct)
        {
            var id = "sarmat-" + Guid.NewGuid().ToString("N");
            await SendAsync(socket, BuildRequest(requestType, id), ct).ConfigureAwait(false);
            while (true)
            {
                var message = await ReceiveObjectAsync(socket, ct).ConfigureAwait(false);
                if (MiniJson.Int(message, "op") != 7) continue;
                var data = MiniJson.Object(message["d"]);
                if (MiniJson.String(data, "requestId") != id) continue;
                var requestStatus = MiniJson.Object(data["requestStatus"]);
                if (!MiniJson.Bool(requestStatus, "result"))
                    throw new InvalidOperationException("OBS " + requestType + " failed: " +
                        MiniJson.String(requestStatus, "comment"));
                return data;
            }
        }

        internal static IDictionary<string, object> BuildRequest(string requestType, string requestId) =>
            new Dictionary<string, object>
            {
                ["op"] = 6,
                ["d"] = new Dictionary<string, object>
                {
                    ["requestType"] = requestType,
                    ["requestId"] = requestId
                }
            };

        public static string Authentication(string password, string salt, string challenge)
        {
            using (var sha = SHA256.Create())
            {
                var secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes((password ?? "") + salt)));
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
            }
        }
        private static async Task SendAsync(ClientWebSocket socket, object value, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(MiniJson.Serialize(value));
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        private static async Task<IDictionary<string, object>> ReceiveObjectAsync(ClientWebSocket socket, CancellationToken ct)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("OBS closed the connection");
                    output.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                return MiniJson.Object(MiniJson.Parse(Encoding.UTF8.GetString(output.ToArray())));
            }
        }
    }
}
