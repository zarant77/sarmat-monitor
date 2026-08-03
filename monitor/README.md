# Sarmat Monitor

Node.js server that authenticates Sarmat stations, keeps their latest telemetry packet in memory,
and displays all connected stations in a web interface. No telemetry history is written to disk.

## Requirements

- Node.js 20 or newer

## Configuration and startup

Copy `config.example.json` to the ignored `config.json`, replace every example secret, and start the
server:

```powershell
Copy-Item config.example.json config.json
corepack pnpm install
corepack pnpm start
```

The `admins` array contains the secrets accepted by the web interface. Each item in `stations`
defines a permitted telemetry client and its `title`, `color`, and unique `secret`. Station titles
and colors shown by the web interface always come from this server configuration.

The web interface is available at `http://localhost:8080/`. It refreshes connected-station data
every second. A different configuration path can be selected with `SARMAT_CONFIG`; hosting services
can provide the complete JSON through `SARMAT_CONFIG_JSON`. `PORT` overrides the configured port.

Telemetry thresholds are stored in the repository at `shared/telemetry-thresholds.json` and are
automatically included in the release package. Ruijie link quality is transmitted and displayed as
RSSI in dBm.

## Routes

- `GET /` — monitoring web interface.
- `POST /api/login` — validates an admin secret.
- `GET /api/stations` — current connected-station snapshot; requires an admin secret.
- `GET /health` — process health.
- `/ws/station` — station connection; requires a configured station secret.

Protected routes require `Authorization: Bearer <secret>`. Use TLS (`wss://` and `https://`) outside
an isolated network. Stations send binary MessagePack telemetry immediately after connecting:

```text
[sequence, voltageV, currentA, satellites, hdop, headingDeg,
 relativeAltitudeM, linkRssiDbm, flags]
```

`flags`: bit 0 is OBS recording; bit 1 is ARMED.
