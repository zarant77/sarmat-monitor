# Sarmat Plugin for Mission Planner

Sarmat Plugin adds a compact safety tab to Mission Planner Flight Data. Its configurable,
responsive widget grid displays flight telemetry and monitors OBS Studio recording together with
a Ruijie wireless bridge.
All warnings and audio are gated by the vehicle's `ARMED` state.

At the `DISARMED → ARMED` transition, Sarmat checks the Mission Planner flight mode. If the
vehicle was armed outside PostHold/PosHold, a translucent red warning blinks at the bottom of the
main HUD for five seconds. Reaching PostHold dismisses it early for the remainder of that armed
flight; later mode changes do not reactivate it. Disarming resets the check for the next takeoff.

## Compatibility and API

- Mission Planner plugin API: `MissionPlanner.Plugin.Plugin`
- Flight telemetry: `PluginHost.cs` (`CurrentState.armed`, `battery_voltage`, `satcount`, `gpshdop`)
- Flight Data integration: a dedicated `Sarmat` `TabPage` inside the native
  `FlightData.tabControlactions` list; the plugin also registers it in `TabListOriginal`
- UI: Windows Forms
- Target framework: **.NET Framework 4.7.2 (`net472`)**
- Verified build target: Mission Planner **1.3.83**

## Sarmat Monitor

The optional **Monitor** settings tab streams the current Mission Planner telemetry to
`monitor` once per second. Configure:

- **Enabled** — starts the background connection;
- **WebSocket URL** — normally `ws://<server>:8080/ws/station`;
- **Station secret** — the secret of this station from the monitor `config.json`;
- **Reconnect interval** — retry delay after a failed or closed connection.

The tab shows the current connection state and includes a connection test that uses the values
currently entered in the form. The monitor obtains the station title and color from its own
configuration. The secret is sent only in the WebSocket `Authorization: Bearer` header. Telemetry
uses compact binary MessagePack frames once per second and contains voltage,
current, satellite count, HDOP, heading, relative altitude, Ruijie RSSI, OBS recording state,
and armed state. Network failures do not block Mission Planner, OBS, Ruijie polling, or the plugin
UI.

The plugin does not open its own MAVLink connection and does not run or communicate with
`meow-monitor`.

## Prerequisites

- Windows 10/11
- Mission Planner installed
- .NET Framework 4.7.2 or later
- .NET 8 SDK or Visual Studio 2022 with the .NET Framework 4.7.2 targeting pack
- OBS Studio 28+ when OBS monitoring is enabled

Close Mission Planner before installing or replacing the DLL.

For a downloaded GitHub Release, run `SarmatPlugins-<version>.msi`. Interactive installation always
shows the Mission Planner directory screen so you can choose the exact installed or portable copy. For a local portable
build, the PowerShell installer remains available in `dist`:

```powershell
.\install.ps1
```

The release folder mirrors the Mission Planner installation layout: `plugins/SarmatTelemetry.dll`
is installed into `plugins`, while `icon.png`, `logo.txt`, `logo2.png`, and `splashbg.png` are
installed into the Mission Planner root. Existing files are replaced. The installer also removes
Windows Mark-of-the-Web from all downloaded and installed files.

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

## GitHub Release build

The workflow at `../.github/workflows/release.yml` builds on `windows-latest`, runs the same Release
build and tests, and builds the MSI installer. CI downloads and
caches the official Mission Planner **1.3.83** ZIP so the plugin is compiled against the verified,
reproducible API version.

To create a GitHub Release, create and push a version tag:

```powershell
git switch main
git pull
git tag -a v1.0.0 -m "Sarmat Plugin v1.0.0"
git push origin v1.0.0
```

Pushing a tag matching `v*` starts **Release build** in GitHub Actions. When the build and tests
pass, the workflow creates a GitHub Release named after the tag and attaches
`SarmatPlugins-v1.0.0.msi` together with the Sarmat Monitor ZIP. Release tags must use the
`vMAJOR.MINOR.PATCH` format. Use a new tag for each release, for example `v1.0.1` or `v1.1.0`.

## MSI installer

The repository-level WiX project at `../installer/SarmatPlugins.Installer.wixproj` creates the shared per-machine MSI. Interactive
setup always shows the Mission Planner directory screen, initially set to
`C:\Program Files (x86)\Mission Planner`; select the exact Mission Planner copy you want to modify.
Use **Browse** from any selected Sarmat feature to choose the shared Mission Planner root folder
that already contains `MissionPlanner.exe`.
The **Sarmat Telemetry**, **Sarmat Vision Hold**, and **Sarmat Altitude Assist** features can each
be installed or removed independently through Windows Apps → Sarmat Plugins → Modify. Uninstalling
the complete product removes every selected plugin DLL.

The optional **SarmatTheme** feature is disabled by default. When selected, it installs every
branding asset from the repository-level `theme` directory (`icon.png`, `logo.txt`, `logo2.png`,
and `splashbg.png`) into
the selected Mission Planner directory. Theme files are deliberately preserved when the MSI is
uninstalled so Mission Planner is not left with missing branding files.

After creating `dist`, build an installer locally with:

```powershell
dotnet build ..\installer\SarmatPlugins.Installer.wixproj -c Release -p:ProductVersion=1.5.3
```

The MSI is written to the repository-level `artifacts` directory.

For a build without creating a GitHub Release, open **GitHub → Actions → Release build → Run
workflow**. The resulting release packages are available in the run's **Artifacts** section.

## Install

Run the installer and select the exact folder containing `MissionPlanner.exe`. A supplied
`-MissionPlannerPath` is used only as the initial folder in the selector:

```powershell
.\scripts\install.ps1 -MissionPlannerPath "C:\Program Files (x86)\Mission Planner"
```

The installer creates the `plugins` folder if needed, makes a timestamped backup of an existing
`SarmatTelemetry.dll`, copies the new DLL, and runs `Unblock-File`.

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

Enter the bridge IP address and administrator password. The username defaults to `admin`.
The plugin automatically selects HTTP or HTTPS. Router TLS certificates are accepted because the bridge is
accessed only on the trusted local network.

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
Alerts are always enabled while armed. After the fixed three-second arming grace period:

- Ruijie disconnected/stale or RSSI bad: Critical
- Battery below threshold: Critical
- Current above threshold: Critical
- OBS disconnected/not recording: Warning
- Satellites below threshold: Warning
- HDOP above threshold: Warning

Activation and recovery use fixed two-second debounce periods. All red boundaries come from the
repository-level `shared/telemetry-thresholds.json`, which is embedded into the plugin build.
A sound is played once whenever an alert kind transitions into its bad state. Recovery is silent.
After playback, new alert sounds are suppressed for the configured cooldown (default `10 s`, range
`1–300 s`). Disarming resets alert transitions and cancels pending playback.
The Audio tab also accepts a custom PCM WAV warning file. An empty path uses the embedded sound;
missing, unreadable, or invalid custom files safely fall back to the embedded `warning.wav`.

The dashboard colors voltage, current, satellites, HDOP, RSSI, and distance to home using the same
shared thresholds. Estimated battery usage is always yellow.
Every dashboard item uses the same title/value/status widget. Status colors are green (good),
yellow (normal), and red (bad). The grid calculates its column count from the available width and
automatically wraps widgets into additional rows. Header and value fonts are measured against the
actual visible text and reduced automatically so every item fits without scrolling. Under
**Settings → Widgets**, each widget can be shown or hidden independently. Available widgets are Sat Count, GPS HDOP, Dist to Home, Bat used,
Ruijie, OBS, Ground Speed, Vertical Speed, Air Speed, Altitude, Battery, and Current. The Battery
widget combines voltage and present current, for example `44,2V 10A`.
OBS uses the compact values `REC`, `NR`, and `DIS`.
Drag entries in the Widgets list to change their order on the dashboard; the checked state and
saved order are preserved together.

The **Mission Planner UI** settings tab controls native HUD elements without removing or replacing
Mission Planner controls. It can toggle battery indicators, cell voltage, altitude, speed,
heading, roll/pitch, cross-track error, GPS, EKF, vibration, pre-arm and connection status,
AOA/SSA, and icons versus text. The current HUD state is used on first setup; saved choices are
then restored after Mission Planner startup and after each vehicle connection, using the native
HUD resize routine.

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
Once the Sarmat GStreamer source has started successfully, that fact is persisted. On the next
Mission Planner run, the source is restored on the first vehicle connection; later
disconnected-to-connected transitions also restart it once.

On each new vehicle connection, Sarmat also checks Mission Planner's configured USB joystick. If
`MainV2.joystick` is already enabled and valid, it is left untouched. Otherwise, the plugin uses
the saved `joystick_name`, confirms the device is currently present, and restores it through the
native `JoystickBase.Create/start` path. Missing devices and failed best-effort reconnects never
block the vehicle connection or show a modal dialog.

The **General** settings tab includes automatic vehicle reconnect and a MAVLink silence timeout
(default `10 s`). This watchdog uses the actual Mission Planner MAVLink packet counter rather than
the UDP socket's open state: UDPCl may remain formally connected after the aircraft link is lost.
If no packets arrive for the configured interval, Sarmat calls Mission Planner's native disconnect
and connect methods using the currently selected port and baud. Further attempts are rate-limited
to one per timeout interval, and a deliberate Mission Planner disconnect disables the watchdog.

## Settings and logs

- Settings: `%APPDATA%\SarmatPlugin\settings.json`
- Log: `%APPDATA%\SarmatPlugin\logs\telemetry-plugin.log`
- Rotated log: `telemetry-plugin.log.1`

Passwords, tokens, SID values, cookies, and authorization values are redacted from log messages.

## Troubleshooting

- **Sarmat tab is absent:** confirm `SarmatTelemetry.dll` is directly inside Mission Planner's
  `plugins` directory, restart Mission Planner, and inspect the Sarmat log.
- **DLL is blocked:** run `Unblock-File` on the installed DLL or reinstall with `install.ps1`.
- **Build cannot find MissionPlanner.exe:** pass the directory containing `MissionPlanner.exe`, not
  the executable itself.
- **OBS disconnected:** enable OBS WebSocket v5, verify port/password, and ensure OBS is running.
- **Ruijie TLS error:** verify the address; enable insecure TLS only for a trusted local bridge.
- **Ruijie authentication error:** verify the administrator password and wait for any router account
  lockout to expire.
- **No audio:** confirm the vehicle is armed, Audio is enabled, set a non-zero volume,
  and use **Test sound** in Settings.

Mission Planner must be used for the final manual verification: plugin discovery, tab placement,
theme/layout behavior, live vehicle telemetry, OBS, Ruijie hardware, and audible volume.
