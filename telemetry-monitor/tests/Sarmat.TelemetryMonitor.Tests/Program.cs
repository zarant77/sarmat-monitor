using Sarmat.TelemetryMonitor.Protocol;
using Sarmat.TelemetryMonitor.Configuration;
using Sarmat.TelemetryMonitor.Services;

var failures = 0;
Run("decodes aggregator station configuration", Configuration);
Run("decodes aggregator telemetry snapshot", Snapshot);
if (Environment.GetEnvironmentVariable("SARMAT_AGGREGATOR_INTEGRATION") == "1")
    Run("connects to local telemetry aggregator", LocalIntegration);
Console.WriteLine(failures == 0 ? "All tests passed." : $"{failures} test(s) failed.");
return failures;

void Run(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}

void Configuration()
{
    var bytes = new byte[]
    {
        0x92, 0x01, 0x91, 0x92, 0xa3, (byte)'R', (byte)'e', (byte)'d',
        0xa7, (byte)'#', (byte)'F', (byte)'F', (byte)'0', (byte)'0', (byte)'0', (byte)'0'
    };
    var stations = MonitorProtocol.ReadConfiguration(MonitorProtocol.Decode(bytes));
    Equal(1, stations.Count);
    Equal("Red", stations[0].Name);
    Equal("#FF0000", stations[0].Color);
}

void Snapshot()
{
    var bytes = new byte[]
    {
        0x91, 0x9b, 0x00, 0x02, 0x07, 0x16, 0x0e, 0x12, 0x01,
        0xcd, 0x01, 0x12, 0x7b, 0x56, 0x03
    };
    var snapshots = MonitorProtocol.ReadSnapshot(MonitorProtocol.Decode(bytes), 1);
    var station = snapshots[0] ?? throw new Exception("Station snapshot is missing.");
    Equal(0, station.Status);
    Equal(7u, station.Sequence);
    Equal(22d, station.Voltage);
    Equal(274d, station.Heading);
    Equal(86, station.RuijieQuality);
    Equal((byte)3, station.Flags);
}

void LocalIntegration()
{
    using var client = new TelemetryClient(new MonitorConfig
    {
        AggregatorUrl = "ws://127.0.0.1:8080/ws/monitor",
        Secret = "sarmat-main-monitor-2026",
        ReconnectSeconds = 1
    });
    var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    string? error = null;
    client.ConnectionChanged += (_, value) => error = value;
    client.ConfigurationReceived += stations => result.TrySetResult(stations.Count);
    client.Start();
    if (!result.Task.Wait(TimeSpan.FromSeconds(5)))
        throw new Exception("No station configuration received. " + (error ?? "No connection error reported."));
    Equal(3, result.Task.Result);
}

void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected '{expected}', got '{actual}'.");
}
