using System.IO;
using System.Text.Json;

namespace Sarmat.TelemetryMonitor.Configuration;

public sealed class MonitorConfig
{
    public string AggregatorUrl { get; set; } = "ws://127.0.0.1:8080/ws/monitor";
    public string Secret { get; set; } = "";
    public int ReconnectSeconds { get; set; } = 5;

    public static MonitorConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Monitor configuration was not found: {path}{Environment.NewLine}" +
                "Copy config.example.json to config.json and set the aggregator URL and secret.");

        var config = JsonSerializer.Deserialize<MonitorConfig>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
            throw new InvalidDataException("Monitor configuration is empty.");
        config.AggregatorUrl = (config.AggregatorUrl ?? "").Trim();
        config.Secret = (config.Secret ?? "").Trim();
        config.ReconnectSeconds = Math.Clamp(config.ReconnectSeconds, 1, 300);
        if (!Uri.TryCreate(config.AggregatorUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
            throw new InvalidDataException("aggregatorUrl must start with ws:// or wss://.");
        if (string.IsNullOrWhiteSpace(config.Secret))
            throw new InvalidDataException("secret is required in config.json.");
        return config;
    }
}
