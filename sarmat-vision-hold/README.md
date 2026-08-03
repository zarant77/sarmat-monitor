# Sarmat Vision Hold

Незалежний плагін Mission Planner, який перетворює налаштований RTSP-потік на optical flow. Він не використовує RC override і не керує roll/pitch. У live-режимі надсилає стандартний `OPTICAL_FLOW_RAD`, а стабілізацію виконує ArduPilot FlowHold/EKF.

> За замовчуванням `DiagnosticsOnly=true`, а `EnableLiveControl=false`. На реальному апараті нічого не надсилається і режими не перемикаються.

## Модулі

- `CameraFrameSource` — RTSP/FFmpeg capture; `OpticalFlowTracker` — Shi–Tomasi + pyramidal Lucas–Kanade.
- `AttitudeCompensator`, `GroundVelocityEstimator`, `FlowQualityEstimator` — компенсація IMU, масштабування relative altitude та gates.
- `RcSwitchListener` — пасивний hysteresis/debounce/stale listener без запису RC.
- `OpticalFlowMavlinkPublisher`, `EkfSourceController` — SITL-only live path.
- `VisionHoldStateMachine` — `Disabled`, `WarmingUp`, `Ready`, `Active`, `Degraded`, `Lost`.
- `VisionHoldPanel` — окрема вкладка Flight Data.

Налаштування й лог незалежні від Sarmat Plugin: `%APPDATA%\Sarmat\VisionHold\settings.json` і `vision-hold.log`.

## Збірка DLL

```powershell
.\scripts\build.ps1 -MissionPlannerPath 'C:\Program Files (x86)\Mission Planner' -Configuration Release
```

Скопіюйте весь вміст `dist\plugins` до `Mission Planner\plugins`.

## RC switch

Після першого запуску закрийте Mission Planner і відредагуйте `settings.json`: `RcChannel` (1–18), `RcEnableThreshold` (1700), `RcDisableThreshold` (1300), `RcInverted`, `RcDebounceMs` (300), `RcStaleMs` (1000). Значення лише читаються з телеметрії Mission Planner. Зміна фіксується один раз після debounce; недоступний або застарілий канал примусово дає OFF.

## SITL

1. Залиште `DiagnosticsOnly=true`, `EnableLiveControl=false`; подайте RTSP і перевірте FPS, frame age, points, quality, raw/compensated flow та height.
2. Перевірте RC9: вище 1700 — ON, нижче 1300 — OFF; середня зона зберігає стан.
3. У ArduCopter SITL заздалегідь налаштуйте EKF source set без GPS XY та FlowHold. GPS фізично не вимикайте.
4. Лише для SITL задайте `DiagnosticsOnly=false`, `EnableLiveControl=true`, `NonGpsEkfSourceSet`, `FallbackMode`, потім перезапустіть Mission Planner.
5. `Active` можливий лише після warm-up, свіжих кадрів, достатніх FPS/quality/points, валідної висоти та MAVLink.
6. Заморозьте RTSP, зірвіть tracking або MAVLink: публікація має припинитися, стан перейти в `Lost/Degraded`, а режим — повернутися або перейти в `AltHold`.

Live control експериментальний і призначений лише для SITL до окремої льотної валідації.
