using System.IO;
using System.Net.WebSockets;
using Sarmat.TelemetryMonitor.Configuration;
using Sarmat.TelemetryMonitor.Models;
using Sarmat.TelemetryMonitor.Protocol;

namespace Sarmat.TelemetryMonitor.Services;

internal sealed class TelemetryClient : IDisposable
{
    private readonly MonitorConfig config;
    private readonly CancellationTokenSource cancellation = new();
    private Task? worker;

    public event Action<string, string?>? ConnectionChanged;
    public event Action<IReadOnlyList<StationDescriptor>>? ConfigurationReceived;
    public event Action<IReadOnlyList<StationSnapshot?>>? SnapshotReceived;

    public TelemetryClient(MonitorConfig config) => this.config = config;

    public void Start() => worker ??= Task.Run(() => RunAsync(cancellation.Token));

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                ConnectionChanged?.Invoke("Connecting", null);
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + config.Secret);
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                await socket.ConnectAsync(new Uri(config.AggregatorUrl), token);
                ConnectionChanged?.Invoke("Connected", null);

                var configuration = MonitorProtocol.ReadConfiguration(await ReceiveAsync(socket, token));
                ConfigurationReceived?.Invoke(configuration);
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var snapshot = MonitorProtocol.ReadSnapshot(await ReceiveAsync(socket, token),
                        configuration.Count);
                    SnapshotReceived?.Invoke(snapshot);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                ConnectionChanged?.Invoke("Disconnected", ex.Message);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(config.ReconnectSeconds), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }

    private static async Task<object?> ReceiveAsync(ClientWebSocket socket, CancellationToken token)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("Aggregator closed the connection" +
                    (string.IsNullOrWhiteSpace(result.CloseStatusDescription)
                        ? "." : ": " + result.CloseStatusDescription));
            if (result.MessageType != WebSocketMessageType.Binary)
                throw new InvalidDataException("Aggregator sent a non-binary frame.");
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > 64 * 1024) throw new InvalidDataException("Aggregator frame is too large.");
        } while (!result.EndOfMessage);
        return MonitorProtocol.Decode(stream.ToArray());
    }

    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
