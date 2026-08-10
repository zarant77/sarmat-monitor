# Altitude Assist testing

## Automated

Run `scripts\build.ps1 -MissionPlannerPath "C:\Program Files (x86)\Mission Planner"`. Gesture, controller, manual override, failsafe, and deterministic climb/descent model tests run without Mission Planner.

## Manual diagnostics (no vehicle output)

1. Install the MSI and start Mission Planner.
2. Open Flight Data → Altitude Assist and confirm the red `ALTITUDE CONTROL OUTPUT LOCKED — DIAGNOSTICS ONLY` banner.
3. Connect SITL in LOITER/ALT_HOLD, arm, and climb above 3 m using normal SITL controls.
4. Move CH3 through neutral→high→neutral three separate times, each high pulse 70–500 ms and all within 1.5 s. Confirm CLIMBING and a positive *calculated* output while physical output remains LOCKED/zero.
5. Make one deliberate CH3 movement and confirm immediate MANUAL_OVERRIDE and zero output. Return neutral; a new triple gesture is required.
6. Repeat with three down pulses. Confirm DESCENDING, progressive slowdown, HOLD at 50 m, and zero output. This is not landing.
7. During AUTO disconnect SITL, disarm, stop telemetry, or change to STABILIZE; confirm FAILSAFE/rejection and zero output.
8. Close Mission Planner and confirm `%APPDATA%\Sarmat\AltitudeAssist\settings.json` is saved.

Not covered by automated tests: real Mission Planner layout/HUD overlay appearance across versions, real RC calibration/autodetection beyond configured CH3, real SITL transport behavior, MSI upgrade/uninstall on a clean VM, and any real-aircraft behavior. Real aircraft testing is prohibited for this locked build.
