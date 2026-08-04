# SarmatVisionHold.KestrelClient

Read-only Kestrel Drift Vision Protocol v1 consumer and optical-flow validator. It never sends flight commands; the only outbound message is the required consumer `hello`.

## Coordinate contract

- Kestrel world: X/Z horizontal, Y up. Body velocity is supplied by Kestrel after yaw rotation.
- Camera mount Euler angles are applied to body velocity. The default camera pitch is `-pi/2` (nadir).
- Image X grows right and image Y grows down. Positive texture X/Y therefore means right/down.
- Texture translation is opposite camera translation. Positive OpenCV image rotation is CCW; positive Kestrel yaw maps to clockwise texture rotation.
- Ground truth intentionally validates categories and signs, not exact pixels/frame. Camera altitude/FOV projection is a future extension point.

## Run

PowerShell:

```powershell
dotnet run --project .\tools\SarmatVisionHold.KestrelClient -- `
  --url "ws://127.0.0.1:8765" `
  --output ".\artifacts\kestrel-live" `
  --preview --save-annotated-video
```

cmd.exe:

```bat
dotnet run --project .\tools\SarmatVisionHold.KestrelClient -- ^
  --url "ws://127.0.0.1:8765" ^
  --output ".\artifacts\kestrel-live" ^
  --preview --save-annotated-video
```

Use `--headless` on machines without a desktop. Other options: `--config`, `--reconnect`, `--no-reconnect`, `--sync-timeout-ms`, `--max-pending`, `--save-raw-frames`, `--max-duration`, and `--log-level`.

## Manual validation

1. In terminal 1, from Kestrel Drift: `npm install`, then `npm run vision:bridge`.
2. In terminal 2: `npm run vision:dev`.
3. In the browser enable **Vision Export** and lower-camera preview; lift, translate, yaw, then hover.
4. In terminal 3 run the command above. Confirm translation signs change, yaw becomes rotation, hover becomes STILL, and validation is mostly PASS.
5. Stop/restart the bridge and confirm the window stays alive, reports DISCONNECTED, reconnects, and creates a new session directory.

Each producer session creates `<output>/<session-id>/session.csv`, `report.md`, and `snapshots/`. Preview keys: Q/Esc quit, Space pause, S snapshot, V vectors, T telemetry.
