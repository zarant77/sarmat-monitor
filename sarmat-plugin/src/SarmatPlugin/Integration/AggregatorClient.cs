using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SarmatPlugin.Core;
using SarmatPlugin.Infrastructure;

namespace SarmatPlugin.Integration
{
    internal sealed class AggregatorClient
    {
        private readonly PluginSettings settings;
        private readonly AppLog log;
        private readonly Action<string> updateStatus;
        private uint sequence;

        public AggregatorClient(PluginSettings settings, AppLog log, Action<string> updateStatus)
        {
            this.settings = settings;
            this.log = log;
            this.updateStatus = updateStatus;
        }

        public async Task RunAsync(Func<TelemetrySnapshot> readTelemetry,
            Func<ObsStatus> readObs, Func<RuijieStatus> readRuijie, CancellationToken token)
        {
            if (!settings.AggregatorEnabled)
            {
                updateStatus("Disabled");
                return;
            }

            Validate(settings.AggregatorUrl, settings.AggregatorSecret,
                settings.MonitorStationName, settings.MonitorStationColor);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    updateStatus("Connecting");
                    using (var socket = CreateSocket(settings.AggregatorSecret))
                    {
                        await socket.ConnectAsync(new Uri(settings.AggregatorUrl), token).ConfigureAwait(false);
                        await SendMetadataAsync(socket, settings.MonitorStationName,
                            settings.MonitorStationColor, token).ConfigureAwait(false);
                        updateStatus("Connected");
                        log.Info("Telemetry aggregator connected");

                        using (var connected = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            var send = SendLoopAsync(socket, readTelemetry, readObs, readRuijie, connected.Token);
                            var receive = ReceiveUntilClosedAsync(socket, connected.Token);
                            var completed = await Task.WhenAny(send, receive).ConfigureAwait(false);
                            connected.Cancel();
                            await completed.ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    updateStatus("Disconnected: " + ex.Message);
                    log.Warn("Telemetry aggregator connection failed: " + ex.Message);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(settings.AggregatorReconnectSeconds), token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
            updateStatus(settings.AggregatorEnabled ? "Stopped" : "Disabled");
        }

        public static async Task TestConnectionAsync(string url, string secret,
            string stationName, string stationColor, CancellationToken token)
        {
            Validate(url, secret, stationName, stationColor);
            using (var socket = CreateSocket(secret))
            {
                await socket.ConnectAsync(new Uri(url), token).ConfigureAwait(false);
                await SendMetadataAsync(socket, stationName, stationColor, token).ConfigureAwait(false);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection test", token)
                    .ConfigureAwait(false);
            }
        }

        private static Task SendMetadataAsync(ClientWebSocket socket, string name, string color,
            CancellationToken token)
        {
            var json = "{\"name\":\"" + EscapeJson(name.Trim()) + "\",\"color\":\"" +
                color.Trim().ToUpperInvariant() + "\"}";
            var bytes = Encoding.UTF8.GetBytes(json);
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }

        private static string EscapeJson(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private async Task SendLoopAsync(ClientWebSocket socket, Func<TelemetrySnapshot> readTelemetry,
            Func<ObsStatus> readObs, Func<RuijieStatus> readRuijie, CancellationToken token)
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var telemetry = readTelemetry();
                var obs = readObs();
                var ruijie = readRuijie();
                byte flags = 0;
                if (obs != null && obs.Recording == true) flags |= 1;
                if (telemetry.Armed) flags |= 2;
                var heading = telemetry.Heading % 360;
                if (heading < 0) heading += 360;
                var packet = MessagePackTelemetryEncoder.Encode(sequence++, telemetry.BatteryVoltage,
                    telemetry.CurrentAmps, telemetry.Satellites, telemetry.Hdop, heading,
                    telemetry.Altitude, ruijie?.Rssi, flags);
                await socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary,
                    true, token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }
        }

        private static async Task ReceiveUntilClosedAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[256];
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("Aggregator closed the connection" +
                        (string.IsNullOrWhiteSpace(result.CloseStatusDescription)
                            ? "" : ": " + result.CloseStatusDescription));
            }
        }

        private static ClientWebSocket CreateSocket(string secret)
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + secret);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            return socket;
        }

        private static void Validate(string url, string secret, string stationName, string stationColor)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) ||
                (endpoint.Scheme != "ws" && endpoint.Scheme != "wss"))
                throw new ArgumentException("Aggregator URL must start with ws:// or wss://");
            if (string.IsNullOrWhiteSpace(secret))
                throw new ArgumentException("Aggregator secret is required");
            if (string.IsNullOrWhiteSpace(stationName) || stationName.Trim().Length > 100)
                throw new ArgumentException("Station name is required and must not exceed 100 characters");
            if (string.IsNullOrWhiteSpace(stationColor) ||
                !System.Text.RegularExpressions.Regex.IsMatch(stationColor.Trim(), "^#[0-9A-Fa-f]{6}$"))
                throw new ArgumentException("Station color must use the #RRGGBB format");
        }
    }
}
