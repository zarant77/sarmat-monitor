# OPTICAL_FLOW_RAD diagnostics mapping

ReplayAnalyzer constructs an offline model with the same fields and units as MAVLink `OPTICAL_FLOW_RAD`. It does not serialize or transmit that model.

## Camera intrinsics

For image width `W`, height `H`, horizontal FOV `HFOV` and vertical FOV `VFOV`:

```text
fx = W / (2 tan(HFOV/2))
fy = H / (2 tan(VFOV/2))
cx = (W-1)/2
cy = (H-1)/2
```

If only one FOV is supplied, square pixels are assumed (`fx=fy`) and the other FOV is derived. FOV must be finite and between 0° and 180°. Configured resolution must match the decoded video. The code is ready to accept calibrated `fx/fy/cx/cy` and distortion maps later, but it does not claim lens calibration now.

## Fields

| Field | Unit | Replay meaning |
|---|---:|---|
| `time_usec` | µs | Monotonic source video time. Repeated/backward values are advanced by 1 µs. |
| `sensor_id` | — | Configured diagnostics sensor ID; currently 0. |
| `integration_time_us` | µs | Actual interval between the two video frames. Must be positive. |
| `integrated_x` | rad | Angular flow around sensor X, `atan2(compensated_dv, fy)`. |
| `integrated_y` | rad | Angular flow around sensor Y, `-atan2(compensated_du, fx)`. |
| `integrated_xgyro` | rad | Integrated right-hand camera-frame gyro X. |
| `integrated_ygyro` | rad | Integrated right-hand camera-frame gyro Y. |
| `integrated_zgyro` | rad | Integrated right-hand camera-frame gyro Z. |
| `temperature` | cdegC | `-32768`, meaning unknown. |
| `quality` | 0…255 | Composite diagnostics confidence. |
| `time_delta_distance_us` | µs | Age of the selected range sample, only when valid and fresh. |
| `distance` | m | Selected AGL/range value, or `-1` when unknown/stale. |

The flow values are integrated angular displacement, not pixels/frame and not radians/second. Divide by `integration_time_us × 1e-6` only when an angular rate is needed.

## Quality and rejection

Quality combines tracked-point count, inlier ratio, forward/backward error, compensation residual, frame/telemetry age, clock-alignment confidence, texture score and altitude validity. Missing/stale IMU, decoder errors and large frame gaps force quality to zero. Invalid altitude cannot retain full quality.

No publishable model is built for `LOST`. `DEGRADED` can either be rejected or emitted with quality capped at 63. Distance is never invented: invalid/stale range becomes `distance=-1` and `time_delta_distance_us=0`.

`DiagnosticsOnly=true` accepts only `NullOpticalFlowRadPublisher`, `CsvOpticalFlowRadPublisher`, and `MockOpticalFlowRadPublisher`. A different implementation throws before replay begins.
