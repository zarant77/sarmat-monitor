# Sarmat Altitude Assist

Independent Mission Planner plugin (`SarmatAltitudeAssist.dll`) for diagnostic evaluation of a vertical-flight assistant. Three completed neutral→up→neutral pulses start calculation toward the working altitude; three down pulses select the descent altitude. Any deliberate up/down input during AUTO cancels immediately.

The first release is compile/runtime locked: `NullVerticalControlOutput` records only zero and cannot send RC, joystick, MAVLink, mode, or motor commands. Calculated commands are visible in diagnostics. Relative altitude is `CurrentState.alt`; the physical vertical input is `CurrentState.ch3in` by default and is normalized with configurable MIN/TRIM/MAX/reversal. Settings are stored at `%APPDATA%\Sarmat\AltitudeAssist\settings.json`; timestamped state/telemetry transitions are written under the sibling `logs` directory.

AUTO requires a connection, fresh heartbeat/telemetry, armed and airborne vehicle, valid relative altitude, valid settings, and an altitude-controlling Copter mode: ALT_HOLD, LOITER, POSHOLD, GUIDED, BRAKE, or SPORT. The plugin never changes mode and rejects other modes. States are IDLE, CLIMBING/DESCENDING, TARGET_REACHED, HOLD, MANUAL_OVERRIDE/CANCELLED, and FAILSAFE. At target it releases output; it does not land and does not replace ArduPilot's altitude controller.

Failsafes include disconnect, heartbeat/telemetry loss or staleness, disarm, invalid/jumping altitude, unsupported mode during AUTO, timeout, control-loop exception, manual input, and shutdown. Output release is idempotent.
