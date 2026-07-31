# Sarmat Plugin for Mission Planner

Sarmat Plugin adds a compact safety tab to Mission Planner Flight Data. Its configurable,
responsive widget grid displays flight telemetry and monitors OBS Studio recording together with
a Ruijie wireless bridge.
All warnings and audio are gated by the vehicle's `ARMED` state.

## Compatibility and API

- Mission Planner plugin API: `MissionPlanner.Plugin.Plugin`
- Flight telemetry: `PluginHost.cs` (`CurrentState.armed`, `battery_voltage`, `satcount`, `gpshdop`)
- Flight Data integration: a dedicated `Sarmat` `TabPage` inside the native
  `FlightData.tabControlactions` list; the plugin also registers it in `TabListOriginal`
- UI: Windows Forms
- Target framework: **.NET Framework 4.7.2 (`net472`)**
- Verified build target: Mission Planner **1.3.83**

The plugin does not open its own MAVLink connection and does not run or communicate with
`meow-monitor`.

## Prerequisites

- Windows 10/11
- Mission Planner installed
- .NET Framework 4.7.2 or later
- .NET 8 SDK or Visual Studio 2022 with the .NET Framework 4.7.2 targeting pack
- OBS Studio 28+ when OBS monitoring is enabled

Close Mission Planner before installing or replacing the DLL.

## Build

From PowerShell:

```powershell
.\scripts\build.ps1 -MissionPlannerPath "C:\Program Files (x86)\Mission Planner"
```

Or:

```bat
scripts\build.bat "C:\Program Files (x86)\Mission Planner"
```

The script restores packages, builds Release, runs the unit-test executable, and creates `dist`.
For another installed copy, pass its directory as `MissionPlannerPath`.

## Install

Run an elevated PowerShell when Mission Planner is installed under `Program Files`:

```powershell
.\scripts\install.ps1 -MissionPlannerPath "C:\Program Files (x86)\Mission Planner"
```

The installer creates the `plugins` folder if needed, makes a timestamped backup of an existing
`SarmatPlugin.dll`, copies the new DLL, and runs `Unblock-File`.

Restart Mission Planner and open **Flight Data**. The **Sarmat** tab is placed first in the native
lower tab list. All standard Mission Planner tabs remain available. Right-click anywhere inside the
Sarmat tab and select **Settings** to open `Sarmat Plugin Settings`.

## Uninstall

Close Mission Planner, then run:

```powershell
.\scripts\uninstall.ps1 -MissionPlannerPath "C:\Program Files (x86)\Mission Planner"
```

Settings and logs are intentionally preserved.

## OBS WebSocket setup

1. In OBS, open **Tools → WebSocket Server Settings**.
2. Enable the WebSocket server.
3. Keep the default port `4455`, or enter the matching endpoint in Sarmat settings.
4. Copy the OBS WebSocket password into the plugin.
5. Click **Test connection**. The status includes both connectivity and recording state.

Default endpoint: `ws://127.0.0.1:4455`.

The implementation uses OBS WebSocket v5 Hello/Identify authentication and `GetRecordStatus`.
The plugin sends `StartRecord` once on the `DISARMED → ARMED` transition and `StopRecord` once on
the `ARMED → DISARMED` transition. Between those edges it only reads `GetRecordStatus`, so manual
OBS Start/Stop remains available and the dashboard shows the actual recording state. A transition
that occurs while OBS is disconnected stays pending and is retried after reconnect.

## Ruijie setup

Enter the bridge HTTPS address and administrator password. The username defaults to `admin`.
Self-signed/legacy router certificates can be supported with **Allow insecure TLS**.

The adapter implements the actual Ruijie LuCI flow:

1. GET `/cgi-bin/luci/` and detect legacy or encrypted-key `GibberishAES` authentication.
2. Encrypt the password in the router's OpenSSL-compatible AES-256-CBC format.
3. POST legacy (`pwd`) or modern (`password`) login data to `/cgi-bin/luci/api/auth`.
4. Use the returned `webauth` cookie, SID, or token.
5. POST `devSta.get` / `wdsLinkQuality` to `/cgi-bin/luci/api/cmd`.
6. Re-authenticate once when a session expires.

RSSI is averaged across available uplink/downlink H/V values. Quality is scored from RSSI with the
same channel-utilization and noise penalties used by the reference implementation.

## Safety behavior

While disarmed, Safety is `Inactive`, audio is stopped, and OBS/Ruijie factual states remain visible.
After arming and the configured grace period:

- Ruijie disconnected/stale: Critical
- Battery below threshold: Critical
- OBS disconnected/not recording: Warning
- Satellites below threshold: Warning
- HDOP above threshold: Warning

Activation and recovery use independent debounce periods. Recovery hysteresis is `+0.5 V` for
battery, `+1` satellite, and `-0.05` HDOP. Critical audio supersedes Warning audio. Only one pattern
runs at a time, repeats use the configured interval, and a restored pattern plays after full recovery.

The dashboard colors Sat Count and GPS HDOP against their configured limits. Distance to home uses
the configurable **Safe dist to home** value (default `50 m`): up to half is green, between half and
the limit is yellow, and the limit or above is red. Estimated battery usage is always yellow.
Every dashboard item uses the same title/value/status widget. Status colors are green (good),
yellow (normal), and red (bad). The grid calculates its column count from the available width and
automatically wraps widgets into additional rows. Header and value fonts are measured against the
actual visible text and reduced automatically so every item fits without scrolling. Under
**Settings → Widgets**, each widget can be shown or hidden independently. Available widgets are Sat Count, GPS HDOP, Dist to Home, Bat used,
Ruijie, OBS, Ground Speed, Vertical Speed, Air Speed, Altitude, Battery Voltage, and Current.
OBS uses the compact values `REC`, `NR`, and `DIS`.
Drag entries in the Widgets list to change their order on the dashboard; the checked state and
saved order are preserved together.

In addition to the built-in widgets, the plugin discovers every public scalar telemetry property
and field exposed by the installed Mission Planner `CurrentState` at startup. Numeric, Boolean,
text, enum, and timestamp values become optional entries in **Settings → Widgets**. This includes
attitude, position, navigation, GPS2, sensor, RC, vibration, wind, EKF, mission, radio, and custom
`NAMED_VALUE_FLOAT` slots when exposed by that Mission Planner version. Dynamically discovered
items are off by default and only selected values are read during each telemetry update.

The Sarmat context menu includes **Start Sarmat RTSP video**. It stores the supplied RTSP pipeline
in Mission Planner's native `gstreamer_url` setting and starts it through
`FlightData.hudGStreamer`; it does not launch an external player. Successful startup is silent;
an error dialog is shown only when the source cannot be started. After startup, the plugin sets
the native Mission Planner HUD to 16:9 and invokes its normal resize routine.

## Settings and logs

- Settings: `%APPDATA%\SarmatPlugin\settings.json`
- Log: `%APPDATA%\SarmatPlugin\logs\sarmat-plugin.log`
- Rotated log: `sarmat-plugin.log.1`

Passwords, tokens, SID values, cookies, and authorization values are redacted from log messages.

## Troubleshooting

- **Sarmat tab is absent:** confirm `SarmatPlugin.dll` is directly inside Mission Planner's
  `plugins` directory, restart Mission Planner, and inspect the Sarmat log.
- **DLL is blocked:** run `Unblock-File` on the installed DLL or reinstall with `install.ps1`.
- **Build cannot find MissionPlanner.exe:** pass the directory containing `MissionPlanner.exe`, not
  the executable itself.
- **OBS disconnected:** enable OBS WebSocket v5, verify port/password, and ensure OBS is running.
- **Ruijie TLS error:** verify the address; enable insecure TLS only for a trusted local bridge.
- **Ruijie authentication error:** verify the administrator password and wait for any router account
  lockout to expire.
- **No audio:** confirm the vehicle is armed, Alerts and Audio are enabled, Muted is off, and test
  the patterns from Settings.

Mission Planner must be used for the final manual verification: plugin discovery, tab placement,
theme/layout behavior, live vehicle telemetry, OBS, Ruijie hardware, and audible volume.
