# Sarmat Telemetry Monitor

Windows desktop client that connects to `telemetry-aggregator` and displays the current state of
all configured stations in a table.

The application icon uses a blue-and-yellow drone and telemetry mark. Source PNG and Windows ICO
assets are stored in `src/Sarmat.TelemetryMonitor/assets`.

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

The simplest local launch command is:

```powershell
.\run.cmd
```

The script creates `config.json` from the example when it is missing. You can also run the project
directly:

```powershell
dotnet run --project .\src\Sarmat.TelemetryMonitor\Sarmat.TelemetryMonitor.csproj
```

Use the **Settings** button in the application header to change the aggregator URL, secret, and
reconnect interval. Saving reconnects immediately and writes the active `config.json`.
The window size and maximized state are restored between launches. Use the **Columns** button to
show or hide individual table columns; that selection is saved in the same configuration file.

Run protocol tests with:

```powershell
dotnet run --project .\tests\Sarmat.TelemetryMonitor.Tests\Sarmat.TelemetryMonitor.Tests.csproj
```

The client reconnects automatically, keeps no history, and shows the latest snapshot received
from the aggregator.
