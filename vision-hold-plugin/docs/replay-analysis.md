# Offline video + Mission Planner TLOG replay

This stage validates camera/IMU mathematics and timing on recorded flights before any live Mission Planner or autopilot integration.

It does not control the drone, change EKF sources, enable FlowHold, open serial/UDP, send MAVLink, change flight mode, or create RC overrides.

## Record suitable inputs

Record the cleanest camera stream available, preferably without OSD, reticle, stabilization overlays or resampling. Preserve the original MKV/MP4 container so variable-frame-rate timestamps are retained. Record the Mission Planner telemetry log during the same interval and copy the matching `.tlog` from the Mission Planner logs directory. Match files by UTC flight time and verify that the tlog contains `ATTITUDE`/`ATTITUDE_QUATERNION` plus an IMU source.

Required camera information:

- exact decoded width and height;
- horizontal or vertical FOV;
- camera mount roll/pitch/yaw relative to MAVLink body FRD;
- preferably a measured range/AGL source.

Barometric altitude is only a replay-start delta. It drifts, follows pressure, is not terrain range and must not be treated as accurate scale near uneven ground.

## Run

```powershell
cd C:\Users\PegasArm\Documents\sarmat-plugin\vision-hold-plugin

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

Use `--video-offset-ms N` when an independently measured offset is available. The convention is `tlog_time = video_time + offset`. `--start` and `--duration` are seconds on the video timeline. For unattended processing use `--headless`.

Auto-sync extracts global image rotation, cross-correlates it with prioritized IMU/yaw rate, reports alternative peaks and refuses to trust a low-confidence maximum. Low-confidence synchronization remains visible as DEGRADED/LOST and reduced quality.

## Telemetry priorities

Attitude: valid `ATTITUDE_QUATERNION`, then `ATTITUDE`. Quaternion data that is stale at a requested frame falls back to fresh Euler data, which is converted to a quaternion and SLERPed.

Angular rate: `HIGHRES_IMU`, `SCALED_IMU`, `SCALED_IMU2`, `SCALED_IMU3`, `ATTITUDE` rates, then scaled `RAW_IMU`.

Altitude: downward `DISTANCE_SENSOR`, `RANGEFINDER`, terrain/bottom clearance, relative altitude, then barometric delta from replay start. The report records the sources actually present and selected.

## Preview

Yellow vectors are raw tracks, magenta vectors are predicted rotation, and green vectors are compensated translation. The overlay includes both clocks, sync error, attitude, gyro, integrated gyro, altitude source, radians, `OPTICAL_FLOW_RAD` fields, quality, state and reason.

Keys: Space pause, Left/Right inspect buffered frames, R reset tracker, S snapshot, 1 raw, 2 rotation, 3 compensated, 4 all, Q/Esc exit.

## Outputs

```text
artifacts/replay-flight-01/
├── replay.csv
├── optical-flow-rad.csv
├── report.md
├── annotated.mkv              # when requested
├── synchronization.json
├── config-resolved.json
└── snapshots/
```

`replay.csv` is frame-oriented and contains raw, predicted and compensated flow with telemetry timing. `optical-flow-rad.csv` contains every diagnostics model plus rejected rows and reasons. `report.md` summarizes decoder/timing health, actual telemetry sources, synchronization confidence, compensation residual, state fractions, hover/yaw/translation candidates, validation warnings and largest anomalies.

## Interpreting readiness

A good replay has trusted synchronization, fresh IMU, low point-wise compensation residual during rotation, near-zero compensated translation during yaw/roll/pitch-only intervals, stable signs during translation, and no publishable rows during LOST.

Passing replay is necessary but not sufficient for flight. Real lens distortion, rolling shutter, vibration, exposure/blur, transport latency, camera/IMU hardware clock alignment, range validity, ArduPilot parameterization and EKF fusion still require separate SITL/HIL and controlled flight validation.
