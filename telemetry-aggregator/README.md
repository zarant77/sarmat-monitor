# Telemetry Aggregator

A small Node.js WebSocket server that authenticates configured Sarmat stations and keeps only the
latest telemetry packet from each station in memory. It does not store history or write telemetry
to disk.

## Requirements

- Node.js 20 or newer

## Configuration

Copy `config.example.json` to the ignored `config.json`, then edit it before running the server.
Every station and monitor has its own shared secret.
The same station secret must be entered in that station's Sarmat Plugin settings.

Do not expose the example secrets on an untrusted network. Secrets are sent in the WebSocket HTTP
upgrade request, so use TLS (`wss://`) outside an isolated network.

## Run

```powershell
Copy-Item config.example.json config.json
corepack pnpm install
corepack pnpm start
```

The GitHub Release ZIP also includes `install.cmd` and `start.cmd` for Windows hosts.

Use another configuration file when needed:

```powershell
$env:SARMAT_CONFIG = "C:\path\to\config.json"
npm start
```

Endpoints:

- `GET /health` returns the process health.
- `/ws/station` accepts station connections.
- `/ws/monitor` accepts read-only monitor connections.

Both WebSocket endpoints require an HTTP header:

```text
Authorization: Bearer <configured-secret>
```

## Station protocol

Stations send binary WebSocket frames encoded with MessagePack. A telemetry frame is a fixed
nine-element array:

```text
[sequence, voltageV, currentA, satellites, hdop, headingDeg,
 relativeAltitudeM, ruijieQualityPercent, flags]
```

`nil` is accepted for unavailable measurements. `flags` uses bit 0 for OBS recording and bit 1
for armed state.

## Monitor protocol

On connection, a monitor receives the protocol version and station presentation data:

```text
[1, [[name, color], ...]]
```

It then receives snapshots in the same order as stations in `config.json`. Each station entry is
either `nil` or:

```text
[status, ageMs, sequence, voltageV, currentA, satellites, hdop, headingDeg,
 relativeAltitudeM, ruijieQualityPercent, flags]
```

Statuses are `0` online, `1` stale, and `2` offline.
