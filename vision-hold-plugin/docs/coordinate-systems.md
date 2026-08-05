# Coordinate systems used by replay analysis

All sign conversions live in `SarmatVisionHold.Replay`; CLI and output code must not add local sign flips.

## Frames

MAVLink body frame is right-handed FRD:

- +X: aircraft forward;
- +Y: aircraft right;
- +Z: aircraft down;
- positive roll, pitch and yaw follow the right-hand rule about +X, +Y and +Z.

MAVLink local navigation data uses NED:

- +X: north;
- +Y: east;
- +Z: down.

The OpenCV pinhole camera frame is right-handed:

- +X: image right;
- +Y: image down;
- +Z: forward through the lens;
- pixel `u` grows right and pixel `v` grows down.

## Attitude and camera mount

`BodyToNed` rotates a body-frame vector into NED. MAVLink quaternion fields use `q1=w`, `q2=x`, `q3=y`, `q4=z`. Euler fallback is constructed as yaw(Z) × pitch(Y) × roll(X); interpolation always uses quaternion SLERP, so yaw crossing ±π does not pass through zero.

The CLI camera mount angles are degrees and define:

```text
CameraFromBody = Yaw(Z) × Pitch(Y) × Roll(X)
cameraRate = CameraFromBody × bodyRate
CameraToWorld = BodyToNed × inverse(CameraFromBody)
```

The default mount pitch is -90°, appropriate for a nadir camera under this convention. A real installation must measure and configure all three mount angles.

## Image rotation prediction

For a tracked point in the previous image:

```text
ray_old = normalize([(u-cx)/fx, (v-cy)/fy, 1])
newCameraFromOldCamera = inverse(CurrentCameraToWorld) × PreviousCameraToWorld
ray_new = newCameraFromOldCamera × ray_old
predicted_pixel = project(ray_new)
```

Gyro mode integrates camera-frame body rates and uses the inverse camera rotation for the static scene:

```text
integratedCameraGyro = integral(cameraRate dt)
newCameraFromOldCamera = quaternion(-integratedCameraGyro)
```

Raw displacement minus the point-specific predicted rotational displacement is passed to a robust median translation estimate. This is deliberately not a single global X/Y subtraction: roll, pitch and yaw create spatially varying flow.

## Mapping image flow to MAVLink axes

Let `du` be compensated pixel displacement to the right and `dv` displacement down. MAVLink `OPTICAL_FLOW_RAD` describes angular flow around sensor X/Y axes:

```text
integrated_x =  atan2(dv, fy)
integrated_y = -atan2(du, fx)
```

Therefore:

- texture moving down produces positive `integrated_x`;
- texture moving right produces negative `integrated_y`;
- `integrated_xgyro/ygyro/zgyro` are right-hand camera-frame angular increments and are not pixel-axis values.

These mappings are covered by unit tests for positive and negative X/Y and all gyro/mount axes.
