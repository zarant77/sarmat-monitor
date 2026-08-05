# SarmatVisionHold.ReplayAnalyzer

Offline, diagnostics-only replay of a camera recording and a matching Mission Planner `.tlog`.

The analyzer reads both files, aligns their clocks, interpolates attitude/gyro/altitude for every decoded video frame, reuses `SparseOpticalFlowProcessor`, removes point-wise rotational image motion, converts residual flow to radians and writes an internal model equivalent to MAVLink `OPTICAL_FLOW_RAD`.

It never opens serial/UDP links and never sends MAVLink. `ReplaySafety.DiagnosticsOnly` rejects every publisher except null, CSV and in-memory mock implementations.

## Run

```powershell
dotnet run --project .\tools\SarmatVisionHold.ReplayAnalyzer -- `
  --video "C:\Users\PegasArm\Videos\flight-01.mkv" `
  --tlog "C:\Users\PegasArm\Documents\Mission Planner\logs\flight-01.tlog" `
  --output ".\artifacts\replay-flight-01" `
  --horizontal-fov 90 `
  --camera-mount-pitch -90 `
  --preview `
  --auto-sync `
  --save-annotated-video
```

Camera mount CLI angles are degrees. `--video-offset-ms` maps video time to tlog receive time as `tlog = video + offset`. If auto-sync confidence is low, the report marks the replay degraded and uses the manual offset as a fallback.

Preview keys: Space pause, Left/Right inspect buffered frames, R reset tracker, S snapshot, 1 raw vectors, 2 rotational prediction, 3 compensated vectors, 4 all, Q/Esc exit.

See [replay analysis](../../docs/replay-analysis.md), [coordinate systems](../../docs/coordinate-systems.md), and [OPTICAL_FLOW_RAD mapping](../../docs/optical-flow-rad-mapping.md).
