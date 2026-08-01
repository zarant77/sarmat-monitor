# Sarmat Telemetry Monitor

Windows desktop client that connects to `telemetry-aggregator` and displays the current state of
all configured stations in a table.

## Configuration

Copy `config.example.json` to the ignored `config.json` and set values matching the aggregator:

```json
{
  "aggregatorUrl": "ws://127.0.0.1:8080/ws/monitor",
  "secret": "sarmat-main-monitor-2026",
  "reconnectSeconds": 5
}
```

The `secret` must match one entry in the aggregator's `clients` configuration. An alternative
configuration path can be supplied through the `SARMAT_MONITOR_CONFIG` environment variable.
An installed copy creates its editable configuration at
`%APPDATA%\Sarmat\TelemetryMonitor\config.json` on first launch.

## Run

```powershell
dotnet run --project .\src\Sarmat.TelemetryMonitor\Sarmat.TelemetryMonitor.csproj
```

Run protocol tests with:

```powershell
dotnet run --project .\tests\Sarmat.TelemetryMonitor.Tests\Sarmat.TelemetryMonitor.Tests.csproj
```

The client reconnects automatically, keeps no history, and shows the latest snapshot received
from the aggregator.
