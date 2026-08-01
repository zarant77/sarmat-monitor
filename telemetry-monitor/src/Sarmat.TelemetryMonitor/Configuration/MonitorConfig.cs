using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sarmat.TelemetryMonitor.Configuration;

public sealed class MonitorConfig
{
    public string AggregatorUrl { get; set; } = "ws://127.0.0.1:8080/ws/monitor";
    public string Secret { get; set; } = "";
    public int ReconnectSeconds { get; set; } = 5;
    public double WindowWidth { get; set; } = 1220;
    public double WindowHeight { get; set; } = 440;
    public bool WindowMaximized { get; set; }
    public List<string> HiddenColumns { get; set; } = new();
    [JsonIgnore] public string SourcePath { get; private set; } = "";

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
        config.WindowWidth = Math.Clamp(config.WindowWidth, 900, 10000);
        config.WindowHeight = Math.Clamp(config.WindowHeight, 320, 10000);
        config.HiddenColumns ??= new List<string>();
        config.HiddenColumns = config.HiddenColumns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!Uri.TryCreate(config.AggregatorUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
            throw new InvalidDataException("aggregatorUrl must start with ws:// or wss://.");
        if (string.IsNullOrWhiteSpace(config.Secret))
            throw new InvalidDataException("secret is required in config.json.");
        config.SourcePath = Path.GetFullPath(path);
        return config;
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
            throw new InvalidOperationException("Configuration source path is not set.");
        var directory = Path.GetDirectoryName(SourcePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(SourcePath, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) + Environment.NewLine);
    }
}
